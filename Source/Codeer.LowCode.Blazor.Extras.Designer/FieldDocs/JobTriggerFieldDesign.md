## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

ボタンを押す (またはスクリプトで `Run()`) と、サーバーがホストのジョブ (`dotnet LowCodeApp.Server.dll job <JobName> <JSON>`) を別プロセスで起動し、終わるまで待つフィールド。ジョブは押したユーザーで動く。終了コード 0 なら成功のトースト、それ以外はジョブの標準エラーの内容がエラーのトーストに出る。待っている間ボタンは押せない。このフィールド自体は保存しない (保存ボタンは別に置ける。保存は通常どおり)。

- ジョブ本体はホストのサーバープロジェクト (`LowCodeApp.Server/Jobs/`) に C# で書く (デザインファイルでは作れない)。DB に書きたいならジョブが自分で `ModuleDataIO` で保存する (編集履歴・意味検索の索引はそのとき付く。生 SQL では付かない)
- 見た目は固定 (枠線のボタン)。凝るときは `ShowButton` を `false` にして ButtonField の OnClick から `Run()` を呼ぶ
- 呼び出し先はホストの `POST /api/job/run` (テンプレの `JobController`)。ホストにこの API が無いと失敗のトーストになる

### 同期 / 非同期

- 同期 (既定) はジョブが終わるまで待ち、結果 (成功 / 標準エラー) が画面のトーストに返る
- 非同期 (`IsAsynchronous`) は起動したらすぐ「開始しました」を出して戻る。結果は画面に返らず、失敗はサーバーのログ (`JobProcess.Log`) に出る
- 非同期のジョブの状態を問い合わせる口は無い (終わったかどうかはジョブ自身が DB 等に残す)

### ジョブに渡る JSON (引数 1 つ)

| `UseSubmitData` | 画面の状態 | JSON |
|---|---|---|
| `true` | 変更がある・新規 (保存ボタンが送る内容がある) | `{"submit":[ModuleSubmitData]}` (保存ボタンが送るのと同じ内容) |
| `true` | 送る内容が無い (変更なし・表を持たないモジュール) | `{"data":ModuleData}` (画面の現在値) |
| `false` | — | `{}` |

`UseSubmitData` が `true` なら、保存ボタンと同じく先に入力チェック (必須等) を通し、エラーがあればジョブを起動しない。

### プロパティ

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `JobName` | string | `""` | 起動するジョブ名。必須 (空ならデザインチェックのエラー) |
| `ShowButton` | bool | `true` | ボタンを出す。`false` なら画面には何も出ず、スクリプトの `Run()` からだけ実行する (そのときはレイアウトの `DataOnlyFields` に入れる) |
| `ButtonText` | string | `""` | ボタンの文言。空なら「実行」 |
| `UseSubmitData` | bool | `true` | 画面の内容をジョブに渡す (上の表)。`false` なら `{}` |
| `IsAsynchronous` | bool | `false` | 非同期で実行する (上の「同期 / 非同期」) |

### JSON例

```json
{
  "JobName": "ImportOrder",
  "ShowButton": true,
  "ButtonText": "取込",
  "UseSubmitData": true,
  "IsAsynchronous": false,
  "Name": "ImportJob",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign"
}
```

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Run()` | void | ジョブを実行する (ボタンを押したのと同じ。同期なら終わるまで待って結果をトースト、非同期なら起動したら戻る)。実行中は無視。 |
| `IsRunning` | bool | サーバーの応答を待っている間 true (非同期ならすぐ false に戻る)。 |

ButtonField から実行する例 (`ShowButton` を `false` にしてこのフィールドを `DataOnlyFields` に入れる):

```csharp
void RunButton_OnClick()
{
    ImportJob.Run();
}
```

値・データ系メソッドは公開しない (`IsModified` は常に `false`。保存には何も載せない)。
