using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>メールプレビュー (dry-run): 変数区間・除外理由・HTML 生成。</summary>
    public class MailPreviewTest
    {
        [Test]
        public void FillWithSpans_変数が入った区間を解決後テキスト上の位置で返す()
        {
            var vars = new Dictionary<string, string> { ["Name"] = "佐藤", ["Rank"] = "", ["Id.Value"] = "42" };
            var (text, spans) = MailTemplateEngine.FillWithSpans("{Name} 様 ({Rank}) {{リテラル}} #{Id.Value}", vars);

            Assert.That(text, Is.EqualTo("佐藤 様 () {リテラル} #42"));
            Assert.That(spans.Select(e => (e.Name, e.Start, e.Length)), Is.EqualTo(new[]
            {
                ("Name", 0, 2),
                ("Rank", 6, 0),      //空の値も区間として残る (プレビューで「(空)」と示す)
                ("Id.Value", 16, 2),
            }));
            //Fill と同じ結果
            Assert.That(MailTemplateEngine.Fill("{Name} 様 ({Rank}) {{リテラル}} #{Id.Value}", vars), Is.EqualTo(text));
        }

        [Test]
        public void Build_除外理由を返し除外行でも変数は解決される()
        {
            var design = new ModuleDesign { Name = "Customer" };
            design.Fields.Add(new TextFieldDesign { Name = "Name" });
            design.Fields.Add(new TextFieldDesign { Name = "Email" });
            design.Fields.Add(new BooleanFieldDesign { Name = "OptOut" });
            ModuleData Row(string name, string email, bool optOut)
            {
                var d = new ModuleData { Name = "Customer" };
                d.Fields["Name"] = new TextFieldData { Value = name };
                d.Fields["Email"] = new TextFieldData { Value = email };
                d.Fields["OptOut"] = new BooleanFieldData { Value = optOut };
                return d;
            }
            var names = new[] { "Name" };

            var ok = MailRecipientBuilder.Build(design, Row("田中", "a@example.com", false), "Email", "OptOut", names);
            Assert.That(ok.Exclusion, Is.EqualTo(MailRecipientExclusion.None));
            Assert.That(ok.Recipient!.To, Is.EqualTo("a@example.com"));

            var optOut = MailRecipientBuilder.Build(design, Row("鈴木", "b@example.com", true), "Email", "OptOut", names);
            Assert.That(optOut.Exclusion, Is.EqualTo(MailRecipientExclusion.OptOut));
            Assert.That(optOut.Recipient, Is.Null);
            Assert.That(optOut.Variables["Name"], Is.EqualTo("鈴木")); //参考表示用に解決される

            var noAddress = MailRecipientBuilder.Build(design, Row("佐藤", "", false), "Email", "OptOut", names);
            Assert.That(noAddress.Exclusion, Is.EqualTo(MailRecipientExclusion.NoAddress));
        }

        [Test]
        public void HTMLは自己完結でデータを埋め込みscript閉じタグをエスケープする()
        {
            var doc = new MailPreviewDocument
            {
                Kind = "bulk", Title = "8月キャンペーン", Total = 2, SendCount = 1, ExcludedByOptOut = 1,
                Items =
                {
                    new MailPreviewItem { DisplayName = "佐藤", To = "a@example.com", Subject = "佐藤 様へ", Body = "<b>本文</b></script><script>alert(1)</script>" },
                    new MailPreviewItem { DisplayName = "鈴木", To = "b@example.com", Excluded = "OptOut" },
                }
            };
            var html = MailPreviewHtml.Render(doc);

            Assert.That(html, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(html, Does.Not.Contain("/*__PREVIEW_DATA__*/"));
            Assert.That(html, Does.Contain("\"kind\":\"bulk\"").And.Contain("\"displayName\":\"佐藤\"").And.Contain("\"excluded\":\"OptOut\""));
            //本文中の "</script>" はデータの script 要素を閉じないようエスケープされる
            Assert.That(html, Does.Contain("<\\/script>"));
            Assert.That(html, Does.Not.Contain("</script><script>alert(1)"));
            //外部依存なし (CDN 等を読まない)
            Assert.That(html, Does.Not.Contain("http://").And.Not.Contain("https://"));
        }
    }
}
