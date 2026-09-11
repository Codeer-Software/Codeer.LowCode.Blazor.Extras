using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Properties;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// MailField の送信・プレビューの入口検査。クライアントの値を信用せず、
    ///   1. SourceModule / FieldName のフィールドを今のユーザーがユーザー権限だけで読めること (アプリアクセス条件・モジュールの UserReadCondition・
    ///      ユーザーだけで偽と確定する PermissionField の読取条件。本体の ModuleDataIO.CheckUserReadAuthorization。行は読まないので DataReadCondition と行依存の条件は見ない)
    ///   2. そのフィールドが MailField であること
    /// を確かめてデザインを返す。送信インフラの呼び名はこのデザインの MailInfraName を使う。通らなければ LowCodeException。
    /// 行は読まないので、DB に繋がっていないモジュールや未保存のレコードからも送れる。
    /// </summary>
    static class MailFieldAuthorization
    {
        internal static async Task<MailFieldDesign> CheckAsync(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName)
        {
            moduleName ??= string.Empty;
            fieldName ??= string.Empty;
            return await moduleDataIO.CheckUserReadAuthorization(moduleName, fieldName) as MailFieldDesign
                ?? throw LowCodeException.Create(Resources.MailField_NotFound, moduleName, fieldName);
        }
    }
}
