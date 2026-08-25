using Codeer.LowCode.Blazor.Repository.Design;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// 埋め込みモジュールテンプレート (Example の実機確認済みデザイン) をプロジェクトに合わせて書き換える。
    /// 書き換えは「モジュール名参照 (ModuleName キー)」「ユーザーモジュールの表示名/メールアドレスフィールド」
    /// 「データソース・テーブル名」に限定した決定的な変換で、レイアウト等はテンプレートのまま。
    /// </summary>
    internal static class ModuleTemplateEngine
    {
        /// <summary>テンプレート内のユーザーモジュール名 (Example の既定)。</summary>
        internal const string TemplateUserModule = "AppUser";

        /// <summary>
        /// モジュールテンプレート JSON を書き換える。
        /// </summary>
        /// <param name="templateJson">テンプレート JSON テキスト。</param>
        /// <param name="moduleName">書き換え後のモジュール名。</param>
        /// <param name="dbTable">書き換え後のテーブル名。</param>
        /// <param name="dataSourceName">データソース名。</param>
        /// <param name="moduleNameMap">モジュール名参照の置換表 (テンプレート名 → 生成名。ユーザーモジュール含む)。</param>
        /// <param name="userDisplayNameField">ユーザーモジュールの表示名フィールド (既定 Name)。</param>
        /// <param name="userEmailField">ユーザーモジュールのメールアドレスフィールド (既定 Email)。</param>
        /// <param name="removeTurnNotifyMail">順番到達通知メール (MailField + 契約 TurnNotifyMail) を取り除くか。</param>
        internal static string RewriteModuleJson(string templateJson, string moduleName, string dbTable,
            string dataSourceName, Dictionary<string, string> moduleNameMap,
            string userDisplayNameField = "Name", string userEmailField = "Email",
            bool removeTurnNotifyMail = false)
        {
            var root = JsonNode.Parse(templateJson)!.AsObject();

            root["Name"] = moduleName;
            root["DbTable"] = dbTable;
            root["DataSourceName"] = dataSourceName;

            //ユーザーモジュールの表示名フィールド (リンクの DisplayTextVariable) の書き換えは
            //モジュール名の置換前に行う (テンプレート内の "AppUser" 参照で判定するため)
            if (userDisplayNameField != "Name")
                RewriteUserLinkDisplayText(root, userDisplayNameField);

            RewriteModuleNames(root, moduleNameMap);
            RewriteMailFieldVariables(root, userDisplayNameField, userEmailField);

            if (removeTurnNotifyMail)
                RemoveTurnNotifyMail(root);

            return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// 指定フィールドを候補付き選択から素の文字列フィールドに置き換える (参照先マスタを生成しないとき用)。
        /// </summary>
        internal static string ReplaceFieldWithText(string moduleJson, string fieldName)
        {
            var root = JsonNode.Parse(moduleJson)!.AsObject();
            var fields = root["Fields"]!.AsArray();
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i]!.AsObject();
                if (field["Name"]?.GetValue<string>() != fieldName) continue;
                var replaced = new JsonObject
                {
                    ["Name"] = fieldName,
                    ["DbColumn"] = field["DbColumn"]?.GetValue<string>() ?? string.Empty,
                    ["DisplayName"] = field["DisplayName"]?.GetValue<string>() ?? string.Empty,
                    ["TypeFullName"] = typeof(TextFieldDesign).FullName,
                };
                fields[i] = replaced;
            }
            return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>モジュールスクリプト (.mod.cs) 内のモジュール名 (ModuleSearcher&lt;Xxx&gt; 等) を置換する。</summary>
        internal static string RewriteScript(string script, Dictionary<string, string> moduleNameMap)
        {
            //長い名前から置換する (ApprovalFlow が ApprovalFlowMember を壊さないように)
            foreach (var kv in moduleNameMap.Where(e => e.Key != e.Value).OrderByDescending(e => e.Key.Length))
                script = Regex.Replace(script, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value);
            return script;
        }

        //すべての "ModuleName" キー (SearchCondition / 権限条件) の値を置換表で書き換える
        static void RewriteModuleNames(JsonNode node, Dictionary<string, string> map)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var kv in obj.ToList())
                    {
                        if (kv.Key == "ModuleName" && kv.Value is JsonValue v
                            && v.TryGetValue<string>(out var name) && map.TryGetValue(name, out var replaced))
                        {
                            obj["ModuleName"] = replaced;
                        }
                        else if (kv.Value != null)
                        {
                            RewriteModuleNames(kv.Value, map);
                        }
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item != null) RewriteModuleNames(item, map);
                    }
                    break;
            }
        }

        //ユーザーモジュールを参照するリンクフィールドの DisplayTextVariable "Name.Value" を差し替える
        static void RewriteUserLinkDisplayText(JsonNode node, string displayNameField)
        {
            switch (node)
            {
                case JsonObject obj:
                    var isUserLink =
                        obj["SearchCondition"]?["ModuleName"]?.GetValue<string>() == TemplateUserModule
                        && obj["DisplayTextVariable"]?.GetValue<string>() == "Name.Value";
                    if (isUserLink) obj["DisplayTextVariable"] = $"{displayNameField}.Value";

                    foreach (var kv in obj.ToList())
                    {
                        if (kv.Value != null) RewriteUserLinkDisplayText(kv.Value, displayNameField);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item != null) RewriteUserLinkDisplayText(item, displayNameField);
                    }
                    break;
            }
        }

        //MailField (通知メールテンプレート) の変数のうち、ユーザーモジュールのフィールドを指す
        //".Name.Value" / ".Email.Value" を実フィールド名に差し替える (宛先・差出人・本文の {変数})
        static void RewriteMailFieldVariables(JsonNode node, string displayNameField, string emailField)
        {
            if (displayNameField == "Name" && emailField == "Email") return;

            switch (node)
            {
                case JsonObject obj:
                    var typeFullName = obj["TypeFullName"]?.GetValue<string>() ?? string.Empty;
                    if (typeFullName.EndsWith(".MailFieldDesign", StringComparison.Ordinal))
                    {
                        foreach (var kv in obj.ToList())
                        {
                            if (kv.Value is not JsonValue v || !v.TryGetValue<string>(out var text)) continue;
                            var rewritten = text
                                .Replace(".Name.Value", $".{displayNameField}.Value")
                                .Replace(".Email.Value", $".{emailField}.Value");
                            if (rewritten != text) obj[kv.Key] = rewritten;
                        }
                    }
                    foreach (var kv in obj.ToList())
                    {
                        if (kv.Value != null) RewriteMailFieldVariables(kv.Value, displayNameField, emailField);
                    }
                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        if (item != null) RewriteMailFieldVariables(item, displayNameField, emailField);
                    }
                    break;
            }
        }

        //順番到達通知メールを外す: MailField を Fields から取り除き、メンバー契約の TurnNotifyMail を空にする
        static void RemoveTurnNotifyMail(JsonObject root)
        {
            if (root["Fields"] is not JsonArray fields) return;

            foreach (var field in fields.ToList())
            {
                var typeFullName = field?["TypeFullName"]?.GetValue<string>() ?? string.Empty;
                if (typeFullName.EndsWith(".MailFieldDesign", StringComparison.Ordinal))
                    fields.Remove(field);
                else if (typeFullName.EndsWith(".ApprovalMemberContractFieldDesign", StringComparison.Ordinal)
                    && field!["TurnNotifyMail"] != null)
                    field["TurnNotifyMail"] = string.Empty;
            }
        }
    }
}
