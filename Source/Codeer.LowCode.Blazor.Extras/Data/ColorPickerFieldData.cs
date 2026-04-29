using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class ColorPickerFieldData() : ValueFieldDataBase<string>(typeof(ColorPickerFieldData).FullName!), ICloneable<ColorPickerFieldData>
    {
        public ColorPickerFieldData Clone() => (ColorPickerFieldData)MemberwiseClone();
    }
}
