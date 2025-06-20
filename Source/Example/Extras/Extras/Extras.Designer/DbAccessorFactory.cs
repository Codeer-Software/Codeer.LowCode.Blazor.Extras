using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Server.Shared;

namespace Extras.Designer
{
    public class DbAccessorFactory : IDbAccessorFactory
    {
        public IDbAccessor Create(DataSource[] dataSources) => new DbAccessor(dataSources);
    }
}
