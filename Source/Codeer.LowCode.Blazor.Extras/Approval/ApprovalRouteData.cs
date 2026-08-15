using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>
    /// 経路の中間表現。どの定義ソース (v1 はスクリプト組み立てのみ) もこの形に落ち、
    /// サーバーが検証してメンバー行へスナップショットする。
    /// スクリプトから直接組み立てられるよう、ビルダーメソッドを持つ。
    /// </summary>
    public class ApprovalRouteData
    {
        public string Name { get; set; } = string.Empty;
        public List<ApprovalStepData> Steps { get; set; } = new();

        /// <summary>ステップを追加して返す (流れるように書ける)。</summary>
        [ScriptName("AddStep")]
        public ApprovalStepData AddStep(string name)
        {
            var step = new ApprovalStepData { Name = name };
            Steps.Add(step);
            return step;
        }
    }

    /// <summary>経路の1ステップ。</summary>
    public class ApprovalStepData
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Approval / Confirmation。Confirmation はフローの進行をブロックしない回覧。</summary>
        public string StepType { get; set; } = ApprovalStepType.Approval.ToDesignValue();

        /// <summary>RequiredMembers / All / Any。</summary>
        public string CompletionPolicy { get; set; } = ApprovalCompletionPolicy.RequiredMembers.ToDesignValue();

        /// <summary>却下・差し戻し時にコメントを必須にするか。</summary>
        public bool IsCommentRequiredOnReject { get; set; } = true;

        /// <summary>ApplicantOnly / AnyPreviousStep。</summary>
        public string ReturnScope { get; set; } = ApprovalReturnScope.ApplicantOnly.ToDesignValue();

        public List<ApprovalMemberData> Members { get; set; } = new();

        /// <summary>承認者を追加する (メンバーの解決はスクリプト側の責務 = 実ユーザー Id を渡す)。</summary>
        [ScriptName("AddMember")]
        public ApprovalStepData AddMember(string userId, bool isRequired)
        {
            Members.Add(new ApprovalMemberData { UserId = userId, IsRequired = isRequired });
            return this;
        }

        /// <summary>必須承認者を追加する。</summary>
        [ScriptName("AddMember")]
        public ApprovalStepData AddMember(string userId) => AddMember(userId, true);
    }

    /// <summary>経路上の1承認者 (解決済みの実ユーザー Id)。</summary>
    public class ApprovalMemberData
    {
        public string UserId { get; set; } = string.Empty;
        public bool IsRequired { get; set; } = true;
    }
}
