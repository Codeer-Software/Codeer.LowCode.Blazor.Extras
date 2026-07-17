using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [ToolboxIcon(PackIconMaterialKind = "Qrcode")]
    [Designer(DisplayName = "$QrCodeField")]
    public class QrCodeFieldDesign() : FieldDesignBase(typeof(QrCodeFieldDesign).FullName!), IDataDependentField
    {
        /// <summary>
        /// QR化する文字列を取得する参照フィールド。省略時は <see cref="Text"/> を使用。
        /// いずれもスクリプトから <c>Field.Text</c> で上書き可能。
        /// </summary>
        [Designer(CandidateType = CandidateType.Field, DisplayName = "$QrCodeFieldSourceField")]
        public string SourceField { get; set; } = string.Empty;

        /// <summary>
        /// QR化する固定文字列 (<see cref="SourceField"/> 未設定時の初期値)。
        /// </summary>
        [Designer(DisplayName = "$QrCodeFieldText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>誤り訂正レベル。</summary>
        [Designer(DisplayName = "$QrCodeFieldEccLevel")]
        public QrCodeEccLevel EccLevel { get; set; } = QrCodeEccLevel.M;

        /// <summary>前景色 (モジュールの色)。</summary>
        [Designer(CandidateType = CandidateType.Color, DisplayName = "$QrCodeFieldDarkColor")]
        public string DarkColor { get; set; } = "#000000";

        /// <summary>背景色。</summary>
        [Designer(CandidateType = CandidateType.Color, DisplayName = "$QrCodeFieldLightColor")]
        public string LightColor { get; set; } = "#FFFFFF";

        public override string GetWebComponentTypeFullName() => typeof(QrCodeFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new QrCodeField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldFieldExistence(Name, nameof(SourceField), SourceField).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SourceField, x => SourceField = x)
            .Build();

        public List<string> GetDependencyFields()
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(SourceField)) list.Add(SourceField);
            return list;
        }
    }
}
