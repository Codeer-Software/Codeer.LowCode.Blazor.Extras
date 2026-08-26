using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>Origin record of a send operation (for the history's SourceModule/SourceId).</summary>
    internal class MailHistorySource
    {
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }

    /// <summary>送信明細 1 行分 (宛先ごとの解決後の文面と成否)。履歴契約の Details が設定されているときだけ書かれる。</summary>
    internal class MailHistoryDetail
    {
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Mail.HistoryModuleName のモジュールへ、送信操作1回につき1行の履歴を書く。
    /// 履歴契約の Details (一覧) が設定されていれば、その参照先の明細モジュールへ 1 宛先 1 行の明細も書く。
    /// フィールド名は各モジュール上の契約フィールド経由で解決する
    /// (空の役割 = 記録しない。契約フィールドが無いモジュールは既定の役割名を使う)。
    /// 書き込みは内部 add デリゲート経由で、操作ユーザーの書き込み権限に依存しない (履歴はシステムの記録)。
    /// </summary>
    /// <remarks>
    /// **履歴を取る設定 (Mail.HistoryModuleName) なのにモジュールが契約を満たしていない場合は
    /// 送信前の <see cref="Validate"/> で例外にする = メールを送らない** (静かに記録が欠けるのを防ぐ。
    /// 履歴モジュールは appsettings 指定なのでデザインチェックからは辿れず、実行時に検出するしかない)。
    /// 一方、書き込み時の障害 (DB エラー等) は送信後なので logError のみで送信は失敗させない。
    /// 記録したくない項目は契約フィールドを置いてその役割を空にする。
    /// </remarks>
    public class MailHistoryWriter
    {
        readonly string _historyModuleName;
        readonly DesignData _designData;
        readonly Func<ModuleData, Task<string>> _addInternalAsync;
        readonly Action<string>? _logError;

        /// <param name="addInternalAsync">内部の追加経路。採番された Id を返す (明細が履歴行を参照するのに使う)。</param>
        public MailHistoryWriter(string historyModuleName, DesignData designData,
            Func<ModuleData, Task<string>> addInternalAsync, Action<string>? logError = null)
        {
            _historyModuleName = historyModuleName;
            _designData = designData;
            _addInternalAsync = addInternalAsync;
            _logError = logError;
        }

        /// <summary>
        /// 履歴モジュール (と明細モジュール) が契約を満たしているかを検証する (送信前に呼ぶ)。
        /// モジュール不在・必須役割の不備・契約が名指ししたフィールドの不在は例外 = 送信させない。
        /// </summary>
        /// <remarks>
        /// **必須以外の役割は空にできる** (= その項目は記録しない)。契約フィールドを置いていない場合は
        /// 既定の役割名で書くが、既定名のフィールドが無い項目は「記録しない」として扱う
        /// (必須役割だけは無いとエラー)。
        /// </remarks>
        internal void Validate()
        {
            var design = _designData.Modules.Find(_historyModuleName)
                ?? throw new InvalidOperationException(
                    $"Mail history module '{_historyModuleName}' (Mail.HistoryModuleName) does not exist.");

            var contract = MailContracts.History(design);
            var names = contract ?? new MailHistoryContractFieldDesign();
            var problems = new List<string>();
            foreach (var (role, fieldName) in GetRoles(names))
                CheckRole(design, contract != null, names.IsRoleRequired(role), role, fieldName, problems);

            if (problems.Count > 0)
                throw new InvalidOperationException(
                    $"Mail history module '{_historyModuleName}' does not implement the mail history contract: " +
                    string.Join(", ", problems) +
                    ". Add the fields, or put a MailHistoryContractField on the module and leave the roles you do not record empty.");

            //明細 (任意): Details が設定されていれば、その一覧の先のモジュールが明細契約を満たすこと
            var detailModule = ResolveDetailModule(design, names, out var detailError);
            if (detailError != null)
                throw new InvalidOperationException($"Mail history module '{_historyModuleName}': {detailError}");
            if (detailModule == null) return;

            var detailContract = MailContracts.HistoryDetail(detailModule);
            var detailNames = detailContract ?? new MailHistoryDetailContractFieldDesign();
            foreach (var (role, fieldName) in GetDetailRoles(detailNames))
                CheckRole(detailModule, detailContract != null, detailNames.IsRoleRequired(role), role, fieldName, problems);
            if (problems.Count > 0)
                throw new InvalidOperationException(
                    $"Mail history detail module '{detailModule.Name}' does not implement the mail history detail contract: " +
                    string.Join(", ", problems) + ".");
        }

        static void CheckRole(ModuleDesign design, bool hasContract, bool required, string role, string fieldName, List<string> problems)
        {
            if (string.IsNullOrEmpty(fieldName))
            {
                if (required) problems.Add($"{role} (required) is empty");
                return;
            }
            if (design.Fields.Any(f => f.Name == fieldName)) return;
            //契約が名指ししたフィールドの不在は設定ミス。既定名フォールバックは記録しないだけ (必須は除く)
            if (required || hasContract) problems.Add($"{role} -> '{fieldName}' does not exist");
        }

        //Details 役割から明細モジュールを解く。未設定 = null (エラーなし)。設定に不備があれば error に理由
        ModuleDesign? ResolveDetailModule(ModuleDesign historyDesign, MailHistoryContractFieldDesign names, out string? error)
        {
            error = null;
            if (string.IsNullOrEmpty(names.Details)) return null;
            var listField = historyDesign.Fields.FirstOrDefault(e => e.Name == names.Details);
            if (listField is not ListFieldDesignBase list)
            {
                error = $"Details -> '{names.Details}' is not a list field.";
                return null;
            }
            var detailModule = _designData.Modules.Find(list.SearchCondition.ModuleName);
            if (detailModule == null)
                error = $"Details -> '{names.Details}' refers to module '{list.SearchCondition.ModuleName}' which does not exist.";
            return detailModule;
        }

        //役割名 → 記録先フィールド名 (契約フィールドが無い場合は既定名)
        static IEnumerable<(string Role, string FieldName)> GetRoles(MailHistoryContractFieldDesign names)
        {
            yield return (nameof(names.SentAt), names.SentAt);
            yield return (nameof(names.MailInfraName), names.MailInfraName);
            yield return (nameof(names.Subject), names.Subject);
            yield return (nameof(names.TotalCount), names.TotalCount);
            yield return (nameof(names.SuccessCount), names.SuccessCount);
            yield return (nameof(names.FailureDetails), names.FailureDetails);
            yield return (nameof(names.SourceModule), names.SourceModule);
            yield return (nameof(names.SourceId), names.SourceId);
        }

        static IEnumerable<(string Role, string FieldName)> GetDetailRoles(MailHistoryDetailContractFieldDesign names)
        {
            yield return (nameof(names.History), names.History);
            yield return (nameof(names.To), names.To);
            yield return (nameof(names.Subject), names.Subject);
            yield return (nameof(names.Body), names.Body);
            yield return (nameof(names.IsSuccess), names.IsSuccess);
            yield return (nameof(names.Error), names.Error);
        }

        /// <param name="details">宛先ごとの明細 (履歴契約の Details が設定されているときだけ書かれる)。null = 明細なし。</param>
        internal async Task WriteAsync(string mailInfraName, string subject, MailSendResult result, MailHistorySource? source,
            IReadOnlyList<MailHistoryDetail>? details = null)
        {
            try
            {
                //構成の妥当性は送信前の Validate で確認済み (ここに来る時点でモジュールと役割は揃っている)
                var design = _designData.Modules.Find(_historyModuleName);
                if (design == null)
                {
                    _logError?.Invoke($"Mail history module '{_historyModuleName}' does not exist.");
                    return;
                }

                //フィールド名は履歴モジュール上の契約で解決する (契約が無ければ既定名。空の役割 = 記録しない)
                var names = MailContracts.History(design) ?? new MailHistoryContractFieldDesign();

                var data = new ModuleData { Name = _historyModuleName };
                var set = CreateSetter(design, data);
                set(names.SentAt, e => ((DateTimeFieldData)e).Value = DateTime.Now);
                set(names.MailInfraName, e => ((TextFieldData)e).Value = mailInfraName);
                set(names.Subject, e => ((TextFieldData)e).Value = subject);
                set(names.TotalCount, e => ((NumberFieldData)e).Value = result.TotalCount);
                set(names.SuccessCount, e => ((NumberFieldData)e).Value = result.SuccessCount);
                set(names.FailureDetails, e =>
                {
                    var json = JsonSerializer.Serialize(result.Failures);
                    if (e is JsonFieldData jsonData) jsonData.Value = json;
                    else ((TextFieldData)e).Value = json;
                });
                if (source != null)
                {
                    set(names.SourceModule, e => ((TextFieldData)e).Value = source.SourceModule);
                    set(names.SourceId, e => ((TextFieldData)e).Value = source.SourceId);
                }

                if (!data.Fields.Any())
                {
                    _logError?.Invoke($"Mail history module '{_historyModuleName}' has none of the contract role fields.");
                    return;
                }
                var historyId = await _addInternalAsync(data);

                if (details == null || details.Count == 0) return;
                var detailModule = ResolveDetailModule(design, names, out _);
                if (detailModule == null) return;
                await WriteDetailsAsync(detailModule, historyId, details);
            }
            catch (Exception ex)
            {
                //履歴は送信の従属機能。履歴の失敗で送信を失敗にしない
                _logError?.Invoke($"Failed to write mail history: {ex.Message}");
            }
        }

        //明細: 1 宛先 1 行。履歴行の Id を History 役割 (Link) に入れる
        async Task WriteDetailsAsync(ModuleDesign detailModule, string historyId, IReadOnlyList<MailHistoryDetail> details)
        {
            var names = MailContracts.HistoryDetail(detailModule) ?? new MailHistoryDetailContractFieldDesign();
            foreach (var detail in details)
            {
                var data = new ModuleData { Name = detailModule.Name };
                var set = CreateSetter(detailModule, data);
                set(names.History, e => ((ValueFieldDataBase<string>)e).Value = historyId);
                set(names.To, e => ((TextFieldData)e).Value = detail.To);
                set(names.Subject, e => ((TextFieldData)e).Value = detail.Subject);
                set(names.Body, e => ((TextFieldData)e).Value = detail.Body);
                set(names.IsSuccess, e => ((BooleanFieldData)e).Value = detail.IsSuccess);
                set(names.Error, e => ((TextFieldData)e).Value = detail.Error);
                if (!data.Fields.Any()) continue;
                await _addInternalAsync(data);
            }
        }

        //役割のフィールド名が空・不在なら書かない。型違いはログしてスキップ
        Action<string, Action<FieldDataBase>> CreateSetter(ModuleDesign design, ModuleData data)
            => (fieldName, setValue) =>
            {
                if (string.IsNullOrEmpty(fieldName)) return;
                var fieldDesign = design.Fields.FirstOrDefault(e => e.Name == fieldName);
                var fieldData = fieldDesign?.CreateData();
                if (fieldData == null) return;
                try
                {
                    setValue(fieldData);
                    data.Fields[fieldName] = fieldData;
                }
                catch
                {
                    _logError?.Invoke($"Mail history field '{fieldName}' of '{design.Name}' has an unexpected type ({fieldData.GetType().Name}).");
                }
            };
    }
}
