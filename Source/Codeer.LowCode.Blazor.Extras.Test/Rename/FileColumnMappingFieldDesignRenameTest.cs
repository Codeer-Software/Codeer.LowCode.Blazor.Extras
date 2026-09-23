using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class FileColumnMappingFieldDesignRenameTest
    {
        static FileColumnMappingFieldDesign CreateField() => new()
        {
            Name = "Mapping1",
            Columns = new MappingColumns
            {
                Items =
                [
                    new MappingColumn { ExternalName = "得意先", Field = "Customer.Value" },
                    new MappingColumn { ExternalName = "取引先", FixedValue = "JP0001" },
                    new MappingColumn { ExternalName = "得意先コード", Field = "Customer.Value" }
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
        public void ChangeModuleDoesNothing()
        {
            //列構成はモジュール名を参照しない (値の引き当ては FileValueConversionField 側)
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Module,
                Source = "EdiMap",
                Destination = "EdiMap2",
            };
            var result = CreateField().ChangeName(context);
            Assert.That(result.RenameNeeded, Is.False);
        }
    }
}
