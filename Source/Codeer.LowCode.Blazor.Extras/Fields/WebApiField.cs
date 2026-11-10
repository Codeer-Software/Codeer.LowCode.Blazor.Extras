using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// ボタン (またはスクリプトの Execute()) でサーバーに外部の Web API の呼び出しを頼み、結果を <see cref="WebApiResult"/> で返す。
    /// 送るのはモジュール名・フィールド名・引数の JSON だけ (接続先・メソッド・パスはサーバーがデザインから読む)。
    /// 引数なしで呼ぶときは ArgumentsField の JsonField の内容を送り、成功した応答の本文は ResultField の JsonField に入れる。
    /// 値を持たず、保存 (Submit) には何も載せない。
    /// </summary>
    public class WebApiField : FieldBase<WebApiFieldDesign>
    {
        public WebApiField(WebApiFieldDesign design) : base(design) { }

        /// <summary>呼び出しを頼んでいる間 true。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>ボタンの文言 (デザインが空なら既定)。</summary>
        internal string ButtonText => string.IsNullOrEmpty(Design.ButtonText) ? Resources.WebApi_DefaultButtonText : Services.AppInfoService.Localize(Design.ButtonText);

        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;
        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        /// <summary>
        /// 引数を付けて呼ぶ (ArgumentsField は使わない)。送る前にキー (入れ子・配列の中のオブジェクトも) の先頭 1 文字を揃える (UseUpperCamelCaseNames)。
        /// 引数の最上位の値はパスの {名前} の差し込みに使い、Get / Delete では残りのスカラー値がクエリに、Post / Put では全体がボディになる (配列の JsonObject も送れる。そのときはボディにだけ使う)。
        /// 通知は出さない (結果は戻り値で見る)。実行中・デザインモード・押せない状態 (IsViewOnly・IsEnabled が false) では呼ばずに StatusCode 0 の結果を返す。
        /// </summary>
        [ScriptName("Execute")]
        public async Task<WebApiResult> ExecuteAsync(JsonObject args)
            //キーも要素も無い JsonObject は配列 ([]) として書き出されるので、空のオブジェクトとして送る
            => await RunAsync(() => args.Count == 0 ? "{}" : ConvertNames(JsonConverterEx.SerializeObject(args, indented: false), Design.UseUpperCamelCaseNames)) ?? NotExecuted();

        /// <summary>引数なしで呼ぶ (引数は ArgumentsField の JsonField の内容。無ければ {})。</summary>
        [ScriptName("Execute")]
        public async Task<WebApiResult> ExecuteAsync()
            => await RunAsync(GetArgumentsFromField) ?? NotExecuted();

        /// <summary>
        /// ボタンを押したときの動作。引数なし (ArgumentsField の JsonField の内容。無ければ {}) で呼ぶ。
        /// 失敗 (IsSuccess でない) か ErrorMessage があればエラーの通知を出す。成功は通知しない。呼んだ後に OnCompleted のスクリプトを呼ぶ。
        /// </summary>
        internal async Task ClickAsync()
        {
            var result = await RunAsync(GetArgumentsFromField);
            if (result == null) return;
            if (!result.IsSuccess || !string.IsNullOrEmpty(result.ErrorMessage))
            {
                await Services.UIService.NotifyError(string.IsNullOrEmpty(result.ErrorMessage)
                    ? string.Format(Resources.WebApi_FailedFormat, result.StatusCode)
                    : result.ErrorMessage);
            }
            if (!string.IsNullOrEmpty(Design.OnCompleted)) await Module!.ExecuteScriptAsync(Design.OnCompleted, result);
        }

        //ArgumentsField の JsonField の今の内容 (キーはそのまま)。無い・JsonField でない・値なしなら {}
        string GetArgumentsFromField() => FindJsonField(Design.ArgumentsField)?.GetJson() ?? "{}";

        JsonField? FindJsonField(string name) => string.IsNullOrEmpty(name) ? null : Module?.GetField(name) as JsonField;

        //呼べない状態のときは null。引数は呼べるときだけ作る
        async Task<WebApiResult?> RunAsync(Func<string> getArguments)
        {
            //他のボタンと同じく、書けない (ViewOnly) か無効なら動かさない (サーバーも書き込み権限で判定する)
            if (Services.AppInfoService.IsDesignMode || IsRunning || Module == null || IsViewOnly || IsEnabled == false) return null;

            IsRunning = true;
            NotifyStateChanged();
            try
            {
                WebApiResult result;
                try
                {
                    var request = new WebApiExecuteRequest
                    {
                        ModuleName = Module.Design.Name,
                        FieldName = Design.Name,
                        Arguments = getArguments(),
                    };
                    result = ToResult(await SendAsync(request));
                }
                catch (Exception e)
                {
                    return new WebApiResult { ErrorMessage = e.Message };
                }
                await SetResultFieldAsync(result);
                return result;
            }
            finally
            {
                IsRunning = false;
                NotifyStateChanged();
            }
        }

        //共通の口で送る。ホストに届かなかったときはその理由を ErrorMessage に入れる (トーストは出さない)
        async Task<WebApiExecuteResponse> SendAsync(WebApiExecuteRequest request)
        {
            var http = Services.Provider?.GetService<IHttpService>();
            if (!ExtrasTransport.CanSend(http)) return new WebApiExecuteResponse { ErrorMessage = Resources.WebApi_NotConfigured };
            var error = string.Empty;
            var (response, delivered) = await ExtrasTransport.CallAsync<WebApiExecuteResponse>(http, WebApiNames.Feature, WebApiNames.Execute, request,
                onError: (_, text) => error = text);
            if (response != null) return response;
            return new WebApiExecuteResponse { ErrorMessage = !delivered && !string.IsNullOrEmpty(error) ? error : Resources.WebApi_NotDelivered };
        }

        //成功した応答の本文 (空・空白でなければ) をそのまま ResultField の JsonField に入れる。外側の形 (オブジェクト・配列・値だけ) が合うかは JsonField が決める。
        //入れられない (形・型が合わない・JSON として読めない) ときは JsonField は変わらず、理由を ErrorMessage に入れる
        async Task SetResultFieldAsync(WebApiResult result)
        {
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Body)) return;
            var target = FindJsonField(Design.ResultField);
            if (target == null) return;
            try
            {
                await target.SetJsonAsync(result.Body);
            }
            catch (LowCodeException e)
            {
                result.ErrorMessage = e.Message;
            }
        }

        //キーの先頭 1 文字を揃えた JSON にする (2 文字目以降・値はそのまま)。揃えた結果が同じ名前になるキーは後勝ち
        internal static string ConvertNames(string json, bool upperCamelCaseNames)
        {
            using var source = JsonDocument.Parse(json);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                Write(writer, source.RootElement, upperCamelCaseNames);
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        static void Write(Utf8JsonWriter writer, JsonElement element, bool upperCamelCaseNames)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var e in element.EnumerateObject()) properties[ConvertName(e.Name, upperCamelCaseNames)] = e.Value;
                    writer.WriteStartObject();
                    foreach (var e in properties)
                    {
                        writer.WritePropertyName(e.Key);
                        Write(writer, e.Value, upperCamelCaseNames);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var e in element.EnumerateArray()) Write(writer, e, upperCamelCaseNames);
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        static string ConvertName(string name, bool upperCamelCaseNames)
        {
            if (name.Length == 0) return name;
            var first = upperCamelCaseNames ? char.ToUpperInvariant(name[0]) : char.ToLowerInvariant(name[0]);
            return first == name[0] ? name : first + name.Substring(1);
        }

        //ボディは既存の WebApiService と同じく JsonObject にする (配列はそのまま JsonObject の並び、JSON でない・スカラーは空)
        static WebApiResult ToResult(WebApiExecuteResponse response)
        {
            var result = new WebApiResult { StatusCode = response.StatusCode, ErrorMessage = response.ErrorMessage, Body = response.Body ?? string.Empty };
            if (string.IsNullOrEmpty(response.Body)) return result;
            try
            {
                result.JsonObject = JsonConverterEx.ToJsonObject(response.Body);
            }
            catch
            {
                result.JsonObject = new();
            }
            return result;
        }

        static WebApiResult NotExecuted() => new() { ErrorMessage = Resources.WebApi_NotExecuted };
    }
}
