using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test
{
    public static class Utilities
    {
        public static ModuleDesign CreateModule(string moduleName = "mod") => new()
            { Name = moduleName, DataSourceName = "datasource", DbTable = "table" };

        public static Dictionary<string, List<DbTableDefinition>> CreateDataSource() =>
            new()
            {
                ["datasource"] =
                [
                    new DbTableDefinition
                    {
                        Name = "table",
                        Columns = [new DbColumnDefinition { Name = "DbColumn" }]
                    }
                ]
            };

        public static (DesignData, ModuleDesign) CreateDesignData(string moduleName = "mod")
        {
            var designData = new DesignData();
            var module = CreateModule(moduleName);
            designData.AddModule(module);
            return (designData, module);
        }

        public static void AddModule(this DesignData designData, ModuleDesign module)
            => ((IEditableModuleDesign)designData.Modules).Add(module);

        public static void AssertFieldLocation(this DesignCheckInfo info, string module, string field, string member)
        {
            Assert.Multiple(() =>
            {
                Assert.That(info, Is.InstanceOf<FieldDesignCheckInfo>());
                Assert.That(((FieldDesignCheckInfo)info).Location.Module, Is.EqualTo(module));
                Assert.That(((FieldDesignCheckInfo)info).Location.Field, Is.EqualTo(field));
                Assert.That(((FieldDesignCheckInfo)info).Location.Member, Is.EqualTo(member));
            });
        }
    }
}
