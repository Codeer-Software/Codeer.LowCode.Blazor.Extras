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

        /// <summary>CSV の区切り文字。既定はカンマ。タブは "\t" と書く。</summary>
        [Designer]
        public string Delimiter { get; set; } = ",";

        /// <summary>一括ダウンロードのファイル拡張子 (例 "csv" / "txt")。</summary>
        [Designer]
        public string FileExtension { get; set; } = "csv";

        /// <summary>一括更新で受け付ける最大データ行数 (ヘッダ行を除く)。</summary>
        [Designer]
        public int MaxRows { get; set; } = 500;

        /// <summary>一括ダウンロードのファイル拡張子。</summary>
        public string Extension => string.IsNullOrEmpty(FileExtension) ? "csv" : FileExtension;

        /// <summary>区切り文字の実効値 ("\t" 表記をタブに解決。不正なら ',')。</summary>
        public char GetDelimiterChar()
        {
            if (Delimiter == "\\t" || Delimiter == "\t") return '\t';
            return string.IsNullOrEmpty(Delimiter) ? ',' : Delimiter[0];
        }

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
