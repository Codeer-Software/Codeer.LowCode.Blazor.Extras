using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class FileValueConversionFieldDesignRenameTest
    {
        static FileValueConversionFieldDesign CreateField() => new()
        {
            Name = "Customer_Conversion",
            TargetField = "Customer",
            ConversionModule = "EdiMap",
            ExternalField = "EdiCode",
            InternalField = "CustomerCode",
        };

        [Test]
        public void ChangeTargetField()
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
                Assert.That(field.TargetField, Is.EqualTo("Client"));
                Assert.That(field.ExternalField, Is.EqualTo("EdiCode"));
                Assert.That(field.InternalField, Is.EqualTo("CustomerCode"));
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
            var result = CreateField().ChangeName(context);
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
                Assert.That(field.ConversionModule, Is.EqualTo("EdiMap2"));
                Assert.That(field.TargetField, Is.EqualTo("Customer"));
                Assert.That(field.ExternalField, Is.EqualTo("EdiCode"));
            });
        }

        [Test]
        public void ChangeExternalField()
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
                Assert.That(field.ExternalField, Is.EqualTo("EdiCode2"));
                Assert.That(field.InternalField, Is.EqualTo("CustomerCode"));
                //自モジュールではないので TargetField は変わらない
                Assert.That(field.TargetField, Is.EqualTo("Customer"));
            });
        }

        [Test]
        public void ChangeInternalField()
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
            Assert.That(field.InternalField, Is.EqualTo("CustomerCode2"));
        }

        [Test]
        public void LinkField対象の省略時はリンク先モジュールのフィールド改名に追従する()
        {
            //ConversionModule 省略 → リンク先 Owners のフィールド Name の改名に ExternalField が追従する
            var (designData, module) = Utilities.CreateDesignData();
            module.Fields.Add(new LinkFieldDesign { Name = "Owner", SearchCondition = new SearchCondition { ModuleName = "Owners" } });
            var field = new FileValueConversionFieldDesign { Name = "Owner_Conversion", TargetField = "Owner", ExternalField = "Name" };
            module.Fields.Add(field);

            var context = new RenameContext(designData)
            {
                Type = RenameType.Field,
                ModuleName = "Owners",
                OwnerModule = "mod",
                Source = "Name",
                Destination = "DisplayName",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.Multiple(() =>
            {
                Assert.That(field.ExternalField, Is.EqualTo("DisplayName"));
                Assert.That(field.ConversionModule, Is.EqualTo(string.Empty)); //省略のまま
                Assert.That(field.InternalField, Is.EqualTo(string.Empty));
            });
        }
    }
}
