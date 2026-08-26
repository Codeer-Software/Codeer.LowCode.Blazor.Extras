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

        /// <summary>件名・本文テンプレートで使われている変数名の一覧 (重複なし)。</summary>
        public static List<string> GetVariableNames(string? subject, string? body)
            => GetVariableNames(subject).Concat(GetVariableNames(body)).Distinct().ToList();

        public static string Fill(string? template, IReadOnlyDictionary<string, string> variables)
            => FillWithSpans(template, variables).Text;

        /// <summary>
        /// Fill と同じ差し込みを行い、変数が入った区間 (解決後テキスト上の位置) も返す。
        /// プレビューで「どこに何が入ったか」をハイライトするために使う。
        /// </summary>
        public static (string Text, List<MailTemplateSpan> Spans) FillWithSpans(string? template, IReadOnlyDictionary<string, string> variables)
        {
            var spans = new List<MailTemplateSpan>();
            if (string.IsNullOrEmpty(template)) return (string.Empty, spans);

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
                    var resolved = variables.TryGetValue(name, out var value) ? value : string.Empty;
                    spans.Add(new MailTemplateSpan { Start = sb.Length, Length = resolved.Length, Name = name });
                    sb.Append(resolved);
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
            return (sb.ToString(), spans);
        }
    }
}
