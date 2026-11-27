using Codeer.LowCode.Blazor.Extras.Server.Jobs;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    public class JobRuntimeProcessResultTest
    {
        [TestCase("a\r\nb\r\n", "b")]
        [TestCase("a\nb\n", "b")]
        [TestCase("a\nb", "b")]
        [TestCase("a\r\n\r\n", "")]
        [TestCase("", "")]
        [TestCase("only", "only")]
        public void Messageは標準出力の最後の行(string output, string expected)
            => Assert.That(new JobRuntimeProcessResult("X", 0, output, "").Message, Is.EqualTo(expected));

        [TestCase("\r\n  msg \r\n   at Stack", "msg")]
        [TestCase("msg\nmore", "msg")]
        [TestCase("", "ジョブ X が失敗しました (終了コード 1)")]
        [TestCase("  \r\n", "ジョブ X が失敗しました (終了コード 1)")]
        public void ErrorMessageは標準エラーの最初の空でない行(string error, string expected)
            => Assert.That(new JobRuntimeProcessResult("X", 1, "", error).ErrorMessage, Is.EqualTo(expected));

        [TestCase("a\r\n#job-result:{\"A\":1}\r\nmsg\r\n", "{\"A\":1}")]
        [TestCase("#job-result:{\"A\":1}\nmsg", "{\"A\":1}")]
        [TestCase("a\r\n\r\nmsg\r\n", null)]
        [TestCase("#job-result:{\"A\":1}\r\n\r\nmsg\r\n", null)]
        [TestCase("\r\n#job-result:{\"A\":1}\r\n", null)]
        [TestCase("#job-result:\r\nmsg\r\n", null)]
        [TestCase("only", null)]
        [TestCase("", null)]
        public void ResultJsonは標準出力の最後から2行目の印の後ろ(string output, string? expected)
            => Assert.That(new JobRuntimeProcessResult("X", 0, output, "").ResultJson, Is.EqualTo(expected));

        [Test]
        public void 失敗したらResultJsonはnull()
            => Assert.That(new JobRuntimeProcessResult("X", 1, "#job-result:{\"A\":1}\r\nmsg\r\n", "err").ResultJson, Is.Null);

        [Test]
        public void 印は約束の定数()
            => Assert.That(JobRuntime.ResultJsonPrefix, Is.EqualTo("#job-result:"));

        [Test]
        public void Succeededは終了コード0()
        {
            Assert.That(new JobRuntimeProcessResult("X", 0, "", "").Succeeded, Is.True);
            Assert.That(new JobRuntimeProcessResult("X", 2, "", "").Succeeded, Is.False);
            Assert.That(new JobRuntimeProcessResult("X", 2, "out", "err").ToString(), Does.StartWith("ジョブ X が失敗しました (終了コード 2): err").And.Contain("out"));
        }
    }
}
