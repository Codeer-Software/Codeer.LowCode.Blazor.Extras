using System.Reactive.Linq;
using Codeer.LowCode.Blazor.Designer.Match;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Match;
using Reactive.Bindings;

namespace Codeer.LowCode.Blazor.Extras.Designer.Controls
{
    /// <summary>条件の種類 (承認フローフィールドの検索コントロール)。</summary>
    public enum ApprovalSearchKind
    {
        /// <summary>フロー状態 (State コピー列。未申請 = null 含む複数選択)。</summary>
        State,

        /// <summary>申請者 (Applicant コピー列と変数の比較)。</summary>
        Applicant,

        /// <summary>現在の承認待ちに指定ユーザーがいる (メンバー一覧への存在条件)。</summary>
        CurrentApprover,

        /// <summary>最終承認ステップの番が指定ユーザーに回っている (メンバー一覧への存在条件)。</summary>
        FinalApprover,
    }

    /// <summary>
    /// ApprovalFlowField の検索コントロール ViewModel (基底なしの自己完結型)。
    /// 状態 / 申請者 / 現在の承認待ち / 最終承認の番 を高レベルに選ばせて、
    /// State・Applicant コピー列とメンバー一覧 (MembersListFieldName) への存在条件を組み立てる。
    /// ユーザーに ListField のパスや Waiting 等の内部値を書かせないための専用 UI。
    /// </summary>
    public class ApprovalFlowSearchControlViewModel
    {
        readonly IMatchConditionData _parent;

        public ApprovalFlowSearchControlViewModel(IMatchConditionData parent)
        {
            _parent = parent;

            KindItems =
            [
                new KindItem(ApprovalSearchKind.State, Properties.Resources.ApprovalSearch_Kind_State),
                new KindItem(ApprovalSearchKind.Applicant, Properties.Resources.ApprovalSearch_Kind_Applicant),
                new KindItem(ApprovalSearchKind.CurrentApprover, Properties.Resources.ApprovalSearch_Kind_CurrentApprover),
                new KindItem(ApprovalSearchKind.FinalApprover, Properties.Resources.ApprovalSearch_Kind_FinalApprover),
            ];
            States =
            [
                new StateItem(Properties.Resources.ApprovalSearch_State_NotSubmitted, null),
                new StateItem(Properties.Resources.ApprovalSearch_State_InProgress, ApprovalFlowStatuses.InProgress),
                new StateItem(Properties.Resources.ApprovalSearch_State_Completed, ApprovalFlowStatuses.Completed),
                new StateItem(Properties.Resources.ApprovalSearch_State_Rejected, ApprovalFlowStatuses.Rejected),
                new StateItem(Properties.Resources.ApprovalSearch_State_Returned, ApprovalFlowStatuses.Returned),
                new StateItem(Properties.Resources.ApprovalSearch_State_Withdrawn, ApprovalFlowStatuses.Withdrawn),
            ];

            Load(parent.Condition);

            IsStateKind = Kind.Select(e => e == ApprovalSearchKind.State)
                .ToReactiveProperty(Kind.Value == ApprovalSearchKind.State);
            IsVariableKind = Kind.Select(e => e != ApprovalSearchKind.State)
                .ToReactiveProperty(Kind.Value != ApprovalSearchKind.State);

            Kind.Skip(1).Subscribe(_ => UpdateCondition());
            Variable.Skip(1).Subscribe(_ => UpdateCondition());
            foreach (var state in States)
            {
                state.IsChecked.Skip(1).Subscribe(_ => UpdateCondition());
            }
        }

        public IReadOnlyList<KindItem> KindItems { get; }
        public IReadOnlyList<StateItem> States { get; }

        public ReactiveProperty<ApprovalSearchKind> Kind { get; } = new(ApprovalSearchKind.State);

        /// <summary>比較相手の変数 (申請者 / 承認者ユーザー)。既定は現在ユーザーの Id。</summary>
        public ReactiveProperty<string?> Variable { get; } =
            new($"{SystemFieldNames.CurrentUser}.Id.Value");

        public ReactiveProperty<bool> IsStateKind { get; }
        public ReactiveProperty<bool> IsVariableKind { get; }

        public IEnumerable<string> VariableCandidates => _parent.VariableCandidates;

        string StateTarget => $"{_parent.SearchTargetField}.{nameof(ApprovalFlowFieldData.State)}";
        string ApplicantTarget => $"{_parent.SearchTargetField}.{nameof(ApprovalFlowFieldData.Applicant)}";
        string MembersListFieldName => (_parent.FieldDesign as ApprovalFlowFieldDesign)?.MembersListFieldName ?? string.Empty;

        void UpdateCondition() => _parent.Condition = Build();

