using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 詳細レイアウトに配置できる Excel 帳票ダウンロードボタン。
    /// スクリプトの Excel オブジェクトで典型的な「テンプレート Excel の {{フィールド名}} プレースホルダを
    /// 自モジュールの値で置換してダウンロードする」処理を、スクリプトなしで実行する。
    /// テンプレートはアプリのリソース (Resources) に置く。出力は xlsx / PDF を選択できる
    /// (PDF はサーバーの変換エンドポイント等、Excel オブジェクトと同じ仕組みを使う)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "FileExcelOutline")]
    [Designer(DisplayName = "$ExcelReportButtonField")]
    public class ExcelReportButtonFieldDesign() : FieldDesignBase(typeof(ExcelReportButtonFieldDesign).FullName!)
    {
        /// <summary>テンプレート Excel のリソースパス。テンプレート内の {{フィールド名}} が自モジュールの値で置換される。</summary>
        [Designer(Index = 1, CandidateType = CandidateType.Resource, DisplayName = "$ExcelReportButtonTemplateResourcePath")]
        public string TemplateResourcePath { get; set; } = string.Empty;

        /// <summary>出力形式 (Excel / PDF)。</summary>
        [Designer(Index = 2, DisplayName = "$ExcelReportButtonFormat")]
        public ExcelReportFormat Format { get; set; } = ExcelReportFormat.Xlsx;

        /// <summary>ダウンロードファイル名 (拡張子は出力形式から自動付与)。空ならテンプレートのファイル名。</summary>
        [Designer(Index = 3, DisplayName = "$ExcelReportButtonDownloadFileName")]
        public string DownloadFileName { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(ExcelReportButtonFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new ExcelReportButtonField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            if (string.IsNullOrEmpty(TemplateResourcePath))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new() { Module = context.OwnerModule, Field = Name, Member = nameof(TemplateResourcePath) },
                    Message = Properties.Resources.ExcelReportButtonTemplateRequired
                });
            }
            return result;
        }
    }
}
