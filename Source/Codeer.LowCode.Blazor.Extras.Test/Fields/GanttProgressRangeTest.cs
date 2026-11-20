using System.Globalization;
using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// ガントのタスクの進捗: 表示できる範囲 (バーの 0% 〜 100%) の外の値を入れさせない。
    /// </summary>
    public class GanttProgressRangeTest
    {
        [Test]
        public void パーセントは0から100まで通す()
        {
            Assert.That(GanttProgressRange.IsValid(0m, ProgressScale.Percent), Is.True);
            Assert.That(GanttProgressRange.IsValid(50.5m, ProgressScale.Percent), Is.True);
            Assert.That(GanttProgressRange.IsValid(100m, ProgressScale.Percent), Is.True);
            Assert.That(GanttProgressRange.IsValid(100.1m, ProgressScale.Percent), Is.False);
            Assert.That(GanttProgressRange.IsValid(150m, ProgressScale.Percent), Is.False);
            Assert.That(GanttProgressRange.IsValid(-1m, ProgressScale.Percent), Is.False);
        }

        [Test]
        public void 割合は0から1まで通す()
        {
            Assert.That(GanttProgressRange.IsValid(0m, ProgressScale.Ratio), Is.True);
            Assert.That(GanttProgressRange.IsValid(0.5m, ProgressScale.Ratio), Is.True);
            Assert.That(GanttProgressRange.IsValid(1m, ProgressScale.Ratio), Is.True);
            Assert.That(GanttProgressRange.IsValid(1.01m, ProgressScale.Ratio), Is.False);
            Assert.That(GanttProgressRange.IsValid(50m, ProgressScale.Ratio), Is.False);
            Assert.That(GanttProgressRange.IsValid(-0.1m, ProgressScale.Ratio), Is.False);
        }

        [Test]
        public void 空は見ない()
        {
            Assert.That(GanttProgressRange.IsValid(null, ProgressScale.Percent), Is.True);
            Assert.That(GanttProgressRange.IsValid(null, ProgressScale.Ratio), Is.True);
            Assert.That(GanttProgressRange.GetError(null, ProgressScale.Percent, "進捗率"), Is.Null);
        }

        [Test]
        public void 範囲の外のときは項目の名前と範囲でメッセージを組み立てる()
        {
            Assert.That(Error("ja-JP", 150m, ProgressScale.Percent), Is.EqualTo("進捗率は0から100の間で入力してください。"));
            Assert.That(Error("ja-JP", 50m, ProgressScale.Ratio), Is.EqualTo("進捗率は0から1の間で入力してください。"));
            Assert.That(Error("en-US", 150m, ProgressScale.Percent), Is.EqualTo("進捗率 must be between 0 and 100."));
            Assert.That(Error("ja-JP", 100m, ProgressScale.Percent), Is.Null);
        }

        //文言はリソース (表示言語) で決まるので、表示言語を切り替えて組み立てる
        static string? Error(string cultureName, decimal value, ProgressScale scale)
        {
            var backup = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                return GanttProgressRange.GetError(value, scale, "進捗率");
            }
            finally
            {
                CultureInfo.CurrentUICulture = backup;
            }
        }
    }
}
