using Codeer.LowCode.Blazor.Extras.Csv;

namespace Codeer.LowCode.Blazor.Extras.Test.Csv
{
    public class CsvTextParserTest
    {
        static List<List<string>> Parse(string text, char delimiter = ',')
        {
            using var reader = new StringReader(text);
            return CsvTextParser.Parse(reader, delimiter);
        }

        [Test]
        public void PlainCells()
        {
            var rows = Parse("a,b,c\r\n1,2,3\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b", "c" }, new[] { "1", "2", "3" } }));
        }

        [Test]
        public void QuotedCellContainingDelimiter()
        {
            var rows = Parse("\"大阪, 北区\",100\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "大阪, 北区", "100" } }));
        }

        [Test]
        public void DoubledQuotesBecomeSingleQuote()
        {
            var rows = Parse("\"彼は\"\"OK\"\"と言った\",x\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "彼は\"OK\"と言った", "x" } }));
        }

        [Test]
        public void NewlinesInsideQuotedCell()
        {
            var rows = Parse("\"1行目\r\n2行目\",\"a\nb\"\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "1行目\r\n2行目", "a\nb" } }));
        }

        [Test]
        public void LfOnlyLineEndings()
        {
            var rows = Parse("a,b\n1,2\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "1", "2" } }));
        }

        [Test]
        public void LastRowWithoutTrailingNewline()
        {
            var rows = Parse("a,b\r\n1,2");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "1", "2" } }));
        }

        [Test]
        public void TrailingEmptyCellIsKept()
        {
            var rows = Parse("a,b,\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b", "" } }));
        }

        [Test]
        public void EmptyAndAllEmptyRowsAreSkipped()
        {
            var rows = Parse("a,b\r\n\r\n,,\r\n\"\",\"\"\r\n1,2\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "1", "2" } }));
        }

        [Test]
        public void EmptyInputReturnsNoRows()
        {
            Assert.That(Parse(string.Empty), Is.Empty);
        }

        [Test]
        public void QuotedEmptyCellWithValue()
        {
            var rows = Parse("\"\",a\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "", "a" } }));
        }

        [Test]
        public void TabDelimiter()
        {
            var rows = Parse("a\tb\t\"c1\tc2\"\r\nx,y\tz\t1\r\n", '\t');
            Assert.That(rows, Is.EqualTo(new[]
            {
                new[] { "a", "b", "c1\tc2" },
                new[] { "x,y", "z", "1" }, //タブ区切りではカンマはただの文字
            }));
        }

        [Test]
        public void SemicolonDelimiter()
        {
            var rows = Parse("a;\"b1;b2\";c\r\n", ';');
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b1;b2", "c" } }));
        }

        //以下2つは「不正な CSV を黙って寛容に読む」現仕様の固定化。挙動を変える場合はここを直す

        [Test]
        public void LenientBareQuoteInUnquotedCell()
        {
            //RFC 4180 的には不正。途中の引用符で引用モードに入り後続の区切りを飲み込む
            var rows = Parse("a\"b,c\r\n");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "ab,c\r\n" } }));
        }

        [Test]
        public void LenientUnterminatedQuote()
        {
            //閉じ引用符がない場合は残り全部が1セルになる
            var rows = Parse("\"abc,def\r\nx,y");
            Assert.That(rows, Is.EqualTo(new[] { new[] { "abc,def\r\nx,y" } }));
        }

        [Test]
        public void EscapePlainTextPassesThrough()
        {
            Assert.That(CsvTextParser.Escape("東京 100", ','), Is.EqualTo("東京 100"));
        }

        [Test]
        public void EscapeNullBecomesEmpty()
        {
            Assert.That(CsvTextParser.Escape(null, ','), Is.EqualTo(string.Empty));
        }

        [Test]
        public void EscapeQuotesCellContainingDelimiterQuoteOrNewline()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CsvTextParser.Escape("大阪, 北区", ','), Is.EqualTo("\"大阪, 北区\""));
                Assert.That(CsvTextParser.Escape("彼は\"OK\"と言った", ','), Is.EqualTo("\"彼は\"\"OK\"\"と言った\""));
                Assert.That(CsvTextParser.Escape("1行目\r\n2行目", ','), Is.EqualTo("\"1行目\r\n2行目\""));
            });
        }

        [Test]
        public void EscapeDependsOnDelimiter()
        {
            Assert.Multiple(() =>
            {
                //タブ区切りではカンマは囲まない、タブは囲む
                Assert.That(CsvTextParser.Escape("a,b", '\t'), Is.EqualTo("a,b"));
                Assert.That(CsvTextParser.Escape("a\tb", '\t'), Is.EqualTo("\"a\tb\""));
            });
        }

        [Test]
        public void EscapeParseRoundTrip()
        {
            string[] cells = ["plain", "大阪, 北区", "彼は\"OK\"と言った", "1行目\r\n2行目", "", "  前後空白  "];
            var line = string.Join(',', cells.Select(e => CsvTextParser.Escape(e, ',')));
            var rows = Parse(line + "\r\n");
            Assert.That(rows[0], Is.EqualTo(cells));
        }
    }
}
