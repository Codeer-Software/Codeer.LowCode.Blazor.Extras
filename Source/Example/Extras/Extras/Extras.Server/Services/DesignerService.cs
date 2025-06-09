using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Transfer;
using Codeer.LowCode.Blazor.Repository.Data;
using Extras.Client.Shared.Services;
using Extras.Server.Shared;

namespace Extras.Server.Services
{
    internal static class DesignerService
    {
        static object _sync = new();
        static DesignData _designData = new();
        static TransferDesignData _transferData = new();

        internal static DesignData GetDesignData()
        {
            lock (_sync)
            {
                var designData = DesignDataFileManager.GetDesignData(SystemConfig.Instance.DesignFileDirectory, _designData);
                if (ReferenceEquals(_designData, designData)) return _designData;
                DbAccessor.ClearTableDefinitionCache();
                _designData = designData;
                _transferData = _designData.CreateTransferDesignData();
                return _designData;
            }
        }

        internal static byte[] GetDesignDataForFront(ModuleData? currentUser)
        {
            var data = GetDesignData();
            return _transferData.AddResolvedPageFrames(data.ResolvePageFrames(new PageLinkUrlResolver(), currentUser)).ToBinary();
        }

        internal static MemoryStream? GetResource(string resourcePath)
            => DesignDataFileManager.GetResource(SystemConfig.Instance.DesignFileDirectory, resourcePath);
    }
}
