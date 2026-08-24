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
    /// Mail.HistoryModuleName のモジュールへ、送信操作1回につき1行の履歴を書く。
    /// フィールド名は履歴モジュール上の MailHistoryContractField 経由で解決する
    /// (空の役割 = 記録しない。契約フィールドが無いモジュールは既定の役割名を使う)。
    /// 書き込みは内部 add デリゲート経由で、操作ユーザーの書き込み権限に依存しない (履歴はシステムの記録)。
    /// </summary>
    /// <remarks>
    /// **履歴を取る設定 (Mail.HistoryModuleName) なのにモジュールが契約を満たしていない場合は
    /// 送信前の <see cref="Validate"/> で例外にする = メールを送らない** (静かに記録が欠けるのを防ぐ。
    /// 履歴モジュールは appsettings 指定なのでデザインチェックからは辿れず、実行時に検出するしかない)。
    /// 一方、書き込み時の障害 (DB エラー等) は送信後なので logError のみで送信は失敗させない。
    /// 記録したくない項目は契約フィールドを置いてその役割を空にする。
    /// </remarks>
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

        /// <summary>
        /// 履歴モジュールが契約を満たしているかを検証する (送信前に呼ぶ)。
        /// モジュール不在・必須役割の不備・契約が名指ししたフィールドの不在は例外 = 送信させない。
        /// </summary>
        /// <remarks>
        /// **必須以外の役割は空にできる** (= その項目は記録しない)。契約フィールドを置いていない場合は
        /// 既定の役割名で書くが、既定名のフィールドが無い項目は「記録しない」として扱う
        /// (必須役割だけは無いとエラー)。
        /// </remarks>
        public void Validate()
        {
            var design = _designData.Modules.Find(_historyModuleName)
                ?? throw new InvalidOperationException(
                    $"Mail history module '{_historyModuleName}' (Mail.HistoryModuleName) does not exist.");

            var contract = MailContracts.History(design);
            var names = contract ?? new Designs.MailHistoryContractFieldDesign();
            var problems = new List<string>();
            foreach (var (role, fieldName) in GetRoles(names))
            {
                var required = names.IsRoleRequired(role);
                if (string.IsNullOrEmpty(fieldName))
                {
                    if (required) problems.Add($"{role} (required) is empty");
                    continue;
                }
                if (design.Fields.Any(f => f.Name == fieldName)) continue;
                //契約が名指ししたフィールドの不在は設定ミス。既定名フォールバックは記録しないだけ (必須は除く)
                if (required || contract != null) problems.Add($"{role} -> '{fieldName}' does not exist");
            }
            if (problems.Count == 0) return;

            throw new InvalidOperationException(
                $"Mail history module '{_historyModuleName}' does not implement the mail history contract: " +
                string.Join(", ", problems) +
                ". Add the fields, or put a MailHistoryContractField on the module and leave the roles you do not record empty.");
        }

        //役割名 → 記録先フィールド名 (契約フィールドが無い場合は既定名)
        static IEnumerable<(string Role, string FieldName)> GetRoles(Designs.MailHistoryContractFieldDesign names)
        {
            yield return (nameof(names.SentAt), names.SentAt);
            yield return (nameof(names.MailInfraName), names.MailInfraName);
            yield return (nameof(names.Subject), names.Subject);
            yield return (nameof(names.TotalCount), names.TotalCount);
            yield return (nameof(names.SuccessCount), names.SuccessCount);
            yield return (nameof(names.FailureDetails), names.FailureDetails);
            yield return (nameof(names.SourceModule), names.SourceModule);
            yield return (nameof(names.SourceId), names.SourceId);
        }

        public async Task WriteAsync(string mailInfraName, string subject, MailSendResult result, MailHistorySource? source)
        {
            try
            {
                //構成の妥当性は送信前の Validate で確認済み (ここに来る時点でモジュールと役割は揃っている)
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
                Set(names.MailInfraName, e => ((TextFieldData)e).Value = mailInfraName);
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
