# ジョブ (バッチ)

ホストのアプリが持つジョブ (C# の処理) を、同じアプリの別プロセスで動かす部品です。画面のボタン ([JobTriggerField](JobTriggerField.md)) から起動するときも、
コマンドライン (`dotnet LowCodeApp.Server.dll job <ジョブ名>`) から起動するときも、同じ経路を通ります。

- 親側 `JobProcess`: このプロセス自身を `job <ジョブ名> --stdin` で起動し、入力を標準入力で渡して結果 (`JobResult`) を受け取る。待たずに起動したジョブは Id で状態を問い合わせられる
- 子側 `JobMode`: `job` より後ろの引数を受けてジョブを 1 つ実行し、終了コードを返す
- 親と子の約束 (引数の並び・標準入力・環境変数・終了コード・UTF-8・結果の文言の取り出し) はこの 2 つの中に閉じています。ホストはジョブの一覧と DB アクセスの組み立てだけを書きます

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

画面のボタン (JobTriggerField) の受け口は Extras.Server の `JobApi` です。ホストは共通の API の対応表 (テンプレートの `Extras/ExtrasApiTable.cs`) に `new JobApi(JobRunner.Names)` (動かせるジョブの名前。無い名前は起動しない) を並べます。
`JobApi` はフィールドへの書き込み権限を確かめてから、デザインの JobTriggerField に従って `JobProcess` を呼びます。

```csharp
//JobApi の中 (自分の Controller で受けるときも同じ呼び方)
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

//Program.cs (失敗の記録先と、非同期のジョブの状態の置き場)
builder.Services.AddDistributedMemoryCache();
...
JobProcess.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
JobProcess.Cache = app.Services.GetRequiredService<IDistributedCache>();
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

### 同時実行

実行中のジョブはジョブ名 (大文字小文字は区別しない) で数えています。同期 (`RunAsync`) と非同期 (`StartAsync`) のどちらも数えます。
`allowConcurrent` が `false` のとき、同じ名前のジョブが実行中 (誰が起動したものでも) なら起動しません。JobApi はデザインの JobTriggerField の `AllowsConcurrentRuns` を渡します。

---

## 親と子の約束

| 項目 | 親 (`JobProcess`) | 子 (`JobMode`) |
|---|---|---|
| 引数 | `job <ジョブ名> --stdin` | `--stdin` を標準入力の内容に置き換える (`JobMode.StandardInputArgument`) |
| 入力 | 標準入力に UTF-8 で書いて閉じる (コマンドラインの長さの上限に掛からない) | 標準入力を UTF-8 で読み切ってからジョブを動かす |
| 実行ユーザー | 環境変数 `Jobs__UserId` (`JobProcess.UserIdEnvironmentVariable`) | 設定の `Jobs:UserId` として読める (環境変数が appsettings を上書きする) |
| 出力 | 標準出力・標準エラーを UTF-8 で読み切る (パイプが詰まらないように、終了を待つ前に読み始める) | `Console.OutputEncoding` を UTF-8 にする |
| 成功 | 終了コード 0。文言は標準出力の最後の行 | ジョブの戻り値を最後に 1 行書く (改行は空白に。null なら空行) |
| 失敗 | 終了コード 0 以外。文言は標準エラーの最初の空でない行 | 例外のメッセージを標準エラーに、例外の全文を標準出力に書いて 1。ジョブが無ければ標準エラーに「ジョブ 'X' がありません」を書いて 2 |
| 失敗の記録 | 同期・非同期とも、終了コードが 0 以外なら標準エラーと標準出力の全文を `LoggerFactory` に Error で書く | — |

手で同じことをするには `echo {"data":{},"submit":null} | dotnet LowCodeApp.Server.dll job Sample --stdin`。

### 起動する実行ファイル

| 設定 | 既定 |
|---|---|
| `JobProcess.HostExecutable` | `EntryDll` の隣の apphost (Windows は `.exe`、Linux は拡張子なし)。無ければ (single-file 発行) このプロセスの実行ファイル。apphost はランタイムを自分で見つけるので、IIS (`w3wp.exe`) の下や PATH に `dotnet` が無いサーバーでも動く。Linux で実行ビットが無い apphost は使わない |
| `JobProcess.EntryDll` | このプロセスの入口の dll (`LowCodeApp.Server.dll`)。`HostExecutable` が空のとき `dotnet <dll>` で起動する |
| `JobProcess.DotnetPath` | `HostExecutable` が空のときの `dotnet` (このプロセスを起動した dotnet。dotnet から起動されていなければ PATH の `dotnet`) |

起動できないとき (実行ファイルが無い等) は `RunAsync` / `StartAsync` が例外を投げます (ジョブの失敗ではなく環境の不備)。

---

## 制限

- 同じジョブを重ねて動かさないようにするには JobTriggerField の `AllowsConcurrentRuns` を `false` にします。止まるのは 1 台のサーバーの中だけです。サーバーを複数台で動かすときや、コマンドライン・タスクスケジューラから起動したジョブとは重なることがあるので、厳密な排他が要るならジョブが自分で取ります
- 同期 (`RunAsync`) にタイムアウトはありません。長い処理はホストが非同期 (`StartAsync`) で起動します
- ジョブが起動した別のプロセスが標準出力を引き継いだまま残ると、ジョブが終わっても出力の終わりを待ち続けます。ジョブから長く動くプロセスを起動するときは、標準出力・標準エラーをリダイレクトして切り離します
- 非同期の結果は終了から `FinishedRetention` の間だけ `JobProcess.Cache` に残ります。共有キャッシュにすれば複数台でもどのサーバーからでも問い合わせられますが、同時実行の判定 (`AllowsConcurrentRuns`) だけは 1 台の中です。長く残したい結果はジョブ自身が DB 等に残します
- 成功したジョブの標準出力はどこにも残りません (使うのは最後の 1 行だけ)。失敗したときは全文がログに出るので、ジョブは受け取った内容や秘密情報を標準出力に出さないようにします
- 子プロセスの書き込みは、親のリクエストの監査ログ (AuditLog) には載りません。記録されるのは親の呼び出し 1 件です
