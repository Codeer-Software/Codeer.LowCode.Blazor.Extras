using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Approval
{
    /// <summary>
    /// Example の承認フローサンプルデザインが正しく読み込めることの検証。
    /// デザイン JSON は型名・プロパティ名の誤りが「静かに既定値へ落ちる」ため、
    /// 読み込み結果の構造をここで固定する (サンプルの回帰ガード)。
    /// </summary>
    public class ApprovalSampleDesignTest
    {
        static string DesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Example", "Design"));

        //GetDesignData は App.zip を読む形式のため、リポジトリのデザインフォルダを一時 zip にして読む
        static DesignData Load()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"approval_design_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(DesignDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Test]
        public void サンプルデザインが読み込める()
        {
            var d = Load();
            Assert.That(d.AppSettings.CurrentUserModuleDesignName, Is.EqualTo("AppUser"));
            foreach (var name in new[] { "AppUser", "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory", "ExpenseRequest" })
            {
                Assert.That(d.Modules.Find(name), Is.Not.Null, name);
            }
        }

        [Test]
        public void エンジン用モジュールはUIを持たず検索用モジュールが一覧を担う()
        {
            var d = Load();
            foreach (var name in new[] { "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory" })
            {
                var module = d.Modules.Find(name)!;
                Assert.That(module.ListLayouts[""].Elements.SelectMany(e => e).All(e => string.IsNullOrEmpty(e.FieldName)), Is.True, name);
                Assert.That(module.SearchLayouts.Values.All(e => e.Layout.GetDescendantLayouts<FieldLayoutDesign>().Count == 0), Is.True, name);
                Assert.That(d.Scripts.ContainsKey(name), Is.False, name);
            }
            //申請書側の権限評価 (メンバー行) に使う列は DataOnlyFields として残る
            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.ListLayouts[""].DataOnlyFields, Is.EquivalentTo(new[] { "StepType", "IsFinalStep", "Status", "ApproverUser" }));

            foreach (var name in new[] { "MyApprovalList", "ApprovalStatusList" })
            {
                var module = d.Modules.Find(name)!;
                Assert.That(module.DbTable, Is.Empty, name);
                Assert.That(module.CanCreate || module.CanUpdate || module.CanDelete, Is.False, name);
                var query = module.Fields.OfType<QueryFieldDesign>().Single();
                var outputs = query.QuerySetting.Parameters.Where(e => !e.IsParameter).Select(e => e.Name).ToList();
                foreach (var f in module.Fields.OfType<DbValueFieldDesignBase>())
                    Assert.That(outputs, Does.Contain(f.DbColumn), $"{name}.{f.Name}");
                Assert.That(module.ListLayouts[""].Elements[0].Select(e => e.FieldName), Does.Contain("OpenRequestButton"), name);
                Assert.That(d.Scripts[name], Does.Contain("OpenRequest_OnClick"), name);
            }
            var inbox = d.Modules.Find("MyApprovalList")!.Fields.OfType<QueryFieldDesign>().Single();
            Assert.That(inbox.QuerySetting.Parameters.Any(e => e.IsParameter && e.Name == "current_user_id"), Is.True);
            //承認状況の検索 = 状態 (複数選択) / 申請者 (ユーザー選択) / 申請種別 (申請書モジュールを表示名で選択)。入力パラメータは持たない
            var statusList = d.Modules.Find("ApprovalStatusList")!;
            Assert.That(statusList.Fields.OfType<QueryFieldDesign>().Single().QuerySetting.Parameters.Any(e => e.IsParameter), Is.False);
            var searchFields = statusList.SearchLayouts[""].Layout.GetDescendantLayouts<FieldLayoutDesign>().Select(e => e.FieldName)
                .Where(n => statusList.Fields.First(f => f.Name == n) is not LabelFieldDesign);
            Assert.That(searchFields, Is.EquivalentTo(new[] { "Status", "Applicant", "TargetModuleName" }));
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "Status")).AllowOrSearch, Is.True);
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "Applicant")).SearchCondition.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "TargetModuleName")).EnumName, Is.EqualTo("ApprovalRequestType"));
            Assert.That(d.Enums.Single(e => e.Name == "ApprovalRequestType").Members.Select(e => e.GetValue()), Is.EqualTo(new[] { "PurchaseRequest" }));
            //待ち手 (今 Waiting のメンバー名) が一覧に出る。申請種別は Select の表示名で見せる (モジュール名そのものは出さない)
            var columns = statusList.ListLayouts[""].Elements[0].Select(e => e.FieldName).ToList();
            Assert.That(columns, Does.Contain("WaitingNames").And.Contain("TargetModuleName"));
        }

        [Test]
        public void 申請書モジュールの構造()
        {
            var d = Load();
            var request = d.Modules.Find("ExpenseRequest")!;

            var field = request.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            Assert.That(field.Name, Is.EqualTo("Approval"));
            Assert.That(field.DbColumn, Is.EqualTo("approval_id"));
            Assert.That(field.FlowModuleName, Is.EqualTo("ApprovalFlow"));

            //dotted リンク列はレイアウト使用から自動合成される (フロー側の Select+enum のクローンになる)
            var statusClone = request.Fields.FirstOrDefault(e => e.Name == "Approval.Status");
            Assert.That(statusClone, Is.InstanceOf<SelectFieldDesign>());
            Assert.That(((SelectFieldDesign)statusClone!).EnumName, Is.EqualTo("ApprovalFlowStatus"));

            //編集ロック条件は汎用条件エディタの行モデル (1行 = 1ターゲット):
            //Or 直下に "Approval.Status" への葉条件4つ (null = 未申請 + 編集可能3状態)
            var condition = request.DataWriteCondition.Condition as MultiMatchCondition;
            Assert.That(condition, Is.Not.Null);
            Assert.That(condition!.IsOrMatch, Is.True);
            var stateLeaves = condition.Children.OfType<FieldValueMatchCondition>().ToList();
            Assert.That(stateLeaves.Count, Is.EqualTo(4));
            Assert.That(stateLeaves.All(e => e.SearchTargetVariable == "Approval.Status.Value"), Is.True);
            Assert.That(stateLeaves.Count(e => e.Value is NullValue), Is.EqualTo(1));

            //専用の検索コントロールは持たない (条件は汎用エディタでリンク越しパスとして書く)
            Assert.That(field.GetSearchControlTypeFullName(), Is.Empty);
        }

        [Test]
        public void 経路マスタモジュールは契約を持たないただのモジュール()
        {
            //経路マスタは経路モジュールのスクリプト (Load) が ModuleSearcher で読む「ただのモジュール」。
            //承認フロー側 (フィールド / エンジン) は経路マスタの形を知らない = 契約フィールドを置かない
            var d = Load();
            foreach (var name in new[] { "ApprovalRoute", "ApprovalRouteStep", "ApprovalRouteStepMember" })
            {
                var module = d.Modules.Find(name)!;
                Assert.That(module, Is.Not.Null, name);
                Assert.That(module.Fields.Any(e => e is ContractFieldDesignBase), Is.False, name);
            }

            //管理画面は通常のローコード (Steps / Members は普通の ListField = ListInList 構成。ページャなし)
            var route = d.Modules.Find("ApprovalRoute")!;
            var steps = (ListFieldDesign)route.Fields.First(e => e.Name == "Steps");
            Assert.That(steps.UseIndexSort, Is.True);
            Assert.That(steps.PagerPosition, Is.EqualTo(PagerPosition.None));
            Assert.That(steps.SearchCondition.ModuleName, Is.EqualTo("ApprovalRouteStep"));
            Assert.That(steps.SearchCondition.SortConditions.Single().Variable, Is.EqualTo("StepNo.Value"));

            //StepType/CompletionPolicy/ReturnScope はコード定義 enum の Select (値候補と表示名が自動で出る)
            var step = d.Modules.Find("ApprovalRouteStep")!;
            Assert.That(((SelectFieldDesign)step.Fields.First(e => e.Name == "StepType")).EnumName, Is.EqualTo("ApprovalStepType"));
            Assert.That(((SelectFieldDesign)step.Fields.First(e => e.Name == "CompletionPolicy")).EnumName, Is.EqualTo("ApprovalCompletionPolicy"));
            Assert.That(((SelectFieldDesign)step.Fields.First(e => e.Name == "ReturnScope")).EnumName, Is.EqualTo("ApprovalReturnScope"));

            //マスタの読み込みは経路モジュールの Load に共通化し、申請書は new ApprovalRoute().Load(名前) で呼ぶ
            //(LoadRoute のような組み込み API は無い)
            Assert.That(d.Scripts["ApprovalRoute"], Does.Contain("ApprovalRouteData Load(").And.Contain("ModuleSearcher<ApprovalRouteStepMember>"));
            Assert.That(d.Scripts["ExpenseRequest"], Does.Contain("new ApprovalRoute().Load(\"経費ルート\")").And.Contain("OnBuildRoute"));
            Assert.That(d.Scripts["PurchaseRequest"], Does.Contain("new ApprovalRoute().Load(").And.Contain("購買ルート(高額)"));
        }

        [Test]
        public void フローモジュールがメンバー一覧と履歴一覧を持つ()
        {
            //メンバー一覧はフローモジュール側の予約名 "Members"。
            //申請書側の条件は "(フィールド名).Members.～" のリンク越し存在条件で参照する
            //(申請書モジュールごとに一覧フィールドを複製しない)
            var d = Load();
            var flow = d.Modules.Find("ApprovalFlow")!;

            //バインド条件は条件エディタの正準形 (Multi 直下に葉)。bare の葉をルートに置くと
            //エディタが解釈できず、デザイナ保存で空条件に潰される (実機で実証済みの罠)
            static FieldVariableMatchCondition Binding(ListFieldDesignBase list)
                => (FieldVariableMatchCondition)((MultiMatchCondition)list.SearchCondition.Condition!).Children.Single();

            var members = flow.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Members");
            Assert.That(members.SearchCondition.ModuleName, Is.EqualTo("ApprovalFlowMember"));
            Assert.That(Binding(members).SearchTargetVariable, Is.EqualTo("Flow.Value"));
            Assert.That(Binding(members).Variable, Is.EqualTo("Id.Value"));

            var histories = flow.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "Histories");
            Assert.That(histories.SearchCondition.ModuleName, Is.EqualTo("ApprovalHistory"));
            Assert.That(Binding(histories).SearchTargetVariable, Is.EqualTo("Flow.Value"));

            //経路マスタの一覧バインドも同じ正準形
            var routeSteps = (ListFieldDesign)d.Modules.Find("ApprovalRoute")!.Fields.First(e => e.Name == "Steps");
            Assert.That(Binding(routeSteps).SearchTargetVariable, Is.EqualTo("Route.Value"));
            var stepMembers = (ListFieldDesign)d.Modules.Find("ApprovalRouteStep")!.Fields.First(e => e.Name == "Members");
            Assert.That(Binding(stepMembers).SearchTargetVariable, Is.EqualTo("Step.Value"));
        }

        [Test]
        public void 状態系フィールドはデザインenumで型付けされている()
        {
            //汎用の条件エディタで値候補(承認中/承認待ち等)が出るように、SelectField+enumにする
            var d = Load();

            var flowStatus = d.Modules.Find("ApprovalFlow")!.Fields.OfType<SelectFieldDesign>()
                .Single(e => e.Name == "Status");
            Assert.That(flowStatus.EnumName, Is.EqualTo("ApprovalFlowStatus"));

            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.Fields.OfType<SelectFieldDesign>().Single(e => e.Name == "Status").EnumName,
                Is.EqualTo("ApprovalMemberStatus"));
            Assert.That(member.Fields.OfType<SelectFieldDesign>().Single(e => e.Name == "StepType").EnumName,
                Is.EqualTo("ApprovalStepType"));

            //enum は Extras のコード定義 ([DesignEnum] 付き C# enum) から合成される (enum 定義ファイルは不要)
            foreach (var name in new[] { "ApprovalFlowStatus", "ApprovalMemberStatus", "ApprovalStepType" })
            {
                Assert.That(d.Enums.Any(e => e.Name == name && e.IsCodeDefined), Is.True, name);
            }

            //保存値 = メンバー名 (エンジンの文字列プロトコル値と一致)、表示名はリソースから解決される
            var flowStatusEnum = d.Enums.First(e => e.Name == "ApprovalFlowStatus");
            Assert.That(flowStatusEnum.FindMemberByName("InProgress")!.GetValue(),
                Is.EqualTo(ApprovalFlowStatus.InProgress.ToDesignValue()));
            Assert.That(flowStatusEnum.FindMemberByName("InProgress")!.GetDisplayText(flowStatusEnum.ValueType),
                Is.EqualTo("承認中").Or.EqualTo("In Progress"));
        }

        [Test]
        public void 高機能サンプルはメンバーの存在条件で権限を表す()
        {
            var d = Load();
            var request = d.Modules.Find("PurchaseRequest")!;

            //申請書側に一覧フィールドの複製は無い (フロー側 Members へのリンク越し参照に一本化)
            Assert.That(request.Fields.Any(e => e.Name == "ApprovalMembers"), Is.False);

            //査定額の権限 = 最終承認の番 (汎用条件エディタの行モデル: And 直下に葉条件4つ。
            //And で同じ一覧を指す条件は同一行 exists に自動合成される)
            var permission = request.Fields.OfType<PermissionFieldDesign>()
                .Single(e => e.Name == "AssessedAmountPermission");
            var turn = permission.WriteCondition.Condition as MultiMatchCondition;
            Assert.That(turn, Is.Not.Null);
            Assert.That(turn!.IsOrMatch, Is.False);
            Assert.That(turn.Children.Count, Is.EqualTo(4));
            Assert.That(turn.Children.OfType<FieldValueMatchCondition>()
                .Any(e => e.SearchTargetVariable == "Approval.Status.Value"), Is.True);
            var isFinal = turn.Children.OfType<FieldValueMatchCondition>()
                .Single(e => e.SearchTargetVariable == "Approval.Members.IsFinalStep.Value");
            Assert.That((isFinal.Value as BooleanValue)?.Value, Is.True);
            Assert.That(turn.Children.OfType<FieldVariableMatchCondition>()
                .Single().SearchTargetVariable, Is.EqualTo("Approval.Members.ApproverUser.Value"));

            //申請内容の権限 = 未申請 or 申請者本人 (フロー行の Applicant へのリンク越し参照)
            var requestFields = request.Fields.OfType<PermissionFieldDesign>()
                .Single(e => e.Name == "RequestFieldsPermission");
            var owner = requestFields.WriteCondition.Condition as MultiMatchCondition;
            Assert.That(owner!.Children.OfType<FieldVariableMatchCondition>()
                .Single().SearchTargetVariable, Is.EqualTo("Approval.Applicant.Value"));

            //メンバーモジュール側: 条件が参照するフィールドをリストロードに同梱している
            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.ListLayouts[""].DataOnlyFields,
                Is.EquivalentTo(new[] { "StepType", "IsFinalStep", "Status", "ApproverUser" }));
        }

        [Test]
        public void 承認モジュールの必須フィールドが揃っている()
        {
            var d = Load();
            var request = d.Modules.Find("ExpenseRequest")!;
            var field = request.Fields.OfType<ApprovalFlowFieldDesign>().Single();

            //フィールド自身のチェックで必須フィールド欠落・モジュール不在が出ないこと
            //(DB 列の存在チェックはデータソース定義が要るため対象外)
            var context = new Codeer.LowCode.Blazor.DesignLogic.Check.DesignCheckContext(
                "ExpenseRequest", d, new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>());
            var ret = field.CheckDesign(context);
            var structural = ret.Where(e => e.Message.Contains("承認フロー") || e.Message.Contains("approval flow")).ToList();
            Assert.That(structural, Is.Empty, string.Join(" / ", ret.Select(e => e.Message)));
        }

        [Test]
        public void 承認モジュールは誰も書けない保護条件を持つ()
        {
            var d = Load();
            foreach (var name in new[] { "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory" })
            {
                var module = d.Modules.Find(name)!;
                var condition = module.UserWriteCondition.Condition as FieldValueMatchCondition;
                Assert.That(condition, Is.Not.Null, name);
                Assert.That(condition!.SearchTargetVariable, Is.EqualTo("Id.Value"), name);
            }

            //フローモジュールは楽観ロック (綴り OptimisticLocking) 必須
            var flow = d.Modules.Find("ApprovalFlow")!;
            var optLock = flow.Fields.OfType<Codeer.LowCode.Blazor.Repository.Design.OptimisticLockingFieldDesign>().Single();
            Assert.That(optLock.Name, Is.EqualTo(SystemFieldNames.OptimisticLocking));
            Assert.That(optLock.IncrementVersion, Is.True);
        }
    }
}
