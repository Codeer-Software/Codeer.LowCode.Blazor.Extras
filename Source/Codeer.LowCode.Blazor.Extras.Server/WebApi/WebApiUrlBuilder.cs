using Codeer.LowCode.Blazor.Extras.Server.Properties;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// BaseUrl + Path から送り先の URL を組む。{名前} は引数 (JSON オブジェクトの最上位。オブジェクトでない引数には値が無い) の同じ名前の値 (先頭 1 文字の大文字小文字は区別しない。2 文字目以降は区別する。
    /// 両方の綴りのキーがあれば完全に一致するほう) をエスケープして差し込む。
    /// 組んだ URL が BaseUrl の配下 (同じ scheme・host・port で、パスが BaseUrl のパスで始まり、復号したパスに . / .. の区間が無い) でなければ例外 (送らない)。
    /// </summary>
    static class WebApiUrlBuilder
    {
        internal static Uri Build(string baseUrl, string path, JsonElement arguments, bool appendQuery)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(baseUri.Query)
                || !string.IsNullOrEmpty(baseUri.UserInfo))
            {
                throw new WebApiCallException(Resources.WebApi_InvalidBaseUrl);
            }
            var basePath = baseUri.AbsolutePath.EndsWith('/') ? baseUri.AbsolutePath : baseUri.AbsolutePath + "/";

            var used = new HashSet<string>(StringComparer.Ordinal);
            var relative = Substitute(path, arguments, used).TrimStart('/');
            if (!Uri.TryCreate(baseUri.GetLeftPart(UriPartial.Authority) + basePath + relative, UriKind.Absolute, out var uri) || !IsUnder(uri, baseUri, basePath))
            {
                throw new WebApiCallException(Resources.WebApi_OutsideBaseUrl);
            }
            //外側がオブジェクトでない引数 (配列・値だけ) からはクエリにする値を取り出さない
            if (!appendQuery || arguments.ValueKind != JsonValueKind.Object) return uri;

            var query = new StringBuilder(uri.Query);
            foreach (var e in arguments.EnumerateObject())
            {
                if (used.Contains(e.Name) || !TryGetScalar(e.Value, out var value)) continue;
                query.Append(query.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(e.Name)).Append('=').Append(Uri.EscapeDataString(value));
            }
            return new Uri(uri.GetLeftPart(UriPartial.Path) + query);
        }

        static bool IsUnder(Uri uri, Uri baseUri, string basePath)
        {
            if (uri.Scheme != baseUri.Scheme || !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != baseUri.Port) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (!uri.AbsolutePath.StartsWith(basePath, StringComparison.Ordinal)) return false;
            //相手のサーバーが %2F・%5C・%2E を復号してから正規化しても上の階層に出ないよう、パス全体を復号しきってから / と \ で区切り、. / .. の区間があれば外とみなす
            //(差し込んだ値の中の / は 1 区間のままエスケープされているので、先に区切ると ..%2F.. のような区間を見逃す)
            var decoded = uri.AbsolutePath;
            for (var i = 0; ; i++)
            {
                var next = Uri.UnescapeDataString(decoded);
                if (next == decoded) break;
                //上限を超えて復号で変わり続けるものは判定しきれないので外とみなす
                if (i == MaxUnescapeCount) return false;
                decoded = next;
            }
            return decoded.Split('/', '\\').All(e => e != "." && e != "..");
        }

        //復号を繰り返す上限
        const int MaxUnescapeCount = 10;

        //{名前} を値で置き換える。書き方の規則は WebApiFieldDesign の設計チェックと同じ
        static string Substitute(string path, JsonElement arguments, HashSet<string> used)
        {
            var result = new StringBuilder();
            var i = 0;
            while (i < path.Length)
            {
                var c = path[i];
                if (c == '}') throw new WebApiCallException(Resources.WebApi_InvalidPath);
                if (c != '{')
                {
                    result.Append(c);
                    i++;
                    continue;
                }
                var close = path.IndexOf('}', i + 1);
                if (close < 0) throw new WebApiCallException(Resources.WebApi_InvalidPath);
                var name = path.Substring(i + 1, close - i - 1);
                if (string.IsNullOrWhiteSpace(name) || name.Contains('{')) throw new WebApiCallException(Resources.WebApi_InvalidPath);
                var key = FindKey(arguments, name);
                var value = key == null ? null : FindValue(arguments, key);
                //空・. ・.. はパスの区間の意味を変えるので値として認めない
                if (string.IsNullOrEmpty(value) || value == "." || value == "..") throw new WebApiCallException(string.Format(Resources.WebApi_PathValueMissing, name));
                result.Append(Uri.EscapeDataString(value));
                used.Add(key!);
                i = close + 1;
            }
            return result.ToString();
        }

        //{名前} に合う引数のキー。完全一致があればそれ、無ければ先頭 1 文字の大文字小文字だけが違うキー (2 文字目以降は区別する)。引数がオブジェクトでなければ無し
        static string? FindKey(JsonElement arguments, string name)
        {
            if (arguments.ValueKind != JsonValueKind.Object) return null;
            string? candidate = null;
            foreach (var e in arguments.EnumerateObject())
            {
                if (e.Name == name) return name;
                if (candidate == null && IsSameIgnoringFirstCase(e.Name, name)) candidate = e.Name;
            }
            return candidate;
        }

        static bool IsSameIgnoringFirstCase(string a, string b)
            => a.Length == b.Length && a.Length > 0
                && char.ToLowerInvariant(a[0]) == char.ToLowerInvariant(b[0])
                && string.CompareOrdinal(a, 1, b, 1, a.Length - 1) == 0;

        static string? FindValue(JsonElement arguments, string name)
        {
            foreach (var e in arguments.EnumerateObject())
            {
                if (e.Name == name) return TryGetScalar(e.Value, out var value) ? value : null;
            }
            return null;
        }

        //文字列・数値・真偽値だけ (null・オブジェクト・配列は値として扱わない)
        static bool TryGetScalar(JsonElement element, out string value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    value = element.GetString() ?? string.Empty;
                    return true;
                case JsonValueKind.Number:
                    value = element.GetRawText();
                    return true;
                case JsonValueKind.True:
                    value = "true";
                    return true;
                case JsonValueKind.False:
                    value = "false";
                    return true;
                default:
                    value = string.Empty;
                    return false;
            }
        }
    }
}
