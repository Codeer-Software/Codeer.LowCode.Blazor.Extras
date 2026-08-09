using Codeer.LowCode.Blazor.Extras.Mail;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailTemplateEngineTest
    {
        static readonly Dictionary<string, string> Vars = new()
        {
            ["Name"] = "田中",
            ["Qty"] = "3",
        };

        [TestCase("こんにちは {Name} 様", "こんにちは 田中 様")]
        [TestCase("{Name}{Qty}", "田中3")]
        [TestCase("値なし: {Unknown}!", "値なし: !")]
        [TestCase("リテラル {{Name}} と {Name}", "リテラル {Name} と 田中")]
        [TestCase("閉じない {Name", "閉じない {Name")]
        [TestCase("空 {} 変数", "空  変数")]
        [TestCase("", "")]
        [TestCase("}} と {{", "} と {")]
        public void Fill(string template, string expected)
            => Assert.That(MailTemplateEngine.Fill(template, Vars), Is.EqualTo(expected));

        [Test]
        public void Fill_入れ子の開き括弧はリテラル扱いで後続は解決される()
            => Assert.That(MailTemplateEngine.Fill("{a{Name}", Vars), Is.EqualTo("{a田中"));

        [Test]
        public void Fill_nullは空文字()
            => Assert.That(MailTemplateEngine.Fill(null, Vars), Is.EqualTo(string.Empty));
    }
}
