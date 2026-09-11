using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Properties;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// MailField / BulkMailField の送信・プレビューの入口検査 (<see cref="FieldApiAuthorization"/>)。
    /// フィールドが存在し今のユーザーに見えることを確かめてデザインを返す。送信インフラの呼び名はこのデザインの MailInfraName を使う。
    /// 行は読まないので、DB に繋がっていないモジュールや未保存のレコードからも送れる (一斉送信の宛先は別途 GetListAsync で読むので、宛先側の権限はそこで効く)。
    /// </summary>
    static class MailFieldAuthorization
    {
        internal static async Task<MailFieldDesign> CheckAsync(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName)
            => await FieldApiAuthorization.CheckAsync<MailFieldDesign>(moduleDataIO, moduleName, fieldName, Resources.MailField_NotFound);

        internal static async Task<BulkMailFieldDesign> CheckBulkAsync(ModuleDataIO moduleDataIO, string? moduleName, string? fieldName)
            => await FieldApiAuthorization.CheckAsync<BulkMailFieldDesign>(moduleDataIO, moduleName, fieldName, Resources.BulkMailField_NotFound);
    }
}
