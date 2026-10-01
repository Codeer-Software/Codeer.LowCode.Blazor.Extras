using Codeer.LowCode.Blazor.Extras.Designer;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    //監査ログの AI 用仕様 (SpecDocs/AuditLog.md)。フィールドが無いのでフィールドカタログに載らず、CCFD はこの文書だけで機能を知る。
    //CLI の記述は実装とずれやすい (FieldDocs で前例あり) ので、SetupCli のオプションと突き合わせる
    public class AuditLogSpecDocTest
    {
        [Test]
        public void 監査ログの仕様が埋め込まれている()
        {
            Assert.That(ExtrasSpecDocs.GetResourceNames(), Does.Contain("Codeer.LowCode.Blazor.Extras.Designer.SpecDocs.AuditLog.md"));
            var doc = ExtrasSpecDocs.Load("AuditLog");
            Assert.That(doc, Is.Not.Null.And.Contains("# 監査ログ"));
        }

        [Test]
        public void CLIの記述がSetupCliのオプションと一致する()
        {
            var doc = ExtrasSpecDocs.Load("AuditLog")!;
            //文書の CLI 行に出てくるオプション
            var documented = Regex.Matches(doc, @"--[a-z][a-z-]*").Select(m => m.Value).Distinct().OrderBy(e => e).ToList();
            //実装 (SetupCli.RunAuditLog) が読むオプション。変えたらここと文書の両方を直す
            var implemented = new[] { "--module-name", "--table", "--data-source", "--user-module", "--user-name-field", "--no-pageframe", "--ddl-out" }
                .OrderBy(e => e).ToList();
            Assert.That(documented, Is.EqualTo(implemented));
            Assert.That(doc, Does.Contain("audit-log-setup"));
        }

        [Test]
        public void 分類と結果の候補がサーバーのenumと一致する()
        {
            var doc = ExtrasSpecDocs.Load("AuditLog")!;
            foreach (var name in Enum.GetNames<AuditCategory>()) Assert.That(doc, Does.Contain($"`{name}`"), name);
            foreach (var name in Enum.GetNames<AuditResult>()) Assert.That(doc, Does.Contain($"`{name}`"), name);
        }
    }
}
