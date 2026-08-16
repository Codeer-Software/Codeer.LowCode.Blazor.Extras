using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// メールテンプレートエンジンの低レイヤ (クライアントのプレビューとサーバーの一斉送信で共有)。
    /// {変数} を変数値へ差し込む。リテラルの中括弧は {{ }}。
    /// 未知の変数は空文字になり、閉じていない中括弧はそのまま残る。
    /// </summary>
    internal static class MailTemplateEngine
    {
        /// <summary>テンプレートで使われている {変数} 名の一覧を返す (重複なし。リテラルの {{ }} は除く)。</summary>
        public static List<string> GetVariableNames(string? template)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(template)) return names;

            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{')
                    {
                        i++;
                        continue;
                    }
                    var end = template.IndexOf('}', i + 1);
                    if (end < 0) continue;
                    var name = template.Substring(i + 1, end - i - 1);
                    if (name.Contains('{')) continue;
                    if (name.Length != 0 && !names.Contains(name)) names.Add(name);
                    i = end;
                }
                else if (c == '}')
                {
                    if (i + 1 < template.Length && template[i + 1] == '}') i++;
                }
            }
            return names;
        }

        public static string Fill(string? template, IReadOnlyDictionary<string, string> variables)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            var sb = new StringBuilder(template.Length);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{')
                {
                    //literal "{{"
                    if (i + 1 < template.Length && template[i + 1] == '{')
                    {
                        sb.Append('{');
                        i++;
                        continue;
                    }
                    var end = template.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        sb.Append(c);
                        continue;
                    }
                    var name = template.Substring(i + 1, end - i - 1);
                    if (name.Contains('{'))
                    {
                        //"{a{b}" - not a token, keep the brace and rescan the rest
                        sb.Append(c);
                        continue;
                    }
                    sb.Append(variables.TryGetValue(name, out var value) ? value : string.Empty);
                    i = end;
                }
                else if (c == '}')
                {
                    //literal "}}"
                    if (i + 1 < template.Length && template[i + 1] == '}')
                    {
                        sb.Append('}');
                        i++;
                        continue;
                    }
                    sb.Append(c);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
