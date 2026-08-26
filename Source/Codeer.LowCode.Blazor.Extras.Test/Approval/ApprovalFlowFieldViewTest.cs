using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Approval
{
    /// <summary>
    /// ApprovalFlowField (クライアント側) の表示データ読み込み。
    /// 契約の任意役割が空でも表示が成立すること、申請者判定がフロー行の Applicant で決まることを、
    /// サーバー応答を差し替えた軽量ハーネスで検証する (DB 不要)。
    /// </summary>
    public class ApprovalFlowFieldViewTest
    {
        const string FlowId = "10";

        static DesignData CreateDesignData(bool minimalContracts)
        {
            var d = new DesignData();

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = "Main", DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            d.AddModule(user);

            var request = new ModuleDesign { Name = "Request", DataSourceName = "Main", DbTable = "Requests" };
            request.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            request.Fields.Add(new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "ApprovalId" });
            d.AddModule(request);

            var flow = new ModuleDesign { Name = "ApprovalFlow", DataSourceName = "Main", DbTable = "ApprovalFlows" };
            flow.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            flow.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "Status" });
            flow.Fields.Add(new TextFieldDesign { Name = "TargetModuleName", DbColumn = "TargetModuleName" });
            flow.Fields.Add(new TextFieldDesign { Name = "TargetId", DbColumn = "TargetId" });
            flow.Fields.Add(new LinkFieldDesign { Name = "Applicant", SearchCondition = new SearchCondition("AppUser"), DbColumn = "Applicant" });
            flow.Fields.Add(new NumberFieldDesign { Name = "AttemptNo", DbColumn = "AttemptNo" });
            flow.Fields.Add(new NumberFieldDesign { Name = "CurrentStepNo", DbColumn = "CurrentStepNo" });
            flow.Fields.Add(new ListFieldDesign { Name = "Members", SearchCondition = new SearchCondition("ApprovalFlowMember") });
            flow.Fields.Add(new ListFieldDesign { Name = "Histories", SearchCondition = new SearchCondition("ApprovalHistory") });
            flow.Fields.Add(new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = "Version" });
            flow.Fields.Add(new ApprovalFlowContractFieldDesign { Name = "Contract" });
            d.AddModule(flow);

            var member = new ModuleDesign { Name = "ApprovalFlowMember", DataSourceName = "Main", DbTable = "ApprovalFlowMembers" };
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            member.Fields.Add(new LinkFieldDesign { Name = "Flow", SearchCondition = new SearchCondition("ApprovalFlow"), DbColumn = "FlowId" });
            member.Fields.Add(new NumberFieldDesign { Name = "AttemptNo", DbColumn = "AttemptNo" });
            member.Fields.Add(new NumberFieldDesign { Name = "StepNo", DbColumn = "StepNo" });
            member.Fields.Add(new TextFieldDesign { Name = "StepType", DbColumn = "StepType" });
            member.Fields.Add(new LinkFieldDesign { Name = "ApproverUser", SearchCondition = new SearchCondition("AppUser"), DbColumn = "ApproverUser" });
            member.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "Status" });
            var memberContract = new ApprovalMemberContractFieldDesign { Name = "Contract", TurnNotifyMail = string.Empty };
            if (minimalContracts)
            {
                memberContract.StepName = memberContract.IsFinalStep = memberContract.ActedAt = string.Empty;
                memberContract.CompletionPolicy = memberContract.ReturnScope = string.Empty;
                memberContract.IsCommentRequiredOnReject = memberContract.IsRequired = string.Empty;
            }
            else
            {
                member.Fields.Add(new TextFieldDesign { Name = "StepName", DbColumn = "StepName" });
                member.Fields.Add(new TextFieldDesign { Name = "CompletionPolicy", DbColumn = "CompletionPolicy" });
                member.Fields.Add(new BooleanFieldDesign { Name = "IsCommentRequiredOnReject", DbColumn = "IsCommentRequiredOnReject" });
                member.Fields.Add(new TextFieldDesign { Name = "ReturnScope", DbColumn = "ReturnScope" });
                member.Fields.Add(new BooleanFieldDesign { Name = "IsRequired", DbColumn = "IsRequired" });
                member.Fields.Add(new BooleanFieldDesign { Name = "IsFinalStep", DbColumn = "IsFinalStep" });
                member.Fields.Add(new DateTimeFieldDesign { Name = "ActedAt", DbColumn = "ActedAt" });
            }
            member.Fields.Add(memberContract);
            d.AddModule(member);

            var history = new ModuleDesign { Name = "ApprovalHistory", DataSourceName = "Main", DbTable = "ApprovalHistories" };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            history.Fields.Add(new LinkFieldDesign { Name = "Flow", SearchCondition = new SearchCondition("ApprovalFlow"), DbColumn = "FlowId" });
            var historyContract = new ApprovalHistoryContractFieldDesign { Name = "Contract" };
            if (minimalContracts)
            {
                historyContract.AttemptNo = historyContract.Action = historyContract.ActorUser = string.Empty;
                historyContract.Comment = historyContract.ActedAt = string.Empty;
            }
            else
            {
                history.Fields.Add(new NumberFieldDesign { Name = "AttemptNo", DbColumn = "AttemptNo" });
                history.Fields.Add(new TextFieldDesign { Name = "Action", DbColumn = "Action" });
                history.Fields.Add(new LinkFieldDesign { Name = "ActorUser", SearchCondition = new SearchCondition("AppUser"), DbColumn = "ActorUser" });
                history.Fields.Add(new TextFieldDesign { Name = "Comment", DbColumn = "Comment" });
                history.Fields.Add(new DateTimeFieldDesign { Name = "ActedAt", DbColumn = "ActedAt" });
            }
            history.Fields.Add(historyContract);
            d.AddModule(history);
            return d;
        }

        static ModuleData Row(string moduleName, params (string Name, FieldDataBase Data)[] fields)
        {
            var data = new ModuleData { Name = moduleName };
            foreach (var (name, fieldData) in fields) data.Fields[name] = fieldData;
            return data;
        }

        static TextFieldData Text(string value) => new() { Value = value };
        static NumberFieldData Number(int value) => new() { Value = value };
        static LinkFieldData Link(string value, string display = "") => new() { Value = value, DisplayText = display };
        static BooleanFieldData Bool(bool value) => new() { Value = value };

        //サーバー応答: フロー行 (申請者 "1"・進行中・ステップ 1) / メンバー 2 行 / 履歴 1 行
        static Paging<ModuleData> Respond(GetListRequest request, bool minimal)
        {
            var items = request.Condition.ModuleName switch
            {
                "ApprovalFlow" => new List<ModuleData>
                {
                    Row("ApprovalFlow",
                        ("Id", new IdFieldData { Value = FlowId }),
                        ("Status", Text(ApprovalFlowStatus.InProgress.ToDesignValue())),
                        ("AttemptNo", Number(1)),
                        ("CurrentStepNo", Number(1)),
                        ("Applicant", Link("1", "申請者")),
                        (SystemFieldNames.OptimisticLocking, new OptimisticLockingFieldData())),
                },
                "ApprovalFlowMember" => minimal
                    ? new List<ModuleData>
                    {
                        Row("ApprovalFlowMember", ("Id", new IdFieldData { Value = "100" }), ("AttemptNo", Number(1)), ("StepNo", Number(1)),
                            ("StepType", Text(ApprovalStepType.Approval.ToDesignValue())), ("ApproverUser", Link("2", "課長")),
                            ("Status", Text(ApprovalMemberStatus.Waiting.ToDesignValue()))),
                        Row("ApprovalFlowMember", ("Id", new IdFieldData { Value = "101" }), ("AttemptNo", Number(1)), ("StepNo", Number(2)),
                            ("StepType", Text(ApprovalStepType.Approval.ToDesignValue())), ("ApproverUser", Link("3", "部長")),
                            ("Status", Text(ApprovalMemberStatus.Pending.ToDesignValue()))),
                    }
                    : new List<ModuleData>
                    {
                        Row("ApprovalFlowMember", ("Id", new IdFieldData { Value = "100" }), ("AttemptNo", Number(1)), ("StepNo", Number(1)),
                            ("StepName", Text("課長承認")), ("StepType", Text(ApprovalStepType.Approval.ToDesignValue())),
                            ("IsCommentRequiredOnReject", Bool(true)), ("ApproverUser", Link("2", "課長")), ("IsRequired", Bool(false)),
                            ("Status", Text(ApprovalMemberStatus.Waiting.ToDesignValue()))),
                    },
                "ApprovalHistory" => minimal
                    ? new List<ModuleData> { Row("ApprovalHistory", ("Id", new IdFieldData { Value = "200" }), ("Flow", Link(FlowId))) }
                    : new List<ModuleData>
                    {
                        //履歴上の Submit 実行者は "9" (フロー行の Applicant "1" と食い違わせ、どちらを見ているかを判別する)
                        Row("ApprovalHistory", ("Id", new IdFieldData { Value = "200" }), ("AttemptNo", Number(1)),
                            ("Action", Text(ApprovalAction.Submit.ToDesignValue())), ("ActorUser", Link("9", "誰か")), ("Comment", Text("c"))),
                    },
                _ => new List<ModuleData>(),
            };
            return new Paging<ModuleData> { Items = items, TotalCount = items.Count };
        }

        static async Task<(TestServices Services, ApprovalFlowField Field)> CreateAsync(bool minimal, string currentUserId)
        {
            var services = new TestServices(CreateDesignData(minimal));
            services.App.CurrentUserId = currentUserId;
            services.App.ListProvider = r => Respond(r, minimal);
            var module = await services.CreateModuleAsync("Request");
            var field = (ApprovalFlowField)module.GetField("Approval")!;
            await field.InitializeDataAsync(new ApprovalFlowFieldData { Id = FlowId });
            await field.ReloadAsync();
            return (services, field);
        }

        [Test]
        public async Task 最小契約_任意役割が空でも表示が成立し既定で補われる()
        {
            var (services, field) = await CreateAsync(minimal: true, currentUserId: "2");

            Assert.That(field.FlowStatus, Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(field.Steps.Count, Is.EqualTo(2));

            //StepName が無ければステップ番号で表示
            Assert.That(field.Steps[0].StepName, Is.EqualTo("1"));
            Assert.That(field.Steps[1].StepName, Is.EqualTo("2"));
            Assert.That(field.Steps[0].IsCurrent, Is.True);

            //IsRequired 既定 true / コメント必須 既定 false
            Assert.That(field.Steps[0].Members.Single().IsRequired, Is.True);
            Assert.That(field.IsCommentRequiredOnReject, Is.False);

            //現在ユーザー "2" は承認待ち
            Assert.That(field.CanApprove, Is.True);

            //履歴は Flow しか無くても行として並ぶ (表示項目は空)
            Assert.That(field.History.Count, Is.EqualTo(1));
            Assert.That(field.History[0].Action, Is.Empty);

            //空の役割は検索の SelectFields に乗らない
            foreach (var request in services.App.ListRequests)
                Assert.That(request.Condition.SelectFields, Has.None.Empty, request.Condition.ModuleName);
        }

        [Test]
        public async Task 申請者判定はフロー行のApplicantで決まり履歴に依存しない()
        {
            //通常契約: 履歴の Submit 実行者は "9" だが、フロー行の Applicant は "1"
            var (_, asApplicant) = await CreateAsync(minimal: false, currentUserId: "1");
            Assert.That(asApplicant.ApplicantUserId, Is.EqualTo("1"));
            Assert.That(asApplicant.IsApplicant, Is.True);

            var (_, asOther) = await CreateAsync(minimal: false, currentUserId: "9");
            Assert.That(asOther.IsApplicant, Is.False);

            //最小契約 (履歴に Action/ActorUser 無し) でも同じ
            var (_, minimal) = await CreateAsync(minimal: true, currentUserId: "1");
            Assert.That(minimal.IsApplicant, Is.True);
        }

        [Test]
        public async Task 通常契約_役割の値がそのまま表示に使われる()
        {
            var (_, field) = await CreateAsync(minimal: false, currentUserId: "2");

            Assert.That(field.Steps.Single().StepName, Is.EqualTo("課長承認"));
            Assert.That(field.Steps.Single().Members.Single().IsRequired, Is.False); //列の値 (false) が既定 (true) より優先
            Assert.That(field.IsCommentRequiredOnReject, Is.True);
            Assert.That(field.History.Single().Action, Is.EqualTo(ApprovalAction.Submit.ToDesignValue()));
            Assert.That(field.History.Single().ActorDisplayText, Is.EqualTo("誰か"));
        }
    }
}
