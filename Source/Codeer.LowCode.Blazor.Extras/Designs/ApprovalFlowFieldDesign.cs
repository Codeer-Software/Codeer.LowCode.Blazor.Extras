using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 承認フローフィールド。申請書モジュールに1つ置くと、申請・承認・却下・差し戻し・
    /// 取り戻し・取消・再申請・回覧確認と進捗表示・履歴表示を提供する。
    /// 状態遷移はサーバーの command API だけが行い、承認データはユーザー定義の
    /// 承認モジュール (Flow / Member / History) に保存される (フィールド名は既定名で固定)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CheckDecagramOutline")]
    [Designer(DisplayName = "$ApprovalFlowField")]
    public class ApprovalFlowFieldDesign : FieldDesignBase
    {
        public ApprovalFlowFieldDesign() : base(typeof(ApprovalFlowFieldDesign).FullName!) { }

        /// <summary>承認フロー行への FK 列。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "$DbColumn"),
         DbColumn(nameof(ApprovalFlowFieldData.Id)), Join,
         ModuleMember(Member = nameof(FlowModuleName))]
        public string DbColumn { get; set; } = string.Empty;

        /// <summary>
        /// フロー状態のコピーを保存する列 (任意)。設定するとエンジンが遷移のたびに書き戻し、
        /// 条件式・検索・一覧列で "Approval.State" が自列として使える (null = 未申請)。
        /// </summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "$ApprovalStateDbColumn"),
         DbColumn(nameof(ApprovalFlowFieldData.State))]
        public string StateDbColumn { get; set; } = string.Empty;

        /// <summary>申請者ユーザー Id のコピーを保存する列 (任意)。"Approval.Applicant" が使える。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.DbColumn, DisplayName = "$ApprovalApplicantDbColumn"),
         DbColumn(nameof(ApprovalFlowFieldData.Applicant))]
        public string ApplicantDbColumn { get; set; } = string.Empty;

        [Designer(Index = 6, CandidateType = CandidateType.Module, DisplayName = "$ApprovalFlowModuleName")]
        public string FlowModuleName { get; set; } = "ApprovalFlow";

        [Designer(Index = 7, CandidateType = CandidateType.Module, DisplayName = "$ApprovalMemberModuleName")]
        public string MemberModuleName { get; set; } = "ApprovalFlowMember";

        /// <summary>
        /// 承認メンバーを表示するこのモジュール上の一覧フィールド名 (任意)。
        /// 条件エディタの「現在の承認待ち」「最終承認の番」がこの一覧への存在条件を組み立てる。
        /// クライアント側の条件評価にも使われるため、詳細レイアウトの DataOnlyFields への登録が必要。
        /// </summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$ApprovalMembersListFieldName")]
        public string MembersListFieldName { get; set; } = "ApprovalMembers";

        [Designer(Index = 9, CandidateType = CandidateType.Module, DisplayName = "$ApprovalHistoryModuleName")]
        public string HistoryModuleName { get; set; } = "ApprovalHistory";

        /// <summary>
        /// スクリプトで組み立てた経路の受け入れ (既定 false)。
        /// スクリプト経路はクライアント由来のため「申請者が経路を指定できる」ことと等価。
        /// 無効のままだと経路同梱の申請をサーバーが拒否する (セキュアバイデフォルト)。
        /// </summary>
        [Designer(Index = 10, DisplayName = "$ApprovalAllowScriptRoute")]
        public bool AllowScriptRoute { get; set; }

        /// <summary>
        /// 取り下げの許可範囲 (業務ポリシー)。既定は「承認が始まる前のみ」。
        /// エンジンが強制するのは資格・版・遷移の整合だけで、この種の業務ポリシーはデザインで選ぶ。
        /// </summary>
        [Designer(Index = 11, DisplayName = "$ApprovalWithdrawPolicy")]
        public ApprovalWithdrawPolicy WithdrawPolicy { get; set; } = ApprovalWithdrawPolicy.BeforeFirstApproval;

        [Designer(Index = 12, DisplayName = "$ApprovalShowProgress")]
        public bool ShowProgress { get; set; } = true;

        [Designer(Index = 13, DisplayName = "$ApprovalShowHistory")]
        public bool ShowHistory { get; set; } = true;

        [Designer(Index = 14, DisplayName = "$ApprovalShowComment")]
        public bool ShowComment { get; set; } = true;

        /// <summary>
        /// 組み込みのアクションボタン群を表示するか。
        /// false にすると標準 UI を退かせて、ButtonField ＋ スクリプト API で
        /// アプリ独自の承認 UI を作れる (サーバーの検証はどの UI からでも同じ)。
        /// </summary>
        [Designer(Index = 15, DisplayName = "$ApprovalShowActions")]
        public bool ShowActions { get; set; } = true;

        /// <summary>
        /// 組み込みボタンから隠すアクション (カンマ区切り。例: "Withdraw,Return")。
        /// 一部のボタンだけ外付け (ButtonField ＋ スクリプト API) にしたい場合に使う。
        /// </summary>
        [Designer(Index = 16, DisplayName = "$ApprovalHiddenActions")]
        public string HiddenActions { get; set; } = string.Empty;

        /// <summary>
        /// コメントの入力元にするフィールド (同一モジュール上の任意の文字列系フィールド)。
        /// 指定すると組み込みのコメント欄は表示されず、そのフィールドの値がコメントとして使われる
        /// (RichTextField 等を指定してコメント UI を差し替えられる)。
        /// </summary>
        [Designer(Index = 17, CandidateType = CandidateType.Field, DisplayName = "$ApprovalCommentFieldName")]
        public string CommentFieldName { get; set; } = string.Empty;

        /// <summary>
        /// 経路を組み立てるスクリプト (ApprovalRouteData を返す。null で申請中止)。
        /// 設定すると組み込みの申請・再申請ボタンが表示され、スクリプト API の Submit() / Resubmit() も使える。
        /// </summary>
        [Designer(Index = 18, CandidateType = CandidateType.ScriptEvent, DisplayName = "$ApprovalOnBuildRoute")]
        public string OnBuildRoute { get; set; } = string.Empty;

        /// <summary>
        /// フロー状態が変わった後に呼ぶスクリプト (承認・却下・差し戻し・取り下げ・再申請の成功後)。
        /// 編集可否等、モジュールスクリプト側の表示更新に使う。
        /// </summary>
        [Designer(Index = 19, CandidateType = CandidateType.ScriptEvent, DisplayName = "$ApprovalOnStateChanged")]
        public string OnStateChanged { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(ApprovalFlowFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        //デザイナの条件エディタ用の専用検索コントロール (Extras.Designer 側の WPF コントロール)。
        //状態 (コピー列) / 申請者 / 現在の承認待ち / 最終承認の番 を高レベルに選ばせて条件を組み立てる
        public override string GetSearchControlTypeFullName() =>
            "Codeer.LowCode.Blazor.Extras.Designer.Controls.ApprovalFlowSearchControl";

        public override FieldBase CreateField() => new ApprovalFlowField(this);

        public override FieldDataBase? CreateData() => new ApprovalFlowFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(StateDbColumn), StateDbColumn).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(ApplicantDbColumn), ApplicantDbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnBuildRoute), OnBuildRoute,
                context.GetScriptMethodAttribute(GetType(), nameof(OnBuildRoute))).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnStateChanged), OnStateChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnStateChanged))).AddTo(result);
            context.CheckFieldFieldExistence(Name, nameof(CommentFieldName), CommentFieldName).AddTo(result);

            //HiddenActions のトークン検証 (未知のアクション名は設定ミス)
            foreach (var token in SplitHiddenActions())
            {
                if (token is ApprovalActions.Submit or ApprovalActions.Approve or ApprovalActions.Reject
                    or ApprovalActions.Return or ApprovalActions.Withdraw or ApprovalActions.Resubmit
                    or ApprovalActions.Confirm) continue;
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = nameof(HiddenActions),
                    },
                    Message = string.Format(Properties.Resources.ApprovalCheck_UnknownActionFormat, token),
                });
            }

            context.CheckFieldModuleExistence(Name, nameof(FlowModuleName), FlowModuleName).AddTo(result);
            context.CheckFieldModuleExistence(Name, nameof(MemberModuleName), MemberModuleName).AddTo(result);
            context.CheckFieldModuleExistence(Name, nameof(HistoryModuleName), HistoryModuleName).AddTo(result);

            //承認メンバー一覧フィールド (条件エディタの「現在の承認待ち」等が存在条件に使う) の整合。
            //フィールド自体が無いのは許容 (メンバー条件を使わない構成)。あるなら正しい一覧であること
            if (!string.IsNullOrEmpty(MembersListFieldName))
            {
                var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
                var membersField = ownModule?.Fields.FirstOrDefault(e => e.Name == MembersListFieldName);
                if (membersField != null &&
                    (membersField is not IListFieldDesign membersList ||
                     membersList.SearchCondition.ModuleName != MemberModuleName))
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Location = new FieldDesignDataLocation
                        {
                            Module = context.OwnerModule,
                            Field = Name,
                            Member = nameof(MembersListFieldName),
                        },
                        Message = string.Format(Properties.Resources.ApprovalCheck_MembersListFieldMismatchFormat,
                            MembersListFieldName, MemberModuleName),
                    });
                }
            }

            //各承認モジュールに既定名の必須フィールドが揃っているか (エンジンは綴りで探すため欠落は実行時エラーになる)
            CheckRequiredFields(context, result, nameof(FlowModuleName), FlowModuleName,
            [
                ApprovalFieldNames.Flow.Status, ApprovalFieldNames.Flow.TargetModuleName,
                ApprovalFieldNames.Flow.TargetId, ApprovalFieldNames.Flow.RouteName,
                ApprovalFieldNames.Flow.AttemptNo, ApprovalFieldNames.Flow.CurrentStepNo,
            ]);
            CheckRequiredFields(context, result, nameof(MemberModuleName), MemberModuleName,
            [
                ApprovalFieldNames.Member.Flow, ApprovalFieldNames.Member.AttemptNo,
                ApprovalFieldNames.Member.StepNo, ApprovalFieldNames.Member.StepName,
                ApprovalFieldNames.Member.StepType, ApprovalFieldNames.Member.CompletionPolicy,
                ApprovalFieldNames.Member.IsCommentRequiredOnReject, ApprovalFieldNames.Member.ReturnScope,
                ApprovalFieldNames.Member.ApproverUser, ApprovalFieldNames.Member.IsRequired,
                ApprovalFieldNames.Member.IsFinalStep,
                ApprovalFieldNames.Member.Status, ApprovalFieldNames.Member.ActedAt,
            ]);
            CheckRequiredFields(context, result, nameof(HistoryModuleName), HistoryModuleName,
            [
                ApprovalFieldNames.History.Flow, ApprovalFieldNames.History.AttemptNo,
                ApprovalFieldNames.History.StepNo, ApprovalFieldNames.History.Action,
                ApprovalFieldNames.History.ActorUser, ApprovalFieldNames.History.FromStatus,
                ApprovalFieldNames.History.ToStatus, ApprovalFieldNames.History.Comment,
                ApprovalFieldNames.History.ActedAt,
            ]);

            return result;
        }

        void CheckRequiredFields(DesignCheckContext context, List<DesignCheckInfo> result,
            string memberName, string moduleName, string[] requiredFieldNames)
        {
            var module = context.DesignData.Modules.Find(moduleName);
            if (module == null) return; //モジュール不在は CheckFieldModuleExistence が指摘済み

            foreach (var fieldName in requiredFieldNames)
            {
                if (module.Fields.Any(e => e.Name == fieldName)) continue;
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = memberName,
                    },
                    Message = string.Format(Properties.Resources.ApprovalCheck_RequiredFieldMissingFormat,
                        moduleName, fieldName),
                });
            }
        }

        /// <summary>HiddenActions をトークンに分解する (ランタイムとチェックで共用)。</summary>
        public IEnumerable<string> SplitHiddenActions()
            => HiddenActions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(FlowModuleName, x => FlowModuleName = x)
            .AddModule(MemberModuleName, x => MemberModuleName = x)
            .AddModule(HistoryModuleName, x => HistoryModuleName = x)
            .AddField(MembersListFieldName, x => MembersListFieldName = x)
            .AddField(CommentFieldName, x => CommentFieldName = x)
            .Build();
    }
}
