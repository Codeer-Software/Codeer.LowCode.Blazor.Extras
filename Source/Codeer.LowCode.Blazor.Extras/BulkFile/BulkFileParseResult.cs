using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.BulkFile
{
    /// <summary>
    /// 一括ファイル解析 (parse_file) の結果。クライアント (BulkFileReader) とサーバー (BulkFileTransfer.ParseFileAsync) で共用。
    /// 解釈できなかったセルは値未設定のまま Errors に載る (行全体は捨てない。
    /// トレーラ行などはエラーだらけの項目になるのでスクリプト側で捨てる)。
    /// </summary>
    public class BulkFileParseResult
    {
        /// <summary>解析した行ごとのモジュールデータ (ファイルの行順)。</summary>
        public List<ModuleData> Items { get; set; } = [];

        /// <summary>解釈できなかったセル (対象の Items インデックスとフィールド名付き)。</summary>
        public List<BulkFileCellError> Errors { get; set; } = [];
    }

    /// <summary>解釈できなかったセルの情報。</summary>
    public class BulkFileCellError
    {
        /// <summary>対象の <see cref="BulkFileParseResult.Items"/> のインデックス。</summary>
        public int ItemIndex { get; set; }

        /// <summary>ファイル上の行番号 (1 始まり。ヘッダ行を含めた位置)。</summary>
        public int FileRow { get; set; }

        /// <summary>取込先フィールド名 (SetError の対象。フィールドに対応付かないエラーは空)。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>列の表示名 (外部列名またはフィールド指定)。</summary>
        public string ColumnLabel { get; set; } = string.Empty;

        /// <summary>エラー内容 (行番号プレフィックスなし)。</summary>
        public string Message { get; set; } = string.Empty;
    }
}
