using Codeer.LowCode.Blazor.Components.Dialog.BootstrapButtons;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using R = Codeer.LowCode.Blazor.Extras.Properties.Resources;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 編集履歴フィールドのランタイム。データも DB 列も持たない (記録はサーバーの EditHistoryRecorder)。
    /// 詳細画面で自レコードの履歴を履歴モジュールから読み (通常のモジュールデータ API = 閲覧権限は履歴モジュールの設定)、
    /// 版ごとの差分・過去版の表示・フォームへの復元を提供する。
    /// </summary>
    public class EditHistoryField : FieldBase<EditHistoryFieldDesign>
    {
        /// <summary>「この版を表示」で変更フィールド (セル) に付ける CSS クラス。</summary>
        public const string ChangedClassName = "edit-history-changed";

        /// <summary>「この版を表示」で追加された明細行に付ける CSS クラス。</summary>
        public const string AddedRowClassName = "edit-history-added-row";

        /// <summary>「この版を表示」で削除された明細行 (前の版の内容を打ち消しで残す) に付ける CSS クラス。</summary>
        public const string RemovedRowClassName = "edit-history-removed-row";
        /// <summary>版表示ダイアログの中身のモジュールに付けるクラス。ダイアログの幅を一定にする CSS の目印。</summary>
        public const string VersionDialogClassName = "edit-history-version-dialog";

        readonly List<EditHistoryVersion> _versions = new();
        readonly List<EditHistoryUndeleteTarget> _pendingUndeletes = new();
        int _pageIndex;

        public EditHistoryField(EditHistoryFieldDesign design) : base(design) { }

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        //復元で Id を保って戻した論理削除の行があれば、保存に「削除の取り消し」を同梱する (サーバーの EditHistoryRecorder が base の前に戻す)
        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
        {
            var submit = new FieldSubmitData();
            if (_pendingUndeletes.Count > 0)
                submit.ExtendedData.Add(new EditHistoryUndeleteData { Targets = _pendingUndeletes.ToList() });
            return submit;
        }

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _pendingUndeletes.Clear();
            IsLoaded = false;
            _versions.Clear();
            _pageIndex = 0;
            TotalCount = 0;

            //詳細ページでは初期化時点で読み込む (一覧の行では読まない = 行数分のリクエストになるため)
            if (IsAvailable && ModuleLayoutType == ModuleLayoutType.Detail)
            {
                await ReloadAsync();
            }
        }

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        /// <summary>読み込み済みの版 (新しい順)。</summary>
        [ScriptHide]
        public IReadOnlyList<EditHistoryVersion> Versions => _versions;

        public bool IsLoaded { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>このレコードの版の総数 (履歴モジュールで閲覧できるもの)。</summary>
        public int TotalCount { get; private set; }

        public bool HasMore => _versions.Count < TotalCount;

        /// <summary>履歴を読める状態か (デザインモード・未保存のレコードでは読まない)。</summary>
        [ScriptHide]
        public bool IsAvailable => !Services.AppInfoService.IsDesignMode && !Module.IsNewData;

        /// <summary>復元 (フォームへの反映) ができるか。表示専用・未保存では不可。</summary>
        [ScriptHide]
        public bool CanRestore => !Module.IsViewOnly && !Module.IsNewData;

        ModuleDesign? HistoryModule => Services.AppInfoService.GetDesignData().Modules.Find(Design.HistoryModuleName);

        //契約 (役割→フィールド名)。契約が無ければ既定名
        EditHistoryContractFieldDesign Names => EditHistoryContracts.Contract(HistoryModule) ?? new();

        /// <summary>履歴を先頭から読み直す。</summary>
        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            _versions.Clear();
            _pageIndex = 0;
            await LoadPageAsync();
        }

        /// <summary>次のページ (古い版) を読む。</summary>
        [ScriptName("LoadMore")]
        public async Task LoadMoreAsync()
        {
            if (!HasMore) return;
            await LoadPageAsync();
        }

        async Task LoadPageAsync()
        {
            if (!IsAvailable || IsBusy || HistoryModule == null) return;
            IsBusy = true;
            try
            {
                var pageSize = Math.Max(1, Design.PageSize);
                //ページの次の 1 行も同時に読む (ページ末尾の版の差分の元 = 1 つ前の版)。
                //LimitCount=1 のときの PageIndex は行オフセットになる
                var pages = await Services.ModuleDataService.GetListAsync(new List<GetListRequest>
                {
                    new() { Condition = CreateCondition(pageSize), PageIndex = _pageIndex },
                    new() { Condition = CreateCondition(1), PageIndex = (_pageIndex + 1) * pageSize },
                });
                var page = pages[0];
                var older = pages[1].Items.FirstOrDefault();
                TotalCount = page.TotalCount;

                var number = TotalCount - _pageIndex * pageSize;
                var items = page.Items;
                for (var i = 0; i < items.Count; i++)
                {
                    var version = ToVersion(items[i], number--);
                    var previous = i + 1 < items.Count ? items[i + 1] : older;
                    version.Changes = ComputeChanges(previous, version);
                    _versions.Add(version);
                }
                _pageIndex++;
                IsLoaded = true;
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
        }

        SearchCondition CreateCondition(int limitCount)
        {
            var names = Names;
            var condition = new SearchCondition
            {
                ModuleName = Design.HistoryModuleName,
                Condition = MultiMatchCondition.And(
                    Equal(names.ModuleName, Module.Design.Name),
                    Equal(names.DataId, Module.GetIdText())),
                LimitCount = limitCount,
                SortConditions = new List<SortCondition>(),
                SelectFields = new[] { SystemFieldNames.Id, names.ChangeType, names.Snapshot, names.UserId, names.DateTime }
                    .Where(e => !string.IsNullOrEmpty(e)).ToList(),
            };
            //新しい順。日時があれば日時 (Id が連番でない DB でも正しく並ぶ)、Id で同着を決める
            if (!string.IsNullOrEmpty(names.DateTime))
                condition.SortConditions.Add(new SortCondition { Variable = $"{names.DateTime}.Value", IsDescending = true });
            condition.SortConditions.Add(new SortCondition { Variable = $"{SystemFieldNames.Id}.Value", IsDescending = true });
            return condition;
        }

        static FieldValueMatchCondition Equal(string fieldName, string value) => new()
        {
            SearchTargetVariable = $"{fieldName}.Value",
            Comparison = MatchComparison.Equal,
            Value = MultiTypeValue.Create(value),
        };

        EditHistoryVersion ToVersion(ModuleData row, int number)
        {
            var names = Names;
            var user = row.Fields.GetValueOrDefault(names.UserId);
            return new EditHistoryVersion
            {
                Id = EditHistorySnapshot.GetId(row),
                Number = number,
                ChangeType = GetString(row, names.ChangeType),
                UserText = (user as LinkFieldData)?.DisplayText ?? (user as ValueFieldDataBase<string>)?.Value ?? string.Empty,
                DateTime = (row.Fields.GetValueOrDefault(names.DateTime) as DateTimeFieldData)?.Value,
                Snapshot = EditHistorySnapshot.Deserialize(GetString(row, names.Snapshot)),
            };
        }

        static string GetString(ModuleData row, string fieldName)
            => string.IsNullOrEmpty(fieldName) ? string.Empty
                : (row.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value ?? string.Empty;

        //削除の版は「削除前の内容」なので、その前の版 (更新後の内容) と比べれば差分は無いのが普通。
        //作成の版は前が無いので、値のあるフィールド全部が差分になる
        List<EditHistoryChange> ComputeChanges(ModuleData? previousRow, EditHistoryVersion version)
        {
            //作成の版は「レコードが作成されました」だけ (全項目を並べても読めない。内容は「この版を表示」)
            if (version.Snapshot == null || version.IsDelete || version.IsAdd || version.IsRestore) return new();
            if (previousRow == null)
            {
                //履歴を取り始める前からあったレコードの最初の更新: 比べる版が無い (内容は「この版を表示」で見る)
                version.HasPreviousVersion = false;
                return new();
            }
            var previous = EditHistorySnapshot.Deserialize(GetString(previousRow, Names.Snapshot));
            return EditHistoryDiff.Compute(Services.AppInfoService.GetDesignData(), Module.Design, previous, version.Snapshot, CanRead);
        }

        //閲覧権限のないフィールドは差分にも出さない (このモジュール上のフィールドの権限で判定)
        bool CanRead(string fieldName) => Module.GetField(fieldName)?.HasUserReadPermission != false;

        /// <summary>その版のレコード全体を表示専用のダイアログで表示する。変更フィールドを強調する。</summary>
        [ScriptHide]
        public async Task ShowVersionAsync(EditHistoryVersion version)
        {
            var snapshot = version.Snapshot;
            if (snapshot == null) return;

            //一覧も、別モジュールを自分で読む拡張フィールド (Gantt / Calendar / TaskBoard / MarkerList) も、
            //現在の DB を読ませずにスナップショットの行を見せる (拡張フィールドは下で ShowOwnedRecordsAsync に渡す)
            var module = await this.CreateChildModuleAsync(Module.Design.Name, ModuleLayoutType.Detail, Design.LayoutName, m =>
            {
                foreach (var field in m.GetFields())
                {
                    switch (field)
                    {
                        case ListField list: list.AllowLoad = false; break;
                        case GanttField gantt: gantt.AllowLoad = false; break;
                        case CalendarField calendar: calendar.AllowLoad = false; break;
                        case TaskBoardField board: board.AllowLoad = false; break;
                        case MarkerListField markers: markers.AllowLoad = false; break;
                    }
                }
                return Task.CompletedTask;
            });
            await module.SetDataWithoutInteractionAsync(snapshot);
            //従属レコードを宣言した拡張フィールドには版の行をそのまま見せる (強調は無し)
            foreach (var (fieldDesign, owned) in EditHistoryContracts.OwnedRecords(Module.Design))
            {
                if (module.GetField(fieldDesign.Name) is IOwnedRecordsField ownedField
                    && snapshot.Fields.TryGetValue(owned.Name, out var ownedData) && ownedData is ListFieldData ownedRows)
                {
                    await ownedField.ShowOwnedRecordsAsync(owned.Name, ownedRows.Children);
                }
            }
            module.IsViewOnly = true;
            //過去の内容の表示専用: 保存ボタン (押すと新しいレコードとして送られてしまう) と履歴フィールド自身は出さない
            foreach (var field in module.GetFields())
            {
                if (field is SubmitButtonField or EditHistoryField) field.IsVisible = false;
            }
            module.DialogTitle = string.Format(R.EditHistoryVersionFormat, version.Number);
            module.ClassName = VersionDialogClassName;
            await ApplyHighlightsAsync(module, version.Changes);
            await module.ShowDialogAsync(new SecondaryOutlineButton(R.EditHistoryClose));
        }

        //変更フィールドはセル単位で強調。明細は 変更行=変わったセルだけ / 追加行=行全体 / 削除行=前の版の内容を打ち消しで差し込む。孫も同じ
        static async Task ApplyHighlightsAsync(Module module, List<EditHistoryChange> changes)
        {
            foreach (var change in changes)
            {
                if (!change.IsList)
                {
                    var field = module.GetField(change.FieldName);
                    if (field != null) field.ClassName = AddClass(field.ClassName, ChangedClassName);
                    continue;
                }

                var list = module.GetField<ListField>(change.FieldName);
                if (list == null) continue;
                //ダイアログの行はスナップショットの並びで作られている (Id は振り直されるので行番号で対応づける)
                var rows = list.Rows;
                foreach (var rowChange in change.Rows.Where(e => e.Kind != EditHistoryRowChangeKind.Removed))
                {
                    if (rowChange.RowNumber - 1 >= rows.Count) continue;
                    var row = rows[rowChange.RowNumber - 1];
                    if (rowChange.Kind == EditHistoryRowChangeKind.Added) row.ClassName = AddClass(row.ClassName, AddedRowClassName);
                    else await ApplyHighlightsAsync(row, rowChange.Changes);
                }
                //削除行: 前の版の位置に差し込む (表示専用なので保存には載らない)
                foreach (var rowChange in change.Rows.Where(e => e.Kind == EditHistoryRowChangeKind.Removed && e.Row != null).OrderBy(e => e.RowNumber))
                {
                    var ghosts = await list.InsertRowsAsync(Math.Min(rowChange.RowNumber - 1, list.RowCount), [StripForGhost(rowChange.Row!)]);
                    foreach (var ghost in ghosts) MarkRemoved(ghost);
                }
            }
        }

        static void MarkRemoved(Module row)
        {
            row.ClassName = AddClass(row.ClassName, RemovedRowClassName);
            foreach (var list in row.GetFields().OfType<ListField>())
                foreach (var child in list.Rows) MarkRemoved(child);
        }

        static string AddClass(string current, string className)
            => string.IsNullOrEmpty(current) ? className : $"{current} {className}";

        //幽霊行は新しい行として作る (Id・楽観ロック等のシステム値を外す。孫の一覧はそのまま = 行ごと打ち消し)
        static ModuleData StripForGhost(ModuleData src)
        {
            var copy = src.JsonClone();
            foreach (var name in copy.Fields.Keys.Where(e => EditHistoryContracts.IsExcludedField(e)).ToList())
                copy.Fields.Remove(name);
            return copy;
        }

        /// <summary>その版の内容を編集中のフォームへ反映する (保存はユーザーが行う)。</summary>
        [ScriptHide]
        public async Task RestoreAsync(EditHistoryVersion version)
        {
            var snapshot = version.Snapshot;
            if (snapshot == null || !CanRestore) return;

            var answer = await Services.UIService.ShowMessageBox(
                R.EditHistoryRestoreVersion,
                string.Format(R.EditHistoryRestoreConfirmFormat, version.Number),
                [new PrimaryButton(R.EditHistoryRestore, true), new SecondaryOutlineButton(R.EditHistoryCancel)]);
            if (answer != R.EditHistoryRestore) return;

            await EditHistoryRestorer.ApplyAsync(Module, snapshot,
                (moduleName, id) => _pendingUndeletes.Add(new EditHistoryUndeleteTarget { ModuleName = moduleName, Id = id }));
            await Services.UIService.NotifySuccess(string.Format(R.EditHistoryRestoredFormat, version.Number));
            NotifyStateChanged();
        }
    }
}
