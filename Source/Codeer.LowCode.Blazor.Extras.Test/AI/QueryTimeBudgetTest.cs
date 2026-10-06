using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>QueryTimeBudget: 1 回の返事で SQL に使える時間と、次の SQL のタイムアウトの決め方。</summary>
    public class QueryTimeBudgetTest
    {
        [Test]
        public void 無制限なら1文の上限をそのまま使う()
        {
            var budget = new QueryTimeBudget(0);
            budget.Add(TimeSpan.FromHours(1));
            Assert.That(budget.Remaining, Is.Null);
            Assert.That(budget.IsExhausted, Is.False);
            Assert.That(budget.CommandTimeoutSeconds(15), Is.EqualTo(15));
            Assert.That(budget.CommandTimeoutSeconds(0), Is.EqualTo(0));
        }

        [Test]
        public void 残り時間で1文の上限を縮め最後は使い切る()
        {
            var budget = new QueryTimeBudget(45);
            Assert.That(budget.CommandTimeoutSeconds(15), Is.EqualTo(15));
            Assert.That(budget.CommandTimeoutSeconds(0), Is.EqualTo(45), "1 文が無制限なら残り時間");

            budget.Add(TimeSpan.FromSeconds(40));
            Assert.That(budget.CommandTimeoutSeconds(15), Is.EqualTo(5));

            budget.Add(TimeSpan.FromSeconds(4.2));
            Assert.That(budget.CommandTimeoutSeconds(15), Is.EqualTo(1), "端数は切り上げ・最小 1 秒");
            Assert.That(budget.IsExhausted, Is.False);

            budget.Add(TimeSpan.FromSeconds(1));
            Assert.That(budget.IsExhausted, Is.True);
        }

        [Test]
        public void 同じ返事では同じ予算を使う()
        {
            var items = new Dictionary<string, object>();
            var first = QueryTimeBudget.Of(items, 45);
            Assert.That(QueryTimeBudget.Of(items, 45), Is.SameAs(first));
            Assert.That(QueryTimeBudget.Of(new Dictionary<string, object>(), 45), Is.Not.SameAs(first));
        }
    }
}
