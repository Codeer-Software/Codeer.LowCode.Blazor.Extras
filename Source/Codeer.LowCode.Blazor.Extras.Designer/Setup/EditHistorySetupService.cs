using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// 編集履歴のセットアップ。履歴モジュール (EditHistoryContractField・復活ボタン・対象レコードリンク・検索レイアウト同梱、
    /// 誰も書けない保護条件) と対象モジュール enum をテンプレート (Example の EditHistory) から生成する。それだけ。
    /// 対象モジュール側 (EditHistoryField の配置) はユーザーがデザイナで行う。
    /// - 冪等: 同名モジュール・enum が既に存在すれば生成しない (履歴モジュールは全モジュールで 1 つ共有してよい)。
    /// - 生成後は通常のモジュール (フィールド追加・画面カスタム・リネームすべて自由。契約フィールドが正)。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)。
    /// </summary>
    public static class EditHistorySetupService
    {
        const string TemplateModuleName = "EditHistory";

        /// <summary>対象モジュール enum の名前 (履歴一覧の「モジュール」列の読み替え。メンバー名 = 対象モジュール名 / 表示 = 画面上の名前)。</summary>
        internal const string TargetModuleEnumName = "EditHistoryTargetModule";

        public static SetupResult Run(DesignData designData, string designDir, EditHistorySetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();
            var moduleName = options.HistoryModuleName;

            if (options.CreateTargetModuleEnum) EnsureTargetModuleEnum(designData, designDir);

            if (ModuleExists(designData, designDir, moduleName))
            {
                result.SkippedModules.Add(moduleName);
            }
            else
            {
                var nameMap = new Dictionary<string, string> { [TemplateModuleName] = moduleName };
                if (options.UserModuleName != ModuleTemplateEngine.TemplateUserModule)
                    nameMap[ModuleTemplateEngine.TemplateUserModule] = options.UserModuleName;

                var json = ModuleTemplateEngine.RewriteModuleJson(
                    SetupTemplates.Load($"{TemplateModuleName}.mod.json"),
                    moduleName, MailHistoryModuleFactory.Pluralize(MailHistoryModuleFactory.ToSnakeCase(moduleName)),
                    options.DataSourceName, nameMap, options.UserDisplayNameField);
                //ModuleName (対象モジュール名) の Select: enum を作るならそれを参照、作らないなら enum 無し (素の名前で運用)
                json = ModuleTemplateEngine.SetSelectEnum(json, "ModuleName", options.CreateTargetModuleEnum ? TargetModuleEnumName : string.Empty);

                //型付きで読み直して正規化する (プロパティ名・型の崩れをここで検出し、デザイナ保存と同じ形で書き出す)
                var module = JsonConverterEx.DeserializeObject<ModuleDesign>(json)
                    ?? throw new InvalidOperationException($"Broken template: {TemplateModuleName}");
                ApprovalFlowSetupService.SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.json", JsonConverterEx.SerializeObject(module));
                result.CreatedModules.Add(moduleName);
                result.Ddl.AddRange(module.CreateDDL(dataSourceType, existingTables));

                if (options.AddPageFrameLink)
                {
                    ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir,
                        new List<(string, string, Action<PageLink>?)> { ("編集履歴", moduleName, ConfigureHistoryList) }, result);
                }
            }

            result.Notes.Add(CreateNextStepsNote(options));
            return result;
        }

        static bool ModuleExists(DesignData designData, string designDir, string moduleName)
            => designData.Modules.Find(moduleName) != null
                || File.Exists(Path.Combine(designDir, "Modules", $"{moduleName}.mod.json"));

        //履歴一覧のページ: 新規作成・削除なし (履歴はシステムの記録)。行の「>」で履歴の詳細 (版の内容・復活ボタン) へ
        static void ConfigureHistoryList(PageLink link)
        {
            link.ListPageDesign.UseNavigateToCreate = false;
            if (link.ListPageDesign.ListFieldDesign is not ListFieldDesign list) return;
            list.CanNavigateToDetail = true;
            list.CanCreate = false;
            list.CanUpdate = false;
            list.CanDelete = false;
            list.SearchCondition.SortConditions = [new SortCondition { Variable = "Id.Value", IsDescending = true }];
        }

        //対象モジュール enum を生成する (既存なら触らない)。メンバーはユーザーが対象モジュールごとに足す (名前 = モジュール名)
        static void EnsureTargetModuleEnum(DesignData designData, string designDir)
        {
            var path = Path.Combine(designDir, "Enums", $"{TargetModuleEnumName}.enum.json");
            if (designData.Enums.Any(e => e.Name == TargetModuleEnumName) || File.Exists(path)) return;

            var enumDesign = new EnumDesign { Name = TargetModuleEnumName, ValueType = EnumValueType.String };
            designData.Enums.Add(enumDesign);
            ApprovalFlowSetupService.SaveDesignFile(designDir, "Enums", $"{TargetModuleEnumName}.enum.json", JsonConverterEx.SerializeObject(enumDesign));
        }

        //対象モジュール側の手順 (セットアップはここまで。以降はデザイナで対象モジュールに手を入れる)
        static string CreateNextStepsNote(EditHistorySetupOptions options)
        {
            var enumStep = options.CreateTargetModuleEnum
                ? $"""
                  3. 対象モジュール enum {TargetModuleEnumName} にメンバー (名前 = 対象モジュール名 / 表示 = 画面上の名前) を追加する
                     (履歴一覧の「モジュール」列の表示名と検索候補になる。無いモジュールはデザインチェックが指摘する)

                  """
                : string.Empty;
            return $"""
                編集履歴モジュールを生成しました。履歴を取るモジュール側は次の手順で仕上げてください:
                1. 履歴を取りたいモジュールに EditHistoryField を置く (履歴モジュール = {options.HistoryModuleName})。詳細レイアウトの右カラムやタブに配置する
                2. サーバーの CustomizedModuleDataIO に EditHistoryRecorder を結線する (アプリテンプレートは結線済み。docs/EditHistory.md「サーバーの結線」)
                {enumStep}履歴は誰でも読める状態で生成されます (Snapshot に全項目の値が入る)。閲覧を絞るには {options.HistoryModuleName} の UserReadCondition を設定してください。
                削除したレコードは履歴の詳細の「このレコードを復活」で戻せます (Id を保って戻すには対象モジュールを論理削除にする)。
                """;
        }
    }
}
