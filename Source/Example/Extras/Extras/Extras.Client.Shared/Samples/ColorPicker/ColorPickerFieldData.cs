using Codeer.LowCode.Blazor.Repository.Data;

namespace Extras.Client.Shared.Samples.ColorPicker
{
    public class ColorPickerFieldData() : ValueFieldDataBase<string>(typeof(ColorPickerFieldData).FullName!), ICloneable<ColorPickerFieldData>
    {
        public ColorPickerFieldData Clone() => (ColorPickerFieldData)MemberwiseClone();
    }
}
