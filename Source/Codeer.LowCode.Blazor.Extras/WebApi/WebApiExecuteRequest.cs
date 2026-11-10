namespace Codeer.LowCode.Blazor.Extras.WebApi
{
    /// <summary>
    /// WebApiField からホストへの呼び出しの依頼。
    /// 接続先・メソッド・パスはホストが ModuleName / FieldName のデザイン (WebApiFieldDesign) から読む (クライアントからは送らない)。
    /// </summary>
    public class WebApiExecuteRequest
    {
        /// <summary>WebApiField を持つモジュールの名前。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>WebApiField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>引数の JSON (ふつうはオブジェクト。配列・値だけも可)。パスの差し込み・クエリ・ボディの元になる (オブジェクトでなければボディだけ)。</summary>
        public string Arguments { get; set; } = string.Empty;
    }
}
