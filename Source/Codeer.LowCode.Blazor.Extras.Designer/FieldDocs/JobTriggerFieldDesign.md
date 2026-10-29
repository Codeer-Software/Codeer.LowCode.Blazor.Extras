## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign`

ボタンを押す (またはスクリプトで `Run()`) と、画面の内容の JSON をホストに渡してジョブ (サーバーの C# の処理) の実行を頼み、結果を通知 (トースト) で出すフィールド。動かすジョブは `JobName` で、ホストがこのデザインから読む (クライアントからジョブ名は送らない)。このフィールド自体は保存しない (保存ボタンは別に置ける)。

- **ホストの実装が要る** (ジョブ本体も含めて C# で書く。デザインファイルでは作れない)。Starter テンプレートでは Web サーバー (Cookie。MAUI はそのサーバー経由) だけで、WPF / WinForms は未対応 (押すと「このアプリではジョブを実行できません」の通知が出る)。仕組みは Codeer.LowCode.Blazor.Extras の `docs/Jobs.md`、ジョブの書き方はテンプレートの `Jobs/README.md`
- **このフィールドに書けるユーザーが実行でき、ジョブはそのユーザーの権限で動く** (`ExecuteSqlField` の実行と同じ)。書けない (PermissionField の書き込み条件・閲覧モード・`CanCreate` / `CanUpdate` が `false` の読み取り専用のモジュール) ときは他のボタンと同じく押せず、`Run()` も何もせず `false` (読み取り専用のモジュールで使うなら OnLoad のスクリプトでこのフィールドの `IsViewOnly` を `false` にする)。絞るなら **CurrentUser だけで決まる** PermissionField の書き込み条件を使う。行の値を見る条件は、画面ではその行でボタンが押せなくなるが、**サーバーの実行可否の判定には使われない** (対象行が無い。モジュールの `DataReadCondition` / `DataWriteCondition` は設計チェック `JobTriggerFieldDesign:2`、このフィールドを対象にした PermissionField の行条件は `JobTriggerFieldDesign:3` で検出)。行の状態で実行を止めたいなら、ジョブが受け取った `data` や DB を見て自分で確かめる
- ジョブが DB に保存しても画面には反映されない (新規の画面は仮 Id のまま。そのまま保存ボタンを押すと二重に登録される)。DB に書く用途は「保存ボタン → スクリプトから `Run()`」の順にするか、ジョブは画面の内容を読むだけにする
- 見た目は固定 (枠線のボタン)。凝るときは `ShowButton` を `false` にして ButtonField の OnClick から `Run()` を呼ぶ

### 押すと何が起きるか

1. `UseSubmitData` が `true` なら、保存ボタンと同じく入力チェック (必須等) を通す。エラーがあればジョブを頼まない
2. 画面の内容を JSON にしてホストに渡す (下の「ジョブに渡る JSON」)
3. ホストがジョブを動かす。同期 (既定) なら終わるまで待つ (その間ボタンは押せない)。非同期 (`IsAsynchronous`) なら起動したらすぐ戻り、「開始しました」を通知する。結果は待たない (ジョブの Id を `JobId` / `JobIds` に持ち、スクリプトの `GetJobStatus` で問い合わせる。フィールドは自分では問い合わせない)
4. 結果を通知に出す (下の「応答」)

`AllowsConcurrentRuns` が `false` (既定) なら、同じジョブ (`JobName`) が実行中 (誰が起動したものでも・同期と非同期のどちらでも) のときはホストが起動せず、失敗の通知になる (判定はサーバーがジョブ名で行う)。

### ジョブに渡る JSON (1 つ)

| `UseSubmitData` | JSON |
|---|---|
| `true` | `{"data": ModuleData, "submit": [ModuleSubmitData] または null}` — `data` は画面の現在値、`submit` は保存ボタンが送るのと同じ内容 (変更なし・表を持たないモジュールなど送るものが無ければ `null`) |
| `false` | `{}` |

`data` はボタン (`Run()`) 起点のときだけ渡る。将来の別の起点 (保存時・スケジュール) では無いことがあるので、ジョブは `data` の有無を見て書く。

DB 列格納モードの FileField (`DbColumnFileContent`) は実体ごと (base64) `data` に入る (ファイルを選び直したときは `submit` にも)。大きい添付のあるモジュールでは `UseSubmitData` を `false` にするか、ジョブで Id から読み直す。

### 応答

| 応答 | 通知 | `Run()` の戻り値 |
|---|---|---|
| `Succeeded` | 成功の通知。`Message` があればそれ、空なら「ジョブ X が完了しました」 | `true` |
| それ以外 (失敗) | エラーの通知。`Error` があればそれ、空なら「ジョブ X が失敗しました」 | `false` |
| `Started` (非同期で起動できた) | 「ジョブ X を開始しました」だけ。`JobId` があれば `JobId` と `JobIds` に入る (結果は `GetJobStatus` で問い合わせる) | `true` |
| 応答なし (通信の失敗) | 出さない (ホストが通知済み) | `false` |
| ホストが例外を投げた・送り先が未設定 | 例外のメッセージ | `false` |

### C# クラス定義 (真実の源)

```csharp
public class JobTriggerFieldDesign : FieldDesignBase
{
    public string JobName { get; set; } = string.Empty;
    public bool ShowButton { get; set; } = true;
    public string ButtonText { get; set; } = string.Empty;
    public bool UseSubmitData { get; set; } = true;
    public bool IsAsynchronous { get; set; }
    public bool AllowsConcurrentRuns { get; set; }
    // 親 FieldDesignBase から継承: Name（IgnoreModification / OnValidateInput はデザイナ非表示）
}
```

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `JobName` | string | `""` | 実行するジョブの名前 (ホストが解決する)。必須 (空ならデザインチェックのエラー `JobTriggerFieldDesign:1`) |
| `ShowButton` | bool | `true` | ボタンを出す。`false` なら画面には何も出ず、スクリプトの `Run()` からだけ実行する (そのときはレイアウトの `DataOnlyFields` に入れる) |
| `ButtonText` | string | `""` | ボタンの文言。空なら「実行」 |
| `UseSubmitData` | bool | `true` | 画面の内容をジョブに渡す (上の表)。`false` なら `{}` |
| `IsAsynchronous` | bool | `false` | 非同期で実行する (起動したらすぐ戻り「開始しました」を通知する。結果は `JobId` / `JobIds` と `GetJobStatus` で問い合わせる) |
| `AllowsConcurrentRuns` | bool | `false` | 同じジョブ (`JobName`) を重ねて動かすことを許す。`false` なら、そのジョブが実行中 (誰が起動したものでも・同期と非同期のどちらでも) に起動すると失敗の通知になる (判定はサーバーがジョブ名で行う。1 台のサーバーの中だけ) |

### JSON例

```json
{
  "JobName": "ImportOrder",
  "ShowButton": true,
  "ButtonText": "取込",
  "UseSubmitData": true,
  "IsAsynchronous": false,
  "AllowsConcurrentRuns": false,
  "Name": "ImportJob",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign"
}
```

### 検索

検索には対応しない。DB列マッピングなし。

### ホストの結線 (ユーザーの作業。テンプレートには入っている)

- クライアント: 設定不要 (共通の API `ExtrasTransport.EndPoint` = `/api/extras` に送る。`GetJobStatus` も同じ口で問い合わせる)。ジョブ専用のエンドポイントで受けるホストは `JobTriggerField.EndPoint` を設定する (状態は `GET {EndPoint}/{jobId}`)。HTTP を使わないホストは `JobTriggerField.RunCoreAsync` と `JobTriggerField.GetJobStatusCoreAsync` (非同期のジョブの状態の問い合わせ) を差し替える
- サーバー: ホストの共通の API (テンプレートの `Controllers/ExtrasController.cs`) の対応表 `Extras/ExtrasApiTable.cs` に `JobApi` が載っていること (テンプレートは載せ済み: `new JobApi(JobRunner.Names)`。動かせるジョブは `Jobs/JobRunner`)。`JobApi` が `JobRunRequest` を受け、書き込み権限を確かめて Extras.Server の `JobProcess` でジョブを別プロセスとして起動する。非同期では `JobId` を返し、状態の問い合わせに `JobStatusResponse` を返す。`AllowsConcurrentRuns` が `false` で同じジョブが実行中なら起動せず失敗を返す

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Run()` | bool | ジョブを実行する (ボタンを押したのと同じ)。同期は終わるまで待ち、成功なら `true`。非同期は起動できたら `true` (終わるのは待たない)。入力エラー・失敗・実行中・書けない (ViewOnly) か無効のときは `false` |
| `IsRunning` | bool | ジョブを頼んでいる間 `true` (同期は終わるまで・非同期は起動するまで) |
| `JobId` | string | 終わったことをまだ確かめていないジョブのうち、最後に起動したものの Id (`JobIds` の最後。無ければ空)。`GetJobStatus` で終わったと分かると `JobIds` から外れるので、同じジョブを二度問い合わせない |
| `JobIds` | string[] | 非同期で起動して、終わったことをまだ確かめていないジョブの Id (起動順)。`GetJobStatus` で終わったと分かったら外れる |
| `GetJobStatus(jobId)` | JobStatusResponse | 非同期のジョブの状態を 1 回問い合わせる (自動では呼ばれない)。終わっていれば `JobIds` から外す。分からない (知らない Id = サーバーの再起動・終了から 30 分以上・通信できない・問い合わせる口が無いホスト) なら `null`。サーバーが知らない Id も `JobIds` から外す (通信できなかっただけなら残る)。通信の失敗は通知しない |
| `GetJobStatus()` | JobStatusResponse | 引数を省く (空文字も同じ) と `JobIds` の先頭 (一番古いもの) を問い合わせる。`JobIds` が空なら問い合わせずに `null`。`JobId` (`JobIds` の最後) とは違うものを指す (同時実行を許さないなら同じもの) |

`JobStatusResponse`: `Status` (JobStatus。`JobStatus.Running` = まだ実行中 / `JobStatus.Done` = 成功して終わった / `JobStatus.Error` = 失敗して終わった) / `Message` (string。成功したときのジョブの戻り値の文言) / `Error` (string。失敗したときのエラーの文言)

ButtonField から実行する例 (`ShowButton` を `false` にしてこのフィールドを `DataOnlyFields` に入れる):

```csharp
void RunButton_OnClick()
{
    if (ImportJob.Run())
    {
        //成功したときだけの続き
    }
}
```

非同期のジョブを起動して、終わるまで 3 秒ごとに確かめる例 (同時実行を許さないジョブ。レイアウトに本体の `TimerField` を `Poll` (`IntervalMilliseconds: 3000`, `OnTick: "CheckJob"`) として置く。画面から外れると止まる。フィールド自身に待つ仕組みは無い):

```csharp
void RunButton_OnClick()
{
    if (ImportJob.Run()) Poll.Start();
}

void CheckJob()
{
    var status = ImportJob.GetJobStatus();
    if (status == null || status.Status == JobStatus.Running) return;
    Poll.Stop();
    if (status.Status == JobStatus.Done) MessageBox.Show("完了しました: " + status.Message);
    else MessageBox.Show("失敗しました: " + status.Error);
}
```

複数のジョブを並べて動かすときは `foreach (var id in ImportJob.JobIds)` で Id を 1 つずつ `GetJobStatus(id)` に渡す。

値・データ系メソッドは公開しない (`IsModified` は常に `false`。保存には何も載せない)。

---

## CSS

```html
<button type="button" class="btn btn-outline-primary" data-system="job-trigger">実行</button>
```

`ShowButton` が `false` なら何も描かない。
