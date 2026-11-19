using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

// Example のテスト用 DB (SQLite) を、SampleData の SQL から作る。
//   dotnet run --project Source/Example/SampleData/Setup [-- [DB のパス] [--reset-samples]]
// 何度実行してもよい。今あるデータは消さない (--reset-samples を付けたときだけ、サンプルの表を作り直す)。
// 実行する SQL と順番はここに書いてあるとおりで、README.md の「手で実行する場合」と同じ。

const string DefaultDbPath = @"C:\Codeer.LowCode.Blazor.Local\Data\sqlite_sample_extras.db";

// 今あるデータを消さない SQL (CREATE TABLE IF NOT EXISTS / INSERT OR IGNORE)。毎回実行する
string[] baseFiles = ["base_tables.sql", "users_sample.sql"];

// 表を作り直す SQL (DROP → CREATE → INSERT)。表がまだ無いときだけ実行する。順番は依存の無い順
string[] sampleFiles = ["sales_sample.sql", "inquiry_sample.sql", "job_trigger_sample.sql", "tag_sample.sql"];

Console.OutputEncoding = Encoding.UTF8;
var resetSamples = args.Contains("--reset-samples");
var dbPath = args.FirstOrDefault(e => !e.StartsWith("--")) ?? DefaultDbPath;
var sqlDirectory = Path.GetDirectoryName(Path.GetDirectoryName(SourceFile())!)!;

var missing = baseFiles.Concat(sampleFiles).Where(e => !File.Exists(Path.Combine(sqlDirectory, e))).ToList();
if (missing.Count > 0)
{
    Console.Error.WriteLine($"SQL が見つかりません: {string.Join(", ", missing)} ({sqlDirectory})");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
Console.WriteLine($"DB: {dbPath}{(File.Exists(dbPath) ? "" : " (新しく作ります)")}");

using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
connection.Open();

// 以前から DB を持っている場合: ユーザーの表に部署の列が無ければ足す (CREATE TABLE IF NOT EXISTS は、すでにある表の列を変えない)
if (TableExists("app_users") && !ColumnExists("app_users", "dept"))
{
    Execute("ALTER TABLE app_users ADD COLUMN dept TEXT");
    Console.WriteLine("app_users に dept 列を足しました");
}

foreach (var file in baseFiles) Run(file);

foreach (var file in sampleFiles)
{
    // その SQL が作る表 (CREATE TABLE の表) が全部あれば、データを消さないように飛ばす
    var tables = Regex.Matches(Read(file), @"CREATE\s+TABLE\s+(\w+)", RegexOptions.IgnoreCase).Select(e => e.Groups[1].Value).ToList();
    if (!resetSamples && tables.Count > 0 && tables.All(TableExists))
    {
        Console.WriteLine($"飛ばしました: {file} (表がすでにあります。作り直すなら --reset-samples)");
        continue;
    }
    Run(file);
}

Console.WriteLine();
Console.WriteLine("表と行数:");
foreach (var table in Query("select name from sqlite_master where type = 'table' and name not like 'sqlite_%' order by name"))
{
    Console.WriteLine($"  {table,-30} {Query($"select count(*) from \"{table}\"").Single()}");
}
return 0;

void Run(string file)
{
    using var transaction = connection.BeginTransaction();
    Execute(Read(file), transaction);
    transaction.Commit();
    Console.WriteLine($"実行しました: {file}");
}

string Read(string file) => File.ReadAllText(Path.Combine(sqlDirectory, file), Encoding.UTF8);

void Execute(string sql, SqliteTransaction? transaction = null)
{
    using var command = connection.CreateCommand();
    command.Transaction = transaction;
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

List<string> Query(string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    using var reader = command.ExecuteReader();
    var result = new List<string>();
    while (reader.Read()) result.Add(reader.GetValue(0).ToString() ?? string.Empty);
    return result;
}

bool TableExists(string table)
    => Query($"select name from sqlite_master where type = 'table' and name = '{table}'").Count > 0;

bool ColumnExists(string table, string column)
    => Query($"select name from pragma_table_info('{table}')").Contains(column, StringComparer.OrdinalIgnoreCase);

// この Program.cs の場所 (SQL はその 1 つ上のフォルダ)。dotnet run で動かす前提
static string SourceFile([CallerFilePath] string path = "") => path;
