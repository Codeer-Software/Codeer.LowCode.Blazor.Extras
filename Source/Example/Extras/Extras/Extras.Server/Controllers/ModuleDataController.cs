using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Utils;
using Excel.Report.PDF;
using MessagePack;
using Microsoft.AspNetCore.Mvc;
using Extras.Client.Shared.Services;
using Extras.Server.Services;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Web;

namespace Extras.Server.Controllers
{
    [ApiController]
    [Route("api/module_data")]
    public class ModuleDataController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;
        //監査ログ: 分類は各アクションの [Audit]、対象 (モジュール・Id) と保存結果のエラーはここから足す。記録自体はミドルウェア
        readonly AuditContext _audit;

        public ModuleDataController(DataService dataService, AuditContext audit)
        {
            _dataService = dataService;
            _audit = audit;
        }

        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpGet("config")]
        public SystemConfigForFront GetSystemConfig()
            => SystemConfig.Instance.ForFront();

        [HttpGet("design")]
        public async Task<IActionResult> GetDesignData()
        {
            await LicenseService.UpdateAsync(Request);
            await _dataService.ModuleDataIO.CheckAppAuthorization();
            return this.FileWithETag(DesignerService.GetDesignDataForFront(await _dataService.ModuleDataIO.GetCurrentUser()), "application/octet-stream");
        }

        [HttpPost("list"), Audit(AuditCategory.DataRead)]
        public async Task<IActionResult> GetListAsync(List<GetListRequest> request)
        {
            var ret = new List<Paging<ModuleData>>();
            foreach (var e in request)
            {
                var page = await _dataService.ModuleDataIO.GetListAsync(e.Condition, e.PageIndex);
                _audit.AddRead(e.Condition.ModuleName, page);
                ret.Add(page);
            }
            return File(new MemoryStream(MessagePackSerializer.Typeless.Serialize(ret)), "application/octet-stream");
        }

        [HttpPost, Audit(AuditCategory.DataWrite)]
        public async Task<List<ModuleSubmitResult>> SubmitAsync()
        {
            //FileFieldのDB列格納モードでファイル実体(byte[])を運ぶため、listの応答と同様にMessagePackで受ける
            using var memory = new MemoryStream();
            await Request.Body.CopyToAsync(memory);
            memory.Position = 0;
            var data = MessagePackSerializer.Typeless.Deserialize(memory) as List<ModuleSubmitData>;
            //監査: 行ごとの対象 (Add / Update / Delete) と採番 Id、結果のエラーを記録する
            return await _audit.RecordSubmitAsync(data!, () => _dataService.ModuleDataIO.SubmitWithTransactionAsync(data!));
        }

        [HttpPost("list_file"), Audit(AuditCategory.Export)]
        public async Task<IActionResult> GetListFileAsync(SearchCondition? condition)
        {
            _audit.AddTarget(condition?.ModuleName ?? string.Empty, null, "Export");
            return Ok(await BulkFileTransfer.GetListFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, condition!));
        }

        [HttpPost("submit_by_file"), Audit(AuditCategory.DataWrite)]
        public async Task<List<ModuleSubmitResult>> SubmitByFileAsync(string? moduleName)
        {
            _audit.AddTarget(moduleName ?? string.Empty, null, "Import");
            var results = await BulkFileTransfer.SubmitByFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body);
            var error = results.FirstOrDefault(e => !string.IsNullOrEmpty(e.ExceptionMessage))?.ExceptionMessage;
            if (error != null) _audit.Fail(error);
            return results;
        }

        //スクリプトの一括ファイル出力 (BulkFileTransferService.Download(List<Module>)) 用。
        //クライアントで加工済みのモジュールデータ列をそのままファイル化する
        [HttpPost("list_file_by_data"), Audit(AuditCategory.Export)]
        public async Task<IActionResult> GetListFileByDataAsync(string? moduleName)
        {
            _audit.AddTarget(moduleName ?? string.Empty, null, "Export");
            return Ok(await BulkFileTransfer.GetListFileByDataAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body));
        }

        //スクリプトの一括保存 (BulkFileTransferService.Submit(List<Module>)) 用。
        //クライアントで加工済みのモジュールデータ列を一括保存する (ファイル取込と同じ追加/更新判定の経路)
        [HttpPost("bulk_submit"), Audit(AuditCategory.DataWrite)]
        public async Task<IActionResult> BulkSubmitAsync(string? moduleName)
            => Content(await BulkFileTransfer.BulkSubmitAsync(_dataService.ModuleDataIO, moduleName, Request.Body, _audit), "application/json");

        //スクリプトの一括ファイル取込 (BulkFileReader) 用。ファイルを解析してモジュールデータ列を返す (DB には書き込まない)。
        //ModuleData はポリモーフィックなので JsonConverterEx で直列化して返す
        [HttpPost("parse_file")]
        public async Task<IActionResult> ParseFileAsync(string? moduleName)
            => Content(Codeer.LowCode.Blazor.Json.JsonConverterEx.SerializeObject(
                await BulkFileTransfer.ParseFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body)),
                "application/json");

        [HttpGet("resource")]
        public IActionResult GetResourceAsync(string? resource)
        {
            var mem = DesignerService.GetResource(resource ?? string.Empty);
            return mem == null ? Ok() : this.FileWithETag(mem.ToArray(), "application/octet-stream");
        }

        [HttpGet("download"), Audit(AuditCategory.DataRead)]
        public async Task<IActionResult> DownloadFileAsync(string? moduleName, string? id, string? fieldName)
        {
            _audit.AddTarget(moduleName ?? string.Empty, id, $"Download:{fieldName}");
            var location = await _dataService.ModuleDataIO.FileFieldDataIO.GetFileLocation(moduleName!, id!, fieldName!);
            await _dataService.DbAccess.ClearAsync();
            return this.FileWithETag((await StorageAccess.ReadFileAsync(SystemConfig.Instance.FileStorages, location)).ToArray(), "application/octet-stream");
        }

        [HttpPost("upload"), Audit(AuditCategory.DataWrite)]
        public async Task<Codeer.LowCode.Blazor.DataIO.FileInfo> UploadFileAsync(string? moduleName, string? fieldName, string? fileName)
        {
            _audit.AddTarget(moduleName ?? string.Empty, null, $"Upload:{fieldName}");
            var info = _dataService.ModuleDataIO.FileFieldDataIO.GetFileSaveInfo(moduleName ?? string.Empty, fieldName ?? string.Empty);
            return await _dataService.TemporaryFileManager.AddFileAsync(info, fileName, Request.Body);
        }
    }
}
