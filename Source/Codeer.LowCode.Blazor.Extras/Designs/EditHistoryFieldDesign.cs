using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 編集履歴フィールド。モジュールに 1 つ置くと、そのモジュールのレコードの保存 (作成・更新・削除) ごとに
    /// レコード全体 (従属レコード込み) のスナップショットが履歴モジュールへ記録される (サーバーの EditHistoryRecorder)。
    /// 詳細画面では版の一覧 (変更されたフィールドの 旧 → 新) と「この版を表示」「この版に戻す」を提供する。
    /// 「戻す」は過去の内容を編集中のフォームへ反映するだけで、ユーザーが保存して確定する
    /// (権限・検証・楽観ロック・復元自体の履歴記録が全部通常の保存経路で済む)。
    /// 履歴モジュールのフィールド名は、そのモジュールに置いた EditHistoryContractField の役割で解決する。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "History")]
    [Designer(DisplayName = "$EditHistoryField")]
    public class EditHistoryFieldDesign : FieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeContractFieldMissing = 1;
        private const int CodeDuplicated = 2;
        private const int CodeDeleteArchive = 3;

        public EditHistoryFieldDesign() : base(typeof(EditHistoryFieldDesign).FullName!) { }

        /// <summary>履歴を書く先のモジュール (EditHistoryContractField を置いたモジュール)。複数の対象モジュールで共有してよい。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Module, DisplayName = "$EditHistoryHistoryModuleName")]
        public string HistoryModuleName { get; set; } = "EditHistory";

        /// <summary>「この版を表示」で過去の版を表示するときの自モジュールの詳細レイアウト。空 = 既定。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DetailLayout, DisplayName = "$EditHistoryLayoutName")]
        public string LayoutName { get; set; } = string.Empty;

        /// <summary>一度に読み込む版の数 (「さらに表示」で次を読む)。</summary>
        [Designer(Index = 5, DisplayName = "$EditHistoryPageSize")]
        public int PageSize { get; set; } = 20;

        public override string GetWebComponentTypeFullName() => typeof(EditHistoryFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new EditHistoryField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldModuleExistence(Name, nameof(HistoryModuleName), HistoryModuleName).AddTo(result);

            var historyModule = context.DesignData.Modules.Find(HistoryModuleName);
            if (historyModule != null && EditHistoryContracts.Contract(historyModule) == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeContractFieldMissing),
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(HistoryModuleName) },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                        HistoryModuleName, nameof(EditHistoryContractFieldDesign)),
                });
            }

            //1 モジュールに 1 つ (記録は 1 か所へ・解決が曖昧にならないように)
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && ownModule.Fields.Count(e => e is EditHistoryFieldDesign) > 1)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeDuplicated),
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldDuplicatedFormat,
                        nameof(EditHistoryFieldDesign)),
                });
            }
            //履歴と退避 (削除テーブルへの移動) は「消したものを取っておく」置き場が 2 つになるので併用しない
            if (ownModule != null && ownModule.Fields.Any(e => e is DeleteArchiveFieldDesign))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeDeleteArchive),
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.EditHistoryCheck_DeleteArchive,
                });
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
            => context.Builder(base.ChangeName(context))
                .AddModule(HistoryModuleName, x => HistoryModuleName = x)
                .Build();
    }
}
