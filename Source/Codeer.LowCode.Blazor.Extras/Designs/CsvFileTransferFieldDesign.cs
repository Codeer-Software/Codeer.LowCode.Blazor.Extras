using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 一覧ページの一括ダウンロード/一括更新のファイル形式を Excel (xlsx) から CSV に切り替える設定用フィールド。
    /// モジュールの Fields に定義するだけで有効になる (レイアウト配置は不要)。
    /// クライアントは一括ダウンロードのファイル拡張子を切り替え (IBulkFileTransferFieldDesign)、
    /// サーバー側 (テンプレートから移譲される BulkFileTransfer) は同じモジュールデザインを参照して
    /// CSV 生成/取り込みに分岐する。
    /// 列構成は既定では内部名ヘッダ (FieldName.DataMemberName) のラウンドトリップ用。
    /// 相手仕様固定の列 (WebEDI 等) にするには <see cref="MappedFileTransferFieldDesign"/> を併用する
    /// (Mapped 単独なら Excel のまま列だけ差し替わる)。
    /// このフィールドを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    public class CsvFileTransferFieldDesign() : FieldDesignBase(typeof(CsvFileTransferFieldDesign).FullName!), IBulkFileTransferFieldDesign
    {
        /// <summary>CSV のエンコーディング。既定は UTF-8 (BOM 付き。Excel でダブルクリックしても文字化けしない)。</summary>
        [Designer]
        public CsvEncodingKind Encoding { get; set; } = CsvEncodingKind.Utf8Bom;

        /// <summary>CSV の区切り文字。既定はカンマ。</summary>
        [Designer]
        public CsvDelimiterKind Delimiter { get; set; } = CsvDelimiterKind.Comma;

        /// <summary>一括ダウンロードのファイル拡張子 (例 "csv" / "txt")。</summary>
        [Designer]
        public string FileExtension { get; set; } = "csv";

        //本体クライアントが一括ダウンロードのファイル名の拡張子として参照する。未設定なら "csv"
        string IBulkFileTransferFieldDesign.Extension => string.IsNullOrEmpty(FileExtension) ? "csv" : FileExtension;

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
