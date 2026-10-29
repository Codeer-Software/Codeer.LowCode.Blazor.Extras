# JobTriggerField - ジョブトリガー

**ボタンを押すと、サーバーのジョブ（C# で書いたバッチ処理）を実行するフィールド**です。画面の内容をジョブに渡し、結果を通知で表示します。

ジョブ本体は C# で書きます（デザインファイルでは作れません）。ジョブの書き方・定期実行はマニュアルの [バッチ処理（ジョブ）](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/user_code/batch_job.md)、ホスト側の仕組みは [ジョブ (バッチ)](Jobs.md) を参照してください。

Codeer.LowCode.Blazor.Extras 0.23.0 以降（Codeer.LowCode.Blazor 1.3.53 以降）。アプリのテンプレートでは Web サーバーのアプリ（MAUI はそのサーバー経由）で動きます。WPF / WinForms のアプリでは使えません（押すと「このアプリではジョブを実行できません」の通知が出ます）。

## いつ使うか

- 画面に入力した条件で、取り込み・集計・帳票作成などのまとまった処理を実行したい
- スクリプトでは書きにくい処理（外部システムとの連携・大量データの処理）をボタンから動かしたい
- 時間のかかる処理を、画面を待たせずに動かして、あとで結果を確かめたい

## プロパティ

| C#名 | 日本語表示名 | 型 | 既定値 | 説明 |
|---|---|---|---|---|
| **Name** | 名前 | string | `""` | フィールド識別子 |
| **JobName** | ジョブ名 | string | `""` | 実行するジョブの名前（必須） |
| **ShowButton** | ボタンを表示 | bool | `true` | `false` なら画面には何も出ず、スクリプトの `Run()` からだけ実行する |
| **ButtonText** | テキスト | string | `""` | ボタンの文言。空なら「実行」 |
| **UseSubmitData** | 画面の内容を渡す | bool | `true` | 画面の内容をジョブに渡す。`false` なら何も渡さない |
| **IsAsynchronous** | 非同期で実行 | bool | `false` | `true` ならジョブを起動したらすぐ戻り、「開始しました」を通知する。結果はスクリプトの `GetJobStatus` で問い合わせる |
| **AllowsConcurrentRuns** | 同時実行を許す | bool | `false` | 同じジョブ（ジョブ名）を重ねて動かすことを許す。`false` なら、そのジョブが実行中（誰が起動したものでも・同期と非同期のどちらでも）に実行すると失敗の通知になる |

## 動作

1. 「画面の内容を渡す」が `true` なら、保存ボタンと同じく入力チェック（必須など）を行います。エラーがあれば実行しません
2. 画面の内容をサーバーに送り、デザインに設定したジョブ名のジョブを実行します（ジョブ名は画面から送らず、サーバーがデザインから読みます）
3. 結果を通知で表示します（非同期では「開始しました」を通知するだけで、結果は待ちません。結果はスクリプトで問い合わせます）

| 結果 | 通知 |
|---|---|
| 成功 | ジョブの戻り値の文言（無ければ「ジョブ X が完了しました」） |
| 失敗 | ジョブの例外のメッセージ（無ければ「ジョブ X が失敗しました」） |
| 非同期で起動した | 「ジョブ X を開始しました」 |
| 同時実行を許さないジョブが実行中 | 「ジョブ 'X' は実行中です」（失敗の通知） |

- 実行中はボタンを押せません（非同期は起動するまで）。非同期のジョブはブラウザを閉じても動き続けます
- このフィールド自体は何も保存しません。ジョブが DB に保存しても開いている画面には反映されないので、保存とジョブを続けて行うときは「保存してからジョブ」の順にしてください（下のスクリプトの例）

## ジョブに渡る内容

| 画面の内容を渡す | ジョブが受け取る JSON |
|---|---|
| `true` | `{"data": 画面の現在の値, "submit": 保存ボタンが送るのと同じ内容 (変更が無ければ null)}` |
| `false` | `{}` |

- ジョブの `args[0]` に JSON の文字列として渡ります
- DB 列に格納するモードのファイル（FileField）は、ファイルの中身ごと JSON に入ります。大きいファイルのある画面では「画面の内容を渡す」を `false` にして、ジョブで Id から読み直してください

## 権限

- **このフィールドに書き込めるユーザーが実行できます**。ジョブはそのユーザーの権限で動きます
- 書き込めないとき（権限の書き込み条件・閲覧モード・新規作成/更新ができないモジュール）は、ほかのボタンと同じく押せません。新規作成/更新ができないモジュールで使うときは、スクリプトでこのフィールドの `IsViewOnly` を `false` にしてください
- 実行できるユーザーを絞るときは、PermissionField の書き込み条件を **ログインユーザー (CurrentUser) だけで決まる条件** にしてください。行の値を見る条件は、画面ではその行でボタンが押せなくなりますが、サーバーでの実行可否の判定には使われません（デザインチェックで警告が出ます）
- 監査ログを有効にしているときは「誰が・どのモジュールのどのジョブを・成否（非同期では起動の成否）」が 1 件記録されます。ジョブの中で何をしたかの記録はジョブで行ってください

