using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.BulkFile
{
    /// <summary>
    /// 標準形式 (内部名ヘッダ) への FileValueConversionField の適用。見出しは変えず、対象列のセルの値だけを引き当てる。
    /// </summary>
    public class FileValueConversionTransformTest
    {
        //Customer (テキスト) は EdiMap で明示変換、Owner (Link → Owners) は既定値 (リンク先モジュール・Id) で名前解決
        static ModuleDesign CreateModule(bool withConversion = true)
        {
            var module = Utilities.CreateModule();
            module.Fields.Add(new TextFieldDesign { Name = "Customer" });
            module.Fields.Add(new LinkFieldDesign { Name = "Owner", SearchCondition = new SearchCondition { ModuleName = "Owners" } });
            module.Fields.Add(new NumberFieldDesign { Name = "Qty" });
            if (!withConversion) return module;
            module.Fields.Add(new FileValueConversionFieldDesign
            {
                Name = "Customer_Conversion",
                TargetField = "Customer",
                ConversionModule = "EdiMap",
                ExternalField = "EdiCode",
                InternalField = "CustomerCode",
            });
            module.Fields.Add(new FileValueConversionFieldDesign { Name = "Owner_Conversion", TargetField = "Owner", ExternalField = "Name" });
            return module;
        }

        static Task<List<List<string>>> GetTableTexts(SearchCondition condition) => Task.FromResult<List<List<string>>>(condition.ModuleName switch
        {
            //C-0002 に 2 つの外部コードが対応 (先頭の行が採られる)
            "EdiMap" => [["EdiCode.Value", "CustomerCode.Value"], ["A001", "C-0001"], ["A002", "C-0002"], ["A002X", "C-0002"]],
            "Owners" => [["Id.Value", "Name.Value"], ["1", "山田"], ["2", "佐藤"]],
            _ => throw new InvalidOperationException($"unexpected module {condition.ModuleName}"),
        });

        static Task<List<List<string>>> NoTableTexts(SearchCondition condition)
            => throw new InvalidOperationException("変換フィールドなしのテストで変換表が要求された");

        [Test]
        public async Task 出力は見出しを変えず対象列の値だけを外部値にする()
        {
            List<List<string>> texts =
            [
                ["Customer.Value", "Owner.Value", "Qty.Value"],
                ["C-0001", "2", "5"],
                ["", "", "6"],       //空セルは空のまま
                ["ZZZ", "9", "7"],   //引き当てられない内部値はそのまま
            ];
            var result = await FileValueConversionTransform.ToExternalAsync(texts, CreateModule(), GetTableTexts);
            Assert.That(result, Is.EqualTo(new[]
            {
                new[] { "Customer.Value", "Owner.Value", "Qty.Value" },
                new[] { "A001", "佐藤", "5" },
                new[] { "", "", "6" },
                new[] { "ZZZ", "9", "7" },
            }));
        }

        [Test]
        public async Task 出力の対象はValueメンバの列だけ()
        {
            //DisplayText 等の他メンバの列は変換しない
            List<List<string>> texts = [["Owner.DisplayText", "Owner"], ["2", "2"]];
            var result = await FileValueConversionTransform.ToExternalAsync(texts, CreateModule(), GetTableTexts);
            Assert.That(result[1], Is.EqualTo(new[] { "2", "佐藤" })); //メンバ省略は Value
        }

        [Test]
        public async Task 変換フィールドが無ければ変換表を読まずそのまま返す()
        {
            List<List<string>> texts = [["Customer.Value"], ["C-0001"]];
            var result = await FileValueConversionTransform.ToExternalAsync(texts, CreateModule(withConversion: false), NoTableTexts);
            Assert.That(result, Is.SameAs(texts));
            Assert.That(result[1][0], Is.EqualTo("C-0001"));
            var (converted, errors) = await FileValueConversionTransform.ToInternalAsync(texts, CreateModule(withConversion: false), NoTableTexts);
            Assert.That(errors, Is.Empty);
            Assert.That(converted[1][0], Is.EqualTo("C-0001"));
        }

        [Test]
        public async Task 取込は外部値を内部値にし空セルは空にし引き当て失敗は行番号付きエラー()
        {
            List<List<string>> texts =
            [
                ["Owner.Value", "Customer.Value"],
                ["佐藤", "A001"],
                [" ", ""],
                ["鈴木", "ZZZ"],
            ];
            var (converted, errors) = await FileValueConversionTransform.ToInternalAsync(texts, CreateModule(), GetTableTexts);
            Assert.Multiple(() =>
            {
                Assert.That(converted[1], Is.EqualTo(new[] { "2", "C-0001" }));
                Assert.That(converted[2], Is.EqualTo(new[] { "", "" }));
                Assert.That(converted[3], Is.EqualTo(new[] { "", "" })); //引き当てられないセルは未設定
                Assert.That(errors, Is.EqualTo(new[]
                {
                    "Row 4, Owner.Value: code '鈴木' was not found in 'Owners'.",
                    "Row 4, Customer.Value: code 'ZZZ' was not found in 'EdiMap'.",
                }));
            });
        }

        [Test]
        public async Task 取込は列の並び替えや省略があっても見出しで対象列を見つける()
        {
            List<List<string>> texts = [["Qty.Value", "Customer.Value"], ["1", "A002"], ["2"]]; //短い行は触らない
            var (converted, errors) = await FileValueConversionTransform.ToInternalAsync(texts, CreateModule(), GetTableTexts);
            Assert.Multiple(() =>
            {
                Assert.That(errors, Is.Empty);
                Assert.That(converted[1], Is.EqualTo(new[] { "1", "C-0002" }));
                Assert.That(converted[2], Is.EqualTo(new[] { "2" }));
            });
        }

        [Test]
        public async Task 複数行に一致する値は先頭の行を採る()
        {
            var (converted, _) = await FileValueConversionTransform.ToInternalAsync([["Customer.Value"], ["A002X"]], CreateModule(), GetTableTexts);
            Assert.That(converted[1][0], Is.EqualTo("C-0002"));
            var external = await FileValueConversionTransform.ToExternalAsync([["Customer.Value"], ["C-0002"]], CreateModule(), GetTableTexts);
            Assert.That(external[1][0], Is.EqualTo("A002"));
        }

        [Test]
        public async Task 取込のセルエラーは構造化される()
        {
            var (_, errors) = await FileValueConversionTransform.ToInternalWithCellErrorsAsync(
                [["Qty.Value", "Owner.Value"], ["1", "佐藤"], ["2", "鈴木"]], CreateModule(), GetTableTexts);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(errors[0].ItemIndex, Is.EqualTo(1));
                Assert.That(errors[0].FileRow, Is.EqualTo(3));
                Assert.That(errors[0].FieldName, Is.EqualTo("Owner"));
                Assert.That(errors[0].ColumnLabel, Is.EqualTo("Owner.Value"));
                Assert.That(errors[0].Message, Is.EqualTo("code '鈴木' was not found in 'Owners'."));
            });
        }
    }
}
