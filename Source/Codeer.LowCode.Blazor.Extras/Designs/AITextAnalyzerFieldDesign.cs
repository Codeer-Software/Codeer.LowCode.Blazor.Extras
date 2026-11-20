using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [ToolboxIcon(PackIconMaterialKind = "HeadSnowflakeOutline")]
    [Designer(DisplayName = "$AITextAnalyzerField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class AITextAnalyzerFieldDesign() : FieldDesignBase(typeof(AITextAnalyzerFieldDesign).FullName!)
    {
        [Designer(CandidateType = CandidateType.MultilineString)]
        public string Remarks { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string DataImportCompleted { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.Field)]
        [TargetFieldType(Types = [typeof(FileFieldDesign)])]
        public string FileField { get; set; } = string.Empty;

        /// <summary>
        /// 解析に受け付けるファイル拡張子 ("pdf, jpg, png"。ドット有無・大文字小文字は不問)。
        /// FileField を連携している場合はその FileField の AllowedExtensions が優先 (保存できないファイルを解析しても意味がない)。
        /// どちらも空ならドキュメント解析 (Azure Document Intelligence) が扱える形式を既定にする。
        /// </summary>
        [Designer]
        public string AllowedExtensions { get; set; } = string.Empty;

        /// <summary>
        /// 解析するファイルが画像 (jpg / png / gif / webp) のとき、文書解析 (OCR) の結果に加えて画像そのものもモデルに渡す。
        /// OCR が落とす記号・小数点・似た形の文字をモデルが画像で確かめられる。画像の分だけトークンが増える。
        /// 食い違うときは画像を正とさせるので、細かい文字が詰まった帳票ではモデルの読み違いが OCR の正しい値を上書きすることがある。
        /// 画像を受け取れないモデルでは呼び出しが失敗し、OCR の結果だけでやり直す。
        /// </summary>
        [Designer]
        public bool SendImage { get; set; }

        /// <summary>ドキュメント解析 (Azure Document Intelligence) が扱えるファイル形式</summary>
        public static readonly IReadOnlyList<string> DocumentAnalysisExtensions =
            ["pdf", "jpg", "jpeg", "png", "bmp", "tiff", "tif", "heif", "docx", "xlsx", "pptx", "html"];

        /// <summary>実際に受け付ける拡張子 (連携 FileField の設定 → 自身の設定 → ドキュメント解析の既定 の順)</summary>
        public IReadOnlyList<string> GetAllowedExtensions(ModuleDesign? moduleDesign)
        {
            var fileField = moduleDesign?.Fields.FirstOrDefault(e => e.Name == FileField) as FileFieldDesign;
            var fromFileField = FileExtensionFilter.Parse(fileField?.AllowedExtensions);
            if (fromFileField.Any()) return fromFileField;
            var own = FileExtensionFilter.Parse(AllowedExtensions);
            return own.Any() ? own : DocumentAnalysisExtensions;
        }

        public bool IsAllowedFileName(ModuleDesign? moduleDesign, string? fileName)
            => FileExtensionFilter.IsAllowed(GetAllowedExtensions(moduleDesign), fileName);

        public override string GetWebComponentTypeFullName() => typeof(AITextAnalyzerFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new AITextAnalyzerField(this);
        public override FieldDataBase? CreateData() => null;
    }
}
