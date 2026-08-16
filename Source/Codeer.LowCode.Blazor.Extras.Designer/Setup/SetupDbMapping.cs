using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// モジュールデザインから機械的に DDL を生成する。
    /// Designer.Standard の DbMapping と同一ロジックの複製 (Extras.Designer は Standard を参照しない方針のため。
    /// 挙動を変えるときは両方を確認すること)。
    /// </summary>
    internal static class SetupDbMapping
    {
        /// <summary>
        /// モジュールの DDL を生成する。existingTables (現在のDBスキーマ) を渡すと差分DDLになる:
        /// テーブルが無い/スキーマ未取得 → CREATE TABLE、テーブルが有る → 不足している列だけ ALTER TABLE ADD。
        /// 追加のみ (型変更・列削除はしない=安全側)。
        /// </summary>
        internal static List<string> CreateDDL(this ModuleDesign module, DataSourceType dataSourceType,
            List<DbTableDefinition>? existingTables = null)
        {
            var columns = module.Fields
                .SelectMany(field => CreateColumns(dataSourceType, field))
                .ToList();

            var existing = existingTables?.FirstOrDefault(
                t => string.Equals(t.Name, module.DbTable, StringComparison.OrdinalIgnoreCase));

            if (existing == null) return CreateTableDdl(module.DbTable, columns);

            var existingColumns = new HashSet<string>(
                existing.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var missing = columns.Where(c => !existingColumns.Contains(c.Name)).ToList();

            var ddl = new List<string>();
            if (missing.Count == 0)
                ddl.Add($"-- {module.DbTable}: 追加する列はありません");
            else
                ddl.AddRange(missing.Select(c => AlterAddColumn(dataSourceType, module.DbTable, c.Name, c.Type)));
            return ddl;
        }

        /// <summary>1 フィールド分の列だけを対象に、不足していれば ALTER TABLE ADD を生成する (申請書側の FK 列用)。</summary>
        internal static List<string> CreateAlterAddForField(ModuleDesign module, FieldDesignBase field,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var existing = existingTables?.FirstOrDefault(
                t => string.Equals(t.Name, module.DbTable, StringComparison.OrdinalIgnoreCase));
            var existingColumns = new HashSet<string>(
                existing?.Columns.Select(c => c.Name) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            return CreateColumns(dataSourceType, field)
                .Where(c => !existingColumns.Contains(c.Name))
                .Select(c => AlterAddColumn(dataSourceType, module.DbTable, c.Name, c.Type))
                .ToList();
        }

        static List<string> CreateTableDdl(string table, List<(string Name, string Type)> columns)
        {
            var ddl = new List<string> { $"CREATE TABLE {table} (" };
            for (var i = 0; i < columns.Count; i++)
            {
                var comma = i < columns.Count - 1 ? "," : "";
                ddl.Add($"  {columns[i].Name} {columns[i].Type}{comma}");
            }
            ddl.Add(");");
            return ddl;
        }

        static string AlterAddColumn(DataSourceType dataSourceType, string table, string name, string type) => dataSourceType switch
        {
            DataSourceType.SQLServer => $"ALTER TABLE {table} ADD {name} {type};",
            DataSourceType.Oracle => $"ALTER TABLE {table} ADD ({name} {type});",
            _ => $"ALTER TABLE {table} ADD COLUMN {name} {type};" // SQLite / PostgreSQL / MySQL
        };

        // フィールド → (列名, 型) のペア列。File のように複数列を持つフィールドは複数返す。
        static List<(string Name, string Type)> CreateColumns(DataSourceType dataSourceType, FieldDesignBase field)
        {
            switch (field)
            {
                case DbValueFieldDesignBase dbValue when !string.IsNullOrEmpty(dbValue.DbColumn):
                    return [(dbValue.DbColumn, ColumnType(dataSourceType, dbValue))];

                case OptimisticLockingFieldDesign opt when !string.IsNullOrEmpty(opt.DbColumn):
                    return [(opt.DbColumn, IntegerType(dataSourceType))];

                case FileFieldDesign file:
                {
                    var (guid, name, size) = FileColumnTypes(dataSourceType);
                    var list = new List<(string, string)>();
                    if (!string.IsNullOrEmpty(file.DbColumnFileGuid)) list.Add((file.DbColumnFileGuid, guid));
                    if (!string.IsNullOrEmpty(file.DbColumnFileName)) list.Add((file.DbColumnFileName, name));
                    if (!string.IsNullOrEmpty(file.DbColumnFileSize)) list.Add((file.DbColumnFileSize, size));
                    return list;
                }

                default:
                {
                    // 特定のフィールド型を名指しせず、[DbColumn] を付けた文字列プロパティを汎用に列挙する
                    // (ApprovalFlowField の FK 列などはここで拾われる。既定の列型は text)。
                    var text = TextType(dataSourceType);
                    var list = new List<(string, string)>();
                    foreach (var prop in field.GetType().GetProperties())
                    {
                        if (prop.PropertyType != typeof(string)) continue;
                        if (prop.GetCustomAttribute<DbColumnAttribute>() == null) continue;
                        var column = prop.GetValue(field) as string;
                        if (!string.IsNullOrEmpty(column)) list.Add((column, text));
                    }
                    return list;
                }
            }
        }

        static string ColumnType(DataSourceType dataSourceType, DbValueFieldDesignBase field)
        {
            var type = BaseColumnType(dataSourceType, field);

            if (field is not IdFieldDesign && field.IsRequired)
                type += " NOT NULL";

            return type;
        }

        static string BaseColumnType(DataSourceType dataSourceType, DbValueFieldDesignBase field)
        {
            if (field is NumberFieldDesign number && number.MaxFractionDigits is > 0)
                return DecimalType(dataSourceType, number.MaxFractionDigits.Value);

            var fieldType = field.GetType();

            if (fieldType == typeof(IdFieldDesign) && field.DbColumn.ToLower() != "id")
                return ForeignKeyIdType(dataSourceType);

            return TypeMapping(dataSourceType).TryGetValue(fieldType, out var columnType)
                ? columnType
                : TextType(dataSourceType);
        }

        static string IntegerType(DataSourceType dataSourceType) => TypeMapping(dataSourceType)[typeof(NumberFieldDesign)];

        static string ForeignKeyIdType(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => "INTEGER",
            DataSourceType.SQLServer => "BIGINT",
            DataSourceType.PostgreSQL => "BIGINT",
            DataSourceType.MySQL => "BIGINT",
            DataSourceType.Oracle => "NUMBER",
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static string TextType(DataSourceType dataSourceType) => TypeMapping(dataSourceType)[typeof(TextFieldDesign)];

        static string DecimalType(DataSourceType dataSourceType, int scale) => dataSourceType switch
        {
            DataSourceType.SQLite => "NUMERIC",
            DataSourceType.SQLServer => $"DECIMAL(18,{scale})",
            DataSourceType.PostgreSQL => $"NUMERIC(18,{scale})",
            DataSourceType.MySQL => $"DECIMAL(18,{scale})",
            DataSourceType.Oracle => $"NUMBER(18,{scale})",
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static (string Guid, string Name, string Size) FileColumnTypes(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => ("TEXT", "TEXT", "INTEGER"),
            DataSourceType.SQLServer => ("UNIQUEIDENTIFIER", "NVARCHAR(MAX)", "INT"),
            DataSourceType.PostgreSQL => ("UUID", "TEXT", "INTEGER"),
            DataSourceType.MySQL => ("CHAR(36)", "TEXT", "INT"),
            DataSourceType.Oracle => ("VARCHAR2(36)", "VARCHAR2(4000)", "NUMBER"),
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        static Dictionary<Type, string> TypeMapping(DataSourceType dataSourceType) => dataSourceType switch
        {
            DataSourceType.SQLite => SqliteTypeMapping,
            DataSourceType.SQLServer => SqlserverTypeMapping,
            DataSourceType.PostgreSQL => PostgresqlTypeMapping,
            DataSourceType.MySQL => MysqlTypeMapping,
            DataSourceType.Oracle => OracleTypeMapping,
            _ => throw new Exception($"Database type not supported: {dataSourceType}")
        };

        private static readonly Dictionary<Type, string> SqliteTypeMapping = new()
        {
            {typeof(IdFieldDesign), "INTEGER PRIMARY KEY AUTOINCREMENT"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INTEGER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BOOLEAN"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> SqlserverTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGINT IDENTITY(1,1) PRIMARY KEY"},
            {typeof(TextFieldDesign), "NVARCHAR(MAX)"},
            {typeof(NumberFieldDesign), "INT"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BIT"},
            {typeof(LinkFieldDesign), "NVARCHAR(MAX)"},
            {typeof(SelectFieldDesign), "NVARCHAR(MAX)"},
            {typeof(RadioGroupFieldDesign), "NVARCHAR(MAX)"}
        };

        private static readonly Dictionary<Type, string> PostgresqlTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGSERIAL PRIMARY KEY"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INTEGER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "TIMESTAMP"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "BOOLEAN"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> MysqlTypeMapping = new()
        {
            {typeof(IdFieldDesign), "BIGINT AUTO_INCREMENT PRIMARY KEY"},
            {typeof(TextFieldDesign), "TEXT"},
            {typeof(NumberFieldDesign), "INT"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "DATETIME"},
            {typeof(TimeFieldDesign), "TIME"},
            {typeof(BooleanFieldDesign), "TINYINT(1)"},
            {typeof(LinkFieldDesign), "TEXT"},
            {typeof(SelectFieldDesign), "TEXT"},
            {typeof(RadioGroupFieldDesign), "TEXT"}
        };

        private static readonly Dictionary<Type, string> OracleTypeMapping = new()
        {
            {typeof(IdFieldDesign), "NUMBER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY"},
            {typeof(TextFieldDesign), "VARCHAR2(4000)"},
            {typeof(NumberFieldDesign), "NUMBER"},
            {typeof(DateFieldDesign), "DATE"},
            {typeof(DateTimeFieldDesign), "TIMESTAMP"},
            {typeof(TimeFieldDesign), "TIMESTAMP WITH LOCAL TIME ZONE"},
            {typeof(BooleanFieldDesign), "NUMBER(1)"},
            {typeof(LinkFieldDesign), "VARCHAR2(4000)"},
            {typeof(SelectFieldDesign), "VARCHAR2(4000)"},
            {typeof(RadioGroupFieldDesign), "VARCHAR2(4000)"}
        };
    }
}
