using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>
    /// Low level mail template engine shared by the client (preview) and the server (bulk sending).
    /// Replaces {Name} tokens with variable values. Use {{ and }} for literal braces.
    /// Unknown variables resolve to an empty string, and unclosed braces are kept as-is.
    /// </summary>
    internal static class MailTemplateEngine
    {
        /// <summary>Extracts the distinct {Name} variable names used in the template (literal {{ }} excluded).</summary>
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
