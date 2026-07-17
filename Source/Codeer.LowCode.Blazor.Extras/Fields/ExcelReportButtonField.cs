using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class ExcelReportButtonField(ExcelReportButtonFieldDesign design)
        : FieldBase<ExcelReportButtonFieldDesign>(design)
    {
        bool _isDownloading;

        //値を持たないフィールド
        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        /// <summary>ダウンロード実行中か (ボタンの二重実行防止)。</summary>
        internal bool IsDownloading => _isDownloading;

        /// <summary>テンプレート Excel に自モジュールの値を書き込み、xlsx / PDF でダウンロードする (スクリプトの Excel オブジェクトと同じ処理)。</summary>
        internal async Task DownloadAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return;
            if (_isDownloading) return;

            _isDownloading = true;
            NotifyStateChanged();
            try
            {
                var stream = await Services.AppInfoService.GetResourceAsync(Design.TemplateResourcePath);
                if (stream == null)
                {
                    await Services.Logger.Error($"Resource '{Design.TemplateResourcePath}' was not found.");
                    return;
                }

                var fileName = string.IsNullOrEmpty(Design.DownloadFileName)
                    ? Path.GetFileName(Design.TemplateResourcePath) : Design.DownloadFileName;

                using var excel = new ScriptObjects.Excel(stream, fileName)
                {
                    Services = Services,
                    Http = Services.Provider?.GetService<IHttpService>()
                };
                await excel.OverWrite(Module!);

                if (Design.Format == ExcelReportFormat.Pdf) await excel.DownloadPdf();
                else await excel.Download();
            }
            finally
            {
                _isDownloading = false;
                NotifyStateChanged();
            }
        }
    }
}
