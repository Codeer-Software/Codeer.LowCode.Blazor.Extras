using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 二要素認証 (TOTP) の秘密鍵列の宣言。ランタイムでは値もデータも持たない
    /// (列は書き込み専用でクライアントへ読み出されず、書き込みはサーバーの TotpLogin がログイン時に直接行う)。
    /// </summary>
    public class TotpSecretField(TotpSecretFieldDesign design) : FieldBase<TotpSecretFieldDesign>(design)
    {
        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
    }
}
