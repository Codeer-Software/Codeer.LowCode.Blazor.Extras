using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Test.CrossTab
{
    // 年・四半期の見出し: 鍵 (期間の開始日) と軸の年度の開始月から 年度と四半期の番号を出す
    public class CrossTabKeyTextTest
    {
        [Test]
        public void 暦年なら年と四半期の番号をそのまま出す()
        {
            Assert.That(CrossTabKeyText.Year(new DateOnly(2026, 1, 1), 1), Is.EqualTo("2026"));
            Assert.That(CrossTabKeyText.Quarter(new DateOnly(2026, 4, 1), 1), Is.EqualTo("2026 Q2"));
            Assert.That(CrossTabKeyText.Quarter(new DateOnly(2026, 10, 1), 1), Is.EqualTo("2026 Q4"));
        }

        [Test]
        public void 年度なら開始月より前の月は前の年度になる()
        {
            //4 月始まり: 2026-01-01 始まりの四半期は 2025 年度の Q4、2026-04-01 は 2026 年度の Q1
            var q4 = CrossTabKeyText.Quarter(new DateOnly(2026, 1, 1), 4);
            var q1 = CrossTabKeyText.Quarter(new DateOnly(2026, 4, 1), 4);
            Assert.That(q4, Does.Contain("2025").And.EndsWith(" Q4"));
            Assert.That(q1, Does.Contain("2026").And.EndsWith(" Q1"));
            Assert.That(CrossTabKeyText.Year(new DateOnly(2025, 4, 1), 4), Does.Contain("2025").And.Not.EqualTo("2025"));
        }
    }
}
