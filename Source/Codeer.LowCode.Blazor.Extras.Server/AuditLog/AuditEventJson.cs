using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    //監査レコードの JSON 1 行。ファイル出力 (FileAuditSink) と、書けなかったときのアプリのログ (AuditLogger の Critical) が同じ形で残す
    static class AuditEventJson
    {
        static readonly JsonSerializerOptions Options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Serialize(AuditEvent e) => JsonSerializer.Serialize(e, Options);
    }
}
