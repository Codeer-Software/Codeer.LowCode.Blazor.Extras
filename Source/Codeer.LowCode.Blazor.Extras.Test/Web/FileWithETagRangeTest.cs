using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;

namespace Codeer.LowCode.Blazor.Extras.Test.Web
{
    /// <summary>テスト用 API (MVC が見つけられるようトップレベルに置く)。FileSystem のストレージから読みながら返す = テンプレートの download と同じ形。</summary>
    [ApiController, Route("api/file_probe")]
    public class FileProbeController : ControllerBase
    {
        public static string Directory = string.Empty;
        public static Guid FileGuid;
        public static string FileName = "movie.mp4";

        [HttpGet("download")]
        public async Task<IActionResult> Download()
        {
            var storages = new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = Directory }) };
            var location = new FileLocation { StorageName = "Local", Guid = FileGuid, FileName = FileName };
            return this.FileWithETag(await StorageAccess.OpenReadAsync(storages, location), location);
        }
    }

    /// <summary>
    /// FileWithETag(Stream, FileLocation) を TestServer で通す。ブラウザの動画再生が頼る Range (206 と Content-Range)、
    /// Accept-Ranges、Guid の ETag による 304、動画だけ Content-Type を付けること (それ以外は octet-stream) を確認する。
    /// </summary>
    public class FileWithETagRangeTest
    {
        static readonly byte[] _content = Enumerable.Range(0, 1000).Select(e => (byte)(e % 251)).ToArray();
        string _dir = string.Empty;
        WebApplication? _app;

        [SetUp]
        public async Task SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "clb_range_" + Guid.NewGuid().ToString("N"));
            FileProbeController.Directory = _dir;
            FileProbeController.FileGuid = Guid.NewGuid();
            FileProbeController.FileName = "movie.mp4";
            await StorageAccess.WriteFile(new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) },
                "Local", FileProbeController.FileGuid, new MemoryStream(_content));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddControllers().AddApplicationPart(typeof(FileProbeController).Assembly);
            _app = builder.Build();
            _app.MapControllers();
            await _app.StartAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        HttpClient Client => _app!.GetTestClient();

        [Test]
        public async Task 全体を返すときは200でAcceptRangesとETagが付く()
        {
            var response = await Client.GetAsync("/api/file_probe/download");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.AcceptRanges, Does.Contain("bytes"));
            Assert.That(response.Headers.ETag!.Tag, Is.EqualTo($"\"{FileProbeController.FileGuid:N}\""));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("video/mp4"));
            Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(_content));
        }

        [Test]
        public async Task Rangeには206で部分を返す()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/file_probe/download");
            request.Headers.Range = new RangeHeaderValue(100, 199);
            var response = await Client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(response.Content.Headers.ContentRange!.ToString(), Is.EqualTo("bytes 100-199/1000"));
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(_content.Skip(100).Take(100)));
        }

        [Test]
        public async Task 末尾までのRange()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/file_probe/download");
            request.Headers.Range = new RangeHeaderValue(990, null);
            var response = await Client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.EqualTo(_content.Skip(990)));
        }

        [Test]
        public async Task 同じETagなら304()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/file_probe/download");
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{FileProbeController.FileGuid:N}\""));
            var response = await Client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
        }

        [Test]
        public async Task 動画以外はoctetStreamのまま()
        {
            FileProbeController.FileName = "page.html";
            var response = await Client.GetAsync("/api/file_probe/download");
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/octet-stream"));
        }

        [TestCase("a.mp4", "video/mp4")]
        [TestCase("a.webm", "video/webm")]
        [TestCase("a.ogv", "video/ogg")]
        [TestCase("a.mov", "video/quicktime")]
        [TestCase("a.m4v", "video/mp4")]
        [TestCase("a.jpg", null)]
        [TestCase("a.svg", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void 動画のContentType(string? fileName, string? expected)
            => Assert.That(ControllerExtensions.GetVideoContentType(fileName), Is.EqualTo(expected));
    }
}
