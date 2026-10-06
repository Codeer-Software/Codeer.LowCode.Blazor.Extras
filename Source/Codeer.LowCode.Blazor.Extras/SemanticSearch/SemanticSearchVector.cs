using Codeer.LowCode.Blazor.SystemSettings;
using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// 埋め込みベクトルの保存形式と、DB のベクトル検索での書き方 (方言はここだけが持つ)。
    /// 保存形式は JSON 配列風のテキスト <c>[0.1,-0.2,…]</c>。pgvector (PostgreSQL) / SQL Server 2025 の VECTOR 型がこの文字列からのキャストを受け付けるので、
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

        /// <summary>この DB 種別でベクトル検索 (距離関数) が使えるか。PostgreSQL は pgvector 拡張、SQL Server は 2025 以降が前提 (無ければ実行時エラー)。</summary>
        public static bool SupportsDbSearch(DataSourceType type)
            => type is DataSourceType.PostgreSQL or DataSourceType.SQLServer;

        /// <summary>
        /// テキスト (<see cref="Encode"/> の形式) をベクトル型にする式。text はパラメータ名 (@p1 等) か、<see cref="Encode"/> の結果を引用符で包んだリテラル。
        /// </summary>
        public static string FromText(DataSourceType type, string text, int dimensions) => type switch
        {
            DataSourceType.PostgreSQL => $"{text}::vector",
            DataSourceType.SQLServer => $"CAST({text} AS VECTOR({dimensions}))",
            _ => throw NotSupported(type),
        };

        /// <summary>ベクトルを SQL に埋め込むリテラル (数値だけを並べるので注入の余地はない)。AI が書く SQL の {embed:…} の置換に使う。</summary>
        public static string Literal(DataSourceType type, float[] vector)
            => FromText(type, $"'{Encode(vector)}'", vector.Length);

        /// <summary>コサイン距離 (0 = 同じ向き〜2) の式。vector は <see cref="FromText"/> / <see cref="Literal"/> の式。</summary>
        public static string CosineDistance(DataSourceType type, string column, string vector) => type switch
        {
            DataSourceType.PostgreSQL => $"({column} <=> {vector})",
            DataSourceType.SQLServer => $"VECTOR_DISTANCE('cosine', {column}, {vector})",
            _ => throw NotSupported(type),
        };

        static NotSupportedException NotSupported(DataSourceType type)
            => new($"SemanticSearch: {type} does not support vector search in the database.");
    }
}
