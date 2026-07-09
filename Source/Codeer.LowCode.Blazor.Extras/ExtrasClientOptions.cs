namespace Codeer.LowCode.Blazor.Extras
{
    /// <summary>
    /// App-side settings for the built-in services and script objects.
    /// Endpoint URLs belong to the app (which owns the controllers), so the app defines them
    /// in one place and passes this object to DI and to ExtrasClientInitializer.Initialize.
    /// DI-constructed services receive it via constructor; script-created objects receive it via [ScriptInject].
    /// Leave an endpoint empty when the feature is not used or is handled by a host-side hook
    /// (e.g. Excel.ConvertPdf / MailService.SendMailAsyncCore / AITextAnalyzerField.FileToModuleDataCoreAsync on desktop).
    /// </summary>
    public class ExtrasClientOptions
    {
        public string MailEndPoint { get; set; } = string.Empty;
        public string ExcelPdfConvertEndPoint { get; set; } = string.Empty;
        public string AITextAnalyzeFileEndPoint { get; set; } = string.Empty;
        public string AITextAnalyzeTextEndPoint { get; set; } = string.Empty;
    }
}
