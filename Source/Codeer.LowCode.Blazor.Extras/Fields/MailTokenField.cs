using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// メールトークン保存フィールドのランタイム。UI もデータ往復も持たない
    /// (トークンの書き込みはサーバー側の MailUserTokenHelper、読み取りは送信時のサーバー内部経路のみ)。
    /// </summary>
    public class MailTokenField(MailTokenFieldDesign design) : FieldBase<MailTokenFieldDesign>(design)
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
