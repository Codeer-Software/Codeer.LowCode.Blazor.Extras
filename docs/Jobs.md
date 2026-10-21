# JobTriggerField (ボタンでジョブを起動する)

モジュールに **JobTriggerField** を置くと、そのボタン (またはスクリプトの `Run()`) で、ホストに**ジョブ**の実行を頼みます。保存はしません (保存ボタンは別に置けて、通常どおり保存されます)。

Extras がするのは「ジョブ名 (呼び名) と JSON をホストの API に渡して、結果を受け取る」ことだけです。`JobName` はホストが解決する呼び名で、ジョブをどう動かすかはホストの持ち物です (Starter テンプレートでは `JobController` / `JobProcess` / `JobRunner` が、ジョブモード `dotnet LowCodeApp.Server.dll job <名前> <JSON>` を別プロセスで起動する)。

Codeer.LowCode.Blazor.Extras 0.23.0 以降。

## プロパティ

| プロパティ | 既定 | 説明 |
|---|---|---|
| `JobName` | `""` | ジョブの呼び名 (ホストが解決する)。必須 (空なら設計チェックのエラー) |
| `ShowButton` | `true` | ボタンを出す。`false` なら何も描かず、スクリプトの `Run()` からだけ実行する |
| `ButtonText` | `""` | ボタンの文言。空なら「実行」 |
| `UseSubmitData` | `true` | 画面の内容をジョブに渡す。`false` なら `{}` |
| `IsAsynchronous` | `false` | 非同期で実行する (下の「同期 / 非同期」) |

見た目は固定 (枠線のボタン)。凝るときは `ShowButton` を `false` にして ButtonField の OnClick から `Run()` を呼びます。

スクリプト: `Run()` (ボタンを押したのと同じ) / `IsRunning`。

## 何が起きるか

1. `UseSubmitData` なら保存ボタンと同じく入力チェックを通し (エラーがあれば頼まない)、画面の内容から JSON を作る
2. `POST {JobTriggerField.EndPoint}/run` に `{ "jobName": "...", "arguments": "<JSON>", "wait": true }` (`wait` = `!IsAsynchronous`) を送る (ボタンは応答が返るまで押せない)
3. ホストが `{ "exitCode": n, "error": "...", "started": false }` を返す (`JobRunResponse`)
4. 終了コード 0 なら成功のトースト。それ以外は `error` (空なら「ジョブ <名前> が失敗しました (終了コード n)」) をエラーのトーストに出す。`started` なら「ジョブ <名前> を開始しました」

## 同期 / 非同期

- 同期 (既定) はジョブが終わるまで待ち、結果 (成功 / エラー) が画面のトーストに返る
- 非同期 (`IsAsynchronous`) は起動したらすぐ「開始しました」を出して戻る。結果は画面に返らず、失敗はサーバーのログに出る (テンプレートは `JobProcess.Log` → ILogger "Jobs")
- 非同期のジョブの状態を問い合わせる口は無い (終わったかどうかはジョブ自身が DB 等に残す)

## JSON の形

ジョブに渡る JSON は 1 つで、次のどれかです (テンプレートのジョブモードでは `args[0]`)。

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

## ホストの API (Extras との約束)

- `POST {EndPoint}/run`、要求 `JobRunRequest { JobName, Arguments, Wait }`、応答 `JobRunResponse { ExitCode, Error, Started }` (どちらも `Codeer.LowCode.Blazor.Extras.Jobs`)
- 受け付けるのはデザインの JobTriggerField に書いたジョブ名だけにする (`JobTriggerFields.IsDeclared(designData, jobName)`。テンプレートは無い名前を終了コード 2 で返す)
- クライアントの `ServiceInitializer` で `JobTriggerField.EndPoint = "/api/job";`

## Starter テンプレートでの実装

ジョブの動かし方はテンプレートのコードです (差し替え可)。

- `Controllers/JobController.cs`: アプリへのアクセス (`CheckAppAccessAsync`) と `IsDeclared` を見て、`Wait` なら `JobProcess.RunAsync` の結果、そうでなければ `JobProcess.Start` して `Started = true` を返す
- `Jobs/JobProcess.cs`: ジョブモードを別プロセスで起動する (`dotnet <ホストの dll> job <JobName> <JSON>`。押したユーザーを環境変数 `Jobs__UserId` で渡す。標準エラーが `error`)。引数の並び・環境変数・終了コードは同じフォルダの `JobRunner` と対
- `Program.cs`: `JobProcess.Log` を ILogger (カテゴリ "Jobs") につなぐ (非同期のジョブの失敗の記録先)

ジョブはボタンを押したユーザーで動き、データの権限はジョブの `ModuleDataIO` の読み書きで効きます。

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

## 制限 (テンプレートの実装)

- ジョブが途中まで書いたものは、失敗しても戻らない (ジョブのトランザクション次第)
- JSON はコマンドラインで渡すので、Windows のコマンドライン長の上限 (約 32,000 文字) を超える内容は起動できない
- 同期では画面がジョブの終わりまで待つ (長い処理は非同期に。HTTP のタイムアウトはホストの設定)

## 設計チェック

- `JobName` が空ならエラー
