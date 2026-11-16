using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataLogic;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Mention
{
    /// <summary>
    /// メンションの候補 = アプリを使える人 (ログインユーザーのモジュールの行のうち、アプリアクセス条件を満たし、ログインアカウント契約の有効フラグが偽でないもの)。
    /// 誰を候補にするかはここだけで決める (段階 2 でページの権限による絞り込みに差し替える場所)。
    /// 行はシステムの権限で読む (問い合わせた人がユーザーのモジュールを読めなくても候補は出す。返すのは Id・表示名・ログイン ID だけ)。
    /// </summary>
    internal static class MentionCandidateQuery
    {
        /// <summary>返す候補の数。</summary>
        internal const int Limit = 10;

        //有効フラグは条件にせず読んでから判定する (列の型が Boolean / 数値 / 文字列のどれでもログインと同じ読み方にするため)。
        //判定で落ちる行があっても Limit 件をそろえられるよう、ページで読み進める
        const int PageSize = 50;
        const int MaxPages = 20;

        internal static async Task<List<MentionCandidate>> FindAsync(ModuleDataIO moduleDataIO, DesignData designData, string? text, ILogger? logger = null)
        {
            var userModule = designData.Modules.Find(designData.AppSettings.CurrentUserModuleDesignName);
            var contract = userModule?.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            var idField = userModule?.Fields.OfType<IdFieldDesign>().FirstOrDefault();
            if (userModule == null || contract == null || idField == null || string.IsNullOrEmpty(contract.LoginName)) return new();

            if (!TryCreateAppAccessCondition(designData, userModule.Name, out var appAccess))
            {
                //ユーザーのモジュールの項目に組み替えられない条件 (別のモジュールの行を見る条件) は、誰がアプリを使えるか決められないので候補を出さない
                logger?.LogWarning("Mention candidates are not available: the app access condition cannot be evaluated on the user module '{Module}'.", userModule.Name);
                return new();
            }

            var displayNameField = string.IsNullOrEmpty(contract.DisplayName) ? contract.LoginName : contract.DisplayName;
            var conditions = new List<MatchConditionBase>();
            if (appAccess != null) conditions.Add(appAccess);
            text = text?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                //打った文字は表示名かログイン ID のどちらかに含まれていればよい (% _ は本体がエスケープする)
                var names = new[] { displayNameField, contract.LoginName }.Distinct().ToArray();
                conditions.Add(MultiMatchCondition.Or(names.Select(Like).ToArray()));
                MatchConditionBase Like(string field) => new FieldValueMatchCondition
                {
                    SearchTargetVariable = $"{field}.Value",
                    Comparison = MatchComparison.Like,
                    Value = new StringValue { Value = text },
                };
            }

            var selectFields = new[] { idField.Name, displayNameField, contract.LoginName, contract.IsActive }.Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
            var access = moduleDataIO.CreateSystemAccess();
            var result = new List<MentionCandidate>();
            for (var pageIndex = 0; pageIndex < MaxPages && result.Count < Limit; pageIndex++)
            {
                var condition = new SearchCondition(userModule.Name)
                {
                    Condition = conditions.Count == 0 ? null : MultiMatchCondition.And(conditions.ToArray()),
                    LimitCount = PageSize,
                    SelectFields = selectFields.ToList(),
                };
                condition.SortConditions.Add(new SortCondition { Variable = $"{displayNameField}.Value" });
                condition.SortConditions.Add(new SortCondition { Variable = $"{idField.Name}.Value" });
                var page = await access.GetListAsync(condition, pageIndex);
                foreach (var row in page.Items)
                {
                    if (!string.IsNullOrEmpty(contract.IsActive) && !LoginAccountStore.IsActiveValue(Value(row, contract.IsActive))) continue;
                    var id = (row.Fields.TryGetValue(idField.Name, out var data) ? data as IdFieldData : null)?.Value;
                    if (string.IsNullOrEmpty(id)) continue;
                    var loginName = Value(row, contract.LoginName)?.ToString() ?? string.Empty;
                    var displayName = Value(row, displayNameField)?.ToString();
                    result.Add(new MentionCandidate { Id = id, LoginName = loginName, DisplayName = string.IsNullOrEmpty(displayName) ? loginName : displayName });
                    if (result.Count == Limit) break;
                }
                if (page.Items.Count < PageSize) break;
            }
            return result;
        }

        static object? Value(ModuleData row, string fieldName)
            => row.TryGetVariableValue(new VariableName($"{fieldName}.Value"), out var value) ? value : null;

        /// <summary>
        /// アプリアクセス条件をユーザーのモジュールの検索条件にする。条件が無ければ null (全員)。
        /// 本体はアプリアクセス条件をユーザーのモジュールの行 (今のユーザーの行) に当てて判定する
        /// (条件のモジュールはユーザーのモジュールか空・項目はユーザーのモジュールの項目)。同じ条件でユーザーのモジュールを検索すれば、通る人の一覧になる。
        /// それ以外の形 (別のモジュールを対象にした条件・CurrentUser.〜 の変数。本体ではどちらも誰も通らない) と、値や変数との比較以外の条件は false (候補を出さない)。
        /// </summary>
        internal static bool TryCreateAppAccessCondition(DesignData designData, string userModuleName, out MatchConditionBase? condition)
        {
            condition = null;
            var appAccess = designData.AppSettings.AppAccessConditions;
            if (!appAccess.HasEffectiveCondition()) return true;
            if (!string.IsNullOrEmpty(appAccess.ModuleName) && appAccess.ModuleName != userModuleName) return false;
            if (!OnUserFieldsOnly(appAccess.Condition)) return false;
            condition = JsonConverterEx.JsonClone(appAccess.Condition);
            return true;
        }

        static bool OnUserFieldsOnly(MatchConditionBase? node) => node switch
        {
            null => true,
            MultiMatchCondition multi => multi.Children.All(OnUserFieldsOnly),
            FieldValueMatchCondition value => IsUserField(value.SearchTargetVariable),
            FieldVariableMatchCondition variable => IsUserField(variable.SearchTargetVariable) && IsUserField(variable.Variable),
            _ => false,
        };

        static bool IsUserField(string variable) => !variable.StartsWith(SystemFieldNames.CurrentUser + ".", StringComparison.Ordinal);
    }
}
