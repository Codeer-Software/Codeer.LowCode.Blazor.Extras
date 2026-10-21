using Codeer.LowCode.Blazor.Extras.Server.Jobs;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    public class JobResultTest
    {
        [TestCase("a\r\nb\r\n", "b")]
        [TestCase("a\nb\n", "b")]
        [TestCase("a\nb", "b")]
        [TestCase("a\r\n\r\n", "")]
        [TestCase("", "")]
        [TestCase("only", "only")]
        public void Messageは標準出力の最後の行(string output, string expected)
            => Assert.That(new JobResult("X", 0, output, "").Message, Is.EqualTo(expected));

        [TestCase("\r\n  msg \r\n   at Stack", "msg")]
        [TestCase("msg\nmore", "msg")]
        [TestCase("", "ジョブ X が失敗しました (終了コード 1)")]
        [TestCase("  \r\n", "ジョブ X が失敗しました (終了コード 1)")]
        public void ErrorMessageは標準エラーの最初の空でない行(string error, string expected)
            => Assert.That(new JobResult("X", 1, "", error).ErrorMessage, Is.EqualTo(expected));

        [Test]
        public void Succeededは終了コード0()
        {
            Assert.That(new JobResult("X", 0, "", "").Succeeded, Is.True);
            Assert.That(new JobResult("X", 2, "", "").Succeeded, Is.False);
            Assert.That(new JobResult("X", 2, "out", "err").ToString(), Does.StartWith("ジョブ X が失敗しました (終了コード 2): err").And.Contain("out"));
        }
    }
}
