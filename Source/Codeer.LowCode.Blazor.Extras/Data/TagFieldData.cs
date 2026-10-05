using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class TagFieldData() : ValueFieldDataBase<string>(typeof(TagFieldData).FullName!), ICloneable<TagFieldData>
    {
        public TagFieldData Clone() => (TagFieldData)MemberwiseClone();
    }
}
