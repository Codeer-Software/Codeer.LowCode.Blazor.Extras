using Codeer.LowCode.Blazor.Extras.SemanticSearch;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>キーワード検索の語の切り方 (SemanticSearchKeywords)。LIKE のパターンとエスケープは本体 (SqlConditionContext.Like) のテスト。</summary>
    public class SemanticSearchKeywordsTest
    {
        [Test]
        public void 空白で区切り重複を除く()
        {
            Assert.That(SemanticSearchKeywords.Split(" 納期  遅延　納期\t山田\r\n"), Is.EqualTo(new[] { "納期", "遅延", "山田" }));
            Assert.That(SemanticSearchKeywords.Split(null), Is.Empty);
            Assert.That(SemanticSearchKeywords.Split("  　 "), Is.Empty);
        }
    }
}
