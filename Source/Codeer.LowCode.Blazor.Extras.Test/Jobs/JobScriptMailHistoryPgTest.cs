using System.Reflection;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Jobs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// Job スクリプトの MailService.Send で送信履歴の書き込みが DB で失敗したとき (PostgreSQL)。
    /// PostgreSQL は失敗した文でトランザクション全体が中断状態になるので、履歴の失敗を Job の失敗にして取り消すことを実 DB で確かめる。
    /// 環境変数 JOB_MAIL_PG_CONNECTION (PostgreSQL の接続文字列) を設定して明示的に実行する。表はテストごとに作って消す。
    /// </summary>
    [TestFixture, NonParallelizable]
    [Explicit("実 DB (PostgreSQL) を使う。JOB_MAIL_PG_CONNECTION を設定して明示的に実行する")]
    public class JobScriptMailHistoryPgTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        DesignData _designData = null!;
        readonly List<MailMessage> _sent = new();
        string _tasks = null!;
        string _history = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        class FakeMailSender(List<MailMessage> sent) : IMailSender
        {
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                sent.Add(message);
                return Task.FromResult(MailSendResult.Success(1));
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients) => throw new NotSupportedException();
        }

        static void ClearScriptTypes()
        {
            typeof(JobExecutor).GetMethod("ClearScriptTypes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            DesignJobs.ResetForTest();
        }

        [SetUp]
        public async Task SetUp()
        {
            var connection = Environment.GetEnvironmentVariable("JOB_MAIL_PG_CONNECTION");
            if (string.IsNullOrEmpty(connection)) Assert.Ignore("JOB_MAIL_PG_CONNECTION が未設定");
            ClearScriptTypes();
            _sent.Clear();

            var suffix = Guid.NewGuid().ToString("N")[..8];
            _tasks = $"job_mail_pg_tasks_{suffix}";
            _history = $"job_mail_pg_history_{suffix}";
            DbAccessor.ClearTableDefinitionCache();
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.PostgreSQL, ConnectionString = connection }
            });
            await _db.ExecuteAsync(Ds, $"CREATE TABLE {_tasks} (id SERIAL PRIMARY KEY, title TEXT)", new());
            //履歴の INSERT を DB の制約で落とす (件名「お知らせ」は入らない)
            await _db.ExecuteAsync(Ds, $"CREATE TABLE {_history} (id SERIAL PRIMARY KEY, sent_at TIMESTAMP, mail_infra_name TEXT, subject TEXT CHECK (subject <> 'お知らせ'), total_count NUMERIC, success_count NUMERIC, failure_details TEXT, source_module TEXT, source_id TEXT)", new());
            _designData = CreateDesignData();
        }

        [TearDown]
        public async Task TearDown()
        {
            if (_db == null) return;
            ClearScriptTypes();
            await _db.ExecuteAsync(Ds, $"DROP TABLE IF EXISTS {_tasks}", new());
            await _db.ExecuteAsync(Ds, $"DROP TABLE IF EXISTS {_history}", new());
            _db.Dispose();
        }

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            var task = new ModuleDesign { Name = "TaskItem", DataSourceName = Ds, DbTable = _tasks };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            d.AddModule(task);

            var history = new ModuleDesign { Name = "MailHistory", DataSourceName = Ds, DbTable = _history };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" });
            history.Fields.Add(new TextFieldDesign { Name = "MailInfraName", DbColumn = "mail_infra_name" });
            history.Fields.Add(new TextFieldDesign { Name = "Subject", DbColumn = "subject" });
            history.Fields.Add(new NumberFieldDesign { Name = "TotalCount", DbColumn = "total_count" });
            history.Fields.Add(new NumberFieldDesign { Name = "SuccessCount", DbColumn = "success_count" });
            history.Fields.Add(new TextFieldDesign { Name = "FailureDetails", DbColumn = "failure_details" });
            history.Fields.Add(new TextFieldDesign { Name = "SourceModule", DbColumn = "source_module" });
            history.Fields.Add(new TextFieldDesign { Name = "SourceId", DbColumn = "source_id" });
            d.AddModule(history);
            return d;
        }

        [Test]
        public async Task 送信履歴を書けなければJobは失敗し確定前の書き込みは消える_メールは送られている()
        {
            DesignJobs.UseMail(new MailConfig { DefaultInfraName = "Main", HistoryModuleName = "MailHistory" },
                name => name == "Main" ? new FakeMailSender(_sent) : null);
            _designData.JobScripts["TestJob"] = """
                bool AddTask(string title)
                {
                    var submit = new ModuleSubmitter();
                    var data = new TaskItem.Data();
                    data.Title = new TextFieldData();
                    data.Title.Value = title;
                    submit.Add(data);
                    var saved = submit.Execute();
                    if (!saved.Succeeded) Logger.Log("書き込み失敗: " + saved.Error);
                    return saved.Succeeded;
                }

                string Run(JobArguments args)
                {
                    AddTask("確定済");
                    DBService.Commit();
                    AddTask("未確定");
                    var m = new MailMessage();
                    m.To.Add("a@example.com");
                    m.Subject = "お知らせ";
                    MailService.Send(m);
                    var after = AddTask("送信後");
                    return after ? "送信後の書き込み成功" : "送信後の書き込み失敗";
                }
                """;

            string? message = null;
            Exception? error = null;
            try
            {
                message = await DesignJobs.RunAsync(new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>())),
                    _designData, "TestJob", []);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            var titles = (await _db.QueryAsync(Ds, $"SELECT title FROM {_tasks} ORDER BY id", new())).Select(e => (string)e["title"]).ToList();
            var historyCount = (await _db.QueryAsync(Ds, $"SELECT id FROM {_history}", new())).Count;
            TestContext.Out.WriteLine($"Job: {(error == null ? "成功 " + message : "失敗 " + error.GetType().Name + ": " + error.Message)}");
            TestContext.Out.WriteLine($"送信: {_sent.Count} 通 / tasks: [{string.Join(", ", titles)}] / 履歴: {historyCount} 行");

            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(error!.Message, Does.Contain("メールは送信しましたが、送信履歴を書けませんでした"));
            Assert.That(_sent, Has.Count.EqualTo(1));
            Assert.That(titles, Is.EqualTo(new[] { "確定済" }));
            Assert.That(historyCount, Is.EqualTo(0));
        }
    }
}
