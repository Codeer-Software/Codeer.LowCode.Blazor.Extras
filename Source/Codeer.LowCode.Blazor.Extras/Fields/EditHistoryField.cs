using Codeer.LowCode.Blazor.Components.Dialog;
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
        internal const string ChangedClassName = "edit-history-changed";

        /// <summary>「この版を表示」で追加された明細行に付ける CSS クラス。</summary>
        internal const string AddedRowClassName = "edit-history-added-row";

        /// <summary>「この版を表示」で削除された明細行 (前の版の内容を打ち消しで残す) に付ける CSS クラス。</summary>
        internal const string RemovedRowClassName = "edit-history-removed-row";

        /// <summary>
        /// 「この版を表示」で変更された従属レコードの行 (Gantt のタスク・カレンダーの予定・カードなど、セル単位の強調が出ない項目) に付ける CSS クラス。
        /// </summary>
        internal const string ChangedRowClassName = "edit-history-changed-row";
        /// <summary>版表示ダイアログの中身のモジュールに付けるクラス。ダイアログの幅を一定にする CSS の目印。</summary>
        internal const string VersionDialogClassName = "edit-history-version-dialog";
        /// <summary>「この版を表示」のダイアログの、編集中の内容をその版に置き換えるボタンに付けるクラス。</summary>
        internal const string RestoreButtonClassName = "edit-history-restore";

        readonly List<EditHistoryVersion> _versions = new();
        //「この版に置き換える」で Id を保って戻した論理削除の行がある版 (履歴行の Id)。保存に同梱し、サーバーがその版のスナップショットから行を戻す
        readonly List<string> _pendingUndeleteVersions = new();
        int _pageIndex;
        //版番号の基準にする件数 (最初のページを読んだときの総数。「さらに表示」の間に版が増えても番号がずれないように固定する)
        int _numberingTotal;
        //読み込み中に読み直しを頼まれた (自動保存の直後に「さらに表示」が動いている等)。読み込みが終わってから先頭から読み直す
        bool _reloadPending;
        //版の一覧を展開しているか。初期化 (画面を開いたとき) は件数だけを読み、ユーザーが「表示」を押してから版を読む
        //(版はスナップショット込みで重いので、開くたびに読まない)。保存で再初期化されても展開状態は保つ
        bool _isExpanded;

        public EditHistoryField(EditHistoryFieldDesign design) : base(design) { }

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        //復元で Id を保って戻した論理削除の行があれば、保存に「その版からの削除の取り消し」を同梱する (サーバーの EditHistoryRecorder が本体の保存の前に戻す)
        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
        {
            var submit = new FieldSubmitData();
            foreach (var historyRowId in _pendingUndeleteVersions)
                submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = Design.HistoryModuleName, HistoryRowId = historyRowId });
            return submit;
        }

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _pendingUndeleteVersions.Clear();
            IsLoaded = false;
            _versions.Clear();
            _pageIndex = 0;
            TotalCount = 0;

            //詳細ページでは初期化時点で読み込む (展開前は件数だけ。一覧の行では読まない = 行数分のリクエストになるため)
            if (IsAvailable && ModuleLayoutType == ModuleLayoutType.Detail)
            {
                await ReloadAsync();
            }
        }

        /// <summary>版の一覧を展開しているか (展開前は件数だけを持つ)。</summary>
        public bool IsExpanded => _isExpanded;

        /// <summary>版の一覧を展開する (版を読み込む)。</summary>
        [ScriptName("Expand")]
        public async Task ExpandAsync()
        {
            if (_isExpanded) return;
            _isExpanded = true;
            await ReloadAsync();
        }

        /// <summary>版の一覧を閉じる (件数の表示に戻す)。</summary>
        [ScriptName("Collapse")]
        public void Collapse()
        {
            if (!_isExpanded) return;
            _isExpanded = false;
            _versions.Clear();
            _pageIndex = 0;
            NotifyStateChanged();
        }

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        //自動保存 (AutoSubmitField = レコードを読み直さない軽量な保存) の後、本体はフィールドの AcceptChanges だけを呼ぶ。
        //通常の保存は再初期化 (InitializeDataAsync) で履歴も読み直されるが、こちらは読み直しが無いので保存で増えた版をここで読む
        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info)
        {
            //同梱した削除の取り消しは保存で確定した (次の保存に持ち越さない)
            _pendingUndeleteVersions.Clear();
            if (ModuleLayoutType != ModuleLayoutType.Detail) return;
            ReloadAfterSubmit = ReloadAfterSubmitAsync();
        }

        /// <summary>保存 (AcceptChanges) をきっかけにした読み直し (完了待ち用)。</summary>
        internal Task? ReloadAfterSubmit { get; private set; }

        async Task ReloadAfterSubmitAsync()
        {
            try
            {
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                await Services.Logger.Error(ex.Message);
            }
        }

        /// <summary>読み込み済みの版 (新しい順)。</summary>
        internal IReadOnlyList<EditHistoryVersion> Versions => _versions;

        public bool IsLoaded { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>このレコードの版の総数 (履歴モジュールで閲覧できるもの)。</summary>
        public int TotalCount { get; private set; }

        public bool HasMore => _versions.Count < TotalCount;

        /// <summary>履歴を読める状態か (デザインモード・未保存のレコードでは読まない)。</summary>
        internal bool IsAvailable => !Services.AppInfoService.IsDesignMode && !Module.IsNewData;

        /// <summary>復元 (フォームへの反映) ができるか。設計で禁止・表示専用・未保存では不可。</summary>
        internal bool CanRestore => Design.CanRestore && !Module.IsViewOnly && !Module.IsNewData;

        ModuleDesign? HistoryModule => Services.AppInfoService.GetDesignData().Modules.Find(Design.HistoryModuleName);

        //契約 (役割→フィールド名)。契約が無ければ既定名
        EditHistoryContractFieldDesign Names => EditHistoryContracts.Contract(HistoryModule) ?? new();

        /// <summary>履歴を先頭から読み直す (展開前は件数だけ)。読み込み中なら、その読み込みが終わってから読み直す (途中の応答が混ざらないように)。</summary>
        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            if (IsBusy)
            {
                _reloadPending = true;
                return;
            }
            do
            {
                _reloadPending = false;
                _versions.Clear();
                _pageIndex = 0;
                _numberingTotal = 0;
                if (_isExpanded) await LoadPageAsync();
                else await LoadCountAsync();
            } while (_reloadPending);
        }

        //件数だけ読む (Id だけ選ぶ = サーバー側でスナップショットの権限落としも走らない軽い要求)
        async Task LoadCountAsync()
        {
            if (!IsAvailable || IsBusy || HistoryModule == null) return;
            if (!HistoryModule.HasUserReadPermission(Services))
            {
                IsLoaded = true;
                NotifyStateChanged();
                return;
            }
            IsBusy = true;
            try
            {
                var page = (await Services.ModuleDataService.GetListAsync(new List<GetListRequest>
                {
                    new() { Condition = CreateCondition(1, countOnly: true), PageIndex = 0 },
                })).FirstOrDefault();
                TotalCount = page?.TotalCount ?? 0;
                _numberingTotal = TotalCount;
                IsLoaded = true;
            }
            finally
            {
                IsBusy = false;
                NotifyStateChanged();
            }
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
            //履歴モジュールを読めないユーザーには要求しない (サーバーが拒否して画面のエラーになるため)。「履歴はありません」になる
            if (!HistoryModule.HasUserReadPermission(Services))
            {
                IsLoaded = true;
                NotifyStateChanged();
                return;
            }
            IsBusy = true;
            try
            {
                await EnsureOwnedModulesAsync();
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
                if (_pageIndex == 0) _numberingTotal = TotalCount;
                //読み込みの間に閉じられたら版は入れない
                if (!_isExpanded) return;

                //版番号 = 閲覧者に見える版の中での古い方からの連番 (履歴モジュールの閲覧条件で見えない版は数えない)
                var number = _numberingTotal - _pageIndex * pageSize;
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

        SearchCondition CreateCondition(int limitCount, bool countOnly = false)
        {
            var names = Names;
            return EditHistoryContracts.VersionsCondition(Design.HistoryModuleName, names, Module.Design.Name, Module.GetIdText(), limitCount,
                countOnly ? [SystemFieldNames.Id] : [SystemFieldNames.Id, names.ChangeType, names.Snapshot, names.UserId, names.DateTime]);
        }

        EditHistoryVersion ToVersion(ModuleData row, int number)
        {
            var names = Names;
            var user = row.Fields.GetValueOrDefault(names.UserId);
            var dateTime = (row.Fields.GetValueOrDefault(names.DateTime) as DateTimeFieldData)?.Value;
            //UTC 保存の変更日時は本体の DateTimeFieldComponent と同じくローカル時刻で見せる
            if (dateTime != null && HistoryModule?.Fields.FirstOrDefault(e => e.Name == names.DateTime) is DateTimeFieldDesign { SaveAsUtc: true })
                dateTime = dateTime.Value.ToLocalTime();
            return new EditHistoryVersion
            {
                Id = EditHistorySnapshot.GetId(row),
                Number = number,
                ChangeType = EditHistoryContracts.GetText(row, names.ChangeType),
                UserText = (user as LinkFieldData)?.DisplayText ?? (user as ValueFieldDataBase<string>)?.Value ?? string.Empty,
                DateTime = dateTime,
                //含めない従属レコード (除外・行ごと) は、指定より前に記録された版に入っていても外す (差分・版表示・復元が触らない)
                Snapshot = EditHistoryPolicy.Strip(Services.AppInfoService.GetDesignData(), Design, EditHistorySnapshot.Deserialize(EditHistoryContracts.GetText(row, names.Snapshot))),
            };
        }

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
            var designData = Services.AppInfoService.GetDesignData();
            var previous = EditHistoryPolicy.Strip(designData, Design, EditHistorySnapshot.Deserialize(EditHistoryContracts.GetText(previousRow, Names.Snapshot)));
            if (previous == null)
            {
                //前の版の内容がこの人には見えない (行の閲覧条件に合わない版 = サーバーが空にして返す)。前の版が無いのと同じ扱い (全部が「空 → 値」に見えないように)
                version.HasPreviousVersion = false;
                return new();
            }
            return EditHistoryDiff.Compute(designData, Module.Design, previous, version.Snapshot, CanRead);
        }

        //閲覧権限のないフィールドは差分にも出さない。自モジュールの項目は自分のフィールド、
        //従属レコード (明細行等) の項目はそのモジュールのフィールド (EnsureOwnedModulesAsync で作った空のモジュール) の権限で判定する
        bool CanRead(ModuleDesign design, string fieldName)
        {
            var module = design.Name == Module.Design.Name ? Module : _ownedModules.GetValueOrDefault(design.Name);
            return module?.GetField(fieldName)?.HasUserReadPermission != false;
        }

        //従属レコードのモジュール (子・孫・埋め込みの中も) ごとに、フィールド権限を引くための空のモジュールを 1 つ作っておく
        //(本体はモジュール生成時にユーザーで確定する権限をフィールドに載せる。行の値に依存する条件は行ごとに違うので見ない)
        readonly Dictionary<string, Module> _ownedModules = new();

        async Task EnsureOwnedModulesAsync()
        {
            var designData = Services.AppInfoService.GetDesignData();
            foreach (var (_, _, _, _, child) in EditHistoryPolicy.Walk(designData, Module.Design, Design))
            {
                if (child == null || child.Name == Module.Design.Name || _ownedModules.ContainsKey(child.Name)) continue;
                _ownedModules[child.Name] = await ModuleCreationService.CreateModuleAsync(Services, new ModuleData { Name = child.Name }, ModuleLayoutType.None);
            }
        }

        /// <summary>その版のレコード全体を表示専用のダイアログで表示する。変更フィールドを強調する。</summary>
        internal async Task ShowVersionAsync(EditHistoryVersion version)
        {
            var snapshot = version.Snapshot;
            if (snapshot == null) return;

            //DB は読まず、版の内容をそのまま見せる (本体の CreateModuleForShowAsync)。従属レコード (明細の一覧・Gantt のタスク・埋め込みモジュールの子等) の行の強調
            //(追加 = 行全体 / 変更 = 枠 + 変わったセル / 削除 = 前の版の行を打ち消しで差し込む) は、行 Id で差分と対応づけて OwnedRecordRow に載せる
            var module = await this.CreateModuleForShowAsync(snapshot, Design.LayoutName,
                (declared, rows) => OwnedRecordsDisplay.Build(rows, version.Changes.FirstOrDefault(e => e.IsList && e.FieldName == declared.Name)));
            //含めない従属レコード (除外・行ごと) は版に無いので、版表示では出さない
            foreach (var (fieldDesign, owned) in EditHistoryContracts.OwnedRecords(Module.Design))
            {
                if (owned.Name == fieldDesign.Name && !EditHistoryPolicy.IsIncluded(Design, owned.Name) && module.GetField(fieldDesign.Name) is { } notIncluded) notIncluded.IsVisible = false;
            }
            //過去の内容の表示専用: 保存ボタン (押すと新しいレコードとして送られてしまう) と履歴フィールド自身は出さない
            foreach (var field in module.GetFields())
            {
                if (field is SubmitButtonField or EditHistoryField) field.IsVisible = false;
            }
            module.DialogTitle = string.Format(R.EditHistoryVersionFormat, version.Number);
            module.ClassName = VersionDialogClassName;
            ApplyHighlights(module, version.Changes);
            //見た内容をそのまま編集中のレコードへ持ってくる操作は、このダイアログから (内容を見てから押す。確認は出さない)
            if (!CanRestoreVersion(version))
            {
                await module.ShowDialogAsync(new SecondaryOutlineButton(R.EditHistoryClose));
                return;
            }
            var answer = await module.ShowDialogAsync(
                new DialogButton("btn btn-primary " + RestoreButtonClassName, R.EditHistoryRestoreVersion), new SecondaryOutlineButton(R.EditHistoryClose));
            if (answer == R.EditHistoryRestoreVersion) await RestoreAsync(version);
        }

        /// <summary>その版の内容で編集中のレコードを置き換えられるか。最新の版 (今の内容) と削除の版は対象外。</summary>
        internal bool CanRestoreVersion(EditHistoryVersion version)
            => CanRestore && version.Snapshot != null && !version.IsDelete && version.Number != TotalCount;

        //値フィールドはセル単位で強調。従属レコード (一覧・Gantt・埋め込みモジュール等) の行は ShowOwnedRecordsAsync に渡した OwnedRecordRow が持つ
        static void ApplyHighlights(Module module, List<EditHistoryChange> changes)
        {
            foreach (var change in changes.Where(e => !e.IsList))
            {
                var field = module.GetField(change.FieldName);
                if (field != null) field.ClassName = AddClass(field.ClassName, ChangedClassName);
            }
        }

        static string AddClass(string current, string className)
            => string.IsNullOrEmpty(current) ? className : $"{current} {className}";

        //復元で Id を保って戻した論理削除の行がある版の取り消しを、次の保存に同梱する (同じ版を二度復元しても 1 回)
        internal void AddPendingUndelete(string historyRowId)
        {
            if (_pendingUndeleteVersions.Contains(historyRowId)) return;
            _pendingUndeleteVersions.Add(historyRowId);
        }

        /// <summary>その版の内容を編集中のフォームへ反映する (保存はユーザーが行う)。</summary>
        internal async Task RestoreAsync(EditHistoryVersion version)
        {
            var snapshot = version.Snapshot;
            if (snapshot == null || !CanRestore) return;

            var applied = await EditHistoryRestorer.ApplyAsync(Module, snapshot, (_, _) => AddPendingUndelete(version.Id));
            if (applied == 0)
            {
                //版に反映できる項目が 1 つも無い (書き込み権限のない項目・添付ファイルだけ、など)。何も起きなかったことを伝える
                await Services.UIService.ShowMessageBox(R.EditHistoryRestoreVersion, R.EditHistoryRestoreNothingApplied,
                    [new PrimaryButton(R.EditHistoryClose, true)]);
                return;
            }
            await Services.UIService.NotifySuccess(string.Format(R.EditHistoryRestoredFormat, version.Number));
            NotifyStateChanged();
        }
    }
}
