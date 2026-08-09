using Codeer.LowCode.Blazor.Extras.ScriptObjects;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// BulkMail(スクリプトオブジェクト)の宛先ソース検証。
    /// 送信自体はMailTransport/コントローラ経由なのでここでは扱わない。
    /// </summary>
    public class BulkMailTest
    {
        [Test]
        public async Task 宛先ソース未指定は失敗()
        {
            var bulk = new BulkMail();
            var result = await bulk.SendAsync();
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures[0].Error, Does.Contain("No recipient source"));
        }

        [Test]
        public async Task 宛先ソース複数指定は失敗()
        {
            var bulk = new BulkMail { Rows = new() };
            bulk.AddRecipient("a@example.com");
            var result = await bulk.SendAsync();
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures[0].Error, Does.Contain("Multiple recipient sources"));
        }

        [Test]
        public async Task エンドポイント未設定は失敗を返す()
        {
            var bulk = new BulkMail();
            bulk.AddRecipient("a@example.com").SetVariable("Name", "田中");
            var result = await bulk.SendAsync(); //Http未注入・エンドポイント未設定
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Failures[0].Error, Does.Contain("not configured"));
        }
    }
}
