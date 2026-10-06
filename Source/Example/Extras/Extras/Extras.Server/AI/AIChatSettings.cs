using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;

namespace Extras.Server.AI
{
    /// <summary>appsettings の "AIChat" セクション (AIChatField のサーバー側の設定。アプリの持ち物)。</summary>
    public class AIChatSettings
    {
        /// <summary>RawDataAccess Agent が読むデータソース名 (複数可)。本番では AI 用の読み取り専用 DB ユーザーで接続するデータソースを指す。</summary>
        public string[] RawDataAccessDataSources { get; set; } = new[] { "SampleSQLite" };

        /// <summary>RawDataAccess Agent の上限値 (SQL のタイムアウト・1 返事の合計時間・同時実行数など。0 / false で無効。待たせる・断る種類のものは既定で無効)。</summary>
        public RawDataAccessOptions RawDataAccess { get; set; } = new();
    }
}
