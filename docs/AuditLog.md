# 監査ログ

「いつ・誰が・どこから・何に・何をして・どうなったか」をサーバーが記録します。フィールドもデザインも要らず、
ホストの appsettings だけで有効化します。上場企業の内部統制 (J-SOX の IT 全般統制) や ISMS の監査証跡として説明できる形を目標にしています。

監査ログは全モジュール・全 API に効き、単体で成立します。編集履歴 (EditHistoryField) は値の変化を追いたいモジュールにだけ置く別の機能で、編集履歴は「業務データが誰によって何に変わったか」、監査ログは
「システムに対して誰が何をしたか (失敗・拒否も含む)」です。

Codeer.LowCode.Blazor.Extras.Server 0.17.0 以降。

- [概略](#概略) — 何が記録されるか、有効化
- [詳細](#詳細) — レコード、分類と結果、出力先、失敗時の扱い、保持と削除、改ざん対策、参照の仕方、ホストの結線、独自 API への追加

---

## 概略

### 何が記録されるか

WebAPI (コントローラのアクション) を単位に記録します。ログインの成功・失敗、レコードの参照・変更、ファイル出力・メール送信、
再索引のような管理操作、それらの失敗と権限による拒否 (401/403) が残ります。加えて、アプリの起動・停止と監査ログ自身の掃除がシステムのイベントとして残ります。

状態を変える操作 (保存・取込・承認・出力・メール送信・管理操作) と認証は **二段** で記録します。操作の前に「誰が・どこから・どの API を呼んだか」(試行)、
操作の後に「対象と結果」です。試行が書けなければ操作を実行しないので、記録の無い操作は起きません。

記録するのは項目名・Id・件数・理由までで、**レコードの値は記録しません** (個人情報を監査ログに複製しない)。
ユーザーや権限の変更も「誰がどのユーザーの行を変えたか」までです。付けた権限の中身まで残すには、ユーザーモジュールに編集履歴 (EditHistoryField) を置いてください。

### 有効化

appsettings に 3 つのセクションを書きます。出力先は DB とファイルのどちらか、または両方です。

```json
"AuditLog": {
  "Enabled": true,
  "FailureMode": "Strict",
  "RetentionDays": 1825,
  "Categories": [ "Authentication", "DataWrite", "Export", "Admin", "System" ],
  "AttemptCategories": [ "Authentication", "DataWrite", "Export", "Admin" ]
},
"AuditLogDatabase": {
  "DataSourceName": "Audit",
  "Table": "audit_log"
},
"AuditLogFile": {
  "Directory": "/var/log/lowcode/audit"
}
```

DB に書く場合はテーブルを先に作ります。列は固定で、`DatabaseAuditSink.CreateTableSql(DataSourceType, table)` が各 DB 向けの `create table` を返します
(SQL Server / PostgreSQL / MySQL / Oracle / SQLite)。

```sql
-- PostgreSQL の例
create table "audit_log" (
  "id" bigserial primary key,
  "occurred_at_utc" timestamp not null,
  "category" varchar(32) not null,
  "action" varchar(128) not null,
  "result" varchar(16) not null,
  "user_id" varchar(256),
  "client_ip" varchar(64),
  "user_agent" varchar(512),
  "request_id" varchar(64),
  "host" varchar(128),
  "targets" text,
  "detail" text
)
```

---

## 詳細

### レコード

| 項目 | 内容 |
|---|---|
| `OccurredAtUtc` | サーバー時刻 (UTC)。試行の行は操作の前、結果の行は操作が終わった時刻 |
| `Category` | 分類 (下記) |
| `Action` | 操作の名前。WebAPI は "Controller.Action" (例 `ModuleData.Submit`、`Account.Login`) |
| `Result` | `Attempt` (操作前の試行) / `Success` / `Failure` / `Denied` |
| `UserId` | 操作したユーザーの Id (ユーザーモジュールの行の Id)。未認証なら空。ログイン成功時は成功したユーザー |
| `ClientIp` / `UserAgent` | 接続元。リバースプロキシ越しの IP は ASP.NET Core の Forwarded Headers ミドルウェアで解決したものが入る |
| `RequestId` | ASP.NET Core の TraceIdentifier。アプリのログ (ILogger) と突き合わせる鍵 |
| `Host` | 発生したサーバー名 (複数インスタンス運用での発生元) |
| `Targets` | 対象のレコードの並び。`Module` / `Id` / `Operation` (Read / Add / Update / Delete / Export / Import / BulkSubmit / Download:フィールド名 / Approval:操作 など) |
| `Detail` | 補足。失敗の理由、試行したログイン名 (`LoginName=...`)、二要素認証の状態、掃除の件数など |

### 分類と結果

| 分類 | 記録される操作 (テンプレートの結線) |
|---|---|
| `Authentication` | ログイン (ID/パスワード・外部 IdP への遷移と IdP からの戻り・モバイルのチケット交換)、ログアウト、認証アプリの解除。ログイン失敗と二要素認証のコード不一致は `Denied` |
| `DataRead` | 一覧・詳細の取得 (返した行ごとに `Read`)、添付ファイルのダウンロード、AI チャット (ユーザーの権限で DB を読む) |
| `DataWrite` | 保存 (行ごとに Add / Update / Delete)、一括取込、アップロード、承認フローの操作 |
| `Export` | 一括ファイル出力、Excel → PDF、メール送信・一斉送信 |
| `Admin` | 意味検索の再索引 |
| `System` | アプリの起動 (有効な設定を Detail に残す)・停止、監査ログの掃除 (消した件数) |
| `Other` | `[Audit]` を付けていない API (設計の取得、リソース、TOTP 状態など) |

外部 IdP (Entra / Google / Cognito / OIDC) のログインは、IdP からの戻りを `Account.ExternalLoginCallback` として記録します。成立なら `Success` と解決したユーザー、
未登録のユーザー・クレームの不備・IdP 側の失敗は `Denied` で、`Detail` に `Provider=...; Error=...; LoginName=...` が入ります。
この戻りはコントローラのアクションではないので、外部ログインの部品 (`AddExternalLogins`) が直接書きます。ホストの追加の結線は要りません。
ネイティブアプリの流れは、チケットを Cookie に交換する `Account.LoginTicket` が成立の記録です。

### 二段の記録 (試行と結果)

`AttemptCategories` の分類では、操作の前に `Result = Attempt` の行を書きます。内容は日時・分類・API 名・ユーザー・接続元・RequestId で、対象や結果はまだ入っていません。
操作の後に通常の行 (Success / Failure / Denied、対象と詳細付き) を書き、2 行は `RequestId` で結びます。

- 前段が書けなければ (Strict) 操作を実行しません。この分類の操作に「記録の無い操作」は起きません
- 後段が書けなかったときは前段だけが残ります。「試行はあったが結果が無い」行は、業務データで結果を確かめる手掛かりです (内容は ILogger の Critical にも残ります)
- 操作がロールバックしても前段は残り、後段が `Failure` になります。監査ログは業務のトランザクションと別なので、失敗の記録がロールバックに巻き込まれません
- 既定 (省略時) は Authentication / DataWrite / Export / Admin。`DataRead` は行数が多いので前段を付けません。書いた場合はその分類だけになります (appsettings の配列は既定に継ぎ足されず置き換わる。空配列は省略と同じ)
- 業務の DB と監査の DB は別トランザクションです (別 DB・ファイルにも書くため)。「両方成功か両方無し」ではなく「試行は必ず残る」で保証します

結果の決まり方:

- アクションが例外を投げた → `Failure` (Detail に例外メッセージ)
- HTTP 401 / 403 → `Denied` (認可ミドルウェアの拒否も含む)
- その他の 4xx / 5xx → `Failure`
- 200 でも業務として失敗したもの (保存結果の `ExceptionMessage`、承認の `ErrorMessage`、ログインのコード不一致) はコントローラが `Failure` / `Denied` に上書きする

`Categories` は **成功した操作の絞り込み** です。失敗と拒否は分類に関係なく常に記録されます。空なら全部記録します。
`DataRead` は行ごとに残るので量が多く、必要な場合だけ入れてください。

### 出力先

| 設定 | 実装 | 内容 |
|---|---|---|
| `AuditLogDatabase` (`DataSourceName` / `Table`) | `DatabaseAuditSink` | テーブルへ 1 行ずつ INSERT。書き込みは操作のトランザクションとは別の接続で行う (操作が失敗しても記録は残る) |
| `AuditLogFile` (`Directory`) | `FileAuditSink` | JSON Lines。`audit-{ホスト名}-{yyyyMMdd}.jsonl` に追記。SIEM やログ収集への転送元 |

両方書くと二重に残ります。DB を主、ファイルを外部転送用の写しにする構成を想定しています。
独自の出力先 (SIEM 直送など) は `IAuditSink` を実装してテンプレートの `AuditSinkTable` に足します。

### 失敗時の扱い (FailureMode)

| 値 | 動き |
|---|---|
| `Strict` (既定) | 1 つでも出力先に書けなければその操作を失敗にする (レスポンスは 500)。起動時に書けなければアプリは起動しない。監査ログの障害でアプリを止める運用 |
| `BestEffort` | 書けなくてもアプリのログ (ILogger の Critical) に残して操作は続ける |

後段の書き込みはレスポンスの先頭が出る前に行います。保存のように操作自体は既にコミットされていることがあり、その場合は前段の行だけが残ります
(結果は ILogger の Critical に内容ごと残ります)。

### 保持と削除

`RetentionDays` より古いレコードを 1 日 1 回 (起動直後と 24 時間ごと) 消し、消した件数を `System` の `AuditLog.Purge` として記録します。0 なら消しません。
ファイルはファイル名の日付で判定し、書きかけの当日分は残ります。
DB の掃除は監査ログのデータソースで DELETE を実行します。DELETE 権限の無い DB ユーザーで繋ぐ場合は `RetentionDays` を 0 にして、掃除は DB 管理者のジョブで行ってください
(0 にしないと掃除が毎日失敗し、アプリのログにエラーが出ます)。

### 改ざん対策

アプリの経路からは追記しかできません。

- 監査ログのテーブルに対してモジュールを作ることはできます (閲覧用)。そのモジュールからの追加・更新・削除は `AuditTableGuard` (IO インターセプタ) がサーバーで拒否します
- DB のユーザーを分け、監査ログのデータソースは UPDATE / DELETE のできないユーザーで繋ぐ運用を推奨します。必要な権限は INSERT と、閲覧用のモジュールを作るなら SELECT です
  (保持期限の掃除をアプリに任せる場合だけ DELETE も要ります。上の「保持と削除」)
- ファイル出力を SIEM などアプリ管理者の手が届かない場所へ転送しておけば、DB 側を直接いじっても写しと食い違うので発覚します

暗号学的な改ざん検知 (ハッシュチェーン) は持ちません。J-SOX / ISMS の要求は「ログの保護」までで、上の権限分離と外部転送で足ります。
PCI DSS のようにログの変更検知が明示的に要る規格には別途の対応が必要です。

### 参照の仕方

専用の画面はありません。監査ログのテーブルに対して通常のデザインで一覧モジュールを作り、UserRead 条件で監査役に絞ってください
(`targets` / `detail` は文字列なので、検索は LIKE 相当になります)。

### ホストの結線

テンプレート (Cookie) には含まれています。既存のアプリに足す場合:

1. `SystemConfig` に `AuditLogSettings` / `AuditLogDatabaseSettings` / `AuditLogFileSettings` を持ち、`Program.cs` で 3 セクションを束ねる
2. `Services/AuditSinkTable.cs` (設定 → `IAuditSink` の並び) を置く
3. `builder.Services.AddAuditLog(SystemConfig.Instance.AuditLog, AuditSinkTable.Create());`
4. `app.UseRouting();` の直後に `app.UseAuditLog();` (認可の前。401/403 も記録するため。認証はミドルウェアが自分で解決するので認証ミドルウェアより前でよい)
5. `CustomizedModuleDataIO` のコンストラクタで `AddInterceptor(new AuditTableGuard(designData, SystemConfig.Instance.AuditLogDatabase));`
6. 各コントローラのアクションに `[Audit(AuditCategory.Xxx)]` を付け、対象や業務上の失敗は `AuditContext` (スコープ) で足す

### 独自 API への追加

Extras の外で WebAPI を足したときも、その入口で同じ部品を呼びます。

```csharp
[HttpPost("close"), Audit(AuditCategory.DataWrite)]
public async Task<IActionResult> CloseAsync(string id, [FromServices] AuditContext audit)
{
    audit.AddTarget("Order", id, "Close");
    var result = await ...;
    if (!result.Ok) audit.Fail(result.Error);
    return Ok(result);
}
```

リクエストの外 (バックグラウンドのジョブ) では `AuditLogger` (シングルトン) に `AuditEvent` を組み立てて `WriteAsync` します。

| 部品 | 役割 |
|---|---|
| `AuditAttribute` | アクションの分類の宣言。クラスに付けると既定になり、アクション側が優先 |
| `AuditContext` | 今のリクエストのレコード。`AddTarget` / `RecordSubmitAsync` (保存を包む) / `AddRead` / `Fail` / `Deny`、`Event.UserId` (ログイン時)。スクリプトの一括保存は `BulkFileTransfer.BulkSubmitAsync` に渡す |
| `AuditLogger` | 保存する部品。分類の絞り込み・全出力先への書き込み・Strict/BestEffort・掃除 |
| `AuditLogMiddleware` | WebAPI を記録 (前段の試行と後段の結果)。`UseAuditLog()` |
| `AuditLogHostedService` | 起動・停止の記録と 1 日 1 回の掃除。`AddAuditLog()` が登録する |
| `IAuditSink` / `DatabaseAuditSink` / `FileAuditSink` | 出力先 |
| `AuditTableGuard` | 監査ログのテーブルをモジュールの保存から守る IO インターセプタ |
