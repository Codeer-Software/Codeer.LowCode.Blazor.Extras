using System.Reflection;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    // デザインチェック指摘のコード規約(コアと同じ): 発行クラスは入れ子の static class Codes に const int の番号を並べ、
    // DesignCheckCode.Create(typeof(発行クラス), 番号) で "発行クラス名:番号" にする。番号はクラス内で 1 からの連番。
    public class DesignCheckCodesTest
    {
        [Test]
        public void 全発行クラスのCodesはconst_intで連番()
        {
            var codesClasses = typeof(MailFieldDesign).Assembly.GetTypes()
                .Where(t => t.IsNested && t.Name == "Codes" && t.IsAbstract && t.IsSealed)
                .ToList();
            Assert.That(codesClasses.Count, Is.GreaterThanOrEqualTo(10));

            foreach (var codes in codesClasses)
            {
                var owner = codes.DeclaringType!.Name;
                var fields = codes.GetFields(BindingFlags.Public | BindingFlags.Static).ToList();
                Assert.That(fields.Count, Is.GreaterThanOrEqualTo(1), owner);
                Assert.That(fields.All(f => f.IsLiteral && f.FieldType == typeof(int)), Is.True, owner);
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
            Assert.That(ret.Select(e => e.Code), Does.Contain(DesignCheckCode.Create(typeof(MailFieldDesign), MailFieldDesign.Codes.ToRequired)));
            Assert.That(ret.All(e => !string.IsNullOrEmpty(e.Code)), Is.True);
        }
    }
}
