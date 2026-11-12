using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class ClipboardCopyButtonFieldDesignRenameTest
    {
        static RenameContext CreateContext(string source, string destination)
            => new(new DesignData())
            {
                Type = RenameType.Field,
                ModuleName = "mod",
                OwnerModule = "mod",
                Source = source,
                Destination = destination,
            };

        [Test]
        public void ChangeSourceField()
        {
            var field = new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Prompt" };
            var result = field.ChangeName(CreateContext("Prompt", "Prompt2"));
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.SourceField, Is.EqualTo("Prompt2"));
        }

        [Test]
        public void ChangeUnrelatedFieldDoesNothing()
        {
            var field = new ClipboardCopyButtonFieldDesign { Name = "Copy", SourceField = "Prompt" };
            var result = field.ChangeName(CreateContext("Other", "Other2"));
            Assert.That(result.RenameNeeded, Is.False);
        }
    }
}
