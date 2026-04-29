using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class ColorPickerField(ColorPickerFieldDesign design)
        : ValueFieldBase<ColorPickerFieldDesign, ColorPickerFieldData, string>(design)
    {
        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }
    }
}
