using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [Designer(DisplayName = "$RichTextField")]
    public class RichTextFieldDesign() : ValueFieldDesignBase(typeof(RichTextFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeMentionNoUserModule = 1;
        private const int CodeMentionNoLoginContract = 2;
        private const int CodeMentionConditionModule = 3;
        private const int CodeMentionConditionVariable = 4;

        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$RichTextFieldDbColumn"), DbColumn(nameof(RichTextFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        /// <summary>
        /// 本文の中で @ を打つと、アプリを使える人 (ログインユーザーのモジュールの行) の候補を出して埋め込めるようにする。
        /// 候補はサーバーに問い合わせる (共通の API の Mention.Candidates)。既定 false。
        /// </summary>
        [Designer(Index = 1, DisplayName = "$RichTextFieldEnableMention")]
        public bool EnableMention { get; set; }

        /// <summary>
        /// メンションの候補にする人の条件 (同じ部署の人だけ、など)。対象はログインユーザーのモジュールで、左辺は候補になる人の項目。
        /// 右辺の CurrentUser.〜 は @ を打っている人の値。空なら絞らない。
        /// 絞り込みであって権限ではない (条件の外の人がこのフィールドを見られなくなるわけではない)。
        /// </summary>
        [Designer(Index = 2, DisplayName = "$RichTextFieldMentionCandidateCondition")]
        public ModuleMatchCondition MentionCandidateCondition { get; set; } = new();

        public override string GetWebComponentTypeFullName() => typeof(RichTextFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new RichTextField(this);

        public override FieldDataBase? CreateData() => new RichTextFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            if (EnableMention) CheckMention(context, result);
            return result;
        }

        //候補はログインユーザーのモジュールの行で、表示名とログイン ID はそこのログインアカウント契約の役割から引く
        void CheckMention(DesignCheckContext context, List<DesignCheckInfo> result)
        {
            var userModuleName = context.DesignData.AppSettings.CurrentUserModuleDesignName;
            var userModule = string.IsNullOrEmpty(userModuleName) ? null : context.DesignData.Modules.Find(userModuleName);
            if (userModule == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionNoUserModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(EnableMention) },
                    Message = Properties.Resources.RichTextMentionCheck_NoUserModule,
                });
                return;
            }
            if (!userModule.Fields.OfType<LoginAccountContractFieldDesign>().Any())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionNoLoginContract),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(EnableMention) },
                    Message = string.Format(Properties.Resources.RichTextMentionCheck_NoLoginContract, userModuleName),
                });
            }
            CheckMentionCandidateCondition(context, userModuleName, result);
        }

        //候補の条件はユーザーのモジュールの検索にそのまま足す。左辺は候補になる人の項目、右辺の CurrentUser.〜 は @ を打っている人の項目
        void CheckMentionCandidateCondition(DesignCheckContext context, string userModuleName, List<DesignCheckInfo> result)
        {
            var conditionModuleName = MentionCandidateCondition.ModuleName;
            if (!string.IsNullOrEmpty(conditionModuleName) && conditionModuleName != userModuleName)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionConditionModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(MentionCandidateCondition) },
                    Message = string.Format(Properties.Resources.RichTextMentionCheck_ConditionModule, conditionModuleName, userModuleName),
                });
                return;
            }

            foreach (var message in Problems(MentionCandidateCondition.Condition).Distinct().ToList())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionConditionVariable),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(MentionCandidateCondition) },
                    Message = message,
                });
            }

            //番号は同じ (:4) で、文言は理由ごとに分ける (無い項目 / 左辺の CurrentUser.〜 / 候補になる人の項目どうしの比較)
            IEnumerable<string> Problems(MatchConditionBase? node)
            {
                if (node is MultiMatchCondition multi)
                {
                    foreach (var e in multi.Children.SelectMany(Problems)) yield return e;
                }
                if (node is IFieldMatchCondition field)
                {
                    //左辺は候補になる人の項目。CurrentUser.〜 は書けない (@ を打っている人の値は右辺に書く)
                    var left = field.SearchTargetVariable;
                    if (IsCurrentUserVariable(left)) yield return string.Format(Properties.Resources.RichTextMentionCheck_ConditionLeftCurrentUser, left);
                    else if (IsMissing(left)) yield return Missing(left);
                }
                if (node is FieldVariableMatchCondition { Variable: var right } && !string.IsNullOrEmpty(right))
                {
                    if (IsMissing(right)) yield return Missing(right);
                    //右辺の変数は CurrentUser.〜 だけ (候補になる人の項目どうしの比較は、本体の検索ができない)
                    else if (!IsCurrentUserVariable(right)) yield return string.Format(Properties.Resources.RichTextMentionCheck_ConditionFieldComparison, right);
                }
            }

            string Missing(string variable) => string.Format(Properties.Resources.RichTextMentionCheck_ConditionVariable, variable, userModuleName);

            //CurrentUser.〜 は、本体がユーザーのモジュールの項目として確かめる
            bool IsMissing(string variable)
                => context.CheckFieldRelativeVariableExistence(Name, nameof(MentionCandidateCondition), userModuleName, variable) != null;
        }

        /// <summary>CurrentUser.〜 (今のユーザーの値) の変数か。</summary>
        internal static bool IsCurrentUserVariable(string variable) => variable.StartsWith(SystemFieldNames.CurrentUser + ".", StringComparison.Ordinal);

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context));
            //条件の項目は左辺も右辺 (CurrentUser.〜) もユーザーのモジュールの項目 (本体の AddMatchCondition は右辺をこのフィールドのモジュールの項目として扱うので使わない)
            var userModuleName = context.DesignData.AppSettings.CurrentUserModuleDesignName;
            builder.AddModule(MentionCandidateCondition.ModuleName, x => MentionCandidateCondition.ModuleName = x);
            if (!string.IsNullOrEmpty(userModuleName)) AddVariables(MentionCandidateCondition.Condition);
            return builder.Build();

            void AddVariables(MatchConditionBase? node)
            {
                if (node is MultiMatchCondition multi) multi.Children.ForEach(AddVariables);
                if (node is IFieldMatchCondition field) builder.AddVariable(userModuleName, field.SearchTargetVariable, x => field.SearchTargetVariable = x);
                if (node is FieldVariableMatchCondition variable) builder.AddVariable(userModuleName, variable.Variable, x => variable.Variable = x);
            }
        }
    }
}