        MatchConditionBase? Build()
        {
            switch (Kind.Value)
            {
                case ApprovalSearchKind.State:
                {
                    var conditions = States.Where(e => e.IsChecked.Value)
                        .Select(MatchConditionBase (e) => new FieldValueMatchCondition
                        {
                            SearchTargetVariable = StateTarget,
                            Comparison = MatchComparison.Equal,
                            Value = MultiTypeValue.Create(e.Value),
                        }).ToList();
                    if (conditions.Count == 0) return null;
                    if (conditions.Count == 1) return conditions[0];
                    return new MultiMatchCondition { IsOrMatch = true, Children = conditions };
                }
                case ApprovalSearchKind.Applicant:
                {
                    if (string.IsNullOrEmpty(Variable.Value)) return null;
                    return new FieldVariableMatchCondition
                    {
                        SearchTargetVariable = ApplicantTarget,
                        Comparison = MatchComparison.Equal,
                        Variable = Variable.Value!,
                    };
                }
                case ApprovalSearchKind.CurrentApprover:
                case ApprovalSearchKind.FinalApprover:
                {
                    var membersList = MembersListFieldName;
                    if (string.IsNullOrEmpty(membersList) || string.IsNullOrEmpty(Variable.Value)) return null;

                    //And で同じ一覧を指す条件は「同一行が全条件を満たす」存在条件として評価される (SQL / メモリ共通)。
                    //State == InProgress を先頭に置くのは、条件エディタがこの行を承認フローフィールドの行として
                    //扱うため (先頭条件の対象フィールドで行の対象を決める) と、意味の自己文書化のため
                    var children = new List<MatchConditionBase>
                    {
                        new FieldValueMatchCondition
                        {
                            SearchTargetVariable = StateTarget,
                            Comparison = MatchComparison.Equal,
                            Value = MultiTypeValue.Create(ApprovalFlowStatuses.InProgress),
                        },
                        Kind.Value == ApprovalSearchKind.FinalApprover
                            ? new FieldValueMatchCondition
                            {
                                SearchTargetVariable = $"{membersList}.{ApprovalFieldNames.Member.IsFinalStep}.Value",
                                Comparison = MatchComparison.Equal,
                                Value = MultiTypeValue.Create(true),
                            }
                            : new FieldValueMatchCondition
                            {
                                SearchTargetVariable = $"{membersList}.{ApprovalFieldNames.Member.StepType}.Value",
                                Comparison = MatchComparison.Equal,
                                Value = MultiTypeValue.Create(ApprovalStepTypes.Approval),
                            },
                        new FieldValueMatchCondition
                        {
                            SearchTargetVariable = $"{membersList}.{ApprovalFieldNames.Member.Status}.Value",
                            Comparison = MatchComparison.Equal,
                            Value = MultiTypeValue.Create(ApprovalMemberStatuses.Waiting),
                        },
                        new FieldVariableMatchCondition
                        {
                            SearchTargetVariable = $"{membersList}.{ApprovalFieldNames.Member.ApproverUser}.Value",
                            Comparison = MatchComparison.Equal,
                            Variable = Variable.Value!,
                        },
                    };
                    return new MultiMatchCondition { Children = children };
                }
            }
            return null;
        }

        //読み込んだ条件から UI 状態を復元する。未知の形は既定表示のまま Condition を保持する
        //(購読は Skip(1) 済みなので、ユーザーが触るまで条件を作り直さない)
        void Load(MatchConditionBase? condition)
        {
            switch (condition)
            {
                case FieldVariableMatchCondition variable
                    when MemberNameOf(variable.SearchTargetVariable) == nameof(ApprovalFlowFieldData.Applicant):
                    Kind.Value = ApprovalSearchKind.Applicant;
                    Variable.Value = variable.Variable;
                    return;

                case FieldValueMatchCondition value when IsStateCondition(value):
                    Kind.Value = ApprovalSearchKind.State;
                    CheckState(value);
                    return;

                case MultiMatchCondition multi:
                {
                    if (multi.Children.Count > 0 &&
                        multi.Children.All(e => e is FieldValueMatchCondition value && IsStateCondition(value)))
                    {
                        Kind.Value = ApprovalSearchKind.State;
                        foreach (var child in multi.Children.OfType<FieldValueMatchCondition>()) CheckState(child);
                        return;
                    }

                    if (!multi.IsOrMatch)
                    {
                        var approver = multi.Children.OfType<FieldVariableMatchCondition>().FirstOrDefault();
                        var hasWaiting = multi.Children.OfType<FieldValueMatchCondition>()
                            .Any(e => MemberNameOf(e.SearchTargetVariable) == "Value" &&
                                      Equals(e.Value.GetValue(), ApprovalMemberStatuses.Waiting));
                        if (approver != null && hasWaiting)
                        {
                            var isFinal = multi.Children.OfType<FieldValueMatchCondition>().Any(e =>
                                new VariableName(e.SearchTargetVariable).FieldName.FullName
                                    .EndsWith("." + ApprovalFieldNames.Member.IsFinalStep));
                            Kind.Value = isFinal ? ApprovalSearchKind.FinalApprover : ApprovalSearchKind.CurrentApprover;
                            Variable.Value = approver.Variable;
                            return;
                        }
                    }
                    break;
                }
            }
        }

        bool IsStateCondition(FieldValueMatchCondition condition)
            => MemberNameOf(condition.SearchTargetVariable) == nameof(ApprovalFlowFieldData.State);

        void CheckState(FieldValueMatchCondition condition)
        {
            var value = condition.Value.GetValue() as string;
            var item = States.FirstOrDefault(e => e.Value == value);
            if (item != null) item.IsChecked.Value = true;
        }

        static string MemberNameOf(string variable) => new VariableName(variable).MemberName;

        public sealed record KindItem(ApprovalSearchKind Kind, string Label);

        public sealed class StateItem(string label, string? value)
        {
            public string Label { get; } = label;
            public string? Value { get; } = value;
            public ReactiveProperty<bool> IsChecked { get; } = new(false);
        }
    }
}
