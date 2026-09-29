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
        private const int CodeOwnedRecordsNotHeld = 4;
        private const int CodeOwnedRecordPathNotFound = 5;
        private const int CodeIndividualRowModuleNoHistory = 6;
        private const int CodeOwnedRecordPathInBoth = 7;
        private const int CodeOnHistoryModule = 8;
        private const int CodeOnQueryModule = 9;
        private const int CodeOnListLayout = 10;

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

        /// <summary>
        /// 履歴に含めない従属レコード (従属宣言の名前。子・孫・埋め込みの中は "Items.Details" のようにドット区切り)。
        /// 記録・差分・版表示・復元のすべてから外れる (件数の多い一覧を履歴に載せないときなど)。
        /// </summary>
        [Designer(Index = 6, DisplayName = "$EditHistoryExcludedOwnedRecords")]
        public List<string> ExcludedOwnedRecords { get; set; } = [];

        /// <summary>
        /// 行ごとに記録する従属レコード (同じくパス)。親の版には含めず、親の保存に乗った行を、行のモジュール自身の履歴
        /// (そのモジュールに置いた EditHistoryField) に 1 行 1 版で記録する。行の差分・版表示・復元は行のモジュールの画面で行う。
        /// </summary>
        [Designer(Index = 7, DisplayName = "$EditHistoryIndividuallyRecordedOwnedRecords")]
        public List<string> IndividuallyRecordedOwnedRecords { get; set; } = [];

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
            //履歴モジュール自身の履歴は取らない (履歴の履歴)。Query モジュールは行の定義が SQL 自身で、記録・行条件の判定ができない
            if (ownModule != null && ownModule.Fields.Any(e => e is EditHistoryContractFieldDesign))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOnHistoryModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.EditHistoryCheck_OnHistoryModule,
                });
            }
            if (ownModule != null && ownModule.Fields.Any(e => e is QueryFieldDesign))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOnQueryModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.EditHistoryCheck_OnQueryModule,
                });
            }
            //詳細レイアウト専用 (一覧の行では読まない = 行数分の読み込みになるため)
            foreach (var (layoutName, layout) in ownModule?.ListLayouts ?? new())
            {
                if (!layout.Elements.SelectMany(e => e).Any(e => e.FieldName == Name)) continue;
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOnListLayout),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.EditHistoryCheck_OnListLayout, layoutName),
                });
            }
            if (ownModule != null) CheckOwnedRecords(context, ownModule, result);
            return result;
        }

        void CheckOwnedRecords(DesignCheckContext context, ModuleDesign ownModule, List<DesignCheckInfo> result)
        {
            //除外・行ごとのパスは従属宣言に実在すること (含めない宣言の先も含めて全部辿る)
            var declared = EditHistoryPolicy.Walk(context.DesignData, ownModule, this, descendIntoNotIncluded: true).ToList();
            foreach (var (member, paths) in new[] { (nameof(ExcludedOwnedRecords), ExcludedOwnedRecords), (nameof(IndividuallyRecordedOwnedRecords), IndividuallyRecordedOwnedRecords) })
            {
                foreach (var path in paths.Distinct())
                {
                    if (declared.Any(e => e.Path == path)) continue;
                    result.Add(new FieldDesignCheckInfo
                    {
                        Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOwnedRecordPathNotFound),
                        Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = member },
                        Message = string.Format(Properties.Resources.EditHistoryCheck_OwnedRecordPathNotFoundFormat, path),
                    });
                }
            }
            //同じ宣言を除外と行ごとの両方に書いたら、どちらにするか決めてもらう (両方だと行ごとの扱いになる)
            foreach (var path in ExcludedOwnedRecords.Intersect(IndividuallyRecordedOwnedRecords).Distinct())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOwnedRecordPathInBoth),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(IndividuallyRecordedOwnedRecords) },
                    Message = string.Format(Properties.Resources.EditHistoryCheck_OwnedRecordPathInBothFormat, path,
                        nameof(ExcludedOwnedRecords), nameof(IndividuallyRecordedOwnedRecords)),
                });
            }
            //行ごとに記録する先 (行のモジュール) には EditHistoryField が要る (そこの履歴モジュールに書く)
            foreach (var path in IndividuallyRecordedOwnedRecords.Distinct())
            {
                var child = declared.FirstOrDefault(e => e.Path == path).Child;
                if (child == null || EditHistoryContracts.Field(child) != null) continue;
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeIndividualRowModuleNoHistory),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(IndividuallyRecordedOwnedRecords) },
                    Message = string.Format(Properties.Resources.EditHistoryCheck_IndividualRowModuleNoHistoryFormat, path, child.Name, nameof(EditHistoryFieldDesign)),
                });
            }
            //「この版に戻す」は従属レコードを版の全件で差し替える (本体の IOwnedRecordsField は持っている行に対して行う) ので、
            //全件を持たない宣言 (サーバーページングの一覧等。宣言の HoldsAllRecords) は親の版に含められない。含めるものだけ見る (除外・行ごとは対象外)。
            //同じ一覧に複数の経路で辿り着いても 1 回だけ
            var reported = new HashSet<(string, string)>();
            foreach (var (path, module, field, owned, _) in EditHistoryPolicy.Walk(context.DesignData, ownModule, this))
            {
                if (owned.HoldsAllRecords || !EditHistoryPolicy.IsIncluded(this, path) || !reported.Add((module.Name, field.Name))) continue;
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryFieldDesign), CodeOwnedRecordsNotHeld),
                    Location = new FieldDesignDataLocation { Module = module.Name, Field = field.Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.EditHistoryCheck_OwnedRecordsNotHeldFormat, module.Name, field.Name, context.OwnerModule),
                });
            }
        }

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context))
                .AddModule(HistoryModuleName, x => HistoryModuleName = x)
                .AddLayout(context.OwnerModule, ModuleLayoutType.Detail, LayoutName, x => LayoutName = x);
            //従属レコードのパス (宣言の名前 = そのモジュールのフィールド名。ドット区切りの各段) もフィールドの改名に追従する
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null)
            {
                var declared = EditHistoryPolicy.Walk(context.DesignData, ownModule, this, descendIntoNotIncluded: true).ToList();
                AddOwnedRecordPaths(builder, declared, ExcludedOwnedRecords);
                AddOwnedRecordPaths(builder, declared, IndividuallyRecordedOwnedRecords);
            }
            return builder.Build();
        }

        static void AddOwnedRecordPaths(RenameContext.RenameResultBuilder builder,
            List<(string Path, ModuleDesign Module, FieldDesignBase Field, OwnedRecordsDesign Owned, ModuleDesign? Child)> declared, List<string> paths)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var parts = paths[i].Split('.');
                for (var j = 0; j < parts.Length; j++)
                {
                    var index = i;
                    var segment = j;
                    var prefix = string.Join(".", parts.Take(j + 1));
                    var (_, module, field, owned, _) = declared.FirstOrDefault(e => e.Path == prefix);
                    if (module == null) continue;
                    //宣言の名前がフィールド名そのもの、またはフィールド名に接尾辞を付けたもの (Gantt の "Gantt:Dependencies" 等)
                    var suffix = owned.Name == field.Name ? string.Empty
                        : owned.Name.StartsWith(field.Name + ":") ? owned.Name[field.Name.Length..] : null;
                    if (suffix == null) continue;
                    builder.AddField(module.Name, field.Name, x =>
                    {
                        var current = paths[index].Split('.');
                        current[segment] = x + suffix;
                        paths[index] = string.Join(".", current);
                    });
                }
            }
        }
    }
}
