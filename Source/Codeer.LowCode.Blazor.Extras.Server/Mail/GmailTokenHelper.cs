using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// <see cref="GmailTokenFieldDesign"/> のサーバー側ヘルパ (PasswordHashHelper と同じ流儀 =
    /// サーバーでしかできない変換のための1手)。クライアントから来た平文のトークンを暗号化する。
    /// </summary>
    /// <remarks>
    /// <see cref="ProtectGmailTokens"/> を <c>ModuleDataIO</c> の派生
    /// (通常 <c>CustomizedModuleDataIO.AddAsync</c> / <c>UpdateAsync</c>) から、
    /// <c>base.AddAsync</c> / <c>base.UpdateAsync</c> の前に呼ぶこと。
    /// 送信されてこないフィールドは触らない (= 空入力なら既存トークン維持)。
    /// 変更履歴などを自分で記録しているアプリは、この呼び出しの後に記録すれば暗号文が残る。
    /// </remarks>
    public static class GmailTokenHelper
    {
        public static void ProtectGmailTokens(ModuleDesign moduleDesign, ModuleData data, string encryptionKey)
        {
            foreach (var tokenFieldDesign in moduleDesign.Fields.OfType<GmailTokenFieldDesign>())
            {
                if (!data.Fields.TryGetValue(tokenFieldDesign.Name, out var fieldData)) continue;
                if (fieldData is not GmailTokenFieldData tokenData) continue;

                //空 = 登録解除 (暗号化せずそのまま空を書く)
                if (string.IsNullOrEmpty(tokenData.RefreshToken)) continue;

                //二重暗号化を防ぐ (同じデータで複数回呼ばれても壊れない)
                if (GmailTokenProtector.IsProtected(tokenData.RefreshToken)) continue;

                data.Fields[tokenFieldDesign.Name] = new GmailTokenFieldData
                {
                    RefreshToken = GmailTokenProtector.Protect(tokenData.RefreshToken, encryptionKey),
                };
            }
        }
    }
}
