using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Transfer;
using Codeer.LowCode.Blazor.Repository.Data;
using System.Security.Cryptography;

namespace Codeer.LowCode.Blazor.Extras.Server.Design
{
    /// <summary>
    /// 読み込んだデザイン 1 版分。Version は App.zip の SHA-256 (監査ログに残る版。App.zip が無ければ空)。
    /// </summary>
    public sealed class LoadedDesign
    {
        public DesignData DesignData { get; }
        public TransferDesignData TransferData { get; }
        public string Version { get; }

        internal LoadedDesign(DesignData designData, TransferDesignData transferData, string version)
        {
            DesignData = designData;
            TransferData = transferData;
            Version = version;
        }

        /// <summary>フロント (WASM) へ渡す形。ページフレームはユーザーごとに解決する (pageLinkUrlResolver はホストの NavigationService の URL 規則)。</summary>
        public byte[] ForFront(IPageLinkUrlResolver pageLinkUrlResolver, ModuleData? currentUser)
            => TransferData.AddResolvedPageFrames(DesignData.ResolvePageFrames(pageLinkUrlResolver, currentUser)).ToBinary();

        /// <summary>同じプロセスで動かすホスト (WPF / WinForms) へ渡す形。ページフレームをユーザーごとに解決した複製 (元は変えない)。</summary>
        public DesignData ForDesktop(IPageLinkUrlResolver pageLinkUrlResolver, ModuleData? currentUser)
        {
            var data = DesignData.Clone();
            data.PageFrames = data.ResolvePageFrames(pageLinkUrlResolver, currentUser).ToPageFrameDesigns();
            return data;
        }
    }

    /// <summary>
    /// 配置されたデザイン (DesignFileDirectory の App.zip) を読み込んで持つ。プロセスに 1 つ作る (ホストが static に持つ)。
    /// App.zip が無ければ空のデザインを持つ。
    /// App.zip が差し替わっていれば次の参照で読み直す (差し替え中に読んだときは更新日時が合うまで読み直す)。読み直したら onReloaded を呼ぶ (ホストはここで DbAccessor.ClearTableDefinitionCache を渡し、表定義のキャッシュを捨てる)。
    /// リクエストの中では版を途中で変えないよう、最初に <see cref="GetCurrent"/> で取った 1 版を最後まで使う。
    /// </summary>
    public class DesignDataStore
    {
        readonly object _sync = new();
        readonly Action? _onReloaded;
        LoadedDesign _current = new(new(), new(), string.Empty);

        public string DesignFileDirectory { get; }

        /// <param name="designFileDirectory">App.zip を置くフォルダ (appsettings の DesignFileDirectory)。</param>
        /// <param name="onReloaded">デザインを読み直したときに呼ぶ (表定義のキャッシュを捨てる等)。</param>
        public DesignDataStore(string designFileDirectory, Action? onReloaded = null)
        {
            DesignFileDirectory = designFileDirectory;
            _onReloaded = onReloaded;
        }

        string ZipFilePath => Path.Combine(DesignFileDirectory, "App.zip");

        /// <summary>今読み込んでいるデザイン (差し替わっていれば読み直してから)。</summary>
        public LoadedDesign GetCurrent()
        {
            lock (_sync)
            {
                //App.zip が無ければ空のデザイン (無いまま繰り返し取っても読み直さない。消えたときだけ空に置き換えて onReloaded を 1 回呼ぶ)
                if (!File.Exists(ZipFilePath))
                {
                    if (_current.Version.Length != 0)
                    {
                        _onReloaded?.Invoke();
                        _current = new(new(), new(), string.Empty);
                    }
                    return _current;
                }
                var designData = Load(_current.DesignData);
                if (ReferenceEquals(_current.DesignData, designData)) return _current;
                var version = ComputeVersion();
                for (var retry = 0; retry < 3 && ZipFileDateTime() != designData.SourceFileDateTime; retry++)
                {
                    designData = Load(designData);
                    version = ComputeVersion();
                }
                _onReloaded?.Invoke();
                _current = new(designData, designData.CreateTransferDesignData(), version);
                return _current;
            }
        }

        /// <summary>今読み込んでいるデザインのデータ。</summary>
        public DesignData GetDesignData()
            => GetCurrent().DesignData;

        /// <summary>デザインのリソース (画像等)。無ければ null。</summary>
        public MemoryStream? GetResource(string resourcePath)
            => DesignDataFileManager.GetResource(DesignFileDirectory, resourcePath);

        DesignData Load(DesignData cache)
            => DesignDataFileManager.GetDesignData(DesignFileDirectory, cache);

        DateTime? ZipFileDateTime()
            => File.Exists(ZipFilePath) ? File.GetLastWriteTime(ZipFilePath) : null;

        string ComputeVersion()
        {
            if (!File.Exists(ZipFilePath)) return string.Empty;
            using var stream = File.OpenRead(ZipFilePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }
}
