using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class BulkFileTransferButtonFieldDesignRenameTest
    {
        static BulkFileTransferButtonFieldDesign CreateField(string searchFieldName = "OrderSearch", string listFieldName = "")
            => new()
            {
                Name = "Transfer",
                SearchFieldName = searchFieldName,
                ListFieldName = listFieldName,
            };

        [Test]
        public void ChangeSearchFieldName()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "mod",
                OwnerModule = "mod",
                Source = "OrderSearch",
                Destination = "OrderSearch2",
            };
            var field = CreateField();
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.SearchFieldName, Is.EqualTo("OrderSearch2"));
        }

        [Test]
        public void ChangeListFieldName()
        {
            var context = new RenameContext(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "mod",
                OwnerModule = "mod",
                Source = "OrderList",
                Destination = "OrderList2",
            };
            var field = CreateField(searchFieldName: "", listFieldName: "OrderList");
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.ListFieldName, Is.EqualTo("OrderList2"));
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
    }
}
