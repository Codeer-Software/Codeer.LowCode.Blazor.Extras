# ジョブ (バッチ)

ホストのアプリが持つジョブ (C# の処理) を、同じアプリの別プロセスで動かす部品です。画面のボタン ([JobTriggerField](JobTriggerField.md)) から起動するときも、
コマンドライン (`dotnet LowCodeApp.Server.dll job <ジョブ名>`) から起動するときも、同じ経路を通ります。

- 親側 `JobProcess`: このプロセス自身を `job <ジョブ名> --stdin` で起動し、入力を標準入力で渡して結果 (`JobResult`) を受け取る
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

### 親側: JobController と Program.cs

```csharp
//JobController (画面のボタン = JobTriggerField の受け口)
if (design.IsAsynchronous)
{
    JobProcess.Start(design.JobName, request.Arguments, userId);     // 起動したら返す
    return new JobRunResponse { Started = true };
}
var result = await JobProcess.RunAsync(design.JobName, request.Arguments, userId);   // 終わるまで待つ
return result.Succeeded ? new JobRunResponse { Succeeded = true, Message = result.Message } : Failure(result.ErrorMessage);

//HTTP としては成功 (200) で返す業務上の失敗。監査ログ (AuditLog) にも失敗として残す
JobRunResponse Failure(string error)
{
    _audit.Fail(error);
    return new JobRunResponse { Succeeded = false, Error = error };
}

//Program.cs (失敗の記録先)
JobProcess.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
```

`JobResult`:

| メンバー | 内容 |
|---|---|
| `Succeeded` | 終了コードが 0 |
| `Message` | 成功したときの文言 = 標準出力の最後の行 (ジョブの戻り値。null なら空) |
| `ErrorMessage` | 失敗したときの文言 = 標準エラーの最初の空でない行 (例外のメッセージ)。無ければ「ジョブ X が失敗しました (終了コード N)」 |
| `ExitCode` / `Output` / `Error` | 終了コード・標準出力の全文・標準エラーの全文 |

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

起動できないとき (実行ファイルが無い等) は `RunAsync` / `Start` が例外を投げます (ジョブの失敗ではなく環境の不備)。

---

## 制限

- 同じジョブが同時に動くことがあります。排他が要るならジョブが自分で取ります
- 同期 (`RunAsync`) にタイムアウトはありません。長い処理はホストが非同期 (`Start`) で起動します
- ジョブが起動した別のプロセスが標準出力を引き継いだまま残ると、ジョブが終わっても出力の終わりを待ち続けます。ジョブから長く動くプロセスを起動するときは、標準出力・標準エラーをリダイレクトして切り離します
- 非同期の結果を問い合わせる口はありません (終わったかどうかはジョブ自身が DB 等に残す)
- 成功したジョブの標準出力はどこにも残りません (使うのは最後の 1 行だけ)。失敗したときは全文がログに出るので、ジョブは受け取った内容や秘密情報を標準出力に出さないようにします
- 子プロセスの書き込みは、親のリクエストの監査ログ (AuditLog) には載りません。記録されるのは親の呼び出し 1 件です
