using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// 埋め込みベクトルの保存形式。
    /// JSON 配列風のテキスト <c>[0.1,-0.2,…]</c>。pgvector (PostgreSQL) / SQL Server 2025 の VECTOR 型がこの文字列からのキャストを受け付けるので、
    /// DB 側のベクトル検索に生成列や直接のキャストでそのまま使える。類似度の計算は DB が行う (サーバーでは計算しない)。
    /// </summary>
    internal static class SemanticSearchVector
    {
        public static string Encode(ReadOnlySpan<float> vector)
        {
            var sb = new StringBuilder(vector.Length * 10 + 2);
            sb.Append('[');
            for (var i = 0; i < vector.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }
    }
}
