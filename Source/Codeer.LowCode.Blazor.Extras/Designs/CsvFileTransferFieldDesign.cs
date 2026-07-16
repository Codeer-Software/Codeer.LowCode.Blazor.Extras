using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 一覧ページの一括ダウンロード/一括更新のファイル形式を CSV に切り替えるフィールド。
    /// モジュールの Fields に定義するだけで有効になる (レイアウト配置は不要)。
    /// クライアントは一括ダウンロードのファイル拡張子を csv にし (IBulkFileTransferFieldDesign)、
    /// サーバー側 (テンプレートの list_file / submit_by_file) は同じモジュールデザインから
    /// このフィールドを参照して CSV 生成/取り込みに分岐する (CsvUtils)。
    /// このフィールドを使うアプリはサーバー側の対応実装が必要。
    /// </summary>
    public class CsvFileTransferFieldDesign() : FieldDesignBase(typeof(CsvFileTransferFieldDesign).FullName!), IBulkFileTransferFieldDesign
    {
        public enum CsvEncodingKind
        {
            Utf8Bom,
            Utf8,
            ShiftJis,
        }

        /// <summary>CSV のエンコーディング。既定は UTF-8 (BOM 付き。Excel でダブルクリックしても文字化けしない)。</summary>
        [Designer]
        public CsvEncodingKind Encoding { get; set; } = CsvEncodingKind.Utf8Bom;

        /// <summary>一括ダウンロードのファイル拡張子。</summary>
        public string Extension => "csv";

        public override string GetWebComponentTypeFullName() => typeof(CsvFileTransferFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new CsvFileTransferField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            return result;
        }
    }
}
