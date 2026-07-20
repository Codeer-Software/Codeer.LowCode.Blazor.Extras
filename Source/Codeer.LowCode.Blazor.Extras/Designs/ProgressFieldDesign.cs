using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [ToolboxIcon(PackIconMaterialKind = "ProgressCheck")]
    [Designer(DisplayName = "$ProgressField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class ProgressFieldDesign() : FieldDesignBase(typeof(ProgressFieldDesign).FullName!), IDataDependentField
    {
        /// <summary>表示形式 (横バー / 半円メーター)。</summary>
        [Designer(DisplayName = "$ProgressFieldDisplayType")]
        public ProgressDisplayType DisplayType { get; set; } = ProgressDisplayType.Bar;

        /// <summary>進捗値を取得する参照フィールド (数値)。</summary>
        [Designer(CandidateType = CandidateType.Field, DisplayName = "$ProgressFieldValueField")]
        [TargetFieldType(Types = [typeof(NumberFieldDesign)])]
        public string ValueField { get; set; } = string.Empty;

        /// <summary>値のスケール解釈。Percent=そのまま% (100で100%)、Ratio=割合 (1.0で100%)。</summary>
        [Designer(DisplayName = "$ProgressFieldScale")]
        public ProgressScale Scale { get; set; } = ProgressScale.Percent;

        /// <summary>バー色を取得する参照フィールド。空の場合は <see cref="BarColor"/> を使用。</summary>
        [Designer(CandidateType = CandidateType.Field, DisplayName = "$ProgressFieldColorField")]
        [TargetFieldType(Types = [typeof(TextFieldDesign), typeof(ColorPickerFieldDesign)])]
        public string ColorField { get; set; } = string.Empty;

        /// <summary>バー色の既定値 (<see cref="ColorField"/> 未設定/空のとき使用)。</summary>
        [Designer(CandidateType = CandidateType.Color, DisplayName = "$ProgressFieldBarColor")]
        public string BarColor { get; set; } = string.Empty;

        /// <summary>進捗率をバー上に表示するか。</summary>
        [Designer(DisplayName = "$ProgressFieldShowValueLabel")]
        public bool ShowValueLabel { get; set; } = true;

        public override string GetWebComponentTypeFullName() => typeof(ProgressFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new ProgressField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldFieldExistence(Name, nameof(ValueField), ValueField).AddTo(result);
            context.CheckFieldFieldExistence(Name, nameof(ColorField), ColorField).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(ValueField, x => ValueField = x)
            .AddField(ColorField, x => ColorField = x)
            .Build();

        public List<string> GetDependencyFields()
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(ValueField)) list.Add(ValueField);
            if (!string.IsNullOrEmpty(ColorField)) list.Add(ColorField);
            return list;
        }
    }
}
