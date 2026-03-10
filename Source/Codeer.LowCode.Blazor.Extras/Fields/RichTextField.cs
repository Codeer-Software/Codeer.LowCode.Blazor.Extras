using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class RichTextField(RichTextFieldDesign design)
        : ValueFieldBase<RichTextFieldDesign, RichTextFieldData, string>(design)
    {
        [ScriptHide]
        public override bool ValidateInput()
        {
            if (Design.IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            ClearError();
            return true;
        }
    }
}
