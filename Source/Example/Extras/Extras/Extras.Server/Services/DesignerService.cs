using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.DesignLogic.Transfer;
using Extras.Client.Shared.Services;
using Codeer.LowCode.Blazor.DbAccess;
using System.Security.Cryptography;

namespace Extras.Server.Services
{
    //読み込んだデザイン 1 版分。Version は App.zip の SHA-256 (監査ログに残す版。デザイナの送信履歴のファイル名にも同じ値が入る)
    record LoadedDesign(DesignData DesignData, TransferDesignData TransferData, string Version)
    {
        internal byte[] ForFront(ModuleData? currentUser)
            => TransferData.AddResolvedPageFrames(DesignData.ResolvePageFrames(new PageLinkUrlResolver(), currentUser)).ToBinary();
    }

    static class DesignerService
    {
        static object _sync = new();
        static LoadedDesign _current = new(new(), new(), string.Empty);

        static string ZipFilePath => Path.Combine(SystemConfig.Instance.DesignFileDirectory, "App.zip");

        //今読み込んでいるデザイン。App.zip が差し替わっていれば読み直す。
        //リクエストの中ではこれを直接使わず、そのリクエストの版に固定した RequestDesign (DataService.Design) を使う
        internal static LoadedDesign GetCurrent()
        {
            lock (_sync)
            {
                var designData = Load(_current.DesignData);
                if (ReferenceEquals(_current.DesignData, designData)) return _current;
                //版は App.zip の SHA-256。ハッシュを取る間に差し替わっていたら読み直す (読み込んだ中身と版を食い違わせない)
                var version = ComputeVersion();
                for (var retry = 0; retry < 3 && ZipFileDateTime() != designData.SourceFileDateTime; retry++)
                {
                    designData = Load(designData);
                    version = ComputeVersion();
                }
                DbAccessor.ClearTableDefinitionCache();
                _current = new(designData, designData.CreateTransferDesignData(), version);
                return _current;
            }
        }

        //リクエストの外 (起動時・バックグラウンドのジョブ) 用。今読み込んでいるデザイン
        internal static DesignData GetDesignData()
            => GetCurrent().DesignData;

        internal static MemoryStream? GetResource(string resourcePath)
            => DesignDataFileManager.GetResource(SystemConfig.Instance.DesignFileDirectory, resourcePath);

        static DesignData Load(DesignData cache)
            => DesignDataFileManager.GetDesignData(SystemConfig.Instance.DesignFileDirectory, cache);

        static DateTime? ZipFileDateTime()
            => File.Exists(ZipFilePath) ? File.GetLastWriteTime(ZipFilePath) : null;

        static string ComputeVersion()
        {
            if (!File.Exists(ZipFilePath)) return string.Empty;
            using var stream = File.OpenRead(ZipFilePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
