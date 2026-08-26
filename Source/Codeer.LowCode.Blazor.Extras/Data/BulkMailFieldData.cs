using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>BulkMailField の送信結果サマリ (BulkMailSummaryEntry の JSON 配列、新しい順)。</summary>
    public class BulkMailFieldData() : ValueFieldDataBase<string>(typeof(BulkMailFieldData).FullName!), ICloneable<BulkMailFieldData>
    {
        public BulkMailFieldData Clone() => (BulkMailFieldData)MemberwiseClone();
    }
}
