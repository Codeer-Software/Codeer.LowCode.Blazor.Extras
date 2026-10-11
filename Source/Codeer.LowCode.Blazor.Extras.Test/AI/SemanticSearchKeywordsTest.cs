using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>キーワード検索の語の切り方と LIKE のエスケープ (SemanticSearchKeywords)。</summary>
    public class SemanticSearchKeywordsTest
    {
        [Test]
        public void 空白で区切り重複を除く()
        {
            Assert.That(SemanticSearchKeywords.Split(" 納期  遅延　納期\t山田\r\n"), Is.EqualTo(new[] { "納期", "遅延", "山田" }));
            Assert.That(SemanticSearchKeywords.Split(null), Is.Empty);
            Assert.That(SemanticSearchKeywords.Split("  　 "), Is.Empty);
        }

        [Test]
        public void 部分一致のパターンにしワイルドカードはエスケープする()
        {
            var e = SemanticSearchKeywords.EscapeChar;
            Assert.That(SemanticSearchKeywords.LikePattern("納期", DataSourceType.PostgreSQL), Is.EqualTo("%納期%"));
            Assert.That(SemanticSearchKeywords.LikePattern("100%_x", DataSourceType.PostgreSQL), Is.EqualTo($"%100{e}%{e}_x%"));
            Assert.That(SemanticSearchKeywords.LikePattern($"a{e}b", DataSourceType.SQLite), Is.EqualTo($"%a{e}{e}b%"));
        }

        [Test]
        public void SQLServerだけ角括弧もエスケープする()
        {
            var e = SemanticSearchKeywords.EscapeChar;
            Assert.That(SemanticSearchKeywords.LikePattern("[重要]", DataSourceType.SQLServer), Is.EqualTo($"%{e}[重要]%"));
            foreach (var type in new[] { DataSourceType.PostgreSQL, DataSourceType.MySQL, DataSourceType.Oracle, DataSourceType.SQLite })
                Assert.That(SemanticSearchKeywords.LikePattern("[重要]", type), Is.EqualTo("%[重要]%"), type.ToString());
        }
    }
}
