using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// SemanticSearchField の保存データ。<see cref="Text"/> は行を文章にしたもの (クライアントが Submit 時に組み立てる)、
    /// <see cref="Vector"/> はその埋め込みベクトル (サーバーの SemanticSearchIndexer が付ける。float32 の並びを base64 にしたもの。未計算なら null)。
    /// どちらも書き込み専用列なので通常の読み込みでは来ない。
    /// </summary>
    public class SemanticSearchFieldData : FieldDataBase
    {
        public SemanticSearchFieldData() : base(typeof(SemanticSearchFieldData).FullName!) { }
        public string? Text { get; set; }
        public string? Vector { get; set; }

        public override bool Equals(object? obj)
        {
            var r = obj as SemanticSearchFieldData;
            if (r == null) return false;
            return Text == r.Text && Vector == r.Vector;
        }

        public override int GetHashCode() => (Text, Vector).GetHashCode();
        public SemanticSearchFieldData Clone() => (SemanticSearchFieldData)MemberwiseClone();
    }
}
