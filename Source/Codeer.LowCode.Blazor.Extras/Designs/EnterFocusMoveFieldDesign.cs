using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    //非推奨: フォーカス制御機能 (FocusControlMode) で Enter によるフォーカス遷移を代替できる。
    //既存デザインのため読み込み・動作は維持するが、デザイナのツールボックス/種別選択では非表示になる。
    [Obsolete]
    [Designer(DisplayName = "$EnterFocusMoveField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class EnterFocusMoveFieldDesign() : FieldDesignBase(typeof(EnterFocusMoveFieldDesign).FullName!)
    {
        public override string GetWebComponentTypeFullName() => typeof(EnterFocusMoveFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new EnterFocusMoveField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            return result;
        }
    }
}
