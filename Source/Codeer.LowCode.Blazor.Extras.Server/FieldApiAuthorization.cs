using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server
{
    /// <summary>
    /// フィールド起点の API (メール送信・一斉送信・AI チャット・承認) の入口検査。クライアントの値を信用せず、
    ///   1. moduleName / fieldName のフィールドを今のユーザーがユーザー権限だけで読めること (アプリアクセス条件・モジュールの UserReadCondition・
    ///      ユーザーだけで偽と確定する PermissionField の読取条件。本体の ModuleDataIO.CheckUserReadAuthorization。行は読まないので DataReadCondition と行依存の条件は見ない)
    ///   2. そのフィールドが期待した型 (TDesign) であること
    /// を確かめてデザインを返す。API の設定値 (送信インフラの呼び名・Agent 名・文書フォルダ等) はこのデザインから取る。通らなければ LowCodeException。
    /// </summary>
    static class FieldApiAuthorization
    {
        internal static async Task<TDesign> CheckAsync<TDesign>(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName, string notFoundMessage)
            where TDesign : FieldDesignBase
        {
            moduleName ??= string.Empty;
            fieldName ??= string.Empty;
            return await moduleDataIO.CheckUserReadAuthorization(moduleName, fieldName) as TDesign
                ?? throw LowCodeException.Create(notFoundMessage, moduleName, fieldName);
        }
    }
}
