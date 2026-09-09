namespace Extras.Server.AI
{
    /// <summary>appsettings の "AIChat" セクション (AIChatField のサーバー側の設定。アプリの持ち物)。</summary>
    public class AIChatSettings
    {
        /// <summary>RawDataAccess Agent が読むデータソース名。本番では AI 用の読み取り専用 DB ユーザーで接続するデータソースを指す。</summary>
        public string RawDataAccessDataSource { get; set; } = "SampleSQLite";
    }
}
