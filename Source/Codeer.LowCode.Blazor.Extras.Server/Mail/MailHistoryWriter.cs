using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>Origin record of a send operation (for the history's SourceModule/SourceId).</summary>
    public class MailHistorySource
    {
        public string SourceModule { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Writes one history record per send operation into the module named by Mail.HistoryModuleName.
    /// Reserved field names are mapped by spelling; only the fields that exist in the module are written.
    /// Writing goes through an internal add delegate so it does not depend on the operating user's
    /// write permission (history is the system's record). A broken history configuration is reported
    /// via logError and never fails the send itself.
    /// </summary>
    public class MailHistoryWriter
    {
        public const string SentAtField = "SentAt";
        public const string SenderNameField = "SenderName";
        public const string SubjectField = "Subject";
        public const string TotalCountField = "TotalCount";
        public const string SuccessCountField = "SuccessCount";
        public const string FailureDetailsField = "FailureDetails";
        public const string SourceModuleField = "SourceModule";
        public const string SourceIdField = "SourceId";

        readonly string _historyModuleName;
        readonly DesignData _designData;
        readonly Func<ModuleData, Task> _addInternalAsync;
        readonly Action<string>? _logError;

        public MailHistoryWriter(string historyModuleName, DesignData designData,
            Func<ModuleData, Task> addInternalAsync, Action<string>? logError = null)
        {
            _historyModuleName = historyModuleName;
            _designData = designData;
            _addInternalAsync = addInternalAsync;
            _logError = logError;
        }

        public async Task WriteAsync(string senderName, string subject, MailSendResult result, MailHistorySource? source)
        {
            try
            {
                var design = _designData.Modules.Find(_historyModuleName);
                if (design == null)
                {
                    _logError?.Invoke($"Mail history module '{_historyModuleName}' does not exist.");
                    return;
                }

                var data = new ModuleData { Name = _historyModuleName };
                void Set(string fieldName, Action<FieldDataBase> setValue)
                {
                    var fieldDesign = design.Fields.FirstOrDefault(e => e.Name == fieldName);
                    var fieldData = fieldDesign?.CreateData();
                    if (fieldData == null) return;
                    try
                    {
                        setValue(fieldData);
                        data.Fields[fieldName] = fieldData;
                    }
                    catch
                    {
                        _logError?.Invoke($"Mail history field '{fieldName}' of '{_historyModuleName}' has an unexpected type ({fieldData.GetType().Name}).");
                    }
                }

                Set(SentAtField, e => ((DateTimeFieldData)e).Value = DateTime.Now);
                Set(SenderNameField, e => ((TextFieldData)e).Value = senderName);
                Set(SubjectField, e => ((TextFieldData)e).Value = subject);
                Set(TotalCountField, e => ((NumberFieldData)e).Value = result.TotalCount);
                Set(SuccessCountField, e => ((NumberFieldData)e).Value = result.SuccessCount);
                Set(FailureDetailsField, e =>
                {
                    var json = JsonSerializer.Serialize(result.Failures);
                    if (e is JsonFieldData jsonData) jsonData.Value = json;
                    else ((TextFieldData)e).Value = json;
                });
                if (source != null)
                {
                    Set(SourceModuleField, e => ((TextFieldData)e).Value = source.SourceModule);
                    Set(SourceIdField, e => ((TextFieldData)e).Value = source.SourceId);
                }

                if (!data.Fields.Any())
                {
                    _logError?.Invoke($"Mail history module '{_historyModuleName}' has none of the reserved fields.");
                    return;
                }
                await _addInternalAsync(data);
            }
            catch (Exception ex)
            {
                //履歴は送信の従属機能。履歴の失敗で送信を失敗にしない
                _logError?.Invoke($"Failed to write mail history: {ex.Message}");
            }
        }
    }
}
