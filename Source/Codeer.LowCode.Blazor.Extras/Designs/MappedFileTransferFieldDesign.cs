using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// マッピング列の定義。ファイル上の列位置 = <see cref="MappingColumns.Items"/> 内の並び順。
    /// </summary>
    public class MappingColumn
    {
        /// <summary>外部ファイルでの列名 (HasHeader 時にヘッダ行へ出力される。取込は列位置で対応付ける)。</summary>
        public string ExternalName { get; set; } = string.Empty;

        /// <summary>対応する内部フィールド ("フィールド名.データメンバ名" 形式。例 "Customer.Value")。空なら取込時は無視、出力時は FixedValue を出す。</summary>
        public string Field { get; set; } = string.Empty;

        /// <summary>日付/数値の書式 (例 "yyyyMMdd")。出力時は書式化、取込時は書式でパースする。コード変換と併用不可 (変換が優先)。</summary>
        public string Format { get; set; } = string.Empty;

        /// <summary>出力時の固定値 (Field が空の列で使う。取引先コードなど)。</summary>
        public string FixedValue { get; set; } = string.Empty;

        /// <summary>コード変換表のモジュール名 (変換表はただの業務モジュール。空なら変換なし)。</summary>
        public string ConversionModule { get; set; } = string.Empty;

        /// <summary>変換表の外部コード側フィールド名 (例 "EdiCode")。</summary>
        public string ConversionExternalField { get; set; } = string.Empty;

        /// <summary>変換表の内部値側フィールド名 (例 "CustomerCode")。</summary>
        public string ConversionInternalField { get; set; } = string.Empty;
    }

    /// <summary>マッピング列のコレクション (デザイナのカスタムプロパティ編集用ラッパー)。</summary>
    public class MappingColumns : ICurrentSettingsText
    {
        public List<MappingColumn> Items { get; set; } = [];

        public string GetCurrentSettings()
            => string.Join(", ", Items.Select(e => string.IsNullOrEmpty(e.ExternalName) ? e.Field : e.ExternalName));
    }

    /// <summary>
    /// 一覧ページの一括ダウンロード/一括更新の列構成を「相手仕様固定の列」(WebEDI・他システム連携等) に切り替えるフィールド。
    /// 列の並び・外部列名・書式・固定値・コード変換 (変換表 = ただの業務モジュール) を宣言し、
    /// サーバー側 (BulkFileTransfer) が内部形式との相互変換を行う。
    /// ファイル形式とは独立した機能で、単独なら Excel (xlsx) のまま列だけ差し替わり、
    /// <see cref="CsvFileTransferFieldDesign"/> と併用すると CSV になる (WebEDI 向け)。
    /// このフィールドを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    public class MappedFileTransferFieldDesign() : FieldDesignBase(typeof(MappedFileTransferFieldDesign).FullName!)
    {
        /// <summary>ファイルにヘッダ行があるか。出力時は ExternalName を1行目に出し、取込時は1行目を読み飛ばす。</summary>
        [Designer]
        public bool HasHeader { get; set; } = true;

        /// <summary>列マッピング (並び順 = ファイルの列位置)。</summary>
        [Designer]
        public MappingColumns Columns { get; set; } = new();

        /// <summary>一括更新で受け付ける最大データ行数 (ヘッダ行を除く)。</summary>
        [Designer]
        public int MaxRows { get; set; } = 500;

        public override string GetWebComponentTypeFullName() => typeof(MappedFileTransferFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new MappedFileTransferField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            //マッピングの参照先 (自モジュールのフィールド・コード変換モジュールとそのフィールド) が存在するか
            var index = 0;
            foreach (var col in Columns.Items)
            {
                var member = $"{nameof(Columns)}[{index++}]";
                if (!string.IsNullOrEmpty(col.Field))
                    context.CheckFieldRelativeFieldExistence(Name, member, context.OwnerModule, col.Field.Split('.')[0]).AddTo(result);
                if (!string.IsNullOrEmpty(col.ConversionModule))
                {
                    context.CheckFieldModuleExistence(Name, member, col.ConversionModule).AddTo(result);
                    context.CheckFieldRelativeFieldExistence(Name, member, col.ConversionModule, col.ConversionExternalField).AddTo(result);
                    context.CheckFieldRelativeFieldExistence(Name, member, col.ConversionModule, col.ConversionInternalField).AddTo(result);
                }
            }
            return result;
        }
    }
}
