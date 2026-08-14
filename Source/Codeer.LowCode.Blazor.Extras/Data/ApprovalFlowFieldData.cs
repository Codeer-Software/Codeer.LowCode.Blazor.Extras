using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// ApprovalFlowField のデータ = 承認フロー行への FK。
    /// FK はサーバー (command API) だけが書く。クライアントは読み取りのみ。
    /// </summary>
    public class ApprovalFlowFieldData : FieldDataBase, ICloneable<ApprovalFlowFieldData>
    {
        public ApprovalFlowFieldData() : base(typeof(ApprovalFlowFieldData).FullName!) { }

        /// <summary>承認フロー行の Id (未申請は null。空文字にすると 1:N バインド条件が null 検索にならない)。</summary>
        public string? Id { get; set; }

        /// <summary>
        /// フロー状態のコピー (未申請は null)。エンジンが遷移のたびに書き戻す。
        /// 条件式で "Approval.State == null(未申請)" 等を自列として書くための非正規化列
        /// (デザインの StateDbColumn を設定したときだけ使われる)。
        /// </summary>
        public string? State { get; set; }

        /// <summary>申請者ユーザー Id のコピー (ApplicantDbColumn 設定時のみ)。</summary>
        public string? Applicant { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj is not ApprovalFlowFieldData r) return false;
            return Id == r.Id && State == r.State && Applicant == r.Applicant;
        }

        public override int GetHashCode() => (Id ?? string.Empty).GetHashCode();

        public ApprovalFlowFieldData Clone() => (ApprovalFlowFieldData)MemberwiseClone();
    }
}
