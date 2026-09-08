using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class MarkdownFieldData() : ValueFieldDataBase<string>(typeof(MarkdownFieldData).FullName!), ICloneable<MarkdownFieldData>
    {
        public MarkdownFieldData Clone() => (MarkdownFieldData)MemberwiseClone();
    }
}
