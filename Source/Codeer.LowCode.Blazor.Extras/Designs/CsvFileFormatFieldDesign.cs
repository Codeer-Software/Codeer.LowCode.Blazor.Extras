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
    /// 相手仕様固定の列 (WebEDI 等) にするには <see cref="FileColumnMappingFieldDesign"/> を併用する
    /// (列マッピング単独なら Excel のまま列だけ差し替わる)。
    /// さらに <see cref="Delimiter"/> を None (区切り文字なし) にすると固定長形式 (固定桁ファイル) になる。
    /// 列幅は列構成と不可分なため FileColumnMappingField の各列で設定し (併用必須 = デザインチェック)、
    /// 幅の単位 (<see cref="FixedLengthWidthUnit"/>)・エンコーディング・拡張子 ("dat" 等) はこのフィールドの指定が使われる。
    /// このフィールドを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    [Designer(DisplayName = "$CsvFileFormatField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class CsvFileFormatFieldDesign() : FieldDesignBase(typeof(CsvFileFormatFieldDesign).FullName!), IBulkFileTransferFieldDesign
    {
        /// <summary>CSV のエンコーディング。既定は UTF-8 (BOM 付き。Excel でダブルクリックしても文字化けしない)。</summary>
        [Designer(DisplayName = "$CsvFileFormatEncoding")]
        public CsvEncodingKind Encoding { get; set; } = CsvEncodingKind.Utf8Bom;

        /// <summary>区切り文字。既定はカンマ。None (区切り文字なし) にすると固定長形式になる。</summary>
        [Designer(DisplayName = "$CsvFileFormatDelimiter")]
        public CsvDelimiterKind Delimiter { get; set; } = CsvDelimiterKind.Comma;

        /// <summary>一括ダウンロードのファイル拡張子 (例 "csv" / "txt"。固定長形式なら "dat" 等)。</summary>
        [Designer(DisplayName = "$CsvFileFormatFileExtension")]
        public string FileExtension { get; set; } = "csv";

        /// <summary>
        /// 固定長形式 (Delimiter = None) での列幅の単位。既定は Byte (Shift_JIS の全角 = 2 バイト。全銀協等のレガシー固定長の典型)。
        /// 行の組み立ては併用必須の FileColumnMappingField の各列の FixedLengthWidth/Alignment/PaddingChar で行う。
        /// </summary>
        [Designer(DisplayName = "$CsvFileFormatFixedLengthWidthUnit")]
        public FixedLengthWidthUnitKind FixedLengthWidthUnit { get; set; } = FixedLengthWidthUnitKind.Byte;

        //本体クライアントが一括ダウンロードのファイル名の拡張子として参照する。未設定なら "csv"
        string IBulkFileTransferFieldDesign.Extension => string.IsNullOrEmpty(FileExtension) ? "csv" : FileExtension;

        public override string GetWebComponentTypeFullName() => typeof(CsvFileFormatFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new CsvFileFormatField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            //固定長形式 (区切り文字なし) は列幅の置き場である列マッピングが無いと成立しない
            if (Delimiter == CsvDelimiterKind.None &&
                context.GetModuleDesign()?.Fields.OfType<FileColumnMappingFieldDesign>().Any() != true)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new() { Module = context.OwnerModule, Field = Name, Member = nameof(Delimiter) },
                    Message = Properties.Resources.CsvFileFormatFixedLengthMappingRequired
                });
            }
            return result;
        }
    }
}
