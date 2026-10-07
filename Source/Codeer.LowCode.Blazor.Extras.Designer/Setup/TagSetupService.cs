using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// タグのセットアップ。タグを付けるモジュールに、タグ付けモジュール (TagLinkContractField。1 行 = レコードに付いたタグ 1 つ = OwnerId + タグ名) を作り、
    /// タグを付けるモジュールに TagField を足す (同名の TagField が結び付きなしであれば結び付ける)。
    /// 画面への配置はデザイナで行う。タグのマスタは作らない。
    /// - 冪等: 同名のモジュールがあれば作らない。TagField が結び付き済みなら触らない。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む): テーブル (タグ名は NOT NULL・長さ = TagField.MaxTagLength)、
    ///   (レコード, タグ名) の一意インデックス、タグ名のインデックス、レコードへの外部キー (SQL で直接消したときの保険。SQLite は CREATE TABLE の列に書く)。
    /// </summary>
    public static class TagSetupService
    {
        /// <summary>タグ名の列の長さ (入力欄・タグ付けモジュールの Name の MaxLength と同じ値)。</summary>
        internal const int NameLength = TagField.MaxTagLength;

        public static SetupResult Run(DesignData designData, string designDir, TagSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var target = designData.Modules.Find(options.TargetModuleName)
                ?? throw new InvalidOperationException($"Module not found: {options.TargetModuleName}");
            if (string.IsNullOrEmpty(target.DbTable))
                throw new InvalidOperationException($"Module '{target.Name}' has no table. Tags can be saved only on a module with a table.");
            var targetId = target.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id)
                ?? throw new InvalidOperationException($"Module '{target.Name}' has no Id field.");
            options = Normalize(options);

            var result = new SetupResult();

            //タグ付け
            if (designData.Modules.Find(options.LinkModuleName) != null || ModuleFileExists(designDir, options.LinkModuleName))
            {
                result.SkippedModules.Add(options.LinkModuleName);
            }
            else
            {
                var link = TagModuleFactory.CreateLink(options.LinkModuleName, options.LinkTableName, options.OwnerColumnName, options.DataSourceName);
                Save(designDir, link);
                ((IEditableModuleDesign)designData.Modules).Add(link);
                result.CreatedModules.Add(link.Name);
                var ddl = link.CreateDDL(dataSourceType, existingTables);
                if (ddl.Any(e => e.StartsWith("CREATE TABLE")))
                {
                    ddl = FixLinkColumns(ddl, options.OwnerColumnName, TagModuleFactory.NameColumn, target.DbTable, targetId.DbColumn, dataSourceType);
                    ddl.AddRange(LinkIndexDdl(link.DbTable, options.OwnerColumnName, TagModuleFactory.NameColumn, target.DbTable, targetId.DbColumn, dataSourceType));
                }
                result.Ddl.AddRange(ddl);
            }

            //タグを付けるモジュールの TagField
            if (!string.IsNullOrEmpty(options.FieldName)) PlaceField(designDir, target, options, result);

            result.Notes.Add(CreateNextStepsNote(options));
            return result;
        }

        /// <summary>空の名前を対象モジュールから決める。</summary>
        public static TagSetupOptions Normalize(TagSetupOptions options)
        {
            var linkModule = string.IsNullOrEmpty(options.LinkModuleName) ? options.TargetModuleName + "Tags" : options.LinkModuleName;
            return new TagSetupOptions
            {
                TargetModuleName = options.TargetModuleName,
                FieldName = options.FieldName,
                LinkModuleName = linkModule,
                LinkTableName = string.IsNullOrEmpty(options.LinkTableName) ? TableName(linkModule) : options.LinkTableName,
                OwnerColumnName = string.IsNullOrEmpty(options.OwnerColumnName) ? "owner_id" : options.OwnerColumnName,
                DataSourceName = options.DataSourceName,
            };
        }

        //既に複数形の名前 (RequestTags) はそのまま
        static string TableName(string moduleName)
        {
            var snake = MailHistoryModuleFactory.ToSnakeCase(moduleName);
            return snake.EndsWith('s') ? snake : MailHistoryModuleFactory.Pluralize(snake);
        }

        /// <summary>
        /// 本体の DDL (CREATE TABLE。1 要素 1 行) の列を直す: OwnerId は Id の型 (本体は IdField の列を Id と同じ型で作る) で NOT NULL、
        /// タグ名は長さ NameLength で NOT NULL (インデックスを張れる長さ。SQLite は長さを持たないので型のまま)。
        /// タグの同一判定は完全一致 (大文字小文字を区別する) なので、既定の照合順序が区別しない SQL Server / MySQL には区別する照合順序を付ける
        /// (PostgreSQL / SQLite / Oracle は既定で区別する)。候補の Like も同じ照合順序で区別する (仕様どおり)。
        /// SQLite は後から外部キーを足せないので、OwnerId の列に外部キーを書く。
        /// </summary>
        internal static List<string> FixLinkColumns(List<string> ddl, string ownerColumn, string nameColumn,
            string ownerTable, string ownerIdColumn, DataSourceType type)
        {
            var ownerType = ColumnType(ddl, ownerColumn);
            var nameType = type switch
            {
                //_CS_AS_KS_WS: 大文字小文字・アクセント・かな・幅を全部区別する (= 完全一致)
                DataSourceType.SQLServer => $"NVARCHAR({NameLength}) COLLATE Latin1_General_CS_AS_KS_WS",
                DataSourceType.MySQL => $"VARCHAR({NameLength}) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin",
                DataSourceType.PostgreSQL => $"VARCHAR({NameLength})",
                DataSourceType.Oracle => $"VARCHAR2({NameLength})",
                _ => ColumnType(ddl, nameColumn) ?? "TEXT",
            };
            var ownerDefinition = ownerType == null ? null
                : type == DataSourceType.SQLite ? $"{ownerType} NOT NULL REFERENCES {ownerTable} ({ownerIdColumn}) ON DELETE CASCADE"
                : $"{ownerType} NOT NULL";
            var nameDefinition = $"{nameType} NOT NULL";
            return ddl.Select(line =>
            {
                var trimmed = line.TrimStart();
                var indent = line[..(line.Length - trimmed.Length)];
                var comma = trimmed.EndsWith(',') ? "," : string.Empty;
                if (ownerDefinition != null && trimmed.StartsWith(ownerColumn + " ", StringComparison.Ordinal)) return $"{indent}{ownerColumn} {ownerDefinition}{comma}";
                if (trimmed.StartsWith(nameColumn + " ", StringComparison.Ordinal)) return $"{indent}{nameColumn} {nameDefinition}{comma}";
                return line;
            }).ToList();
        }

        //CREATE TABLE の列の型 (NOT NULL などを除く)
        static string? ColumnType(List<string> ddl, string column)
        {
            var line = ddl.FirstOrDefault(e => e.TrimStart().StartsWith(column + " ", StringComparison.Ordinal));
            return line?.Trim().TrimEnd(',')[(column.Length + 1)..].Replace(" NOT NULL", string.Empty).Replace(" NULL", string.Empty).Trim();
        }

        /// <summary>
        /// 同じタグを二重に付けない一意インデックス (レコードで引くインデックスを兼ねる)、タグ名で引くインデックス (検索・候補)、
        /// レコードへの外部キー (レコードを SQL で直接消したときの保険。ふだんは本体の DeleteTogether が消す。SQLite は CREATE TABLE に書いたので無し)。
        /// </summary>
        internal static List<string> LinkIndexDdl(string table, string ownerColumn, string nameColumn,
            string ownerTable, string ownerIdColumn, DataSourceType type)
        {
            var ddl = new List<string>
            {
                $"CREATE UNIQUE INDEX ux_{table}_{ownerColumn}_{nameColumn} ON {table} ({ownerColumn}, {nameColumn});",
                $"CREATE INDEX ix_{table}_{nameColumn} ON {table} ({nameColumn});",
            };
            if (type != DataSourceType.SQLite)
                ddl.Add($"ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{ownerColumn} FOREIGN KEY ({ownerColumn}) REFERENCES {ownerTable} ({ownerIdColumn}) ON DELETE CASCADE;");
            return ddl;
        }

        //TagField を足す。同名の TagField が結び付きなしで既にあれば結び付ける
        static void PlaceField(string designDir, ModuleDesign target, TagSetupOptions options, SetupResult result)
        {
            var existing = target.Fields.FirstOrDefault(e => e.Name == options.FieldName);
            if (existing != null && existing is not TagFieldDesign)
            {
                result.Notes.Add($"'{target.Name}' に別の種類のフィールド '{options.FieldName}' があるため、TagField は足していません。");
                return;
            }
            var field = (TagFieldDesign?)existing;
            if (field != null && !string.IsNullOrEmpty(field.SearchCondition?.ModuleName))
            {
                result.Notes.Add($"'{target.Name}.{options.FieldName}' は既にタグ付けモジュール '{field.SearchCondition!.ModuleName}' と結び付いています (変更していません)。");
                return;
            }
            if (field == null)
            {
                field = new TagFieldDesign { Name = options.FieldName, DisplayName = "タグ" };
                target.Fields.Add(field);
            }
            field.SearchCondition = TagModuleFactory.LinkCondition(options.LinkModuleName);
            Save(designDir, target);
            result.Notes.Add($"'{target.Name}' に TagField '{options.FieldName}' を結び付けました。");
        }

        static bool ModuleFileExists(string designDir, string moduleName)
            => FindModuleFile(designDir, moduleName) != null;

        //モジュールファイルはフォルダ分けされていることがある (Modules/SFA/...)
        static string? FindModuleFile(string designDir, string moduleName)
        {
            var dir = Path.Combine(designDir, "Modules");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, $"{moduleName}.mod.json", SearchOption.AllDirectories).FirstOrDefault() : null;
        }

        //既存のファイルはその場所に、新しいモジュールは Modules 直下に (デザイナの保存と同じ BOM 付き UTF-8)
        static void Save(string designDir, ModuleDesign module)
        {
            var path = FindModuleFile(designDir, module.Name);
            var json = JsonConverterEx.SerializeObject(module);
            if (path == null) ApprovalFlowSetupService.SaveDesignFile(designDir, "Modules", $"{module.Name}.mod.json", json);
            else File.WriteAllText(path, json, new System.Text.UTF8Encoding(true));
        }

        static string CreateNextStepsNote(TagSetupOptions options)
        {
            var field = string.IsNullOrEmpty(options.FieldName) ? "TagField (検索条件 = " + options.LinkModuleName + ")" : options.FieldName;
            return $"""
                タグのモジュールを生成しました。次の手順で仕上げてください:
                1. DDL を実行してテーブルを作る ({options.LinkTableName})
                2. {options.TargetModuleName} の詳細・一覧・検索のレイアウトに {field} を置く (一覧の列にも置けます。ページの行の分は一覧の読み込みに同梱されます)
                候補は {options.LinkModuleName} に付いているタグから、よく使われている順に出ます。候補は {options.LinkModuleName} の読み取り条件に従います
                ({options.TargetModuleName} の行の条件は効きません。見せたくないタグがあるなら {options.LinkModuleName} に読み取り条件を書いてください)。
                """;
        }
    }

    /// <summary>タグ付けのモジュールを作る (フィールド構成は契約の既定の役割と同じ)。</summary>
    internal static class TagModuleFactory
    {
        /// <summary>タグ名の列名。</summary>
        internal const string NameColumn = "name";

        internal static ModuleDesign CreateLink(string moduleName, string table, string ownerColumn, string dataSourceName)
        {
            var module = new ModuleDesign { Name = moduleName, DataSourceName = dataSourceName, DbTable = table, CanCreate = true, CanUpdate = true, CanDelete = true };
            module.Fields.Add(new IdFieldDesign { Name = SystemFieldNames.Id, DbColumn = "id" });
            //本体の保存で CLB が入れる (TagField の検索条件の OwnerId.Value = Id.Value)
            module.Fields.Add(new IdFieldDesign { Name = "OwnerId", DbColumn = ownerColumn, IsManualInput = false });
            module.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "タグ名", DbColumn = NameColumn, IsRequired = true, MaxLength = TagField.MaxTagLength });
            module.Fields.Add(new TagLinkContractFieldDesign { Name = "TagLinkContract" });
            //タグ付けの行は TagField が読む: OwnerId と Name を読ませる (レイアウトに無いフィールドは値が空で届く)
            module.ListLayouts[string.Empty] = new ListLayoutDesign { DataOnlyFields = { "OwnerId", "Name" } };
            return module;
        }

        /// <summary>TagField の検索条件: このレコードのタグ付け行を、付けた順に。</summary>
        internal static SearchCondition LinkCondition(string linkModuleName) => new(linkModuleName)
        {
            Condition = MultiMatchCondition.And(new FieldVariableMatchCondition
            {
                SearchTargetVariable = "OwnerId.Value",
                Comparison = MatchComparison.Equal,
                Variable = "Id.Value",
            }),
            SortConditions = [new SortCondition { Variable = "Id.Value" }],
        };
    }
}
