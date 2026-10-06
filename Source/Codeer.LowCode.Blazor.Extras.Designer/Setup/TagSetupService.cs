using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// タグのセットアップ。CLB の多対多の形でタグを持つためのモジュールを作る:
    /// タグのマスタ (TagContractField。アプリで 1 つ・既にあれば使う) と、タグを付けるモジュールのタグ付けモジュール (TagLinkContractField)。
    /// タグを付けるモジュールには TagField を足す (同名の TagField が結び付きなしであれば結び付ける)。画面への配置はデザイナで行う。
    /// - 冪等: 同名のモジュールがあれば作らない。TagField が結び付き済みなら触らない。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む): テーブル、マスタの名前の一意インデックス (大文字小文字を区別しない)、
    ///   タグ付けの (レコード, タグ) の一意インデックスとタグのインデックス、外部キー (SQLite 以外。削除は既定の制限 = 使われているタグは消せない)。
    /// </summary>
    public static class TagSetupService
    {
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

            //マスタ (共有)
            var master = designData.Modules.Find(options.MasterModuleName);
            if (master != null || ModuleFileExists(designDir, options.MasterModuleName))
            {
                if (master != null && master.Fields.OfType<TagContractFieldDesign>().FirstOrDefault() == null)
                    throw new InvalidOperationException($"Module '{options.MasterModuleName}' exists but has no TagContractField.");
                result.SkippedModules.Add(options.MasterModuleName);
            }
            else
            {
                master = TagModuleFactory.CreateMaster(options.MasterModuleName, options.MasterTableName, options.DataSourceName);
                Save(designDir, master);
                ((IEditableModuleDesign)designData.Modules).Add(master);
                result.CreatedModules.Add(master.Name);
                var ddl = master.CreateDDL(dataSourceType, existingTables);
                result.Ddl.AddRange(ddl);
                if (ddl.Any(e => e.StartsWith("CREATE TABLE"))) result.Ddl.AddRange(MasterIndexDdl(master.DbTable, "name", dataSourceType));
                if (options.AddPageFrameLink)
                    ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir, new List<(string, string, Action<PageLink>?)> { ("タグ", master.Name, null) }, result);
            }
            var masterContract = master?.Fields.OfType<TagContractFieldDesign>().FirstOrDefault();
            var masterNameField = masterContract?.TagName ?? "Name";

            //タグ付け
            if (designData.Modules.Find(options.LinkModuleName) != null || ModuleFileExists(designDir, options.LinkModuleName))
            {
                result.SkippedModules.Add(options.LinkModuleName);
            }
            else
            {
                var link = TagModuleFactory.CreateLink(options.LinkModuleName, options.LinkTableName, options.OwnerColumnName,
                    options.MasterModuleName, masterNameField, options.DataSourceName);
                Save(designDir, link);
                ((IEditableModuleDesign)designData.Modules).Add(link);
                result.CreatedModules.Add(link.Name);
                var ddl = FixLinkColumnTypes(link.CreateDDL(dataSourceType, existingTables), options.OwnerColumnName, "tag_id");
                result.Ddl.AddRange(ddl);
                if (ddl.Any(e => e.StartsWith("CREATE TABLE")))
                {
                    var masterTable = master?.DbTable ?? MailHistoryModuleFactory.Pluralize(MailHistoryModuleFactory.ToSnakeCase(options.MasterModuleName));
                    var masterId = (master?.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id))?.DbColumn ?? "id";
                    result.Ddl.AddRange(LinkIndexDdl(link.DbTable, options.OwnerColumnName, "tag_id", target.DbTable, targetId.DbColumn, masterTable, masterId, dataSourceType));
                }
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
                OwnerColumnName = string.IsNullOrEmpty(options.OwnerColumnName) ? MailHistoryModuleFactory.ToSnakeCase(options.TargetModuleName) + "_id" : options.OwnerColumnName,
                MasterModuleName = options.MasterModuleName,
                MasterTableName = string.IsNullOrEmpty(options.MasterTableName) ? TableName(options.MasterModuleName) : options.MasterTableName,
                DataSourceName = options.DataSourceName,
                AddPageFrameLink = options.AddPageFrameLink,
            };
        }

        //既に複数形の名前 (RequestTags) はそのまま
        static string TableName(string moduleName)
        {
            var snake = MailHistoryModuleFactory.ToSnakeCase(moduleName);
            return snake.EndsWith('s') ? snake : MailHistoryModuleFactory.Pluralize(snake);
        }

        //本体の DDL はリンクの列を文字列型で作る。タグの列を Id の型 (OwnerId の列と同じ = 本体が Id に使う型) にそろえ、
        //両方 NOT NULL にする (外部キー・インデックスを張れるように。DDL は 1 要素 1 行)
        internal static List<string> FixLinkColumnTypes(List<string> ddl, string ownerColumn, string tagColumn)
        {
            string? ColumnType(string column)
            {
                var line = ddl.FirstOrDefault(e => e.TrimStart().StartsWith(column + " ", StringComparison.Ordinal));
                return line?.Trim().TrimEnd(',')[(column.Length + 1)..].Replace(" NOT NULL", string.Empty).Trim();
            }
            var idType = ColumnType(ownerColumn);
            if (string.IsNullOrEmpty(idType)) return ddl;
            return ddl.Select(line =>
            {
                var trimmed = line.TrimStart();
                var indent = line[..(line.Length - trimmed.Length)];
                var comma = trimmed.EndsWith(',') ? "," : string.Empty;
                if (trimmed.StartsWith(ownerColumn + " ", StringComparison.Ordinal)) return $"{indent}{ownerColumn} {idType} NOT NULL{comma}";
                if (trimmed.StartsWith(tagColumn + " ", StringComparison.Ordinal)) return $"{indent}{tagColumn} {idType} NOT NULL{comma}";
                return line;
            }).ToList();
        }

        //TagField を足す。同名の TagField が結び付きなしで既にあれば結び付ける (列でタグを持っていた頃のフィールドの移行もこれ)
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
            field.TagModuleName = string.Empty;
            Save(designDir, target);
            result.Notes.Add($"'{target.Name}' に TagField '{options.FieldName}' を結び付けました。");
        }

        //マスタのタグ名: 大文字小文字を区別しない一意 (DB ごとの書き方)。インデックスを張れる長さの列に直してから張る
        internal static List<string> MasterIndexDdl(string table, string nameColumn, DataSourceType type)
        {
            var index = $"ux_{table}_{nameColumn}";
            return type switch
            {
                DataSourceType.SQLServer =>
                [
                    $"ALTER TABLE {table} ALTER COLUMN {nameColumn} NVARCHAR(200) NOT NULL;",
                    $"CREATE UNIQUE INDEX {index} ON {table} ({nameColumn});",
                ],
                DataSourceType.Oracle =>
                [
                    $"ALTER TABLE {table} MODIFY ({nameColumn} VARCHAR2(200) NOT NULL);",
                    $"CREATE UNIQUE INDEX {index} ON {table} (UPPER({nameColumn}));",
                ],
                DataSourceType.MySQL =>
                [
                    $"ALTER TABLE {table} MODIFY {nameColumn} VARCHAR(200) NOT NULL;",
                    $"CREATE UNIQUE INDEX {index} ON {table} ({nameColumn});",
                ],
                DataSourceType.PostgreSQL => [$"CREATE UNIQUE INDEX {index} ON {table} (lower({nameColumn}));"],
                _ => [$"CREATE UNIQUE INDEX {index} ON {table} ({nameColumn} COLLATE NOCASE);"], // SQLite
            };
        }

        //タグ付け: 同じタグを二重に付けない一意インデックス (レコードで引くインデックスを兼ねる)、タグで引くインデックス、外部キー
        internal static List<string> LinkIndexDdl(string table, string ownerColumn, string tagColumn,
            string ownerTable, string ownerIdColumn, string masterTable, string masterIdColumn, DataSourceType type)
        {
            var ddl = new List<string>
            {
                $"CREATE UNIQUE INDEX ux_{table}_{ownerColumn}_{tagColumn} ON {table} ({ownerColumn}, {tagColumn});",
                $"CREATE INDEX ix_{table}_{tagColumn} ON {table} ({tagColumn});",
            };
            //SQLite は後から外部キーを足せない (CREATE TABLE に書く必要がある)。SQLite では付けない
            if (type == DataSourceType.SQLite) return ddl;
            ddl.Add($"ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{ownerColumn} FOREIGN KEY ({ownerColumn}) REFERENCES {ownerTable} ({ownerIdColumn});");
            ddl.Add($"ALTER TABLE {table} ADD CONSTRAINT fk_{table}_{tagColumn} FOREIGN KEY ({tagColumn}) REFERENCES {masterTable} ({masterIdColumn});");
            return ddl;
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
                1. DDL を実行してテーブルを作る ({options.MasterTableName} / {options.LinkTableName})
                2. {options.TargetModuleName} の詳細・一覧・検索のレイアウトに {field} を置く (一覧の列にも置けます。ページの行の分をまとめて 1 回で読みます)
                3. タグの名前の変更・削除は {options.MasterModuleName} の画面で行う (使われているタグは外部キーで消せません)
                保存しない入力欄 (取り込み画面など) には TagField を置き、検索条件を空にして TagModuleName = {options.MasterModuleName} にします。
                """;
        }
    }

    /// <summary>タグのマスタとタグ付けのモジュールを作る (フィールド構成は契約の既定の役割と同じ)。</summary>
    internal static class TagModuleFactory
    {
        internal static ModuleDesign CreateMaster(string moduleName, string table, string dataSourceName)
        {
            var module = new ModuleDesign { Name = moduleName, DataSourceName = dataSourceName, DbTable = table, CanCreate = true, CanUpdate = true, CanDelete = true };
            module.Fields.Add(new IdFieldDesign { Name = SystemFieldNames.Id, DbColumn = "id" });
            module.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "タグ名", DbColumn = "name", IsRequired = true });
            module.Fields.Add(new LabelFieldDesign { Name = "NameLabel", Text = "タグ名" });
            module.Fields.Add(new TagContractFieldDesign { Name = "TagContract", TagName = "Name" });

            var grid = new GridLayoutDesign();
            grid.Rows.Add(new GridRow
            {
                Columns =
                {
                    new GridColumn { Width = 140, Layout = new FieldLayoutDesign { FieldName = "NameLabel" } },
                    new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Name" } },
                }
            });
            module.DetailLayouts[string.Empty] = new DetailLayoutDesign { Layout = grid };
            module.ListLayouts[string.Empty] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name", Label = "タグ名" }]] };

            var search = new SearchGridLayoutDesign();
            search.Rows.Add(new GridRow
            {
                Columns =
                {
                    new GridColumn { Width = 140, Layout = new FieldLayoutDesign { FieldName = "NameLabel" } },
                    new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Name" } },
                }
            });
            module.SearchLayouts[string.Empty] = new SearchLayoutDesign { Layout = search };
            return module;
        }

        internal static ModuleDesign CreateLink(string moduleName, string table, string ownerColumn, string masterModuleName, string masterNameField, string dataSourceName)
        {
            var module = new ModuleDesign { Name = moduleName, DataSourceName = dataSourceName, DbTable = table, CanCreate = true, CanUpdate = true, CanDelete = true };
            module.Fields.Add(new IdFieldDesign { Name = SystemFieldNames.Id, DbColumn = "id" });
            //本体の保存で CLB が入れる (TagField の検索条件の OwnerId.Value = Id.Value)
            module.Fields.Add(new IdFieldDesign { Name = "OwnerId", DbColumn = ownerColumn, IsManualInput = false });
            module.Fields.Add(new LinkFieldDesign
            {
                Name = "Tag",
                DbColumn = "tag_id",
                SearchCondition = new SearchCondition(masterModuleName),
                ValueVariable = "Id.Value",
                //TagField はリンクの表示文字列をタグ名として使う
                DisplayTextVariable = $"{masterNameField}.Value",
            });
            module.Fields.Add(new TagLinkContractFieldDesign { Name = "TagLinkContract" });
            //タグ付けの行は TagField が読む: OwnerId と Tag を読ませる (レイアウトに無いフィールドは値が空で届く)
            module.ListLayouts[string.Empty] = new ListLayoutDesign { DataOnlyFields = { "OwnerId", "Tag" } };
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
