using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    internal static class TagContracts
    {
        internal static TagLinkContractFieldDesign? LinkContract(ModuleDesign? module)
            => module?.Fields.OfType<TagLinkContractFieldDesign>().FirstOrDefault();

        /// <summary>TagField のデータ (タグ付け行) のタグ名 (行の順)。タグ付けモジュールの契約が無ければ空。</summary>
        internal static List<string> TagNames(DesignData? design, TagFieldDesign field, ListFieldData data)
        {
            var link = LinkContract(design?.Modules.Find(field.TagModuleName));
            if (link == null) return new();
            return data.GetModules()
                .Select(row => (row.Fields.GetValueOrDefault(link.TagName) as TextFieldData)?.Value ?? string.Empty)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();
        }
    }
}
