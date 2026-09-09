using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge
{
    /// <summary>
    /// デザイン定義 (DesignData) を AI が読める文章に要約する。JSON を丸ごと渡すと大きすぎるので、
    /// 業務の意味に効くものだけを抜く: モジュール名・表・データソース、フィールドの表示名と型と DB 列、候補値 (コード → 名称)、
    /// リンク (結合の相手とキー)、システム項目 (Id / 論理削除)、行の閲覧条件の有無、スクリプト。
    /// QueryField の SQL はデザインプロジェクトの .sql ファイルにあり実行エンジン専用のバッファに読まれるだけなので出さない (AI はスキーマと定義から自分で SQL を書く)。
    /// </summary>
    internal static class DesignDescriber
    {
        /// <summary>モジュール一覧 (小さい。プロンプトに常時入れられる量を目指す)。</summary>
        public static string ListModules(DesignData design)
        {
            var sb = new StringBuilder();
            sb.AppendLine("モジュール (アプリの画面 / データの単位) の一覧。詳細は describe_module で取る。");
            foreach (var name in design.Modules.GetModuleNames())
            {
                var module = design.Modules.Find(name);
                if (module == null) continue;
                sb.Append("- ").Append(module.Name);
                AppendTitle(sb, design, module);
                if (!string.IsNullOrEmpty(module.DbTable))
                    sb.Append(": 表 ").Append(module.DbTable).Append(string.IsNullOrEmpty(module.DataSourceName) ? "" : " @ " + module.DataSourceName);
                else
                    sb.Append(": 表なし (画面や集計のみ)");
                var links = module.Fields.OfType<LinkFieldDesign>().Select(l => l.SearchCondition.ModuleName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                if (links.Count > 0) sb.Append("; リンク先: ").Append(string.Join(", ", links));
                var urls = PageUrls(design, module);
                if (urls != null) sb.Append("; URL ").Append(urls.Value.List);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// モジュールのページ定義 (そのモジュールを表示するページフレーム上の設定)。サイドバーのリンク → その他のページ → トップページの順で探す。無ければ null。
        /// 画面の呼び名 (タイトル) や URL セグメントはモジュール定義ではなくここにある。
        /// </summary>
        public static (PageFrameDesign Frame, ModulePageDesign Page)? FindModulePage(DesignData design, ModuleDesign module)
        {
            foreach (var name in design.PageFrames.GetPageFrameNames())
            {
                var frame = design.PageFrames.Find(name);
                if (frame == null) continue;
                var page = frame.Left.Links.Concat(frame.Right.Links).FirstOrDefault(l => l.Module == module.Name)
                           ?? frame.OtherPageModuleDesigns.FirstOrDefault(p => p.Module == module.Name)
                           ?? (frame.TopPageModuleDesign?.Module == module.Name ? frame.TopPageModuleDesign : null);
                if (page != null) return (frame, page);
            }
            return null;
        }

        /// <summary>
        /// モジュールの画面上の呼び名。サイドバーのリンクの表示名 → 一覧ページのタイトル → 詳細ページのタイトルの順。どれも無ければ null (モジュール名だけで呼ぶ)。
        /// </summary>
        public static string? Title(DesignData design, ModuleDesign module)
        {
            var found = FindModulePage(design, module);
            if (found == null) return null;
            var page = found.Value.Page;
            var title = (page as PageLink)?.Title;
            if (string.IsNullOrEmpty(title)) title = page.ListPageDesign.PageTitle;
            if (string.IsNullOrEmpty(title)) title = page.DetailPageDesign.PageTitle;
            return string.IsNullOrEmpty(title) || title == module.Name ? null : title;
        }

        static void AppendTitle(StringBuilder sb, DesignData design, ModuleDesign module)
        {
            var title = Title(design, module);
            if (title != null) sb.Append(" (").Append(title).Append(')');
        }

        /// <summary>
        /// モジュールの画面 URL (一覧と詳細の雛形)。CLB の URL は /{ページフレーム}/{モジュールの URL セグメント}[/{Id}]。
        /// ページフレームにそのモジュールのページがあればそのフレームとセグメントを使い、無ければアプリのルートのフレーム (無ければ最初のフレーム) の下で組む。
        /// フレームが 1 つも無ければ null。
        /// </summary>
        public static (string List, string Detail)? PageUrls(DesignData design, ModuleDesign module)
        {
            string? frameName = null;
            var segment = module.Name;
            var found = FindModulePage(design, module);
            if (found != null)
            {
                var (frame, page) = found.Value;
                frameName = string.IsNullOrEmpty(page.PageFrame) ? frame.Name : page.PageFrame;
                if (!string.IsNullOrEmpty(page.ModuleUrlSegment)) segment = page.ModuleUrlSegment;
            }
            else
            {
                var names = design.PageFrames.GetPageFrameNames();
                frameName = names.FirstOrDefault(n => design.PageFrames.Find(n)?.IsApplicationRoot == true) ?? names.FirstOrDefault();
                if (string.IsNullOrEmpty(frameName)) return null;
            }
            var baseUrl = "/" + frameName + "/" + segment;
            return (baseUrl, baseUrl + "/{Id}");
        }

        /// <summary>1 モジュールの詳細。見つからなければ null。</summary>
        public static string? DescribeModule(DesignData design, string moduleName, int maxScriptChars)
        {
            var module = design.Modules.Find(moduleName);
            if (module == null)
            {
                //大文字小文字・画面の呼び名での寄せ
                var alt = design.Modules.GetModuleNames().FirstOrDefault(n => string.Equals(n, moduleName, StringComparison.OrdinalIgnoreCase))
                          ?? design.Modules.GetModuleNames().FirstOrDefault(n => design.Modules.Find(n) is { } m && Title(design, m) == moduleName);
                if (alt == null) return null;
                module = design.Modules.Find(alt)!;
            }

            var sb = new StringBuilder();
            sb.Append("# モジュール ").Append(module.Name);
            AppendTitle(sb, design, module);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(module.DbTable))
                sb.Append("表: ").Append(module.DbTable).Append(string.IsNullOrEmpty(module.DataSourceName) ? "" : " (データソース " + module.DataSourceName + ")").AppendLine();
            else
                sb.AppendLine("表: なし (DB に保存しないモジュール)");

            var logicalDelete = module.Fields.FirstOrDefault(f => f.Name == SystemFieldNames.LogicalDelete) as DbValueFieldDesignBase;
            if (logicalDelete != null && !string.IsNullOrEmpty(logicalDelete.DbColumn))
                sb.Append("論理削除: 列 ").Append(logicalDelete.DbColumn).AppendLine(" が真の行は削除済みとして扱う (通常は集計から除く)");
            if (module.DataReadCondition?.Condition != null)
                sb.AppendLine("行の閲覧条件: あり (画面ではユーザーに応じて行が絞られる。SQL では絞られないので注意)");
            var urls = PageUrls(design, module);
            if (urls != null)
            {
                var idColumn = (module.Fields.FirstOrDefault(f => f.Name == SystemFieldNames.Id) as DbValueFieldDesignBase)?.DbColumn;
                sb.Append("画面 URL: 一覧 ").Append(urls.Value.List).Append(" / 詳細 ").Append(urls.Value.Detail);
                if (!string.IsNullOrEmpty(idColumn)) sb.Append(" ({Id} は列 ").Append(idColumn).Append(" の値)");
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine("## フィールド (名前 [表示名] 型, DB 列, 補足)");
            foreach (var field in module.Fields)
            {
                var type = field.GetType().Name.Replace("FieldDesign", "");
                var column = (field as DbValueFieldDesignBase)?.DbColumn;
                var display = (field as ValueFieldDesignBase)?.DisplayName;
                sb.Append("- ").Append(field.Name);
                if (!string.IsNullOrEmpty(display) && display != field.Name) sb.Append(" [").Append(display).Append(']');
                sb.Append(' ').Append(type);
                if (!string.IsNullOrEmpty(column)) sb.Append(", 列 ").Append(column);
                if (field is ValueFieldDesignBase { IsRequired: true }) sb.Append(", 必須");
                var candidates = Candidates(design, field);
                if (candidates.Count > 0) sb.Append(", 候補値: ").Append(string.Join(", ", candidates));
                if (field is LinkFieldDesign link && !string.IsNullOrEmpty(link.SearchCondition.ModuleName))
                {
                    sb.Append(", リンク → ").Append(link.SearchCondition.ModuleName).Append('.').Append(string.IsNullOrEmpty(link.ValueVariable) ? SystemFieldNames.Id : link.ValueVariable);
                    if (!string.IsNullOrEmpty(link.DisplayTextVariable)) sb.Append(" (表示は ").Append(link.DisplayTextVariable).Append(')');
                }
                sb.AppendLine();
            }

            if (design.Scripts.TryGetValue(module.Name, out var script) && !string.IsNullOrWhiteSpace(script))
            {
                sb.AppendLine();
                sb.AppendLine("## スクリプト (画面のロジック。計算や状態の決まりが書いてある)");
                sb.AppendLine("```csharp");
                sb.AppendLine(script.Length > maxScriptChars ? script[..maxScriptChars] + "\n// … (以下省略)" : script.TrimEnd());
                sb.AppendLine("```");
            }
            return sb.ToString();
        }

        /// <summary>候補値 (コード=名称) の一覧。SelectField の Candidates (表示,値) か、参照する Enum の Members。</summary>
        public static List<string> Candidates(DesignData? design, FieldDesignBase field)
        {
            var result = new List<string>();
            string enumName = string.Empty;
            List<string>? candidates = null;
            if (field is not SelectFieldDesign select) return result;
            enumName = select.EnumName;
            candidates = select.Candidates;
            if (!string.IsNullOrEmpty(enumName) && design != null)
            {
                var e = design.Enums.FirstOrDefault(x => x.Name == enumName);
                if (e != null)
                {
                    foreach (var m in e.Members) result.Add($"{m.Value}={(string.IsNullOrEmpty(m.DisplayText) ? m.Name : m.DisplayText)}");
                    return result;
                }
            }
            if (candidates != null)
            {
                //Candidates は "表示,値" の形 (値が無ければ表示 = 値)
                foreach (var c in candidates)
                {
                    var parts = c.Split(',', 2);
                    result.Add(parts.Length == 2 ? $"{parts[1].Trim()}={parts[0].Trim()}" : c.Trim());
                }
            }
            return result;
        }
    }
}
