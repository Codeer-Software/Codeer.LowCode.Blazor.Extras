using Codeer.LowCode.Blazor.Extras.Services;
using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.Services
{
    public class LocalizeServiceTest
    {
        static LocalizeService? Create(string tsv, string resourceName = "localize.tsv")
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(tsv));
            return LocalizeService.Create(resourceName, ms);
        }

        static void WithCulture(string cultureName, Action action)
        {
            var backup = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = backup;
            }
        }

        const string Tsv =
            "Key\tja-JP\ten-US\r\n" +
            "Hello\tこんにちは\tHello!\r\n" +
            "Save\t保存\tSave\r\n";

        [Test]
        public void SelectsCurrentCultureColumn()
        {
            WithCulture("ja-JP", () =>
            {
                var service = Create(Tsv)!;
                Assert.That(service, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(service.Localize("Hello"), Is.EqualTo("こんにちは"));
                    Assert.That(service.Localize("Save"), Is.EqualTo("保存"));
                });
            });
            WithCulture("en-US", () =>
            {
                var service = Create(Tsv)!;
                Assert.That(service.Localize("Hello"), Is.EqualTo("Hello!"));
            });
        }

        [Test]
        public void CultureHeaderIsCaseInsensitive()
        {
            WithCulture("ja-JP", () =>
            {
                var service = Create("Key\tJA-jp\r\nHello\tこんにちは\r\n")!;
                Assert.That(service.Localize("Hello"), Is.EqualTo("こんにちは"));
            });
        }

        [Test]
        public void UnknownCultureFallsBackToSecondColumn()
        {
            WithCulture("fr-FR", () =>
            {
                var service = Create(Tsv)!;
                Assert.That(service.Localize("Hello"), Is.EqualTo("こんにちは"));
            });
        }

        [Test]
        public void UnknownKeyPassesThrough()
        {
            WithCulture("ja-JP", () =>
            {
                var service = Create(Tsv)!;
                Assert.That(service.Localize("NotDefined"), Is.EqualTo("NotDefined"));
            });
        }

        [Test]
        public void QuotedValueCanContainTabAndNewline()
        {
            //共通 CSV パーサ化で引用符付きセルが使えるようになった (旧実装は素朴な Split でタブ入り訳文が壊れた)
            WithCulture("ja-JP", () =>
            {
                var service = Create("Key\tja-JP\r\nMessage\t\"1行目\r\n2行目\tタブ\"\r\n")!;
                Assert.That(service.Localize("Message"), Is.EqualTo("1行目\r\n2行目\tタブ"));
            });
        }

        [Test]
        public void RowsWithMissingColumnOrEmptyKeyAreIgnored()
        {
            WithCulture("ja-JP", () =>
            {
                var service = Create("Key\tja-JP\r\nHello\tこんにちは\r\nKeyOnlyNoTranslation\r\n\tvalueWithoutKey\r\n")!;
                Assert.Multiple(() =>
                {
                    Assert.That(service.Localize("Hello"), Is.EqualTo("こんにちは"));
                    Assert.That(service.Localize("KeyOnlyNoTranslation"), Is.EqualTo("KeyOnlyNoTranslation"));
                });
            });
        }

        [Test]
        public void NonTsvResourceNameReturnsNull()
        {
            Assert.That(Create(Tsv, "localize.csv"), Is.Null);
        }

        [Test]
        public void NullStreamReturnsNull()
        {
            Assert.That(LocalizeService.Create("localize.tsv", null), Is.Null);
        }

        [Test]
        public void HeaderOnlyOrEmptyReturnsNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Create(string.Empty), Is.Null);
                Assert.That(Create("Key\tja-JP\r\n"), Is.Null);
            });
        }
    }
}
