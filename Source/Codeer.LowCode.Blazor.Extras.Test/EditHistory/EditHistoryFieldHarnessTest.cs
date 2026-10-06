using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryField (クライアント): 履歴モジュールの読み方 (条件・並び・ページ)、版番号の採番、差分、
    /// スナップショットのフォームへの反映 (復元)。DB は使わず、ハーネスの一覧応答で駆動する。
    /// </summary>
    public class EditHistoryFieldHarnessTest
    {
        static ModuleData Order(string id, string title, decimal? amount, params ModuleData[] items)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Amount"] = new NumberFieldData { Value = amount };
            data.Fields["Items"] = new ListFieldData { Children = items.ToList() };
            return data;
        }

        static ModuleData Item(string id, string name, decimal qty)
        {
            var data = new ModuleData { Name = "OrderItem" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Qty"] = new NumberFieldData { Value = qty };
            return data;
        }

        static ModuleData HistoryRow(string id, string changeType, ModuleData snapshot, DateTime dateTime)
        {
            var data = new ModuleData { Name = "EditHistory" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["ChangeType"] = new TextFieldData { Value = changeType };
            data.Fields["Snapshot"] = new TextFieldData { Value = EditHistorySnapshot.Serialize(snapshot) };
            data.Fields["UserId"] = new TextFieldData { Value = "user1" };
            data.Fields["DateTime"] = new DateTimeFieldData { Value = dateTime };
            return data;
        }

        //履歴 3 版 (新しい順に返す): v1 作成 / v2 件名変更 / v3 明細変更
        static readonly ModuleData _v1 = Order("1", "A", 100, Item("10", "X", 1));
        static readonly ModuleData _v2 = Order("1", "B", 100, Item("10", "X", 1));
        static readonly ModuleData _v3 = Order("1", "B", 100, Item("10", "X", 5), Item("12", "Z", 1));

        static List<ModuleData> Rows() =>
        [
            HistoryRow("103", "Update", _v3, new DateTime(2026, 9, 3)),
            HistoryRow("102", "Update", _v2, new DateTime(2026, 9, 2)),
            HistoryRow("101", "Add", _v1, new DateTime(2026, 9, 1)),
        ];

        static TestServices CreateServices(int pageSize = 20, bool logicalDelete = false, bool canRestore = true)
        {
            var design = EditHistoryTestDesigns.Create(logicalDelete: logicalDelete);
            var history = (Extras.Designs.EditHistoryFieldDesign)design.Modules.Find("Order")!.Fields.First(e => e.Name == "History");
            history.PageSize = pageSize;
            history.CanRestore = canRestore;
            var services = new TestServices(design);
            services.App.CurrentUserData = new ModuleData { Name = "AppUser" };
            services.App.ListProvider = request =>
            {
                var rows = Rows();
                var limit = request.Condition.LimitCount ?? rows.Count;
                return new Paging<ModuleData>
                {
                    TotalCount = rows.Count,
                    Items = rows.Skip(request.PageIndex * limit).Take(limit).ToList(),
                };
            };
            return services;
        }

        /// <param name="expand">版の一覧を展開する (初期化では件数だけを読むので、版を見るテストは展開してから)。</param>
        static async Task<(Module Module, EditHistoryField Field)> CreateOrderModuleAsync(TestServices services, ModuleData data, bool expand = true)
        {
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.Detail);
            var field = module.GetField<EditHistoryField>("History")!;
            if (expand) await field.ExpandAsync();
            return (module, field);
        }

        [Test]
        public async Task 詳細ページの初期化では件数だけを読み_展開で版を読む_閉じると件数に戻る()
        {
            var services = CreateServices();
            var (_, field) = await CreateOrderModuleAsync(services, _v3, expand: false);

            Assert.That(field.IsLoaded, Is.True);
            Assert.That(field.IsExpanded, Is.False);
            Assert.That(field.TotalCount, Is.EqualTo(3), "件数は初期化で分かる");
            Assert.That(field.Versions, Is.Empty, "版はまだ読まない (スナップショット込みで重い)");
            var count = services.App.ListRequests.Single();
            Assert.That(count.Condition.SelectFields, Is.EqualTo(new[] { "Id" }), "Id だけ選ぶ = サーバーでスナップショットの権限落としも走らない");
            Assert.That(count.Condition.LimitCount, Is.EqualTo(1));

            await field.ExpandAsync();
            Assert.That(field.IsExpanded, Is.True);
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(services.App.ListRequests.Count, Is.EqualTo(3), "展開でページと次の 1 行を読む");

            //閉じると件数の表示に戻る (版は捨てる)。保存後の読み直しも展開していなければ件数だけ
            field.Collapse();
            Assert.That(field.IsExpanded, Is.False);
            Assert.That(field.Versions, Is.Empty);
            field.AcceptChanges(new SubmitAcceptInfo());
            await field.ReloadAfterSubmit!;
            Assert.That(services.App.ListRequests.Last().Condition.SelectFields, Is.EqualTo(new[] { "Id" }));
            Assert.That(field.TotalCount, Is.EqualTo(3));
        }

        [Test]
        public async Task 展開で履歴を読み_版番号は件数から採番し_差分を持つ()
        {
            var services = CreateServices();
            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(field.IsLoaded, Is.True);
            Assert.That(field.TotalCount, Is.EqualTo(3));
            Assert.That(field.HasMore, Is.False);
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(field.Versions.Select(e => e.ChangeType), Is.EqualTo(new[] { "Update", "Update", "Add" }));
            Assert.That(field.Versions[0].UserText, Is.EqualTo("user1"));
            Assert.That(field.Versions[0].DateTime, Is.EqualTo(new DateTime(2026, 9, 3)));

            //v3: 明細の変更と追加
            var v3 = field.Versions[0].Changes.Single();
            Assert.That((v3.DisplayName, v3.IsList, v3.ChangedCount, v3.AddedCount), Is.EqualTo(("明細", true, 1, 1)));
            //v2: 件名 A → B
            var v2 = field.Versions[1].Changes.Single();
            Assert.That((v2.DisplayName, v2.Before, v2.After), Is.EqualTo(("件名", "A", "B")));
            //v1: 作成の版は差分を出さない (「レコードが作成されました」)
            Assert.That(field.Versions[2].Changes, Is.Empty);
        }

        [Test]
        public async Task 前の版の内容が見えなければ前の版なしと同じ扱い()
        {
            var services = CreateServices();
            //v2 の Snapshot はこの人には見えない (行の閲覧条件に合わない版はサーバーが空にして返す)
            services.App.ListProvider = request =>
            {
                var rows = Rows();
                ((TextFieldData)rows[1].Fields["Snapshot"]).Value = string.Empty;
                var limit = request.Condition.LimitCount ?? rows.Count;
                return new Paging<ModuleData> { TotalCount = rows.Count, Items = rows.Skip(request.PageIndex * limit).Take(limit).ToList() };
            };
            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(field.Versions[0].HasPreviousVersion, Is.False, "全項目が「空 → 値」に見えないように、前の版なしと同じにする");
            Assert.That(field.Versions[0].Changes, Is.Empty);
            Assert.That(field.Versions[1].Snapshot, Is.Null);
        }

        [Test]
        public async Task 履歴の検索条件はモジュール名とレコードIdで_新しい順()
        {
            var services = CreateServices();
            await CreateOrderModuleAsync(services, _v3);

            var requests = services.App.ListRequests.Skip(1).ToList();  //先頭は初期化の件数取り
            Assert.That(requests.Count, Is.EqualTo(2), "ページと、その次の 1 行 (末尾の版の差分の元)");
            var condition = requests[0].Condition;
            Assert.That(condition.ModuleName, Is.EqualTo("EditHistory"));
            var and = (MultiMatchCondition)condition.Condition!;
            Assert.That(and.Children.Cast<FieldValueMatchCondition>().Select(e => e.SearchTargetVariable), Is.EqualTo(new[] { "ModuleName.Value", "DataId.Value" }));
            Assert.That(condition.SortConditions.Select(e => (e.Variable, e.IsDescending)), Is.EqualTo(new[] { ("DateTime.Value", true), ("Id.Value", true) }));
            Assert.That(condition.LimitCount, Is.EqualTo(20));
            Assert.That(requests[1].Condition.LimitCount, Is.EqualTo(1));
            Assert.That(requests[1].PageIndex, Is.EqualTo(20));
        }

        [Test]
        public async Task ページ末尾の版の差分は次の1行を元に計算し_さらに表示で続きを読む()
        {
            var services = CreateServices(pageSize: 2);
            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(field.Versions.Count, Is.EqualTo(2));
            Assert.That(field.HasMore, Is.True);
            var v2 = field.Versions[1].Changes.Single();
            Assert.That((v2.Before, v2.After), Is.EqualTo(("A", "B")), "v1 はページ外だが差分の元として読まれる");

            await field.LoadMoreAsync();
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(field.HasMore, Is.False);
        }

        [Test]
        public async Task 履歴モジュールを読めないユーザーには要求せず履歴なしになる()
        {
            var services = CreateServices();
            services.App.GetDesignData().Modules.Find("EditHistory")!.UserReadCondition = new Codeer.LowCode.Blazor.Repository.Match.ModuleMatchCondition
            {
                Condition = new Codeer.LowCode.Blazor.Repository.Match.FieldValueMatchConditionNonNull
                {
                    SearchTargetVariable = "Check.Value", Comparison = Codeer.LowCode.Blazor.Repository.Match.MatchComparison.Equal,
                    Value = new Codeer.LowCode.Blazor.Repository.BooleanValue { Value = true },
                },
            };
            var user = new ModuleData { Name = "AppUser" };
            user.Fields["Check"] = new BooleanFieldData { Value = false };
            services.App.CurrentUserData = user;

            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(services.App.ListRequests, Is.Empty, "サーバーに要求しない (拒否されて画面のエラーになるため)");
            Assert.That(field.IsLoaded, Is.True);
            Assert.That(field.Versions, Is.Empty);
        }

        [Test]
        public async Task 新規レコードとデザインモードでは読まない()
        {
            var services = CreateServices();
            var module = await services.CreateModuleAsync("Order", ModuleLayoutType.Detail);
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(field.IsAvailable, Is.False);
            Assert.That(field.IsLoaded, Is.False);
            Assert.That(services.App.ListRequests, Is.Empty);
        }

        [Test]
        public async Task 復元は値を反映し_明細は行Idで更新追加削除する_保存はしない()
        {
            var services = CreateServices();
            var (module, field) = await CreateOrderModuleAsync(services, _v3);
            Assert.That(module.IsModified, Is.False);

            await EditHistoryRestorer.ApplyAsync(module, field.Versions[2].Snapshot!, null);  // v1 に戻す

            Assert.That(module.GetField<TextField>("Title")!.Value, Is.EqualTo("A"));
            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(1));
            var row = items.Rows[0];
            Assert.That(row.GetIdText(), Is.EqualTo("10"), "既存行は Id で突き合わせて更新");
            Assert.That(row.GetField<NumberField>("Qty")!.Value, Is.EqualTo(1));
            Assert.That(module.IsModified, Is.True, "保存はユーザーが行う (変更扱いになる)");
            Assert.That(module.GetField<IdField>("Id")!.Value, Is.EqualTo("1"));
        }

        [Test]
        public async Task 復元を禁止すると版に戻せない_表示はできる()
        {
            var services = CreateServices(canRestore: false);
            var (module, field) = await CreateOrderModuleAsync(services, _v3);
            Assert.That(field.Versions.Count, Is.EqualTo(3), "版の一覧は出る");
            Assert.That(field.CanRestore, Is.False);

            await field.RestoreAsync(field.Versions[2]);

            Assert.That(module.GetField<TextField>("Title")!.Value, Is.EqualTo("B"), "フォームは変わらない");
            Assert.That(module.IsModified, Is.False);
        }

        [Test]
        public async Task 復元で消えていた明細行は新しい行として追加される()
        {
            var services = CreateServices();
            var (module, field) = await CreateOrderModuleAsync(services, _v2);

            await EditHistoryRestorer.ApplyAsync(module, field.Versions[0].Snapshot!, null);  // v3 へ

            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(2));
            Assert.That(items.Rows[0].GetField<NumberField>("Qty")!.Value, Is.EqualTo(5));
            var added = items.Rows[1];
            Assert.That(added.IsNewData, Is.True, "Id は振り直し");
            Assert.That(added.GetField<TextField>("Name")!.Value, Is.EqualTo("Z"));
        }

        [Test]
        public async Task 論理削除の明細は復元でIdを保って復活し_保存に削除の取り消しが同梱される()
        {
            var services = CreateServices(logicalDelete: true);
            var (module, field) = await CreateOrderModuleAsync(services, _v2);

            await field.RestoreAsync(field.Versions[0]);  // v3 へ (行 12 が無い → 論理削除なので Id を保って復活)

            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(2));
            var row = items.Rows[1];
            Assert.That(row.IsNewData, Is.False, "Id を保つ");
            Assert.That(row.GetIdText(), Is.EqualTo("12"));
            Assert.That(row.GetField<TextField>("Name")!.Value, Is.EqualTo("Z"));
            Assert.That(row.IsModified, Is.True, "値は Update として送られる");
            var undeletes = field.GetSubmitData().ExtendedData.OfType<EditHistoryUndeleteData>().ToList();
            Assert.That(undeletes.Select(e => e.HistoryRowId), Is.EqualTo(new[] { "103" }), "戻した版の履歴行が保存に同梱される");
        }

        [Test]
        public async Task UTC保存の変更日時はローカル時刻で見せる()
        {
            var services = CreateServices();
            var history = services.App.GetDesignData().Modules.Find("EditHistory")!;
            ((DateTimeFieldDesign)history.Fields.First(e => e.Name == "DateTime")).SaveAsUtc = true;
            var (_, field) = await CreateOrderModuleAsync(services, _v3);
            Assert.That(field.Versions[0].DateTime, Is.EqualTo(new DateTime(2026, 9, 3).ToLocalTime()));
        }

        [Test]
        public async Task 削除の取り消しは重複せず_保存で確定したら次の保存に持ち越さない()
        {
            var services = CreateServices(logicalDelete: true);
            var (_, field) = await CreateOrderModuleAsync(services, _v2);
            field.AddPendingUndelete("103");
            field.AddPendingUndelete("103");
            field.AddPendingUndelete("102");
            var undelete = field.GetSubmitData().ExtendedData.OfType<EditHistoryUndeleteData>().ToList();
            Assert.That(undelete.Select(e => (e.HistoryModuleName, e.HistoryRowId, e.RestoreWholeRecord)), Is.EqualTo(new[] { ("EditHistory", "103", false), ("EditHistory", "102", false) }), "同じ版は 1 回");

            //自動保存 (AcceptChanges だけが呼ばれる) の後は同梱しない
            field.AcceptChanges(new SubmitAcceptInfo());
            if (field.ReloadAfterSubmit != null) await field.ReloadAfterSubmit;
            Assert.That(field.GetSubmitData().ExtendedData, Is.Empty);
        }

        [Test]
        public void 削除の取り消しはExtendedDataで送られる()
        {
            var design = EditHistoryTestDesigns.Create(logicalDelete: true);
            var services = new TestServices(design);
            var module = ModuleCreationService.CreateModuleAsync(services.Core, _v3, ModuleLayoutType.None).Result;
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(field.GetSubmitData().ExtendedData, Is.Empty);

            //復元で復活した行があるときだけ、その版 (履歴行の Id) が同梱される (フィールド自身は変更扱いにならない)
            field.AddPendingUndelete("103");
            var submit = field.GetSubmitData();
            var undelete = submit.ExtendedData.OfType<EditHistoryUndeleteData>().Single();
            Assert.That((undelete.HistoryModuleName, undelete.HistoryRowId, undelete.RestoreWholeRecord), Is.EqualTo(("EditHistory", "103", false)));
            Assert.That(field.IsModified, Is.False);
        }
    }
}
