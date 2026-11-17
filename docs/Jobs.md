# ジョブ (バッチ)

ホストのアプリが持つジョブ (C# の処理) を、同じアプリの別プロセスで動かす部品です。画面のボタン ([JobTriggerField](JobTriggerField.md)) から起動するときも、
コマンドライン (`dotnet LowCodeApp.Server.dll job <ジョブ名>`) から起動するときも、同じ経路を通ります。

- 親側 `JobProcess`: このプロセス自身を `job <ジョブ名> --stdin` で起動し、入力を標準入力で渡して結果 (`JobResult`) を受け取る。待たずに起動したジョブは Id で状態を問い合わせられる
- 子側 `JobMode`: `job` より後ろの引数を受けてジョブを 1 つ実行し、終了コードを返す
- 親と子の約束 (引数の並び・標準入力・環境変数・終了コード・UTF-8・結果の文言の取り出し) はこの 2 つの中に閉じています。ホストはジョブの一覧と DB アクセスの組み立てだけを書きます
- 画面のボタンから起動するジョブは、どこで動かすかをホストが実行の口で決められます。別プロセス (上の 2 つ。既定) か、Web サーバーのプロセスの中 (「実行先」)

Codeer.LowCode.Blazor.Extras.Server 0.23.0 以降。名前空間 `Codeer.LowCode.Blazor.Extras.Server.Jobs`。
ジョブの書き方・起動の仕方 (タスクスケジューラ・Azure WebJobs) はアプリのテンプレート (Codeer.LowCode.Blazor.Starter) の `Server/Jobs/README.md` にあります。

---

## ホストの結線

テンプレート (Cookie) には入っています。ここは何がどこにあるかの説明です。

### 子側: Program.cs

```csharp
var isJob = args is ["job", ..];
if (isJob) Console.OutputEncoding = Encoding.UTF8;           // JobMode より前の出力 (設定の読み込みの失敗など) も UTF-8 で
var builder = isJob
    ? WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory })
    : WebApplication.CreateBuilder(args);
//...設定の読み込み (Web と同じ)...
if (isJob) return await JobMode.RunAsync(args[1..], JobRunner.Names, (name, jobArgs) => JobRunner.RunAsync(builder.Configuration, name, jobArgs));
```

`JobMode.RunAsync(args, jobNames, runJob)`:

| 引数 | 内容 |
|---|---|
| `args` | `job` より後ろの引数 (ジョブ名・ジョブの引数) |
| `jobNames` | ホストが持つジョブの名前 (大文字小文字は区別しない) |
| `runJob` | ジョブを動かす (登録した名前・ジョブの引数 → 成功の文言。失敗は例外)。ホストはここで DB アクセスとジョブを組み立てる (テンプレートの `Jobs/JobRunner.cs`) |

戻り値はプロセスの終了コード (`JobMode.Success` = 0 / `Failure` = 1 / `JobNotFound` = 2)。

### 親側: JobApi と Program.cs

画面のボタン (JobTriggerField) の受け口は Extras.Server の `JobApi` です。ホストは共通の API の対応表 (テンプレートの `Extras/ExtrasApiTable.cs`) に `new JobApi(jobNames)` (動かせるジョブの名前。無い名前は起動しない) を並べます。この形は全部のジョブを別プロセスで動かし、渡した一覧をそのまま持ちます。動かしている間に増減するジョブ (「デザインの Job スクリプト」) があるときや、ジョブによって動かす場所を変えるときは、一覧を返す関数と実行の口を渡す形 (「実行先」) を使います。
`JobApi` はフィールドへの書き込み権限を確かめてから、デザインの JobTriggerField に従って実行の口を動かします。待つかどうか・同時実行の判定・非同期の状態・失敗の記録は `JobApi` が実行の口を包んで行うので、どこで動かしても同じです。

```csharp
//Program.cs (失敗の記録先と、非同期のジョブの状態の置き場)
builder.Services.AddDistributedMemoryCache();
...
JobProcess.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
JobProcess.Cache = app.Services.GetRequiredService<IDistributedCache>();
```

`JobApi` を使わずに自分の Controller で受けるときは、公開の `JobProcess.RunAsync` / `StartAsync` (別プロセス。同時実行の判定・状態・失敗の記録つき) を呼びます。

```csharp
//自分の Controller で受けるとき
if (design.IsAsynchronous)
{
    var jobId = await JobProcess.StartAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);   // 起動したら Id を返す
    if (jobId == null) return Failure($"ジョブ '{design.JobName}' は実行中です");                               // 同時実行を許さないジョブが実行中
    return new JobRunResponse { Started = true, JobId = jobId };
}
var result = await JobProcess.RunAsync(design.JobName, request.Arguments, userId, design.AllowsConcurrentRuns);   // 終わるまで待つ
if (result == null) return Failure($"ジョブ '{design.JobName}' は実行中です");
return result.Succeeded ? new JobRunResponse { Succeeded = true, Message = result.Message } : Failure(result.ErrorMessage);

//HTTP としては成功 (200) で返す業務上の失敗。監査ログ (AuditLog) にも失敗として残す
JobRunResponse Failure(string error)
{
    audit?.Fail(error);
    return new JobRunResponse { Succeeded = false, Error = error };
}

//非同期で起動したジョブの状態 (JobTriggerField の GetJobStatus が問い合わせる)。起動したユーザーにだけ返す (null = 応答なし)
var status = await JobProcess.GetStatusAsync(jobId, userId);
```

`JobProcess.RunAsync(jobName, input, userId, allowConcurrent = true)` は終わるまで待って `JobResult` を返します。`allowConcurrent` が `false` で同じ名前のジョブが実行中なら、起動せずに null を返します。

`JobResult`:

| メンバー | 内容 |
|---|---|
| `Succeeded` | 終了コードが 0 |
| `Message` | 成功したときの文言 = 標準出力の最後の行 (ジョブの戻り値。null なら空) |
| `ErrorMessage` | 失敗したときの文言 = 標準エラーの最初の空でない行 (例外のメッセージ)。無ければ「ジョブ X が失敗しました (終了コード N)」 |
| `ExitCode` / `Output` / `Error` | 終了コード・標準出力の全文・標準エラーの全文 |

### 非同期の結果

| メンバー | 内容 |
|---|---|
| `JobProcess.StartAsync(jobName, input, userId, allowConcurrent = true)` | 起動したら待たずにジョブの Id (文字列) を返す。`allowConcurrent` が `false` で同じ名前のジョブが実行中なら、起動せずに null。起動できなければ例外 |
| `JobProcess.GetStatusAsync(jobId, userId)` | `JobStatusResponse` (`Status` = `JobStatus` の `Running` / `Done` / `Error`、成功の文言 `Message` = `JobResult.Message`、失敗の文言 `Error` = `JobResult.ErrorMessage`)。知らない Id・別のユーザーが起動したジョブ・`Cache` が未設定なら null |
| `JobProcess.Cache` | 状態の置き場 (`IDistributedCache`)。ホストが起動時に設定する。未設定なら `StartAsync` は Id を返すが状態を残さない |
| `JobProcess.FinishedRetention` | 終わった結果を持っておく時間 (終了からの経過。既定 30 分)。過ぎたら `GetStatusAsync` は null |

状態は `JobProcess.Cache` (`IDistributedCache`) に置きます。テンプレートはメモリキャッシュなので、サーバーを再起動すると消えます (JobTriggerField の `GetJobStatus` は null を返します)。サーバーを複数インスタンスで動かすときは、Redis などの共有キャッシュに差し替えます。実行中にサーバーが落ちたジョブは `running` のまま残ります。

### 実行先

```csharp
new JobApi(jobNames, run)
```

| 引数 | 内容 |
|---|---|
| `jobNames` | `Func<IEnumerable<string>>`。動かせるジョブの名前を返す (`new JobApi(jobNames)` と同じ名前)。このプロセスの中で動かすジョブもここに含める (無い名前は起動しない)。デザインの差し替えで変わるジョブ (「デザイナで作った Job」) があるので、起動のたびに呼んで今の一覧を聞く |
| `run` | `Func<string, string, string, Task<string?>>`。実行の口 (ジョブ名 (デザインの `JobName`)・画面の内容の JSON・起動したユーザーの Id → 成功の文言。失敗は例外)。どこで動かすかは、この中でホストが決める |

`new JobApi(jobNames)` は `new JobApi(() => jobNames, JobProcess.RunInNewProcessAsync)` と同じです (全部のジョブが別プロセス)。

`JobProcess.RunInNewProcessAsync(jobName, input, userId)` は、別プロセスで動かす実行の口です。ジョブを `job <ジョブ名> --stdin` で起動して終わるまで待ち、成功なら文言 (`JobResult.Message`。ジョブが null を返したら空) を返し、失敗なら例外 (メッセージ = `JobResult.ErrorMessage`) を投げます。起動できなければ起動の例外です。実行中として数えず、失敗も記録しません (どちらも `JobApi` が行う)。
ジョブによって動かす場所を変えるときは、実行の口の中で別プロセスにするジョブだけこれを呼びます。

```csharp
new JobApi(() => jobNames, (name, input, userId) => runsHere(name)
    ? RunHereAsync(name, input, userId)                     // このプロセスの中 (ホストが DB アクセスを組み立てて動かす)
    : JobProcess.RunInNewProcessAsync(name, input, userId)); // 別プロセス
```

実行の口の約束:

- 入力は画面の内容の JSON (`JobRunRequest.Arguments`。別プロセスでは標準入力で渡り、ジョブの引数の `--stdin` がこれに置き換わる)。ユーザーは押したユーザーの Id
- 要求とは切り離して呼ばれ、非同期では要求が終わった後も動く。このプロセスの中で動かすなら、ホストは要求のもの (DB 接続・`ModuleDataIO`) を使わず、受け取ったユーザーの Id で DB アクセスを組み立てる。`JobApi` は呼ぶ前に要求の DB 接続をプールへ返す
- 成功の文言は戻り値 (改行は空白に。null なら空)。失敗の文言は例外のメッセージの最初の空でない行 (`RunInNewProcessAsync` の失敗は `JobResult.ErrorMessage`)
- 待つかどうか・同時実行の判定・非同期の状態 (`JobProcess.Cache` / `GetStatusAsync`)・失敗の記録 (`JobProcess.LoggerFactory` に Error) は `JobApi` が実行の口を包んで行う。記録の中身は例外の全文 (`RunInNewProcessAsync` の失敗は標準エラーと標準出力の全文 = `JobResult` の記録用の文言)
- 実行の口が `Task` を返す前に投げた例外もジョブの失敗になる (API の呼び出しの例外にはならない)
- 要求の実行コンテキスト (監査ログの `AuditContext.Current` などの AsyncLocal) は引き継がずに呼ぶ (同期・非同期とも)。引き継ぐのはカルチャ (`CurrentCulture` / `CurrentUICulture`) だけ
- 非同期はスレッドプールで呼ぶ。このプロセスの中で動かすジョブは、このプロセスが止まれば (再起動・デプロイ) 止まる
- このプロセスの中で動かすジョブは標準出力を使わない。ジョブの中のログはホストが自分のログに出す

別プロセスの起動自体の失敗 (実行ファイルが無い等) も、`JobApi` からはジョブの失敗です。同期では画面のエラー、非同期では起動済み (Id が返る) の後に状態が `Error` になります (どちらも失敗としてサーバーのログに記録される。非同期では監査ログの 1 件は受け付けた時点の成功のまま)。0.33.0 までの `JobApi` では、同期・非同期とも API の呼び出しが例外になっていました。

### 同時実行

実行中のジョブはジョブ名 (大文字小文字は区別しない) で数えています。`JobApi` の同期・非同期と、公開の `RunAsync` / `StartAsync` のどれもが同じ数を使い、実行の口がどこで動かしても (別プロセス・このプロセスの中) 同じジョブ名として数えます。
`allowConcurrent` が `false` のとき、同じ名前のジョブが実行中 (誰が起動したものでも) なら起動しません。JobApi はデザインの JobTriggerField の `AllowsConcurrentRuns` を渡します。

### デザイナで作った Job

デザイナで作った Job (`Jobs/名前.job.cs`) は本体の `JobExecutor` が動かします (名前の一覧は `JobExecutor.GetJobNames`、実行は `JobExecutor.RunAsync(context, jobName, args, logger)`)。ホストは `JobExecutor.RunAsync` を呼び、結果 (`JobExecutionResult`) を自分のジョブの約束 (戻り値 = 成功の文言・失敗は例外) に変えて、`JobMode.RunAsync` の `runJob`・`JobApi` の実行の口 `run` から動かします。スクリプトの `Logger` の出し先もホストが `logger` で渡します。

### Job スクリプトで使えるもの (MailService / Excel / Resources / FileService)

Job スクリプトで使える機能は、各機能の登録口で足します。プロセスの起動時に 1 回呼びます (何度呼んでも登録は 1 回)。中では本体の `JobExecutor.AddScriptTypes` に登録します。

| 登録口 | 内容 |
|---|---|
| `MailApi.UseInJobScripts(mail, createSender)` | Job スクリプトに `MailService` (メールの送信) を足す。設定は最後に渡したもの。引数は `MailApi` と同じ (`MailConfig` と、呼び名 → 送信インフラの対応表)。呼ばなければ Job スクリプトに `MailService` は無い |
| `ExcelApi.UseInJobScripts()` | Job スクリプトに `Excel` を足す。`Excel.ConvertPdf` が未設定ならサーバーの変換 (Excel.Report.PDF) を入れる (PDF のフォントはホストが `GlobalFontSettings.FontResolver` で設定する)。呼ばなければ Job スクリプトに `Excel` は無い |

本体の Job スクリプトで使えるもの (検索・保存・`DBService.Commit()` 等) に加えて、上の登録口を呼んだホストでは次が使えます。デザイナ (Extras.Designer) は同じものを設計チェック・入力補完に出します。

| 名前 | 内容 |
|---|---|
| `MailService.Send(message)` | メールを送る (`MailApi.UseInJobScripts`)。戻り値は `MailSendResult` (`IsSuccess` / `TotalCount` / `SuccessCount` / `Failures` (宛先 `To` と理由 `Error`))。送れなくても例外にならない |
| `MailMessage` / `MailAttachment` | 送るメッセージ (`To` / `Cc` / `Bcc` / `Subject` / `Body` / `IsBodyHtml` / `ReplyTo` / `Attachments`) と添付 (`FileName` / `ContentBase64`) |
| `Excel` | 画面のスクリプトと同じ Excel オブジェクト (`ExcelApi.UseInJobScripts`)。Job では `OverWrite(ModuleData)` / `ToAttachment(fileName)` / `ToPdfAttachment(fileName)` / `ToMemoryStream()` / `ToPdfMemoryStream()` を使う |
| `Resources.GetMemoryStream(path)` / `GetText(path)` | デザインのリソース (`JobExecutionContext.GetResource` で読む) |
| `FileService.Save(moduleName, fieldName, fileName, content)` / `GetMemoryStream(moduleName, fieldName, id)` | レコードの添付ファイル (FileField の項目) の読み書き (`JobExecutionContext.FileStorage` の置き場所)。`Save` の戻り値をレコードの添付の項目に入れて保存すると行に入る |

メールの送り方は `MailDispatcher` のシステムメールと同じです。送信インフラは `MailConfig.DefaultInfraName`、`DebugRedirectAllTo` が効き、`HistoryModuleName` があれば送信履歴を書きます。差出人は送信インフラ設定のシステム送信者です (`From` を入れても使われない)。

- **送ったメールは取り消せません**。Job が後で失敗する (`Fail`・実行時エラー) と、Job のトランザクションの書き込みは取り消されますが、送ったメールは戻りません。送信履歴も Job のトランザクションで書くので、一緒に取り消されます
- そのため、データを書く Job では **書き込みを `DBService.Commit()` で確定してから送ります**。送った後にも `DBService.Commit()` を呼ぶと、その後で失敗しても送信履歴が残ります
- **送信履歴を書けなかったら Job は失敗します** (「メールは送信しましたが、送信履歴を書けませんでした: 理由」)。最後の `DBService.Commit()` 以降の書き込みは取り消され、メールは送られています
- `Excel` の `Download()` / `DownloadPdf()` は Job では使えません (ダウンロードするブラウザが無い)。ファイルはメールの添付にするか、`FileService.Save` でレコードの添付ファイルにします

期限が近いレコードを検索して、担当者ごとにメールを送る:

```csharp
// Jobs/RemindTasks.job.cs
string Run(JobArguments args)
{
    var search = new ModuleSearcher<TaskItem>();
    search.AddEquals(e => e.Status.Value, "Open");
    search.AddLessThanOrEqual(e => e.DueDate.Value, DateOnly.FromDateTime(DateTime.Today.AddDays(3)));
    var rows = search.ExecuteRaw();
    var failed = 0;
    foreach (var row in rows)
    {
        var mail = new MailMessage();
        mail.To.Add(row.AssigneeEmail.Value);
        mail.Subject = "期限が近いタスク: " + row.Title.Value;
        mail.Body = row.Title.Value + " の期限は " + row.DueDate.Value + " です。(" + CurrentUser.Name.Value + ")";
        var result = MailService.Send(mail);
        if (!result.IsSuccess)
        {
            failed++;
            Logger.Error(result.Failures[0].To + ": " + result.Failures[0].Error);
        }
    }
    return $"{rows.Count} 件 (失敗 {failed} 件)";
}
```

テンプレート (リソースの xlsx) に注文と明細を書き込み、PDF にして添付する:

```csharp
// Jobs/SendOrderSheet.job.cs
string Run(JobArguments args)
{
    var search = new ModuleSearcher<Order>();
    search.AddEquals(e => e.Id.Value, args.Options[0]);
    // 一覧 (明細) の行は、読む項目に一覧と Id を入れたときだけ親と一緒に読まれる
    search.Select(e => e.Id, e => e.Title, e => e.OrderedOn, e => e.Customer, e => e.Lines);
    var order = search.ExecuteRawFirstOrDefault();
    if (order == null) Fail("注文がありません");

    var mail = new MailMessage();
    mail.To.Add(CurrentUser.Email.Value);
    mail.Subject = "注文書 " + order.Title.Value;
    using (var excel = new Excel(Resources.GetMemoryStream("Reports/Order.xlsx"), "Order.xlsx"))
    {
        excel.OverWrite(order);
        mail.Attachments.Add(excel.ToPdfAttachment("注文書.pdf"));
    }
    var result = MailService.Send(mail);
    if (!result.IsSuccess) Fail(result.Failures[0].Error);
    return "送りました";
}
```

同じ帳票を PDF にして、注文の添付ファイル (FileField の項目 `Sheet`) に付ける:

```csharp
// Jobs/AttachOrderSheet.job.cs
string Run(JobArguments args)
{
    var search = new ModuleSearcher<Order>();
    search.AddEquals(e => e.Id.Value, args.Options[0]);
    search.Select(e => e.Id, e => e.Title, e => e.OrderedOn, e => e.Customer, e => e.Lines);
    var order = search.ExecuteRawFirstOrDefault();
    if (order == null) Fail("注文がありません");

    var change = new Order.Data();
    change.Id = new IdFieldData();
    change.Id.Value = order.Id.Value;
    using (var excel = new Excel(Resources.GetMemoryStream("Reports/Order.xlsx"), "Order.xlsx"))
    {
        excel.OverWrite(order);
        change.Sheet = FileService.Save("Order", "Sheet", "注文書.pdf", excel.ToPdfMemoryStream());
    }
    var submit = new ModuleSubmitter();
    submit.Update(change);
    var result = submit.Execute();
    if (!result.Succeeded) Fail(result.Error);
    return "付けました";
}
```

レコードの添付にあるテンプレート (xlsx) を `FileService.GetMemoryStream` で読み、レコードの値で埋めて添付する:

```csharp
// Jobs/SendQuote.job.cs
string Run(JobArguments args)
{
    var search = new ModuleSearcher<Quote>();
    search.AddEquals(e => e.Id.Value, args.Options[0]);
    var quote = search.ExecuteRawFirstOrDefault();
    if (quote == null) Fail("見積がありません");
    var template = FileService.GetMemoryStream("Quote", "Template", quote.Id.Value);
    if (template == null) Fail("テンプレートがありません");

    var mail = new MailMessage();
    mail.To.Add(CurrentUser.Email.Value);
    mail.Subject = "見積書 " + quote.Title.Value;
    using (var excel = new Excel(template, "Template.xlsx"))
    {
        excel.OverWrite(quote);
        mail.Attachments.Add(excel.ToAttachment("見積書.xlsx"));
    }
    var result = MailService.Send(mail);
    if (!result.IsSuccess) Fail(result.Failures[0].Error);
    return "送りました";
}
```

#### Excel の追加分

| メソッド | 内容 |
|---|---|
| `OverWrite(ModuleData data)` | テンプレートを検索で読んだデータ (`ExecuteRaw` 等) で埋める。記法は `OverWrite(Module)` と同じ ([ExcelReportButtonField](ExcelReportButtonField.md) の「テンプレートの書き方」) |
| `ToAttachment(fileName)` | 書き込んだ xlsx を `MailAttachment` にする |
| `ToPdfAttachment(fileName)` | PDF に変換して `MailAttachment` にする。変換は `Excel.ConvertPdf` (`ExcelApi.UseInJobScripts` が入れる)。無ければ例外 |
| `ToMemoryStream()` | 書き込んだ xlsx の内容。`FileService.Save` に渡してレコードの添付ファイルにする |
| `ToPdfMemoryStream()` | PDF に変換した内容。`FileService.Save` に渡してレコードの添付ファイルにする。変換は `ToPdfAttachment` と同じ (無ければ例外) |

`OverWrite(ModuleData)` が解決する記号 (`$` の後ろ):

| 記号 | 書き込まれる値 |
|---|---|
| `$項目名.Value` など FieldData のプロパティ | データに載っている項目の値 (`$Customer.DisplayText` のように FieldData が持つプロパティは同じ名前で読める)。デザインにあってデータに載っていない項目は空 |
| `$Customer.Name.Value` (リンク越しの項目) | データに載っていれば (読む項目に入れたとき) その値 |
| `#LoopRow($Lines.Rows, row)` と `$row.項目名.Value` | 一覧の行の繰り返し (`Rows` は `Children` と同じ) |
| `$子.ChildModule.項目名.Value` | ModuleField の子モジュールの値 (`ChildModule` は `Data` と同じ) |

`OverWrite(Module)` で書けて `OverWrite(ModuleData)` では解決しないもの (セルは書き換わらず記号が残ります): モジュールのスクリプトの変数・関数、先頭がサービスの名前の記号 (`$Resources.…` 等)、画面のフィールドだけが持つメンバー (FieldData に無いもの)。

---

## 親と子の約束

| 項目 | 親 (`JobProcess`) | 子 (`JobMode`) |
|---|---|---|
| 引数 | `job <ジョブ名> --stdin` | `--stdin` を標準入力の内容に置き換える (`JobMode.StandardInputArgument`) |
| 入力 | 標準入力に UTF-8 で書いて閉じる (コマンドラインの長さの上限に掛からない) | 標準入力を UTF-8 で読み切ってからジョブを動かす |
| 起動したユーザー | 環境変数 `Jobs__UserId` (`JobProcess.UserIdEnvironmentVariable`) | 設定の `Jobs:UserId` として読める (環境変数が appsettings を上書きする) |
| 出力 | 標準出力・標準エラーを UTF-8 で読み切る (パイプが詰まらないように、終了を待つ前に読み始める) | `Console.OutputEncoding` を UTF-8 にする |
| 成功 | 終了コード 0。文言は標準出力の最後の行 | ジョブの戻り値を最後に 1 行書く (改行は空白に。null なら空行) |
| 失敗 | 終了コード 0 以外。文言は標準エラーの最初の空でない行 | 例外のメッセージを標準エラーに、例外の全文を標準出力に書いて 1。ジョブが無ければ標準エラーに「ジョブ 'X' がありません」を書いて 2 |
| 失敗の記録 | 同期・非同期とも、終了コードが 0 以外なら標準エラーと標準出力の全文を `LoggerFactory` に Error で書く | — |

手で同じことをするには `echo {"data":{},"submit":null} | dotnet LowCodeApp.Server.dll job <ジョブ名> --stdin`。

### 起動する実行ファイル

| 設定 | 既定 |
|---|---|
| `JobProcess.HostExecutable` | `EntryDll` の隣の apphost (Windows は `.exe`、Linux は拡張子なし)。無ければ (single-file 発行) このプロセスの実行ファイル。apphost はランタイムを自分で見つけるので、IIS (`w3wp.exe`) の下や PATH に `dotnet` が無いサーバーでも動く。Linux で実行ビットが無い apphost は使わない |
| `JobProcess.EntryDll` | このプロセスの入口の dll (`LowCodeApp.Server.dll`)。`HostExecutable` が空のとき `dotnet <dll>` で起動する |
| `JobProcess.DotnetPath` | `HostExecutable` が空のときの `dotnet` (このプロセスを起動した dotnet。dotnet から起動されていなければ PATH の `dotnet`) |

起動できないとき (実行ファイルが無い等) は `RunAsync` / `StartAsync` / `RunInNewProcessAsync` が例外を投げます (ジョブの失敗ではなく環境の不備)。`JobApi` から起動したときは、ジョブの失敗として返ります (「実行先」)。

---

## 制限

- 同じジョブを重ねて動かさないようにするには JobTriggerField の `AllowsConcurrentRuns` を `false` にします。止まるのは 1 台のサーバーの中だけです。サーバーを複数台で動かすときや、コマンドライン・タスクスケジューラから起動したジョブとは重なることがあるので、厳密な排他が要るならジョブが自分で取ります
- 同期 (`RunAsync`) にタイムアウトはありません。長い処理はホストが非同期 (`StartAsync`) で起動します
- ジョブが起動した別のプロセスが標準出力を引き継いだまま残ると、ジョブが終わっても出力の終わりを待ち続けます。ジョブから長く動くプロセスを起動するときは、標準出力・標準エラーをリダイレクトして切り離します
- 非同期の結果は終了から `FinishedRetention` の間だけ `JobProcess.Cache` に残ります。共有キャッシュにすれば複数台でもどのサーバーからでも問い合わせられますが、同時実行の判定 (`AllowsConcurrentRuns`) だけは 1 台の中です。長く残したい結果はジョブ自身が DB 等に残します
- 成功したジョブの標準出力はどこにも残りません (使うのは最後の 1 行だけ)。失敗したときは全文がログに出るので、ジョブは受け取った内容や秘密情報を標準出力に出さないようにします
- ジョブの中の書き込みは、実行先によらず起動したリクエストの監査ログ (AuditLog) には載りません。記録されるのは JobApi の呼び出し 1 件です
