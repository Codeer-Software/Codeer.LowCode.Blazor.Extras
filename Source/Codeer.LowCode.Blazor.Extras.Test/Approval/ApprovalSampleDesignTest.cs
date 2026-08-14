using Codeer.LowCode.Blazor.DesignLogic;
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
        public void 申請書モジュールの構造()
        {
            var d = Load();
            var request = d.Modules.Find("ExpenseRequest")!;

            var field = request.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            Assert.That(field.Name, Is.EqualTo("Approval"));
            Assert.That(field.DbColumn, Is.EqualTo("approval_id"));
            Assert.That(field.StateDbColumn, Is.EqualTo("approval_state"));
            Assert.That(field.ApplicantDbColumn, Is.EqualTo("approval_applicant"));
            Assert.That(field.AllowScriptRoute, Is.True);
            Assert.That(field.FlowModuleName, Is.EqualTo("ApprovalFlow"));

            //dotted リンク列は一覧の表示列として残す (JSON の静かな読込失敗をここで検出する)
            Assert.That(request.Fields.Any(e => e.Name == "Approval.Status"), Is.True);

            //編集ロック条件は専用検索コントロールの正準形:
            //1行 = FieldMatchCondition("Approval") に状態の Or (null = 未申請 + 編集可能3状態)
            var condition = request.DataWriteCondition.Condition as MultiMatchCondition;
            Assert.That(condition, Is.Not.Null);
            Assert.That(condition!.IsOrMatch, Is.True);
            var stateRow = condition.Children.OfType<FieldMatchCondition>().Single();
            Assert.That(stateRow.FieldName, Is.EqualTo("Approval"));
            Assert.That(stateRow.IsOrMatch, Is.True);
            Assert.That(stateRow.Children.Count, Is.EqualTo(4));
            Assert.That(stateRow.Children.OfType<FieldValueMatchCondition>()
                .All(e => e.SearchTargetVariable == "Approval.State"), Is.True);
            Assert.That(stateRow.Children.OfType<FieldValueMatchCondition>()
                .Count(e => e.Value is NullValue), Is.EqualTo(1));

            //条件エディタが専用検索コントロールを出せる (Extras.Designer 側の WPF コントロール)
            Assert.That(field.GetSearchControlTypeFullName(), Is.Not.Empty);
            Assert.That(field.MembersListFieldName, Is.EqualTo("ApprovalMembers"));
        }

        [Test]
        public void 高機能サンプルはメンバーの存在条件で権限を表す()
        {
            var d = Load();
            var request = d.Modules.Find("PurchaseRequest")!;

            //承認メンバーの埋め込みリスト (exists 条件のクライアント評価にも使う)
            var members = request.Fields.OfType<ListFieldDesign>().Single(e => e.Name == "ApprovalMembers");
            Assert.That(members.SearchCondition.ModuleName, Is.EqualTo("ApprovalFlowMember"));
            var binding = members.SearchCondition.Condition as FieldVariableMatchCondition;
            Assert.That(binding, Is.Not.Null);
            Assert.That(binding!.SearchTargetVariable, Is.EqualTo("Flow.Value"));
            Assert.That(binding.Variable, Is.EqualTo("Approval.Id"));

            //査定額の権限 = 最終承認の番 (専用検索コントロールの正準形: FieldMatchCondition の And 行)
            var permission = request.Fields.OfType<PermissionFieldDesign>()
                .Single(e => e.Name == "AssessedAmountPermission");
            var turn = (permission.WriteCondition.Condition as MultiMatchCondition)?
                .Children.OfType<FieldMatchCondition>().Single();
            Assert.That(turn, Is.Not.Null);
            Assert.That(turn!.FieldName, Is.EqualTo("Approval"));
            Assert.That(turn.IsOrMatch, Is.False);
            Assert.That(turn.Children.OfType<FieldValueMatchCondition>()
                .Any(e => e.SearchTargetVariable == "Approval.State"), Is.True);
            var isFinal = turn.Children.OfType<FieldValueMatchCondition>()
                .Single(e => e.SearchTargetVariable == "ApprovalMembers.IsFinalStep.Value");
            Assert.That((isFinal.Value as BooleanValue)?.Value, Is.True);
            Assert.That(turn.Children.OfType<FieldVariableMatchCondition>()
                .Single().SearchTargetVariable, Is.EqualTo("ApprovalMembers.ApproverUser.Value"));

            //申請内容の権限 = 未申請 or 申請者本人 (コピー列 Applicant)
            var requestFields = request.Fields.OfType<PermissionFieldDesign>()
                .Single(e => e.Name == "RequestFieldsPermission");
            var owner = requestFields.WriteCondition.Condition as MultiMatchCondition;
            Assert.That(owner!.Children.OfType<FieldVariableMatchCondition>()
                .Single().SearchTargetVariable, Is.EqualTo("Approval.Applicant"));

            //メンバーモジュール側: 条件が参照するフィールドをリストロードに同梱している
            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.ListLayouts[""].DataOnlyFields,
                Is.EquivalentTo(new[] { "StepType", "IsFinalStep" }));
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
