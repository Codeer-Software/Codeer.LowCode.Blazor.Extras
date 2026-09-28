using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.Approval;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Approval
{
    /// <summary>
    /// 承認フローと編集履歴の組み合わせ (実 DB = SQLite。アプリテンプレートと同じ結線: SubmitAsync を EditHistoryRecorder で包み、
    /// 承認エンジンは内部経路 (Add / Update) で承認モジュールを書く)。
    /// - 申請・再申請は申請書の保存 (SubmitAsync) を通るので、申請者の版として編集履歴に残る
    /// - 承認・却下・差し戻しなどの操作は承認モジュールだけを書く (申請書の保存を通らない) ので、編集履歴には残らない。承認の記録は承認履歴 (ApprovalHistory)
    /// - 承認フローの FK はサーバーが保存の後に書くので、版の差分には出ない
    /// ユーザー: "1"=申請者 / "2"=課長 / "3"=部長
    /// </summary>
    public class ApprovalEditHistoryDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "1";
        DesignData _designData = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult(_currentUserId);

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"approval_edit_history_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Name TEXT, Email TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1','申請者','user1@example.com'),('2','課長','user2@example.com'),('3','部長','user3@example.com')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Requests (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, Amount REAL, ApprovalId INTEGER)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalFlows (Id INTEGER PRIMARY KEY AUTOINCREMENT, Status TEXT, TargetModuleName TEXT, TargetId TEXT, Applicant TEXT, AttemptNo INTEGER, CurrentStepNo INTEGER, Version INTEGER)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalFlowMembers (Id INTEGER PRIMARY KEY AUTOINCREMENT, FlowId INTEGER, AttemptNo INTEGER, StepNo INTEGER, StepName TEXT, StepType TEXT, CompletionPolicy TEXT, IsCommentRequiredOnReject INTEGER, ReturnScope TEXT, ApproverUser TEXT, IsRequired INTEGER, IsFinalStep INTEGER, Status TEXT, ActedAt DATETIME)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, FlowId INTEGER, AttemptNo INTEGER, Action TEXT, ActorUser TEXT, Comment TEXT, ActedAt DATETIME)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time DATETIME)", new());

            _designData = CreateDesignData();
            _currentUserId = "1";
            _errors.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbFile); } catch { }
        }

        #region ハーネス

        /// <summary>テンプレートの CustomizedModuleDataIO と同じ結線: 編集履歴はインターセプタ 1 つ、承認モジュールはシステム経路で書く。</summary>
        sealed class AppModuleDataIO : ModuleDataIO
        {
            public AppModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, List<string> errors)
                : base(design, auth, db, files)
                => AddInterceptor(new EditHistoryRecorder(design, errors.Add));

            public Task<string> AddSystemRecordAsync(ModuleData data) => AddAsync(Guid.NewGuid(), Guid.NewGuid(), data);

            public Task UpdateSystemRecordAsync(ModuleData data) => UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), data);
        }

        AppModuleDataIO CreateIO(string userId)
        {
            _currentUserId = userId;
            return new AppModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), _errors);
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す
        ApprovalEngine CreateEngine(string userId)
        {
            var io = CreateIO(userId);
            return new ApprovalEngine(_designData, io, _db, io.AddSystemRecordAsync, io.UpdateSystemRecordAsync) { LogError = _errors.Add };
        }

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "Email" });
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);

            //申請書 (承認フロー + 編集履歴)
            var request = new ModuleDesign { Name = "Request", DataSourceName = Ds, DbTable = "Requests", CanCreate = true, CanUpdate = true, CanDelete = true };
            request.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            request.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "件名", DbColumn = "Title" });
            request.Fields.Add(new NumberFieldDesign { Name = "Amount", DisplayName = "金額", DbColumn = "Amount" });
            request.Fields.Add(new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "ApprovalId" });
            request.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            request.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(request);

            //承認モジュール群 (誰も書けない = エンジンのシステム経路だけが書く)
            var nobody = new ModuleMatchCondition { ModuleName = "AppUser", Condition = Eq("Id.Value", "no_such_user") };

            var flow = new ModuleDesign { Name = "ApprovalFlow", DataSourceName = Ds, DbTable = "ApprovalFlows", UserWriteCondition = nobody };
            flow.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.Status), DbColumn = "Status" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.TargetModuleName), DbColumn = "TargetModuleName" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.TargetId), DbColumn = "TargetId" });
            flow.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.Applicant), SearchCondition = new SearchCondition("AppUser"), DbColumn = "Applicant" });
            flow.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.AttemptNo), DbColumn = "AttemptNo" });
            flow.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.CurrentStepNo), DbColumn = "CurrentStepNo" });
            flow.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalFlowContractFieldDesign.Members),
                SearchCondition = new SearchCondition("ApprovalFlowMember")
                {
                    Condition = new FieldVariableMatchCondition
                    {
                        SearchTargetVariable = $"{nameof(ApprovalMemberContractFieldDesign.Flow)}.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value",
                    },
                },
            });
            flow.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalFlowContractFieldDesign.Histories),
                SearchCondition = new SearchCondition("ApprovalHistory")
                {
                    Condition = new FieldVariableMatchCondition
                    {
                        SearchTargetVariable = $"{nameof(ApprovalHistoryContractFieldDesign.Flow)}.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value",
                    },
                },
            });
            flow.Fields.Add(new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = "Version", IncrementVersion = true });
            flow.Fields.Add(new ApprovalFlowContractFieldDesign { Name = "Contract" });
            flow.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(flow);

            var member = new ModuleDesign { Name = "ApprovalFlowMember", DataSourceName = Ds, DbTable = "ApprovalFlowMembers", UserWriteCondition = nobody };
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            member.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.Flow), SearchCondition = new SearchCondition("ApprovalFlow"), DbColumn = "FlowId" });
            member.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.AttemptNo), DbColumn = "AttemptNo" });
            member.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.StepNo), DbColumn = "StepNo" });
            member.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.StepName), DbColumn = "StepName" });
            member.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.StepType), DbColumn = "StepType" });
            member.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.CompletionPolicy), DbColumn = "CompletionPolicy" });
            member.Fields.Add(new BooleanFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.IsCommentRequiredOnReject), DbColumn = "IsCommentRequiredOnReject" });
            member.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.ReturnScope), DbColumn = "ReturnScope" });
            member.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.ApproverUser), SearchCondition = new SearchCondition("AppUser"), DbColumn = "ApproverUser" });
            member.Fields.Add(new BooleanFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.IsRequired), DbColumn = "IsRequired" });
            member.Fields.Add(new BooleanFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.IsFinalStep), DbColumn = "IsFinalStep" });
            member.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.Status), DbColumn = "Status" });
            member.Fields.Add(new DateTimeFieldDesign { Name = nameof(ApprovalMemberContractFieldDesign.ActedAt), DbColumn = "ActedAt" });
            member.Fields.Add(new ApprovalMemberContractFieldDesign { Name = "Contract" });
            member.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(member);

            var history = new ModuleDesign { Name = "ApprovalHistory", DataSourceName = Ds, DbTable = "ApprovalHistories", UserWriteCondition = nobody };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            history.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.Flow), SearchCondition = new SearchCondition("ApprovalFlow"), DbColumn = "FlowId" });
            history.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.AttemptNo), DbColumn = "AttemptNo" });
            history.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.Action), DbColumn = "Action" });
            history.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.ActorUser), SearchCondition = new SearchCondition("AppUser"), DbColumn = "ActorUser" });
            history.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.Comment), DbColumn = "Comment" });
            history.Fields.Add(new DateTimeFieldDesign { Name = nameof(ApprovalHistoryContractFieldDesign.ActedAt), DbColumn = "ActedAt" });
            history.Fields.Add(new ApprovalHistoryContractFieldDesign { Name = "Contract" });
            history.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(history);

            //編集履歴モジュール
            var editHistory = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories" };
            editHistory.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            editHistory.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            editHistory.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            editHistory.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            editHistory.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            editHistory.Fields.Add(new TextFieldDesign { Name = "UserId", DbColumn = "user_id" });
            editHistory.Fields.Add(new DateTimeFieldDesign { Name = "DateTime", DbColumn = "date_time" });
            editHistory.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract" });
            editHistory.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(editHistory);
            return d;
        }

        static FieldValueMatchCondition Eq(string variable, string? value)
            => new() { SearchTargetVariable = variable, Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(value) };

        //課長 → 部長 の直列承認
        static ApprovalRouteData CreateRoute()
        {
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2", true);
            route.AddStep("部長承認").AddMember("3", true);
            return route;
        }

        static ModuleData RequestData(string id, string title, decimal amount)
        {
            var data = new ModuleData { Name = "Request" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Amount"] = new NumberFieldData { Value = amount };
            return data;
        }

        static ModuleSubmitData NewRequestSubmit(string title, decimal amount)
        {
            var tempId = IdFieldData.NewId().Value!;
            return new ModuleSubmitData { ModuleName = "Request", Id = tempId, Add = [RequestData(tempId, title, amount)] };
        }

        static ModuleSubmitData UpdateRequestSubmit(string id, string title, decimal amount)
            => new() { ModuleName = "Request", Id = id, Update = [RequestData(id, title, amount)] };

        async Task<ApprovalActionResult> SubmitApprovalAsync(ModuleSubmitData targetSubmitData, string userId = "1")
        {
            var result = await CreateEngine(userId).ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit, TargetModuleName = "Request", FieldName = "Approval", TargetSubmitData = targetSubmitData, Route = CreateRoute(),
            });
            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            return result;
        }

        async Task<ApprovalActionResult> ExecuteAsync(string userId, ApprovalAction action, string flowId, string comment = "")
        {
            var result = await CreateEngine(userId).ExecuteAsync(new ApprovalCommand
            {
                Action = action, TargetModuleName = "Request", FieldName = "Approval", FlowId = flowId, ExpectedVersion = await GetVersionAsync(flowId), Comment = comment,
            });
            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            return result;
        }

        async Task<string> GetVersionAsync(string flowId)
        {
            var value = (await _db.QueryAsync(Ds, $"SELECT Version FROM ApprovalFlows WHERE Id = {flowId}", new())).Single().Values.First();
            return value is null or DBNull ? string.Empty : value.ToString()!;
        }

        async Task<string> GetFlowStatusAsync(string flowId)
            => (await _db.QueryAsync(Ds, $"SELECT Status FROM ApprovalFlows WHERE Id = {flowId}", new())).Single().Values.First()?.ToString() ?? string.Empty;

        /// <summary>編集履歴 (古い順): 変更種別と変更者。</summary>
        async Task<List<(string ChangeType, string UserId)>> EditHistoriesAsync(string dataId)
            => (await _db.QueryAsync(Ds, $"SELECT change_type, user_id FROM edit_histories WHERE module_name = 'Request' AND data_id = '{dataId}' ORDER BY id", new()))
                .Select(e => (e["change_type"]?.ToString() ?? string.Empty, e["user_id"]?.ToString() ?? string.Empty)).ToList();

        async Task<List<ModuleData>> SnapshotsAsync(string dataId)
            => (await _db.QueryAsync(Ds, $"SELECT snapshot FROM edit_histories WHERE module_name = 'Request' AND data_id = '{dataId}' ORDER BY id", new()))
                .Select(e => EditHistorySnapshot.Deserialize(e["snapshot"]!.ToString())!).ToList();

        /// <summary>承認履歴 (古い順): 操作と操作者。</summary>
        async Task<List<(string Action, string Actor)>> ApprovalHistoriesAsync(string flowId)
            => (await _db.QueryAsync(Ds, $"SELECT Action, ActorUser FROM ApprovalHistories WHERE FlowId = {flowId} ORDER BY Id", new()))
                .Select(e => (e["Action"]?.ToString() ?? string.Empty, e["ActorUser"]?.ToString() ?? string.Empty)).ToList();

        #endregion

        [Test]
        public async Task 申請は申請者の版として残り_承認は編集履歴に残らず承認履歴に残る()
        {
            //申請 (新規の申請書を保存しながら) → 版 1 = 作成・申請者
            var submitted = await SubmitApprovalAsync(NewRequestSubmit("経費申請", 1000));
            var id = submitted.TargetId;
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }));

            //課長承認 → 部長承認 → 完了。承認は申請書の保存を通らないので版は増えない
            await ExecuteAsync("2", ApprovalAction.Approve, submitted.FlowId, "OK");
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }));
            await ExecuteAsync("3", ApprovalAction.Approve, submitted.FlowId);
            Assert.That(await GetFlowStatusAsync(submitted.FlowId), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }), "承認で版は増えない");

            //承認の記録は承認履歴にある (申請 / 承認 / 承認)
            var actions = await ApprovalHistoriesAsync(submitted.FlowId);
            Assert.That(actions, Is.EqualTo(new[]
            {
                (ApprovalAction.Submit.ToDesignValue(), "1"), (ApprovalAction.Approve.ToDesignValue(), "2"), (ApprovalAction.Approve.ToDesignValue(), "3"),
            }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 差し戻し後の再申請は申請者の更新の版として残り_承認FKは差分に出ない()
        {
            var submitted = await SubmitApprovalAsync(NewRequestSubmit("経費申請", 1000));
            var id = submitted.TargetId;

            //差し戻し (申請者へ) = 承認モジュールだけ → 版は増えない
            await ExecuteAsync("2", ApprovalAction.Return, submitted.FlowId, "金額を直してください");
            Assert.That(await GetFlowStatusAsync(submitted.FlowId), Is.EqualTo(ApprovalFlowStatus.Returned.ToDesignValue()));
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }));

            //金額を直して再申請 = 申請書の保存を通る → 版 2 (更新・申請者)
            var resubmitted = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit, TargetModuleName = "Request", FieldName = "Approval",
                TargetSubmitData = UpdateRequestSubmit(id, "経費申請", 1200), Route = CreateRoute(),
                FlowId = submitted.FlowId, ExpectedVersion = await GetVersionAsync(submitted.FlowId),
            });
            Assert.That(resubmitted.IsSuccess, Is.True, resubmitted.ErrorMessage);
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1"), ("Update", "1") }));

            //版 1 (申請時。FK はまだ書かれていない) と版 2 (FK あり) の差分は金額だけ。承認 FK は出ない
            var snapshots = await SnapshotsAsync(id);
            Assert.That(snapshots[0].Fields.ContainsKey("Approval") && snapshots[0].Fields["Approval"] is Extras.Data.ApprovalFlowFieldData { Id: not null }, Is.False, "申請の版のスナップショットは FK を書く前の内容");
            Assert.That(((Extras.Data.ApprovalFlowFieldData)snapshots[1].Fields["Approval"]).Id, Is.EqualTo(submitted.FlowId), "再申請の版のスナップショットには FK が入っている");
            var changes = EditHistoryDiff.Compute(_designData, _designData.Modules.Find("Request")!, snapshots[0], snapshots[1], _ => true);
            Assert.That(changes.Select(e => (e.DisplayName, e.Before, e.After)), Is.EqualTo(new[] { ("金額", "1000", "1200") }));
            Assert.That(await ApprovalHistoriesAsync(submitted.FlowId).ContinueWith(t => t.Result.Select(e => e.Action)),
                Is.EqualTo(new[] { ApprovalAction.Submit.ToDesignValue(), ApprovalAction.Return.ToDesignValue(), ApprovalAction.Resubmit.ToDesignValue() }));
            Assert.That(_errors, Is.Empty);
        }

        [Test]
        public async Task 保存済みの申請書を変更せずに申請しても版は増えない()
        {
            //通常の保存で作成 → 版 1
            var tempId = IdFieldData.NewId().Value!;
            var results = await CreateIO("1").SubmitWithTransactionAsync([new ModuleSubmitData { ModuleName = "Request", Id = tempId, Add = [RequestData(tempId, "経費申請", 1000)] }]);
            Assert.That(results.Single().ExceptionMessage, Is.Null.Or.Empty);
            var id = results.Single().DestinationId;
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }));

            //変更なしで申請 (画面は Add / Update の無い送信を送る) → フローはできるが版は増えない
            var submitted = await SubmitApprovalAsync(new ModuleSubmitData { ModuleName = "Request", Id = id });
            Assert.That(submitted.TargetId, Is.EqualTo(id));
            Assert.That(await GetFlowStatusAsync(submitted.FlowId), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await EditHistoriesAsync(id), Is.EqualTo(new[] { ("Add", "1") }));
            Assert.That(_errors, Is.Empty);
        }
    }
}
