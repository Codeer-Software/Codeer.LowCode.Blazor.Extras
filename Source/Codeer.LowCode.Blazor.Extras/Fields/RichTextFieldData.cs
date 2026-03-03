using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class RichTextFieldData() : ValueFieldDataBase<string>(typeof(RichTextFieldData).FullName!), ICloneable<RichTextFieldData>
    {
        public RichTextFieldData Clone() => (RichTextFieldData)MemberwiseClone();
    }
}
