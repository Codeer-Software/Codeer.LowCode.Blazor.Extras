using System.Reflection;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    // デザインチェック指摘のコード規約(コアと同じ): 発行クラスは private const int Code〜 で番号を持ち、
    // DesignCheckCode.Create(typeof(発行クラス), 番号) で "発行クラス名:番号" にする。番号はクラス内で 1 からの連番。
    public class DesignCheckCodesTest
    {
        [Test]
        public void 全発行クラスのCodeはconst_intで連番()
        {
            var owners = typeof(MailFieldDesign).Assembly.GetTypes()
                .Select(t => (Owner: t.Name, Fields: t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name.Length > 4 && f.Name.StartsWith("Code") && char.IsUpper(f.Name[4])).ToList()))
                .Where(e => e.Fields.Count > 0)
                .ToList();
            Assert.That(owners.Count, Is.GreaterThanOrEqualTo(10));

            foreach (var (owner, fields) in owners)
            {
                Assert.That(fields.Select(f => (int)f.GetRawConstantValue()!).OrderBy(e => e),
                    Is.EqualTo(Enumerable.Range(1, fields.Count)), owner);
            }
        }

        [Test]
        public void 指摘はコードを持つ()
        {
            var (d, mod) = Utilities.CreateDesignData();
            var field = new MailFieldDesign { Name = "Mail" };
            mod.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext(mod.Name, d, Utilities.CreateDataSource()));
            Assert.That(ret.Select(e => e.Code), Does.Contain(DesignCheckCode.Create(typeof(MailFieldDesign), 1 /* ToRequired */)));
            Assert.That(ret.All(e => !string.IsNullOrEmpty(e.Code)), Is.True);
        }
    }
}
