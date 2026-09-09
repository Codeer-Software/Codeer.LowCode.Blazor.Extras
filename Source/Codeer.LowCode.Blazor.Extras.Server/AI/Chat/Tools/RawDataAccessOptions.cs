using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools
{
    /// <summary>
    /// <see cref="RawDataAccessToolSet"/> の設定。AI が SQL を実行するデータソースを指定する。
    /// <para>
    /// 権限は DB 側で制御する前提: <see cref="DataSourceName"/> には、AI 用に作った読み取り専用の DB ユーザー
    /// (見せてよい表・列だけ SELECT を GRANT) で接続するデータソースを指定する。アプリ本体と同じ接続を渡すと、
    /// ログインユーザーの権限に関係なく DB 全体が読める。SQLite はユーザーが無いので接続文字列の <c>Mode=ReadOnly</c> で読み取り専用にする。
    /// </para>
    /// </summary>
    public class RawDataAccessOptions
    {
        /// <summary>AI が使うデータソース名 (appsettings の DataSources の Name)。</summary>
        public string DataSourceName { get; set; } = string.Empty;

        /// <summary>データソースへ接続する IDbAccessor を作る (SQL 1 回ごとに作って捨てる)。例: <c>() => new DbAccessor(SystemConfig.Instance.DataSources)</c>。</summary>
        public Func<IDbAccessor> DbAccessorFactory { get; set; } = () => throw new InvalidOperationException("RawDataAccessOptions.DbAccessorFactory is not set.");

        /// <summary>モジュール定義。表と列に業務上の名前 (モジュール名・フィールドの表示名・候補値) を添えてスキーマを説明するのに使う。無くても動く。</summary>
        public IModuleDesigns? Modules { get; set; }

        /// <summary>1 回の SQL で AI に返す行数の上限 (超えた分は切り捨てて「続きあり」と伝える)。</summary>
        public int MaxRows { get; set; } = 200;

        /// <summary>1 回の SQL で AI に返す文字数の上限 (トークンの歯止め)。</summary>
        public int MaxResultChars { get; set; } = 20000;

        /// <summary>SQL 1 文のタイムアウト (秒)。</summary>
        public int CommandTimeoutSeconds { get; set; } = 30;

        /// <summary>スキーマ情報を再読込するまでの時間。</summary>
        public TimeSpan SchemaCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>スキーマ説明から外す表 (DB 側で GRANT していない表は元々見えない。ここは補助)。大文字小文字は区別しない。</summary>
        public IList<string> ExcludedTables { get; } = new List<string>();

        /// <summary>システムプロンプトに追記する業務固有の説明 (用語、よく聞かれる集計の定義など)。</summary>
        public string AdditionalInstructions { get; set; } = string.Empty;
    }
}
