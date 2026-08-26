using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Approval;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Approval
{
    /// <summary>
    /// 承認エンジンのサーバー強制を実DB(SQLite)で検証する。
    /// クライアントを信用しない前提で「command API に直接リクエストが来た」状況を再現する。
    /// ユーザー: "1"=申請者 / "2"=課長 / "3"=部長 / "4"=部外者
    /// 承認モジュールには「誰も書けない」UserWriteCondition を設定し、
    /// エンジンのシステム経路だけが書けることも同時に検証する。
    /// </summary>
    public class ApprovalEngineDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "1";
        DesignData _designData = null!;

        public async Task<string> GetCurrentUserIdAsync()
        {
            await Task.CompletedTask;
            return _currentUserId;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();

            _dbFile = Path.Combine(Path.GetTempPath(), $"approval_test_{Guid.NewGuid():N}.db");
            var dataSources = new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            };
            _db = new DbAccessor(dataSources);

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Name TEXT, Email TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1','申請者','user1@example.com'),('2','課長','user2@example.com'),('3','部長','user3@example.com'),('4','部外者','')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Requests (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, ApprovalId INTEGER)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalFlows (Id INTEGER PRIMARY KEY AUTOINCREMENT, Status TEXT, TargetModuleName TEXT, TargetId TEXT, RouteName TEXT, Applicant TEXT, AttemptNo INTEGER, CurrentStepNo INTEGER, Version INTEGER)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalFlowMembers (Id INTEGER PRIMARY KEY AUTOINCREMENT, FlowId INTEGER, AttemptNo INTEGER, StepNo INTEGER, StepName TEXT, StepType TEXT, CompletionPolicy TEXT, IsCommentRequiredOnReject INTEGER, ReturnScope TEXT, ApproverUser TEXT, IsRequired INTEGER, IsFinalStep INTEGER, Status TEXT, ActedAt TEXT)", new());
            await _db.ExecuteAsync(Ds,
                "CREATE TABLE ApprovalHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, FlowId INTEGER, AttemptNo INTEGER, StepNo INTEGER, Action TEXT, ActorUser TEXT, FromStatus TEXT, ToStatus TEXT, Comment TEXT, ActedAt TEXT)", new());

            _designData = CreateDesignData();
            _currentUserId = "1";
            _sentMails.Clear();
            _mailErrors.Clear();
            _mailSenderThrows = false;
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_dbFile); } catch { }
        }

        #region ハーネス

        //承認モジュールへの書き込みはシステム経路 (protected の Add/Update を公開する)
        class SystemIO : ModuleDataIO
        {
            public SystemIO(DesignData designData, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager tmp)
                : base(designData, auth, db, tmp) { }

            public async Task<string> AddSystemAsync(ModuleData data)
                => await AddAsync(Guid.NewGuid(), Guid.NewGuid(), data);

            public async Task UpdateSystemAsync(ModuleData data)
                => await UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), data);
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す
        ApprovalEngine CreateEngine(string userId)
        {
            _currentUserId = userId;
            var io = CreateIO();
            return new ApprovalEngine(_designData, io, _db, io.AddSystemAsync, io.UpdateSystemAsync)
            {
                //通知メールは常に結線する (契約の TurnNotifyMail が空なら送られない)
                MailDispatcher = new MailDispatcher(
                    //呼び名は契約側で空 = appsettings の既定で解決する (空のままでは送信エラーになる)
                    new MailConfig { DefaultInfraName = "Main" },
                    _ => new FakeMailSender(this)),
                LogError = _mailErrors.Add,
            };
        }

        readonly List<MailMessage> _sentMails = new();
        readonly List<string> _mailErrors = new();
        bool _mailSenderThrows;

        class FakeMailSender(ApprovalEngineDbTest owner) : IMailSender
        {
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                if (owner._mailSenderThrows) throw new InvalidOperationException("mail infrastructure down");
                owner._sentMails.Add(message);
                return Task.FromResult(new MailSendResult { TotalCount = 1, SuccessCount = 1 });
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
                => throw new NotSupportedException();
        }

        SystemIO CreateIO() => new(_designData, this, _db, new TemporaryFileManager(_db, [], []));

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "Email" });
            d.AddModule(user);

            var request = new ModuleDesign
            {
                Name = "Request",
                DataSourceName = Ds,
                DbTable = "Requests",
                CanCreate = true,
                CanUpdate = true,
                CanDelete = true,
                //編集ロック: 未申請 or 再申請できる状態のみ書き込める (サーバー強制の本命)
                DataWriteCondition = new ModuleMatchCondition
                {
                    ModuleName = "Request",
                    Condition = new MultiMatchCondition
                    {
                        IsOrMatch = true,
                        Children =
                        [
                            Eq("Approval.Status.Value", null),
                            Eq("Approval.Status.Value", ApprovalFlowStatus.Returned.ToDesignValue()),
                            Eq("Approval.Status.Value", ApprovalFlowStatus.Withdrawn.ToDesignValue()),
                            Eq("Approval.Status.Value", ApprovalFlowStatus.Rejected.ToDesignValue()),
                        ],
                    },
                },
            };
            request.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            request.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            request.Fields.Add(new ApprovalFlowFieldDesign
            {
                Name = "Approval",
                DbColumn = "ApprovalId",
            });
            //dotted リンク列 (編集ロック条件が JOIN で承認状態を参照する)
            request.Fields.Add(new TextFieldDesign { Name = "Approval.Status", DbColumn = "Status" });
            d.AddModule(request);

            //承認モジュールは「誰も書けない」保護条件 (エンジンのシステム経路だけが書ける)
            var nobody = new ModuleMatchCondition
            {
                ModuleName = "AppUser",
                Condition = Eq("Id.Value", "no_such_user"),
            };

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
                        SearchTargetVariable = $"{nameof(ApprovalMemberContractFieldDesign.Flow)}.Value",
                        Comparison = MatchComparison.Equal,
                        Variable = "Id.Value",
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
                        SearchTargetVariable = $"{nameof(ApprovalHistoryContractFieldDesign.Flow)}.Value",
                        Comparison = MatchComparison.Equal,
                        Variable = "Id.Value",
                    },
                },
            });
            flow.Fields.Add(new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = "Version", IncrementVersion = true });
            flow.Fields.Add(new ApprovalFlowContractFieldDesign { Name = "Contract" });
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
            member.Fields.Add(new MailFieldDesign
            {
                Name = "TurnMail",
                ToVariable = "ApproverUser.Email.Value",
                ReplyToVariable = "Flow.Applicant.Email.Value", //返信先=申請者 (リンク2段 = 再帰解決。差出人はシステム)
                Subject = "承認依頼: {StepName.Value}",
                Body = "{ApproverUser.Name.Value} さん ステップ{StepNo.Value}の承認をお願いします",
            });
            member.Fields.Add(new ApprovalMemberContractFieldDesign { Name = "Contract", TurnNotifyMail = "TurnMail" });
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
            d.AddModule(history);

            return d;
        }


        static FieldValueMatchCondition Eq(string variable, string? value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = MatchComparison.Equal,
            Value = MultiTypeValue.Create(value),
        };

        static ApprovalRouteData CreateRoute()
        {
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2", true);
            route.AddStep("部長承認").AddMember("3", true);
            return route;
        }

        static ModuleSubmitData CreateNewRequestSubmit(string title)
        {
            var tempId = IdFieldData.NewId();
            var data = new ModuleData { Name = "Request" };
            data.Fields["Id"] = tempId;
            data.Fields["Title"] = new TextFieldData { Value = title };
            return new ModuleSubmitData { ModuleName = "Request", Id = tempId.Value!, Add = [data] };
        }

        static ModuleSubmitData CreateUpdateRequestSubmit(string id, string title)
        {
            var data = new ModuleData { Name = "Request" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            return new ModuleSubmitData { ModuleName = "Request", Id = id, Update = [data] };
        }

        async Task<ApprovalActionResult> SubmitAsync(string userId = "1", ApprovalRouteData? route = null)
        {
            var result = await CreateEngine(userId).ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateNewRequestSubmit("経費申請"),
                Route = route ?? CreateRoute(),
            });
            Assert.That(result.IsSuccess, Is.True, result.ErrorMessage);
            return result;
        }

        async Task<ApprovalActionResult> ExecuteAsync(string userId, string action, string flowId,
            string? comment = null, int? targetStepNo = null)
            => await CreateEngine(userId).ExecuteAsync(new ApprovalCommand
            {
                Action = Enum.Parse<ApprovalAction>(action),
                TargetModuleName = "Request",
                FieldName = "Approval",
                FlowId = flowId,
                ExpectedVersion = await GetVersionAsync(flowId),
                Comment = comment ?? string.Empty,
                TargetStepNo = targetStepNo,
            });

        async Task<string> GetVersionAsync(string flowId)
        {
            var rows = await _db.QueryAsync(Ds, $"SELECT Version FROM ApprovalFlows WHERE Id = {flowId}", new());
            var value = rows.Single().Values.First();
            return value is null or DBNull ? string.Empty : value.ToString()!;
        }

        async Task<string> GetFlowValueAsync(string flowId, string column)
        {
            var rows = await _db.QueryAsync(Ds, $"SELECT {column} FROM ApprovalFlows WHERE Id = {flowId}", new());
            return rows.Single().Values.First()?.ToString() ?? string.Empty;
        }

        async Task<List<IDictionary<string, object>>> GetMembersAsync(string flowId, int attemptNo)
            => await _db.QueryAsync(Ds,
                $"SELECT StepNo, StepType, ApproverUser, Status FROM ApprovalFlowMembers WHERE FlowId = {flowId} AND AttemptNo = {attemptNo} ORDER BY StepNo, Id", new());

        static string S(IDictionary<string, object> row, string key) => row[key]?.ToString() ?? string.Empty;

        #endregion

        [Test]
        public async Task 申請_フロー生成とFK設定と履歴()
        {
            var result = await SubmitAsync();

            Assert.That(await GetFlowValueAsync(result.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(result.FlowId, "AttemptNo"), Is.EqualTo("1"));
            Assert.That(await GetFlowValueAsync(result.FlowId, "CurrentStepNo"), Is.EqualTo("1"));
            Assert.That(await GetFlowValueAsync(result.FlowId, "TargetId"), Is.EqualTo(result.TargetId));

            //FK が親レコードに書かれている
            var fk = (await _db.QueryAsync(Ds, $"SELECT ApprovalId FROM Requests WHERE Id = {result.TargetId}", new())).Single().Values.First();
            Assert.That(fk?.ToString(), Is.EqualTo(result.FlowId));

            //Waiting は「本当に今待っている人」だけ。未到達ステップは Pending
            var members = await GetMembersAsync(result.FlowId, 1);
            Assert.That(members.Count, Is.EqualTo(2));
            Assert.That(S(members[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Pending.ToDesignValue()));

            //最終承認ステップのスナップショット (条件式で「最終承認者」を表すため)
            var finals = await _db.QueryAsync(Ds,
                $"SELECT StepNo, IsFinalStep FROM ApprovalFlowMembers WHERE FlowId = {result.FlowId} ORDER BY StepNo", new());
            Assert.That(finals[0].Values.ElementAt(1)?.ToString(), Is.EqualTo("0"));
            Assert.That(finals[1].Values.ElementAt(1)?.ToString(), Is.EqualTo("1"));

            //状態・申請者はフロー行が正 (親レコードへのコピーは無い。申請者はフロー行に記録される)
            Assert.That(await GetFlowValueAsync(result.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(result.FlowId, "Applicant"), Is.EqualTo("1"));

            var history = await _db.QueryAsync(Ds, $"SELECT Action, ActorUser FROM ApprovalHistories WHERE FlowId = {result.FlowId}", new());
            Assert.That(S(history.Single(), "Action"), Is.EqualTo(ApprovalAction.Submit.ToDesignValue()));
            Assert.That(S(history.Single(), "ActorUser"), Is.EqualTo("1"));
        }

        [Test]
        public async Task 申請_不正な経路は拒否()
        {
            //ステップなし
            var empty = new ApprovalRouteData();
            var r1 = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit, TargetModuleName = "Request", FieldName = "Approval", TargetSubmitData = CreateNewRequestSubmit("A"), Route = empty });
            Assert.That(r1.IsSuccess, Is.False);

            //承認ステップなし (回覧のみ)
            var confirmationOnly = new ApprovalRouteData();
            var step = confirmationOnly.AddStep("回覧");
            step.StepType = ApprovalStepType.Confirmation.ToDesignValue();
            step.AddMember("2");
            var r2 = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit, TargetModuleName = "Request", FieldName = "Approval", TargetSubmitData = CreateNewRequestSubmit("A"), Route = confirmationOnly });
            Assert.That(r2.IsSuccess, Is.False);

            //空のユーザーId
            var emptyUser = new ApprovalRouteData();
            emptyUser.AddStep("承認").AddMember("");
            var r3 = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit, TargetModuleName = "Request", FieldName = "Approval", TargetSubmitData = CreateNewRequestSubmit("A"), Route = emptyUser });
            Assert.That(r3.IsSuccess, Is.False);
        }

        [Test]
        public async Task 承認_直列に進み完了する()
        {
            var submit = await SubmitAsync();

            var r1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.True, r1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(submit.FlowId, "CurrentStepNo"), Is.EqualTo("2"));

            //次ステップのメンバーが Pending → Waiting に昇格している
            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));

            var r2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));

            //完了はフロー行の状態で確認する (親レコードへのコピーは無い)
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
        }

        [Test]
        public async Task 承認_承認者以外はサーバーが拒否()
        {
            var submit = await SubmitAsync();

            //申請者 (承認者ではない)
            var r1 = await ExecuteAsync("1", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.False);

            //2番目のステップの承認者 (まだ順番が来ていない)
            var r2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r2.IsSuccess, Is.False);

            //部外者
            var r3 = await ExecuteAsync("4", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r3.IsSuccess, Is.False);
        }

        [Test]
        public async Task 承認_版不一致は拒否()
        {
            var submit = await SubmitAsync();

            var result = await CreateEngine("2").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Approve,
                TargetModuleName = "Request",
                FieldName = "Approval",
                FlowId = submit.FlowId,
                ExpectedVersion = "999",
            });
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));

            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(members[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Pending.ToDesignValue()));
        }

        [Test]
        public async Task 却下_コメント必須と残メンバースキップ()
        {
            var submit = await SubmitAsync();

            //コメントなしは拒否 (既定 IsCommentRequiredOnReject = true)
            var r1 = await ExecuteAsync("2", ApprovalAction.Reject.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.False);

            var r2 = await ExecuteAsync("2", ApprovalAction.Reject.ToDesignValue(), submit.FlowId, comment: "却下理由");
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Rejected.ToDesignValue()));

            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(members[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Rejected.ToDesignValue()));
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Skipped.ToDesignValue()));
        }

        [Test]
        public async Task 差し戻しと再申請_試行世代が分かれる()
        {
            var submit = await SubmitAsync();

            var r1 = await ExecuteAsync("2", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "修正して");
            Assert.That(r1.IsSuccess, Is.True, r1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Returned.ToDesignValue()));

            //申請者以外の再申請は拒否
            var deny = await CreateEngine("2").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateUpdateRequestSubmit(submit.TargetId, "修正済"),
                Route = CreateRoute(),
                FlowId = submit.FlowId,
                ExpectedVersion = await GetVersionAsync(submit.FlowId),
            });
            Assert.That(deny.IsSuccess, Is.False);

            var r2 = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateUpdateRequestSubmit(submit.TargetId, "修正済"),
                Route = CreateRoute(),
                FlowId = submit.FlowId,
                ExpectedVersion = await GetVersionAsync(submit.FlowId),
            });
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);

            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(submit.FlowId, "AttemptNo"), Is.EqualTo("2"));

            //旧世代のメンバーは温存され、新世代が作られる
            Assert.That((await GetMembersAsync(submit.FlowId, 1)).Count, Is.EqualTo(2));
            Assert.That((await GetMembersAsync(submit.FlowId, 2)).Count, Is.EqualTo(2));

            //編集内容も保存されている
            var title = (await _db.QueryAsync(Ds, $"SELECT Title FROM Requests WHERE Id = {submit.TargetId}", new())).Single().Values.First();
            Assert.That(title?.ToString(), Is.EqualTo("修正済"));
        }

        [Test]
        public async Task 差し戻し_過去ステップへはReturnScopeが必要()
        {
            //既定 (ApplicantOnly) ではステップ差し戻し不可
            var submit1 = await SubmitAsync();
            await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit1.FlowId);
            var deny = await ExecuteAsync("3", ApprovalAction.Return.ToDesignValue(), submit1.FlowId, comment: "やり直し", targetStepNo: 1);
            Assert.That(deny.IsSuccess, Is.False);

            //AnyPreviousStep なら過去ステップへ戻せる
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2");
            var step2 = route.AddStep("部長承認");
            step2.ReturnScope = ApprovalReturnScope.AnyPreviousStep.ToDesignValue();
            step2.AddMember("3");

            var submit2 = await SubmitAsync(route: route);
            await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit2.FlowId);
            var allow = await ExecuteAsync("3", ApprovalAction.Return.ToDesignValue(), submit2.FlowId, comment: "やり直し", targetStepNo: 1);
            Assert.That(allow.IsSuccess, Is.True, allow.ErrorMessage);

            Assert.That(await GetFlowValueAsync(submit2.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(submit2.FlowId, "CurrentStepNo"), Is.EqualTo("1"));

            //差し戻し先 = Waiting、それ以降 = Pending (到達状態の正規化)
            var members = await GetMembersAsync(submit2.FlowId, 1);
            Assert.That(S(members[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Pending.ToDesignValue()));
        }

        [Test]
        public async Task 取り下げ_申請者のみで残メンバーはスキップされ再申請できる()
        {
            var submit = await SubmitAsync();

            //申請者以外は拒否
            var deny = await ExecuteAsync("2", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(deny.IsSuccess, Is.False);

            var withdraw = await ExecuteAsync("1", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(withdraw.IsSuccess, Is.True, withdraw.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Withdrawn.ToDesignValue()));

            //承認待ち一覧に処理できない行を残さない
            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(members.All(e => S(e, "Status") == ApprovalMemberStatus.Skipped.ToDesignValue()));

            //取り下げ後は編集して再申請できる
            var resubmit = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateUpdateRequestSubmit(submit.TargetId, "修正版"),
                Route = CreateRoute(),
                FlowId = submit.FlowId,
                ExpectedVersion = await GetVersionAsync(submit.FlowId),
            });
            Assert.That(resubmit.IsSuccess, Is.True, resubmit.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "AttemptNo"), Is.EqualTo("2"));
        }

        [Test]
        public async Task 取り下げ_承認が始まった後は不可()
        {
            var submit = await SubmitAsync();
            var approve = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve.IsSuccess, Is.True, approve.ErrorMessage);

            //既定ポリシー (BeforeFirstApproval): 承認が1件でも付いたら取り下げ不可 (承認者に差し戻してもらう)
            var withdraw = await ExecuteAsync("1", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(withdraw.IsSuccess, Is.False);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
        }

        [Test]
        public async Task 取り下げ_ポリシーAnytimeなら承認後も可()
        {
            //業務ポリシーはデザインで可変 (エンジンは安全性の不変条件だけを強制する)
            var field = _designData.Modules.Find("Request")!.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            field.WithdrawPolicy = ApprovalWithdrawPolicy.Anytime;

            var submit = await SubmitAsync();
            var approve = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve.IsSuccess, Is.True, approve.ErrorMessage);

            var withdraw = await ExecuteAsync("1", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(withdraw.IsSuccess, Is.True, withdraw.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Withdrawn.ToDesignValue()));
        }

        [Test]
        public async Task 完了ポリシー_必須全員と任意1人()
        {
            //必須2人 + 任意1人: 必須2人の承認で完了 (任意はステップ完了で Skipped = 承認不要になった)
            var route = new ApprovalRouteData();
            var step = route.AddStep("合議");
            step.AddMember("2", true).AddMember("3", true).AddMember("4", false);

            var submit = await SubmitAsync(route: route);
            await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
            var optional = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(optional[2], "Status"), Is.EqualTo(ApprovalMemberStatus.Skipped.ToDesignValue()));

            //必須ゼロ: 任意1人の承認で完了 (現行テンプレート互換)
            var anyRoute = new ApprovalRouteData();
            anyRoute.AddStep("誰か1人").AddMember("2", false).AddMember("3", false);
            var submit2 = await SubmitAsync(route: anyRoute);
            await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit2.FlowId);
            Assert.That(await GetFlowValueAsync(submit2.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
        }

        [Test]
        public async Task 並列ステップ_他の人の承認で完了したら相方はスキップされ承認できない()
        {
            //Any: 部長 or 経理 → 次の承認へ。承認しなかった方は Skipped (承認待ちに残らない・押しても拒否)
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2");
            var any = route.AddStep("部長または経理");
            any.CompletionPolicy = ApprovalCompletionPolicy.Any.ToDesignValue();
            any.AddMember("3").AddMember("4");
            route.AddStep("社長決裁").AddMember("2");

            var submit = await SubmitAsync(route: route);
            await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);

            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Approved.ToDesignValue()));
            Assert.That(S(members[2], "Status"), Is.EqualTo(ApprovalMemberStatus.Skipped.ToDesignValue()));
            Assert.That(S(members[3], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));

            var late = await ExecuteAsync("4", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(late.IsSuccess, Is.False);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "CurrentStepNo"), Is.EqualTo("3"));
        }

        [Test]
        public async Task 並列ステップ_過去ステップへ差し戻すとスキップされた相方も承認待ちに戻る()
        {
            var route = new ApprovalRouteData();
            var any = route.AddStep("部長または経理");
            any.CompletionPolicy = ApprovalCompletionPolicy.Any.ToDesignValue();
            any.AddMember("3").AddMember("4");
            var last = route.AddStep("社長決裁");
            last.ReturnScope = ApprovalReturnScope.AnyPreviousStep.ToDesignValue();
            last.AddMember("2");

            var submit = await SubmitAsync(route: route);
            await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            var back = await ExecuteAsync("2", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "やり直し", targetStepNo: 1);
            Assert.That(back.IsSuccess, Is.True, back.ErrorMessage);

            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(S(members[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(members[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(members[2], "Status"), Is.EqualTo(ApprovalMemberStatus.Pending.ToDesignValue()));

            //相方が今度は承認できる
            var ok = await ExecuteAsync("4", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(ok.IsSuccess, Is.True, ok.ErrorMessage);
        }

        [Test]
        public async Task 回覧_フローをブロックせず確認を記録する()
        {
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2");
            var confirmation = route.AddStep("経理回覧");
            confirmation.StepType = ApprovalStepType.Confirmation.ToDesignValue();
            confirmation.AddMember("4");
            route.AddStep("部長承認").AddMember("3");

            var submit = await SubmitAsync(route: route);

            //到達前の回覧は Pending で確認できない
            var early = await ExecuteAsync("4", ApprovalAction.Confirm.ToDesignValue(), submit.FlowId);
            Assert.That(early.IsSuccess, Is.False);

            //回覧がブロックしない: 課長→部長で完了する
            await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));

            //完了後でも確認は記録できる
            var confirm = await ExecuteAsync("4", ApprovalAction.Confirm.ToDesignValue(), submit.FlowId);
            Assert.That(confirm.IsSuccess, Is.True, confirm.ErrorMessage);
            var members = await GetMembersAsync(submit.FlowId, 1);
            var confirmed = members.Single(e => S(e, "StepType") == ApprovalStepType.Confirmation.ToDesignValue());
            Assert.That(S(confirmed, "Status"), Is.EqualTo(ApprovalMemberStatus.Confirmed.ToDesignValue()));

            //確認対象がないユーザーは拒否
            var deny = await ExecuteAsync("2", ApprovalAction.Confirm.ToDesignValue(), submit.FlowId);
            Assert.That(deny.IsSuccess, Is.False);
        }

        [Test]
        public async Task 二重申請はサーバーが拒否()
        {
            var submit = await SubmitAsync();

            //同一レコードへの再度の申請 (FK を無視して直接 command API を叩いた想定)
            var again = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateUpdateRequestSubmit(submit.TargetId, "改ざん"),
                Route = CreateRoute(),
            });
            Assert.That(again.IsSuccess, Is.False);
        }

        [Test]
        public async Task 編集ロック_承認中の親レコード更新はサーバーが拒否()
        {
            var submit = await SubmitAsync();

            //承認中: DataWriteCondition (Approval.Status が編集可能状態でない) で拒否される
            var io = CreateIO();
            _currentUserId = "1";
            var results = await io.SubmitWithTransactionAsync([CreateUpdateRequestSubmit(submit.TargetId, "改ざん")]);
            Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);

            var title = (await _db.QueryAsync(Ds, $"SELECT Title FROM Requests WHERE Id = {submit.TargetId}", new())).Single().Values.First();
            Assert.That(title?.ToString(), Is.EqualTo("経費申請"));

            //差し戻し後は申請者が編集できる
            await ExecuteAsync("2", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "修正して");
            DbAccessor.ClearTableDefinitionCache();
            var io2 = CreateIO();
            var results2 = await io2.SubmitWithTransactionAsync([CreateUpdateRequestSubmit(submit.TargetId, "修正版")]);
            Assert.That(results2.All(e => string.IsNullOrEmpty(e.ExceptionMessage)), Is.True,
                string.Join(",", results2.Select(e => e.ExceptionMessage)));
        }

        [Test]
        public async Task 承認モジュールへの直接書き込みはUserWriteConditionが拒否()
        {
            var submit = await SubmitAsync();

            //クライアント相当の正規経路からフロー行を直接書き換えようとする
            var tamper = new ModuleData { Name = "ApprovalFlow" };
            tamper.Fields["Id"] = new IdFieldData { Value = submit.FlowId };
            tamper.Fields[nameof(ApprovalFlowContractFieldDesign.Status)] = new TextFieldData { Value = ApprovalFlowStatus.Completed.ToDesignValue() };

            var io = CreateIO();
            _currentUserId = "2";
            var results = await io.SubmitWithTransactionAsync(
                [new ModuleSubmitData { ModuleName = "ApprovalFlow", Id = submit.FlowId, Update = [tamper] }]);
            Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
        }

        //承認モジュールの全フィールドを既定名から "X" 付きにリネームし、契約マッピングを追従させる。
        //エンジンの解決が既定名の綴りに依存していないこと (契約 = インターフェイス) の全経路検証
        static void RenameApprovalModuleFields(ModuleDesign module)
        {
            foreach (var f in module.Fields)
            {
                if (f.Name == "Id" || f.Name == SystemFieldNames.OptimisticLocking) continue;
                if (f is ContractFieldDesignBase) continue;
                f.Name = "X" + f.Name;
            }
            var contract = module.Fields.OfType<ContractFieldDesignBase>().First();
            foreach (var role in contract.GetRoleProperties())
                role.SetValue(contract, "X" + role.GetValue(contract));
        }

        [Test]
        public async Task 契約マッピング_フィールド名を変えてもエンジンが動く()
        {
            var flow = _designData.Modules.Find("ApprovalFlow")!;
            var member = _designData.Modules.Find("ApprovalFlowMember")!;
            var history = _designData.Modules.Find("ApprovalHistory")!;
            RenameApprovalModuleFields(flow);
            RenameApprovalModuleFields(member);
            RenameApprovalModuleFields(history);

            //一覧のバインド条件はメンバー / 履歴側のフィールドを参照しているので追従させる
            var membersList = (ListFieldDesign)flow.Fields.First(e => e.Name == "X" + nameof(ApprovalFlowContractFieldDesign.Members));
            ((FieldVariableMatchCondition)membersList.SearchCondition.Condition!).SearchTargetVariable
                = $"X{nameof(ApprovalMemberContractFieldDesign.Flow)}.Value";
            var historiesList = (ListFieldDesign)flow.Fields.First(e => e.Name == "X" + nameof(ApprovalFlowContractFieldDesign.Histories));
            ((FieldVariableMatchCondition)historiesList.SearchCondition.Condition!).SearchTargetVariable
                = $"X{nameof(ApprovalHistoryContractFieldDesign.Flow)}.Value";

            //申請書側の dotted リンク参照 (編集ロック条件) もリネーム後の名前に追従させる
            var request = _designData.Modules.Find("Request")!;
            request.Fields.First(e => e.Name == "Approval.Status").Name = "Approval.XStatus";
            var writeCondition = (MultiMatchCondition)request.DataWriteCondition!.Condition!;
            foreach (var c in writeCondition.Children.OfType<FieldValueMatchCondition>())
                c.SearchTargetVariable = "Approval.XStatus.Value";

            var submit = await SubmitAsync();
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Applicant"), Is.EqualTo("1"));

            //編集ロックもリネーム後の dotted 参照で効いている
            var io = CreateIO();
            _currentUserId = "1";
            var locked = await io.SubmitWithTransactionAsync([CreateUpdateRequestSubmit(submit.TargetId, "改ざん")]);
            Assert.That(locked.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.True);

            var approve1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve1.IsSuccess, Is.True, approve1.ErrorMessage);
            var approve2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve2.IsSuccess, Is.True, approve2.ErrorMessage);

            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
            var members = await GetMembersAsync(submit.FlowId, 1);
            Assert.That(members.All(e => S(e, "Status") == ApprovalMemberStatus.Approved.ToDesignValue()), Is.True);

            var actions = await _db.QueryAsync(Ds,
                $"SELECT Action FROM ApprovalHistories WHERE FlowId = {submit.FlowId} ORDER BY Id", new());
            Assert.That(actions.Select(e => e.Values.First()?.ToString()),
                Is.EqualTo(new[] { ApprovalAction.Submit.ToDesignValue(), ApprovalAction.Approve.ToDesignValue(), ApprovalAction.Approve.ToDesignValue() }));
        }

        //================= 順番到達の通知メール =================

        [Test]
        public async Task 通知_申請時に最初のステップの承認者へメールが飛ぶ()
        {
            var submit = await SubmitAsync();

            Assert.That(_sentMails.Count, Is.EqualTo(1));
            Assert.That(_sentMails[0].To, Is.EqualTo(new[] { "user2@example.com" }));
            Assert.That(_sentMails[0].Subject, Is.EqualTo("承認依頼: 課長承認"));
            Assert.That(_sentMails[0].Body, Does.Contain("課長 さん").And.Contain("ステップ1"));
        }

        [Test]
        public async Task 通知_承認で次のステップの承認者へメールが飛ぶ()
        {
            var submit = await SubmitAsync();
            _sentMails.Clear();

            var approve = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve.IsSuccess, Is.True, approve.ErrorMessage);

            Assert.That(_sentMails.Count, Is.EqualTo(1));
            Assert.That(_sentMails[0].To, Is.EqualTo(new[] { "user3@example.com" }));
            Assert.That(_sentMails[0].Subject, Is.EqualTo("承認依頼: 部長承認"));

            //最終承認の完了では新しい順番は生まれない = 通知なし
            _sentMails.Clear();
            var approve2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve2.IsSuccess, Is.True, approve2.ErrorMessage);
            Assert.That(_sentMails, Is.Empty);
        }

        [Test]
        public async Task 通知_却下では飛ばず_過去ステップ差し戻しで再度飛ぶ()
        {
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2", true);
            var step2 = route.AddStep("部長承認");
            step2.ReturnScope = ApprovalReturnScope.AnyPreviousStep.ToDesignValue();
            step2.AddMember("3", true);
            var submit = await SubmitAsync(route: route);
            var approve = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve.IsSuccess, Is.True, approve.ErrorMessage);
            _sentMails.Clear();

            //ステップ1への差し戻し → 課長の順番が再度回ってくる = 通知
            var back = await ExecuteAsync("3", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "やり直し", targetStepNo: 1);
            Assert.That(back.IsSuccess, Is.True, back.ErrorMessage);
            Assert.That(_sentMails.Count, Is.EqualTo(1));
            Assert.That(_sentMails[0].To, Is.EqualTo(new[] { "user2@example.com" }));

            //却下では新しい順番は生まれない = 通知なし
            _sentMails.Clear();
            var reject = await ExecuteAsync("2", ApprovalAction.Reject.ToDesignValue(), submit.FlowId, comment: "却下");
            Assert.That(reject.IsSuccess, Is.True, reject.ErrorMessage);
            Assert.That(_sentMails, Is.Empty);
        }

        [Test]
        public async Task 通知_アドレスの無い承認者はスキップされる()
        {
            var route = new ApprovalRouteData();
            var step = route.AddStep("承認");
            step.AddMember("2", true);
            step.AddMember("4", true); //Email 空
            await SubmitAsync(route: route);

            Assert.That(_sentMails.Count, Is.EqualTo(1));
            Assert.That(_sentMails[0].To, Is.EqualTo(new[] { "user2@example.com" }));
        }

        [Test]
        public async Task 通知_送信基盤の失敗は承認操作を失敗させない()
        {
            _mailSenderThrows = true;

            var submit = await SubmitAsync(); //Assert内蔵 = 申請自体は成功
            var approve = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(approve.IsSuccess, Is.True, approve.ErrorMessage);

            Assert.That(_sentMails, Is.Empty);
            Assert.That(_mailErrors, Is.Not.Empty);
        }

        [Test]
        public async Task 通知_差出人はシステムで返信先が申請者になる()
        {
            await SubmitAsync();

            Assert.That(_sentMails.Count, Is.EqualTo(1));
            Assert.That(_sentMails[0].From, Is.Empty); //差出人はシステム (インフラ既定)
            Assert.That(_sentMails[0].ReplyTo, Is.EqualTo("user1@example.com")); //返信先=申請者
        }

        #region 契約の任意役割 (空 = 使わない → 既定で動く)

        //メンバー契約の任意役割 (表示系 3 + ポリシー系 4) と履歴契約の Flow 以外を全て空にする。
        //DB 列は残っているが、役割が空なのでエンジンは読み書きしない (= その列を持たないアプリと同じ)
        void UseMinimalContracts()
        {
            var member = _designData.Modules.Find("ApprovalFlowMember")!;
            var mc = member.Fields.OfType<ApprovalMemberContractFieldDesign>().Single();
            foreach (var role in new[] { nameof(mc.StepName), nameof(mc.IsFinalStep), nameof(mc.ActedAt),
                nameof(mc.CompletionPolicy), nameof(mc.ReturnScope), nameof(mc.IsCommentRequiredOnReject), nameof(mc.IsRequired) })
            {
                member.Fields.Remove(member.Fields.First(e => e.Name == role));
                typeof(ApprovalMemberContractFieldDesign).GetProperty(role)!.SetValue(mc, string.Empty);
            }
            var history = _designData.Modules.Find("ApprovalHistory")!;
            var hc = history.Fields.OfType<ApprovalHistoryContractFieldDesign>().Single();
            foreach (var role in new[] { nameof(hc.AttemptNo), nameof(hc.Action), nameof(hc.ActorUser), nameof(hc.Comment), nameof(hc.ActedAt) })
            {
                history.Fields.Remove(history.Fields.First(e => e.Name == role));
                typeof(ApprovalHistoryContractFieldDesign).GetProperty(role)!.SetValue(hc, string.Empty);
            }
        }

        static bool IsDbNull(object? value) => value is null or DBNull;

        [Test]
        public async Task 最小契約_申請から完了まで動き_空の役割は書かれない()
        {
            UseMinimalContracts();
            var submit = await SubmitAsync();

            //メンバー行: 必須役割だけが書かれ、任意役割の列は NULL
            var rows = await _db.QueryAsync(Ds,
                $"SELECT StepNo, StepType, ApproverUser, Status, StepName, CompletionPolicy, ReturnScope, IsCommentRequiredOnReject, IsRequired, IsFinalStep, ActedAt FROM ApprovalFlowMembers WHERE FlowId = {submit.FlowId} ORDER BY StepNo", new());
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(S(rows[0], "StepType"), Is.EqualTo(ApprovalStepType.Approval.ToDesignValue()));
            Assert.That(S(rows[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            foreach (var col in new[] { "StepName", "CompletionPolicy", "ReturnScope", "IsCommentRequiredOnReject", "IsRequired", "IsFinalStep", "ActedAt" })
                Assert.That(IsDbNull(rows[0][col]), Is.True, col);

            //履歴行: Flow だけ
            var histories = await _db.QueryAsync(Ds, $"SELECT FlowId, Action, ActorUser, Comment FROM ApprovalHistories WHERE FlowId = {submit.FlowId}", new());
            Assert.That(histories.Count, Is.EqualTo(1));
            Assert.That(IsDbNull(histories[0]["Action"]), Is.True);
            Assert.That(IsDbNull(histories[0]["ActorUser"]), Is.True);

            //承認 → 次ステップ → 完了 (状態遷移は必須役割だけで成立する)
            var r1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.True, r1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            var r2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));

            //承認済みメンバーの ActedAt は役割が空なので書かれない
            var acted = await _db.QueryAsync(Ds, $"SELECT ActedAt FROM ApprovalFlowMembers WHERE FlowId = {submit.FlowId}", new());
            Assert.That(acted.All(e => IsDbNull(e["ActedAt"])), Is.True);
        }

        [Test]
        public async Task 最小契約_IsRequired既定true_任意メンバー指定でも全員承認が必要()
        {
            //経路では 2 人とも「任意」(isRequired: false) = 本来は誰か 1 人で完了するが、
            //IsRequired 役割が無いアプリでは既定 (true) で動く = 全員必須
            var route = new ApprovalRouteData();
            route.AddStep("合議").AddMember("2", false).AddMember("3", false);

            //比較: 通常契約なら 1 人で完了
            var full = await SubmitAsync(route: route);
            var f1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), full.FlowId);
            Assert.That(f1.IsSuccess, Is.True, f1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(full.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));

            UseMinimalContracts();
            var min = await SubmitAsync(route: route);
            var m1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), min.FlowId);
            Assert.That(m1.IsSuccess, Is.True, m1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(min.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            var m2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), min.FlowId);
            Assert.That(m2.IsSuccess, Is.True, m2.ErrorMessage);
            Assert.That(await GetFlowValueAsync(min.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
        }

        [Test]
        public async Task 最小契約_ReturnScope既定ApplicantOnly_経路でAnyPreviousStepでも過去ステップ差し戻し不可()
        {
            UseMinimalContracts();
            var route = new ApprovalRouteData();
            route.AddStep("課長承認").AddMember("2");
            var step2 = route.AddStep("部長承認");
            step2.ReturnScope = ApprovalReturnScope.AnyPreviousStep.ToDesignValue();
            step2.AddMember("3");
            var submit = await SubmitAsync(route: route);
            var r1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.True, r1.ErrorMessage);

            //役割が空なので経路の AnyPreviousStep はメンバー行に写らず、既定 (申請者へのみ) で判定される
            var toStep1 = await ExecuteAsync("3", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "戻し", targetStepNo: 1);
            Assert.That(toStep1.IsSuccess, Is.False);

            //申請者への差し戻しはできる
            var toApplicant = await ExecuteAsync("3", ApprovalAction.Return.ToDesignValue(), submit.FlowId, comment: "戻し");
            Assert.That(toApplicant.IsSuccess, Is.True, toApplicant.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Returned.ToDesignValue()));
        }

        [Test]
        public async Task 最小契約_コメント必須既定false_経路で必須でもコメントなし却下が通る()
        {
            UseMinimalContracts();
            var submit = await SubmitAsync(); //CreateRoute の各ステップは IsCommentRequiredOnReject = true (ApprovalStepData の既定)
            var r = await ExecuteAsync("2", ApprovalAction.Reject.ToDesignValue(), submit.FlowId);
            Assert.That(r.IsSuccess, Is.True, r.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Rejected.ToDesignValue()));
        }

        [Test]
        public async Task 最小契約_取り下げと再申請は申請者判定が履歴に依存しない()
        {
            //履歴の Action / ActorUser が無くても、申請者の判定 (フロー行の Applicant) は成立する
            UseMinimalContracts();
            var submit = await SubmitAsync();

            var other = await ExecuteAsync("2", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(other.IsSuccess, Is.False);
            var mine = await ExecuteAsync("1", ApprovalAction.Withdraw.ToDesignValue(), submit.FlowId);
            Assert.That(mine.IsSuccess, Is.True, mine.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Withdrawn.ToDesignValue()));

            var resubmit = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit,
                TargetModuleName = "Request",
                FieldName = "Approval",
                TargetSubmitData = CreateNewRequestSubmit("経費申請(再)"),
                Route = CreateRoute(),
                FlowId = submit.FlowId,
                ExpectedVersion = await GetVersionAsync(submit.FlowId),
            });
            Assert.That(resubmit.IsSuccess, Is.True, resubmit.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "AttemptNo"), Is.EqualTo("2"));
        }

        #endregion

        #region 経路ビルダー (AddStep() / AddMembers)

        [Test]
        public async Task 経路_AddMembersは承認者ごとに1ステップの直列承認()
        {
            var route = new ApprovalRouteData().AddMembers(new[] { "2", "3" }); //課長 → 部長
            Assert.That(route.Steps.Count, Is.EqualTo(2));
            var submit = await SubmitAsync(route: route);

            var rows = await _db.QueryAsync(Ds,
                $"SELECT StepNo, StepName, ApproverUser, IsRequired, Status FROM ApprovalFlowMembers WHERE FlowId = {submit.FlowId} ORDER BY StepNo, Id", new());
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(S(rows[0], "StepName"), Is.Empty);
            Assert.That(S(rows[0], "ApproverUser"), Is.EqualTo("2"));
            Assert.That(S(rows[1], "ApproverUser"), Is.EqualTo("3"));
            Assert.That(Convert.ToInt32(rows[0]["IsRequired"]), Is.EqualTo(1));
            Assert.That(S(rows[0], "Status"), Is.EqualTo(ApprovalMemberStatus.Waiting.ToDesignValue()));
            Assert.That(S(rows[1], "Status"), Is.EqualTo(ApprovalMemberStatus.Pending.ToDesignValue()));

            //直列: 2 → 3 の順に承認して完了
            var r1 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.False); //まだ順番ではない
            var r2 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);
            var r3 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r3.IsSuccess, Is.True, r3.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
        }

        [Test]
        public async Task 経路_名前なしステップに複数人を置ける()
        {
            var route = new ApprovalRouteData();
            route.AddStep().AddMember("2").AddMember("3"); //合議 (全員必須)
            var submit = await SubmitAsync(route: route);

            var rows = await _db.QueryAsync(Ds, $"SELECT StepName FROM ApprovalFlowMembers WHERE FlowId = {submit.FlowId}", new());
            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows.All(e => S(e, "StepName") == string.Empty), Is.True);

            var r1 = await ExecuteAsync("2", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r1.IsSuccess, Is.True, r1.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            var r2 = await ExecuteAsync("3", ApprovalAction.Approve.ToDesignValue(), submit.FlowId);
            Assert.That(r2.IsSuccess, Is.True, r2.ErrorMessage);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.Completed.ToDesignValue()));
        }

        [Test]
        public async Task 経路_名前なしステップのエラーは番号で示される()
        {
            var route = new ApprovalRouteData();
            route.AddStep().AddMember("2");
            route.AddStep(); //メンバーなし
            var r = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Submit, TargetModuleName = "Request", FieldName = "Approval",
                TargetSubmitData = CreateNewRequestSubmit("x"), Route = route,
            });
            Assert.That(r.IsSuccess, Is.False);
            Assert.That(r.ErrorMessage, Does.Contain("step 2"));
        }

        #endregion

        #region command API (1 本のエンドポイントで Action により振り分け)

        [Test]
        public async Task コマンド_不正なActionは失敗を返す()
        {
            var submit = await SubmitAsync();
            var r = await CreateEngine("2").ExecuteAsync(new ApprovalCommand
            {
                Action = (ApprovalAction)999,
                TargetModuleName = "Request",
                FieldName = "Approval",
                FlowId = submit.FlowId,
                ExpectedVersion = await GetVersionAsync(submit.FlowId),
            });
            Assert.That(r.IsSuccess, Is.False);
            Assert.That(await GetFlowValueAsync(submit.FlowId, "Status"), Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
        }

        [Test]
        public async Task コマンド_Submit以外はFlowIdが必要()
        {
            await SubmitAsync();
            foreach (var action in new[] { ApprovalAction.Approve, ApprovalAction.Reject, ApprovalAction.Return, ApprovalAction.Withdraw, ApprovalAction.Confirm })
            {
                var r = await CreateEngine("2").ExecuteAsync(new ApprovalCommand
                {
                    Action = action, TargetModuleName = "Request", FieldName = "Approval", FlowId = string.Empty,
                });
                Assert.That(r.IsSuccess, Is.False, action.ToString());
            }
            //Resubmit も FlowId 必須 (Submit と同じ形の要求だが対象フローが要る)
            var rs = await CreateEngine("1").ExecuteAsync(new ApprovalCommand
            {
                Action = ApprovalAction.Resubmit, TargetModuleName = "Request", FieldName = "Approval",
                TargetSubmitData = CreateNewRequestSubmit("x"), Route = CreateRoute(), FlowId = string.Empty,
            });
            Assert.That(rs.IsSuccess, Is.False);
        }

        #endregion

    }
}
