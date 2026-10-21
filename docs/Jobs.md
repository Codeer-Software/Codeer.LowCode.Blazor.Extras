# JobTriggerField (ボタンでジョブを起動する)

モジュールに **JobTriggerField** を置くと、そのボタン (またはスクリプトの `Run()`) で、ホストの**ジョブ** (Starter テンプレートのジョブモード `dotnet LowCodeApp.Server.dll job <名前> [引数...]`) を別プロセスで起動して終わるまで待ちます。保存はしません (保存ボタンは別に置けて、通常どおり保存されます)。

Codeer.LowCode.Blazor.Extras / Extras.Server 0.23.0 以降。

## プロパティ

| プロパティ | 既定 | 説明 |
|---|---|---|
| `JobName` | `""` | 起動するジョブ名。必須 (空なら設計チェックのエラー) |
| `ShowButton` | `true` | ボタンを出す。`false` なら何も描かず、スクリプトの `Run()` からだけ実行する |
| `ButtonText` | `""` | ボタンの文言。空なら「実行」 |
| `UseSubmitData` | `true` | 画面の内容をジョブに渡す。`false` なら `{}` |
| `IsAsynchronous` | `false` | 非同期で実行する (下の「同期 / 非同期」) |

見た目は固定 (枠線のボタン)。凝るときは `ShowButton` を `false` にして ButtonField の OnClick から `Run()` を呼びます。

スクリプト: `Run()` (ボタンを押したのと同じ。終わるまで待つ) / `IsRunning`。

## 何が起きるか

1. `UseSubmitData` なら保存ボタンと同じく入力チェックを通し (エラーがあれば起動しない)、画面の内容から引数の JSON を作る
2. `POST {JobTriggerField.EndPoint}/run` に `{ "jobName": "...", "arguments": "<JSON>", "wait": true }` (`wait` = `!IsAsynchronous`) を送る (ボタンは結果が返るまで押せない)
3. ホストの `JobController` が `JobProcess.RunAsync(jobName, arguments, 押したユーザーの Id)` で `dotnet <ホストの dll> job <JobName> <JSON>` を起動して終わるまで待ち (タイムアウトなし)、`{ "exitCode": n, "error": "<標準エラー>" }` を返す
4. 終了コード 0 なら成功のトースト。それ以外は `error` (空なら「ジョブ <名前> が失敗しました (終了コード n)」) をエラーのトーストに出す

## 同期 / 非同期

- 同期 (既定) はジョブが終わるまで待ち、結果 (成功 / 標準エラー) が画面のトーストに返る
- 非同期 (`IsAsynchronous`) は起動したらすぐ「開始しました」を出して戻る。結果は画面に返らず、失敗はサーバーのログ (`JobProcess.Log`) に出る
- 非同期のジョブの状態を問い合わせる口は無い (終わったかどうかはジョブ自身が DB 等に残す)
- 非同期のとき API は `JobProcess.Start` で起動してすぐ `{ "exitCode": 0, "started": true }` を返す。デザインに無いジョブ名は非同期でも起動せずエラーが画面に返る

## JSON の形

ジョブの引数 (`args[0]`) は JSON 1 つで、次のどれかです。

| JSON | いつ |
|---|---|
| `{"submit":[ModuleSubmitData]}` | `UseSubmitData` で、送る内容がある (変更がある・新規。保存ボタンが送るのと同じ内容) |
| `{"data":ModuleData}` | `UseSubmitData` で、送る内容が無い (変更なし・表を持たないモジュール)。画面の現在値 |
| `{}` | `UseSubmitData` が `false` |

中身は本体の `JsonConverterEx.DeserializeObject<List<ModuleSubmitData>>` / `DeserializeObject<ModuleData>` で戻せます。

```json
{"submit":[{"SubmitId":"...","ModuleName":"Order","Id":"1","Add":[],"Update":[{"Name":"Order","Fields":{"Status":{"Value":"Closed","TypeFullName":"Codeer.LowCode.Blazor.Repository.Data.TextFieldData"},"Id":{"Value":"1","TypeFullName":"Codeer.LowCode.Blazor.Repository.Data.IdFieldData"}}}],"Delete":[],"SearchDelete":[],"ExtendedData":[],"NoTemporaryIdResolution":false}]}
```

DB に書きたいときはジョブが自分で `ModuleDataIO` で保存します (`data.ModuleDataIO.SubmitWithTransactionAsync(submit)` など)。`ModuleDataIO` で保存すれば、編集履歴・意味検索の索引・ホストの `CustomizedModuleDataIO` の override がジョブのトランザクションで付きます。生 SQL で書くと付きません。

## ユーザー

ジョブはボタンを押したユーザーで動きます (環境変数 `Jobs__UserId` = そのユーザーの Id。ジョブモードの `Jobs:UserId` を上書きする)。`ASPNETCORE_ENVIRONMENT` は親と同じものを渡し、設定は子プロセスの exe の隣の appsettings から読みます。
API の入口ではアプリへのアクセス (`CheckAppAccessAsync`) を見て、デザインの JobTriggerField に書いたジョブ名だけを受け付けます (`JobTriggerFields.IsDeclared`。無い名前は終了コード 2 で起動しない)。データの権限はジョブの `ModuleDataIO` の読み書きで (押したユーザーとして) 効きます。

## 制限

- ジョブが途中まで書いたものは、失敗しても戻らない (ジョブのトランザクション次第)
- 引数はコマンドラインで渡すので、Windows のコマンドライン長の上限 (約 32,000 文字) を超える内容は起動できない
- 画面はジョブが終わるまで待つ (長い処理には向かない。HTTP のタイムアウトはホストの設定)

## ホストの結線

Starter テンプレート (Cookie) に入っています。

- `Controllers/JobController.cs`: `POST api/job/run`。`wait` なら `JobProcess.RunAsync` の結果、そうでなければ `JobProcess.Start` して `Started = true` を返す
- `Program.cs`: `JobProcess.Log` を `ILogger` (カテゴリ "Jobs") につなぐ (非同期のジョブの失敗の記録先)
- クライアントの `ServiceInitializer`: `JobTriggerField.EndPoint = "/api/job";`

```csharp
[HttpPost("run"), Audit(AuditCategory.DataWrite)]
public async Task<JobRunResponse> RunAsync(JobRunRequest request)
{
    await _dataService.ModuleDataIO.AuthorizationChecker.CheckAppAccessAsync();
    if (!JobTriggerFields.IsDeclared(_dataService.Design.DesignData, request.JobName))
        return new JobRunResponse { ExitCode = 2, Error = $"ジョブ '{request.JobName}' はデザインにありません。" };
    var userId = await _dataService.GetCurrentUserIdAsync();
    if (!request.Wait)
    {
        JobProcess.Start(request.JobName, request.Arguments, userId);
        return new JobRunResponse { Started = true };
    }
    var (exitCode, _, error) = await JobProcess.RunAsync(request.JobName, request.Arguments, userId);
    return new JobRunResponse { ExitCode = exitCode, Error = error.Trim() };
}
```

## 設計チェック

- `JobName` が空ならエラー