## スクリプト

| メンバー | 型 | 説明 |
|---|---|---|
| `Run()` | bool | ジョブを実行する（ボタンを押したのと同じ）。終わるまで待ち、成功なら `true`。非同期では起動できたら `true`（終わるのは待たない） |
| `IsRunning` | bool | ジョブを頼んでいる間 `true`（同期は終わるまで・非同期は起動するまで） |
| `JobId` | string | 終わったことをまだ確かめていないジョブのうち、最後に起動したものの Id（`JobIds` の最後。無ければ空）。`GetJobStatus` で終わったと分かると `JobIds` から外れるので、同じジョブを二度問い合わせない |
| `JobIds` | string[] | 非同期で起動して、終わったことをまだ確かめていないジョブの Id（起動順）。`GetJobStatus` で終わったと分かったら外れる |
| `GetJobStatus(jobId)` | JobStatusResponse | 非同期のジョブの状態を 1 回問い合わせる。終わっていれば `JobIds` から外す。分からない（サーバーが再起動した・終わってから 30 分以上たった・通信できなかった）ときは `null`（サーバーが忘れたものは `JobIds` から外れ、通信できなかっただけなら残る）。終わるまで待つ機能はありません（確かめたいときに呼びます） |
| `GetJobStatus()` | JobStatusResponse | 引数を省くと `JobIds` の先頭（一番古いもの）を問い合わせる。`JobIds` が空なら問い合わせずに `null`。`JobId`（`JobIds` の最後）とは違うものを指す（同時実行を許さないなら同じもの） |

`GetJobStatus` が返すもの:

| メンバー | 型 | 説明 |
|---|---|---|
| `Status` | JobStatus | `JobStatus.Running` (まだ実行中) / `JobStatus.Done` (成功して終わった) / `JobStatus.Error` (失敗して終わった) |
| `Message` | string | 成功したときのジョブの戻り値の文言 |
| `Error` | string | 失敗したときのエラーの文言 |

見た目を変えたいとき・保存してから実行したいときは、「ボタンを表示」を `false` にし、このフィールドをレイアウトの「表示しないデータ項目」に入れて、ButtonField から呼び出します。

```csharp
void RunButton_OnClick()
{
    if (Module.Submit() != true) return;   // 先に保存する
    if (ImportJob.Run())
    {
        // 成功したときだけの続き
    }
}
```

非同期のジョブを起動して、終わるまで 3 秒ごとに確かめる例（同時実行を許さないジョブ。レイアウトに本体の `TimerField` を `Poll`（`IntervalMilliseconds: 3000`, `OnTick: "CheckJob"`）として置く。画面から外れると止まります）:

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

複数のジョブを並べて動かすときは、`foreach (var id in ImportJob.JobIds)` で Id を 1 つずつ `GetJobStatus(id)` に渡します。

## ホストの結線

アプリのテンプレート（Cookie）には入っています。自分で組むときは次の 2 つです。

- クライアント: 設定は不要です（共通の API `ExtrasTransport.EndPoint` = `/api/extras` に送ります）。HTTP を使わないホストは `JobTriggerField.RunCoreAsync` に送り方を、`JobTriggerField.GetJobStatusCoreAsync` に非同期のジョブの状態の問い合わせ方を設定します（`GetJobStatusCoreAsync` が無ければ `GetJobStatus` は `null`）
- サーバー: 共通の API（`Controllers/ExtrasController.cs`）の対応表 `Services/ExtrasApiTable.cs` に `new JobApi(JobRunner.Names)` を並べます。Extras.Server の `JobApi` が `JobRunRequest`（`Codeer.LowCode.Blazor.Extras.Jobs`）を受け、このフィールドへの書き込み権限を確かめて、`JobProcess` でジョブを別プロセスとして起動します（[ジョブ (バッチ)](Jobs.md)）。非同期では `JobRunResponse` の `JobId` を返し、状態の問い合わせに `JobStatusResponse` を返します。同時実行を許さないジョブが実行中なら起動せずに失敗を返します

ジョブ専用のコントローラで受ける場合は、従来どおり `JobTriggerField.EndPoint` にその URL を設定すればそちらに送ります（状態は `GET {EndPoint}/{jobId}`）。

## 関連

- [ジョブ (バッチ)](Jobs.md) — サーバー側の仕組み（JobProcess / JobMode）
- [バッチ処理（ジョブ）](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/user_code/batch_job.md) — ジョブの書き方・定期実行
