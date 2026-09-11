using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// MailField / BulkMailField の送信・プレビューの入口検査。クライアントの値を信用せず、
    ///   1. SourceModule / FieldName のフィールドを今のユーザーがユーザー権限だけで読めること (アプリアクセス条件・モジュールの UserReadCondition・
    ///      ユーザーだけで偽と確定する PermissionField の読取条件。本体の ModuleDataIO.CheckUserReadAuthorization。行は読まないので DataReadCondition と行依存の条件は見ない)
    ///   2. そのフィールドが MailField (一斉なら BulkMailField) であること
    /// を確かめてデザインを返す。送信インフラの呼び名はこのデザインの MailInfraName を使う。通らなければ LowCodeException。
    /// 行は読まないので、DB に繋がっていないモジュールや未保存のレコードからも送れる (一斉送信の宛先は別途 GetListAsync で読むので、宛先側の権限はそこで効く)。
    /// </summary>
    static class MailFieldAuthorization
    {
        internal static async Task<MailFieldDesign> CheckAsync(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName)
            => await CheckAsync<MailFieldDesign>(moduleDataIO, moduleName, fieldName, Resources.MailField_NotFound);

        internal static async Task<BulkMailFieldDesign> CheckBulkAsync(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName)
            => await CheckAsync<BulkMailFieldDesign>(moduleDataIO, moduleName, fieldName, Resources.BulkMailField_NotFound);

        static async Task<TDesign> CheckAsync<TDesign>(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName, string notFoundMessage)
            where TDesign : FieldDesignBase
        {
            moduleName ??= string.Empty;
            fieldName ??= string.Empty;
            return await moduleDataIO.CheckUserReadAuthorization(moduleName, fieldName) as TDesign
                ?? throw LowCodeException.Create(notFoundMessage, moduleName, fieldName);
        }
    }
}
