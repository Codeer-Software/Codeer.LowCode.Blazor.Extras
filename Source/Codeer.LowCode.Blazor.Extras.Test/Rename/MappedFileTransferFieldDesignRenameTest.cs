using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class MappedFileTransferFieldDesignRenameTest
    {
        static MappedFileTransferFieldDesign CreateField() => new()
        {
            Name = "Mapping1",
            Columns = new MappingColumns
            {
                Items =
                [
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" },
                    new MappingColumn
                    {
                        ExternalName = "得意先コード",
                        Field = "Customer.Value",
                        ConversionModule = "EdiMap",
                        ConversionExternalField = "EdiCode",
                        ConversionInternalField = "CustomerCode"
                    }
                ]
            }
        };

        [Test]
        public void ChangeOwnFieldKeepsDataMember()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "mod",
                OwnerModule = "mod",
                Source = "Customer",
                Destination = "Client",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.Multiple(() =>
            {
                Assert.That(field.Columns.Items[0].Field, Is.EqualTo("Client.Value"));
                Assert.That(field.Columns.Items[1].Field, Is.EqualTo(string.Empty));
                Assert.That(field.Columns.Items[2].Field, Is.EqualTo("Client.Value"));
            });
        }

        [Test]
        public void ChangeUnrelatedFieldDoesNothing()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "mod",
                OwnerModule = "mod",
                Source = "Other",
                Destination = "Other2",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.False);
        }

        [Test]
        public void ChangeConversionModule()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Module,
                Source = "EdiMap",
                Destination = "EdiMap2",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.Multiple(() =>
            {
                Assert.That(field.Columns.Items[2].ConversionModule, Is.EqualTo("EdiMap2"));
                //変換表のフィールド名・自モジュールのフィールドは変わらない
                Assert.That(field.Columns.Items[2].ConversionExternalField, Is.EqualTo("EdiCode"));
                Assert.That(field.Columns.Items[2].ConversionInternalField, Is.EqualTo("CustomerCode"));
                Assert.That(field.Columns.Items[2].Field, Is.EqualTo("Customer.Value"));
            });
        }

        [Test]
        public void ChangeConversionExternalField()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "EdiMap",
                OwnerModule = "mod",
                Source = "EdiCode",
                Destination = "EdiCode2",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.Multiple(() =>
            {
                Assert.That(field.Columns.Items[2].ConversionExternalField, Is.EqualTo("EdiCode2"));
                Assert.That(field.Columns.Items[2].ConversionInternalField, Is.EqualTo("CustomerCode"));
                //自モジュールではないので Field は変わらない
                Assert.That(field.Columns.Items[2].Field, Is.EqualTo("Customer.Value"));
            });
        }

        [Test]
        public void ChangeConversionInternalField()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "EdiMap",
                OwnerModule = "mod",
                Source = "CustomerCode",
                Destination = "CustomerCode2",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.Multiple(() =>
            {
                Assert.That(field.Columns.Items[2].ConversionInternalField, Is.EqualTo("CustomerCode2"));
                Assert.That(field.Columns.Items[2].ConversionExternalField, Is.EqualTo("EdiCode"));
            });
        }
    }
}
