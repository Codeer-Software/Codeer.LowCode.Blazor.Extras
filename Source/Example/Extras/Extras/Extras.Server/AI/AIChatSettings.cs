namespace Extras.Server.AI
{
    /// <summary>appsettings の "AIChat" セクション (AIChatField のサーバー側の設定。アプリの持ち物)。</summary>
    public class AIChatSettings
    {
        /// <summary>RawDataAccess Agent が読むデータソース名 (複数可)。本番では AI 用の読み取り専用 DB ユーザーで接続するデータソースを指す。</summary>
        public string[] RawDataAccessDataSources { get; set; } = new[] { "SampleSQLite" };
    }
}
