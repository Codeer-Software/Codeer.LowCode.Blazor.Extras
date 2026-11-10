namespace Codeer.LowCode.Blazor.Extras.WebApi
{
    /// <summary>
    /// 外部の Web API の呼び出しの結果。相手が返した状態コードとボディはそのまま入る (4xx / 5xx も)。
    /// 呼び出せなかった (接続先が無い・通信できない・タイムアウト・URL が接続先の外 等) ときは <see cref="StatusCode"/> が 0 で <see cref="ErrorMessage"/> に説明が入る。
    /// </summary>
    public class WebApiExecuteResponse
    {
        /// <summary>相手の HTTP の状態コード。呼び出せなかったときは 0。</summary>
        public int StatusCode { get; set; }

        /// <summary>相手の応答のボディ (文字列)。</summary>
        public string Body { get; set; } = string.Empty;

        /// <summary>呼び出せなかったときの説明 (利用者に見せてよい短い文言)。呼び出せたときは空。</summary>
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
