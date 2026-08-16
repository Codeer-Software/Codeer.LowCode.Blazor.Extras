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
    /// Field names are resolved through the MailHistoryContractField on the history module
    /// (roles left empty are not recorded); a module without the contract uses the default role names.
    /// Writing goes through an internal add delegate so it does not depend on the operating user's
    /// write permission (history is the system's record). A broken history configuration is reported
    /// via logError and never fails the send itself.
    /// </summary>
    public class MailHistoryWriter
    {
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

                //フィールド名は履歴モジュール上の契約で解決する (契約が無ければ既定名。空の役割 = 記録しない)
                var names = MailContracts.History(design) ?? new Designs.MailHistoryContractFieldDesign();

                var data = new ModuleData { Name = _historyModuleName };
                void Set(string fieldName, Action<FieldDataBase> setValue)
                {
                    if (string.IsNullOrEmpty(fieldName)) return;
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

                Set(names.SentAt, e => ((DateTimeFieldData)e).Value = DateTime.Now);
                Set(names.SenderName, e => ((TextFieldData)e).Value = senderName);
                Set(names.Subject, e => ((TextFieldData)e).Value = subject);
                Set(names.TotalCount, e => ((NumberFieldData)e).Value = result.TotalCount);
                Set(names.SuccessCount, e => ((NumberFieldData)e).Value = result.SuccessCount);
                Set(names.FailureDetails, e =>
                {
                    var json = JsonSerializer.Serialize(result.Failures);
                    if (e is JsonFieldData jsonData) jsonData.Value = json;
                    else ((TextFieldData)e).Value = json;
                });
                if (source != null)
                {
                    Set(names.SourceModule, e => ((TextFieldData)e).Value = source.SourceModule);
                    Set(names.SourceId, e => ((TextFieldData)e).Value = source.SourceId);
                }

                if (!data.Fields.Any())
                {
                    _logError?.Invoke($"Mail history module '{_historyModuleName}' has none of the contract role fields.");
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
