using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 詳細レイアウトに配置できる一括ダウンロード/一括更新ボタン。
    /// 一覧ページの一括ダウンロード/一括更新 (list_file / submit_by_file) と同じサーバー処理を利用する
    /// (ファイル形式は対象モジュールの <see cref="CsvFileFormatFieldDesign"/> /
    /// <see cref="FileColumnMappingFieldDesign"/> の定義に従う)。
    /// 対象データを決める条件ソースとして、<see cref="SearchFieldName"/> と <see cref="ListFieldName"/> の
    /// どちらか一方だけを設定して使う (画面に見えている検索結果/一覧が対象になる)。
    /// このフィールドを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "SwapVerticalBold")]
    [Designer(DisplayName = "$BulkFileTransferButtonField")]
    public class BulkFileTransferButtonFieldDesign() : FieldDesignBase(typeof(BulkFileTransferButtonFieldDesign).FullName!)
    {
        /// <summary>条件ソース: 検索フィールド名。その検索フィールドの現在の検索条件を使う。<see cref="ListFieldName"/> とどちらか一方だけを設定する。</summary>
        [Designer(Index = 1, Category = "$BulkFileTransferButtonSourceCategory", CandidateType = CandidateType.Field,
            DisplayName = "$BulkFileTransferButtonSearchFieldName")]
        [TargetFieldType(Types = [typeof(SearchFieldDesign)])]
        public string SearchFieldName { get; set; } = string.Empty;

        /// <summary>条件ソース: リストフィールド名 (List/DetailList/TileList)。そのリストの表示中の検索条件を使う (列・件数には縛られず全列/全件)。<see cref="SearchFieldName"/> とどちらか一方だけを設定する。</summary>
        [Designer(Index = 2, Category = "$BulkFileTransferButtonSourceCategory", CandidateType = CandidateType.Field,
            DisplayName = "$BulkFileTransferButtonListFieldName")]
        [TargetFieldType(Types = [typeof(ListFieldDesignBase)])]
        public string ListFieldName { get; set; } = string.Empty;

        /// <summary>一括ダウンロードボタンを表示するか。</summary>
        [Designer(Index = 3, DisplayName = "$CanBulkDataDownload")]
        public bool CanBulkDataDownload { get; set; } = true;

        /// <summary>一括更新 (アップロード) ボタンを表示するか。</summary>
        [Designer(Index = 4, DisplayName = "$CanBulkDataUpdate")]
        public bool CanBulkDataUpdate { get; set; } = true;

        /// <summary>一括更新が成功した後に呼ばれるスクリプトイベント。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.ScriptEvent, DisplayName = "$BulkFileTransferButtonOnUploaded")]
        public string OnUploaded { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(BulkFileTransferButtonFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new BulkFileTransferButtonField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            //条件ソース (検索フィールド名/リストフィールド名) はどちらか一方だけ設定する
            if (string.IsNullOrEmpty(SearchFieldName) == string.IsNullOrEmpty(ListFieldName))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new() { Module = context.OwnerModule, Field = Name },
                    Message = Properties.Resources.BulkFileTransferButtonConditionSourceInvalid
                });
            }

            context.CheckFieldFieldExistence(Name, nameof(SearchFieldName), SearchFieldName).AddTo(result);
            context.CheckFieldFieldInstanceType(Name, nameof(SearchFieldName), SearchFieldName, typeof(SearchFieldDesign)).AddTo(result);
            context.CheckFieldFieldExistence(Name, nameof(ListFieldName), ListFieldName).AddTo(result);
            context.CheckFieldFieldInstanceType(Name, nameof(ListFieldName), ListFieldName, typeof(ListFieldDesignBase)).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnUploaded), OnUploaded,
                context.GetScriptMethodAttribute(GetType(), nameof(OnUploaded))).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SearchFieldName, x => SearchFieldName = x)
            .AddField(ListFieldName, x => ListFieldName = x)
            .Build();
    }
}
