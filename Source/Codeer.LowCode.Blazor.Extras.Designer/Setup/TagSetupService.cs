using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// タグのセットアップ。タグを付けるモジュール &lt;Module&gt; に、タグ付けモジュール &lt;Module&gt;Tags (TagLinkContractField。1 行 = レコードに付いたタグ 1 つ = OwnerId + タグ名) を作り、
    /// タグを付けるモジュールに TagField Tags を足す (同名の TagField が結び付きなしであれば結び付ける)。
    /// 名前・テーブル名・列名・データソースは対象モジュールから決める。画面への配置はデザイナで行う。タグのマスタは作らない。
    /// - 冪等: 同名のモジュールがあれば作らない (TagLinkContractField が無ければ結び付けずに止める)。TagField が結び付き済みなら触らない。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む): 3 列のテーブル、(レコード, タグ名) の一意インデックス、タグ名のインデックス、
    ///   レコードへの外部キー (削除は連鎖。SQLite は CREATE TABLE の列に書く)。
    /// </summary>
    public static class TagSetupService
    {
        internal const string FieldName = "Tags";
        internal const string OwnerColumn = "owner_id";

        public static SetupResult Run(DesignData designData, string designDir, TagSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var target = designData.Modules.Find(options.TargetModuleName)
                ?? throw new InvalidOperationException($"Module not found: {options.TargetModuleName}");
            if (string.IsNullOrEmpty(target.DbTable))
                throw new InvalidOperationException($"Module '{target.Name}' has no table. Tags can be saved only on a module with a table.");
            var targetId = target.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id)
                ?? throw new InvalidOperationException($"Module '{target.Name}' has no Id field.");

            var linkName = target.Name + "Tags";
            var result = new SetupResult();

            //タグ付け
            var existing = designData.Modules.Find(linkName);
            if (existing != null || ModuleFileExists(designDir, linkName))
            {
                if (existing != null && existing.Fields.OfType<TagLinkContractFieldDesign>().FirstOrDefault() == null)
                    throw new InvalidOperationException($"Module '{linkName}' already exists and has no TagLinkContractField. Rename it or add the contract.");
                result.SkippedModules.Add(linkName);
            }
            else
            {
                var link = TagModuleFactory.CreateLink(linkName, TableName(linkName), target.DataSourceName);
                Save(designDir, link);
                ((IEditableModuleDesign)designData.Modules).Add(link);
                result.CreatedModules.Add(link.Name);
                var ownerType = OwnerColumnType(dataSourceType, target, targetId, existingTables);
                result.Ddl.AddRange(TagTableDdl.Create(dataSourceType, link.DbTable, target.DbTable, targetId.DbColumn, ownerType));
                //文字列の Id は列の長さが分からない (外部キーは同じ長さが要る DB がある)
                if (dataSourceType != DataSourceType.SQLite && ownerType == TagTableDdl.TextIdType(dataSourceType))
                    result.Notes.Add($"{link.DbTable}.{OwnerColumn} の型 ({ownerType}) を {target.DbTable}.{targetId.DbColumn} と同じ長さにしてから DDL を実行してください。");
            }

            //タグを付けるモジュールの TagField
            PlaceField(designDir, target, linkName, result);

            result.Notes.Add(CreateNextStepsNote(target.Name, linkName, TableName(linkName)));
            return result;
        }

        //既に複数形の名前 (RequestTags) はそのまま
        static string TableName(string moduleName)
        {
            var snake = MailHistoryModuleFactory.ToSnakeCase(moduleName);
            return snake.EndsWith('s') ? snake : MailHistoryModuleFactory.Pluralize(snake);
        }

        /// <summary>
        /// owner_id の型 = 対象の Id 列の型 (外部キーのため)。DB にテーブルがあればその型 (文字列の型は長さが取れないので既定の長さ)、
        /// 無ければ対象の Id が手入力なら文字列、そうでなければ整数。
        /// </summary>
        internal static string OwnerColumnType(DataSourceType type, ModuleDesign target, IdFieldDesign targetId, List<DbTableDefinition>? existingTables)
        {
            var raw = existingTables?
                .FirstOrDefault(e => string.Equals(e.Name, target.DbTable, StringComparison.OrdinalIgnoreCase))?.Columns
                .FirstOrDefault(e => string.Equals(e.Name, targetId.DbColumn, StringComparison.OrdinalIgnoreCase))?.RawDbTypeName;
            if (!string.IsNullOrEmpty(raw))
                return IsTextType(raw) ? TagTableDdl.TextIdType(type) : raw.ToUpperInvariant();
            return targetId.IsManualInput ? TagTableDdl.TextIdType(type) : TagTableDdl.IntegerIdType(type);
        }

        static bool IsTextType(string raw)
            => raw.Contains("char", StringComparison.OrdinalIgnoreCase) || raw.Contains("text", StringComparison.OrdinalIgnoreCase);

        //TagField を足す。同名の TagField が結び付きなしで既にあれば結び付ける
        static void PlaceField(string designDir, ModuleDesign target, string linkName, SetupResult result)
        {
            var existing = target.Fields.FirstOrDefault(e => e.Name == FieldName);
            if (existing != null && existing is not TagFieldDesign)
            {
                result.Notes.Add($"'{target.Name}' に別の種類のフィールド '{FieldName}' があるため、TagField は足していません。");
                return;
            }
            var field = (TagFieldDesign?)existing;
            if (field != null && !string.IsNullOrEmpty(field.TagModuleName))
            {
                result.Notes.Add($"'{target.Name}.{FieldName}' は既にタグ付けモジュール '{field.TagModuleName}' と結び付いています (変更していません)。");
                return;
            }
            if (field == null)
            {
                field = new TagFieldDesign { Name = FieldName, DisplayName = "タグ" };
                target.Fields.Add(field);
            }
            field.TagModuleName = linkName;
            Save(designDir, target);
            result.Notes.Add($"'{target.Name}' に TagField '{FieldName}' を結び付けました。");
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

        static string CreateNextStepsNote(string target, string linkName, string table) => $"""
            タグのモジュールを生成しました。次の手順で仕上げてください:
            1. DDL を実行してテーブルを作る ({table})
            2. {target} の詳細・一覧・検索のレイアウトに {FieldName} を置く (一覧の列にも置けます。ページの行の分は一覧の読み込みに同梱されます)
            候補は {linkName} に付いているタグから、よく使われている順に出ます。候補は {linkName} の読み取り条件に従います
            ({target} の行の条件は効きません。見せたくないタグがあるなら {linkName} に読み取り条件を書いてください)。
            """;
    }

    /// <summary>タグ付けのモジュールを作る (フィールド構成は契約の既定の役割と同じ)。</summary>
    internal static class TagModuleFactory
    {
        internal static ModuleDesign CreateLink(string moduleName, string table, string dataSourceName)
        {
            var module = new ModuleDesign { Name = moduleName, DataSourceName = dataSourceName, DbTable = table, CanCreate = true, CanUpdate = true, CanDelete = true };
            module.Fields.Add(new IdFieldDesign { Name = SystemFieldNames.Id, DbColumn = "id" });
            //本体の保存で CLB が入れる (TagField の結び付きの OwnerId.Value = Id.Value)
            module.Fields.Add(new IdFieldDesign { Name = "OwnerId", DbColumn = TagSetupService.OwnerColumn, IsManualInput = false });
            module.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "タグ名", DbColumn = TagTableDdl.NameColumn, IsRequired = true, MaxLength = TagField.MaxTagLength });
            module.Fields.Add(new TagLinkContractFieldDesign { Name = "TagLinkContract" });
            return module;
        }
    }

    /// <summary>
    /// タグ付けのテーブルの DDL (id・owner_id・name の 3 列)。
    /// タグの同一判定は完全一致 (大文字小文字を区別する) なので、既定の照合順序が区別しない SQL Server / MySQL の name には区別する照合順序を付ける
    /// (PostgreSQL / SQLite / Oracle は既定で区別する)。候補の Like も同じ照合順序で区別する。
    /// </summary>
    internal static class TagTableDdl
    {
        internal const string NameColumn = "name";

        internal static List<string> Create(DataSourceType type, string table, string ownerTable, string ownerIdColumn, string ownerType)
        {
            var owner = type == DataSourceType.SQLite
                ? $"{ownerType} NOT NULL REFERENCES {ownerTable} ({ownerIdColumn}) ON DELETE CASCADE"
                : $"{ownerType} NOT NULL";
            var ddl = new List<string>
            {
                $"CREATE TABLE {table} (",
                $"  id {IdType(type)},",
                $"  {TagSetupService.OwnerColumn} {owner},",
                $"  {NameColumn} {NameType(type)} NOT NULL",
                ");",
                //同じタグを二重に付けない (レコードで引くインデックスを兼ねる)・タグ名で引く (検索・候補)
                $"CREATE UNIQUE INDEX ux_{table}_{TagSetupService.OwnerColumn}_{NameColumn} ON {table} ({TagSetupService.OwnerColumn}, {NameColumn});",
                $"CREATE INDEX ix_{table}_{NameColumn} ON {table} ({NameColumn});",
            };
            //SQLite は接続ごとに外部キーが既定で無効 (接続文字列の Foreign Keys=True で有効になる)。DDL を実行する人に見えるよう先頭に書く
            if (type == DataSourceType.SQLite)
                ddl.Insert(0, "-- SQLite は接続文字列に Foreign Keys=True を付ける。無いと外部キーが効かず、レコードを消してもタグ付け行が残る");
            //レコードを消せばタグも消える (SQLite は列に書いた)
            if (type != DataSourceType.SQLite)
                ddl.Add($"ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{TagSetupService.OwnerColumn} FOREIGN KEY ({TagSetupService.OwnerColumn}) REFERENCES {ownerTable} ({ownerIdColumn}) ON DELETE CASCADE;");
            return ddl;
        }

        static string IdType(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer => "BIGINT IDENTITY(1,1) PRIMARY KEY",
            DataSourceType.PostgreSQL => "BIGSERIAL PRIMARY KEY",
            DataSourceType.MySQL => "BIGINT AUTO_INCREMENT PRIMARY KEY",
            DataSourceType.Oracle => "NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY",
            _ => "INTEGER PRIMARY KEY AUTOINCREMENT",
        };

        //_CS_AS_KS_WS: 大文字小文字・アクセント・かな・幅を全部区別する (= 完全一致)
        static string NameType(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer => $"NVARCHAR({TagField.MaxTagLength}) COLLATE Latin1_General_CS_AS_KS_WS",
            DataSourceType.MySQL => $"VARCHAR({TagField.MaxTagLength}) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin",
            DataSourceType.PostgreSQL => $"VARCHAR({TagField.MaxTagLength})",
            DataSourceType.Oracle => $"VARCHAR2({TagField.MaxTagLength})",
            _ => "TEXT",
        };

        /// <summary>整数の Id を指す列の型 (SetupDbMapping の外部キーの Id と同じ)。</summary>
        internal static string IntegerIdType(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer or DataSourceType.PostgreSQL or DataSourceType.MySQL => "BIGINT",
            DataSourceType.Oracle => "NUMBER",
            _ => "INTEGER",
        };

        /// <summary>文字列の Id を指す列の型 (一意インデックス (owner_id, name) に収まる長さ)。</summary>
        internal static string TextIdType(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer => "NVARCHAR(450)",
            DataSourceType.MySQL or DataSourceType.PostgreSQL => "VARCHAR(255)",
            DataSourceType.Oracle => "VARCHAR2(255)",
            _ => "TEXT",
        };
    }
}
