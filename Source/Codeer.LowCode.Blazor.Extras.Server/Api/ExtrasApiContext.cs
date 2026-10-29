using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.Api
{
    /// <summary>
    /// 1 回の要求の間にホストが機能へ渡すもの。Web ホストはリクエストごと (テンプレの DataService から)、デスクトップはプロセス内で作る。
    /// </summary>
    public sealed class ExtrasApiContext
    {
        /// <summary>現在のユーザーの権限で読む・書く IO。</summary>
        public required ModuleDataIO ModuleDataIO { get; init; }

        /// <summary>この要求が使うデザイン。</summary>
        public required DesignData DesignData { get; init; }

        /// <summary>この要求の DB 接続 (ModuleDataIO が使っているもの)。</summary>
        public required IDbAccessor DbAccess { get; init; }

        /// <summary>現在のユーザーの Id (ユーザーモジュールの行の Id)。ジョブの所有者の照合に使う。</summary>
        public required string UserId { get; init; }

        /// <summary>機能の警告・失敗の出力先。無ければ出さない。</summary>
        public ILogger? Logger { get; init; }

        /// <summary>この要求の監査レコード。ExtrasApi が今のリクエストのものを入れる (監査ログが無いホスト・リクエストの外では null)。</summary>
        public AuditContext? Audit { get; set; }
    }
}
