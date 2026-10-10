using Codeer.LowCode.Blazor.DataIO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;
using System.Security.Cryptography;

namespace Codeer.LowCode.Blazor.Extras.Server.Web
{
    public static class ControllerExtensions
    {
        static readonly FileExtensionContentTypeProvider _contentTypes = new();

        //FileField の実体ファイルを読みながら返す (全部をメモリに置かない)。Range 要求に応じるので、<video> はシーク・途中からの再生ができ、
        //全部をダウンロードしてから再生にはならない。ETag はファイルの Guid (内容は変わらない = 再訪は 304)。
        //Content-Type は動画 (video/*) だけ元のファイル名の拡張子から決める (それでないとブラウザが再生しない)。それ以外は octet-stream のまま
        //(利用者がアップロードした HTML 等をアプリのオリジンで描画させない。nosniff で推測もさせない)。Stream は応答を書き終えたときに閉じられる
        public static IActionResult FileWithETag(this ControllerBase controller, Stream content, FileLocation location)
        {
            controller.Response.Headers.CacheControl = "no-cache";
            controller.Response.Headers.XContentTypeOptions = "nosniff";
            var contentType = GetVideoContentType(location.FileName) ?? "application/octet-stream";
            var etag = new EntityTagHeaderValue($"\"{location.Guid:N}\"");
            return controller.File(content, contentType, lastModified: null, entityTag: etag, enableRangeProcessing: true);
        }

        internal static string? GetVideoContentType(string? fileName)
            => !string.IsNullOrEmpty(fileName) && _contentTypes.TryGetContentType(fileName, out var contentType) && contentType.StartsWith("video/")
                ? contentType : null;

        //内容ハッシュのETag付きでファイルを返す。ブラウザは再訪時にIf-None-Matchで再検証し、
        //内容が変わっていなければ304でダウンロードを省略できる(FileがIf-None-Matchを自動処理する)。
        public static IActionResult FileWithETag(this ControllerBase controller, byte[] content, string contentType)
        {
            controller.Response.Headers.CacheControl = "no-cache";
            var etag = new EntityTagHeaderValue($"\"{Convert.ToHexString(SHA256.HashData(content))}\"");
            return controller.File(content, contentType, lastModified: null, entityTag: etag);
        }
    }
}
