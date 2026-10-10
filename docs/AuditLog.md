# 監査ログ

「いつ・誰が・どこから・何に・何をして・どうなったか」をサーバーが記録します。フィールドもデザインも要らず、
ホストの appsettings だけで有効化します。上場企業の内部統制 (J-SOX の IT 全般統制) や ISMS の監査証跡として求められる要件を満たす形で設計しています
([上場企業の監査基準への対応](#上場企業の監査基準への対応))。

監査ログは全モジュール・全 API に効き、単体で成立します。編集履歴 (EditHistoryField) は値の変化を追いたいモジュールにだけ置く別の機能で、編集履歴は「業務データが誰によって何に変わったか」、監査ログは
「システムに対して誰が何をしたか (失敗・拒否も含む)」です。

Codeer.LowCode.Blazor.Extras.Server 0.17.0 以降。

- [概略](#概略) — 何が記録されるか、有効化、上場企業の監査基準への対応
- [詳細](#詳細) — レコード、分類と結果、監査の対象外、対象と件数、デザインの版、出力先、失敗時の扱い、保持と削除、改ざん対策、参照の仕方、セットアップ、ホストの結線、独自 API への追加

---

## 概略

### 何が記録されるか

WebAPI (コントローラのアクション) を単位に記録します。ログインの成功・失敗、レコードの参照・変更、ファイル出力・メール送信、
再索引のような管理操作、それらの失敗と権限による拒否 (401/403) が残ります。加えて、アプリの起動・停止がシステムのイベントとして残ります。

どの行にも、その操作が使った **デザインの版** (App.zip の SHA-256) が入ります。デザインは画面や権限の定義そのものなので、
「その操作の時点でどの定義が動いていたか」を行から引けるようにしています ([デザインの版](#デザインの版))。

状態を変える操作 (保存・取込・承認・出力・メール送信・管理操作) と認証は **二段** で記録します。操作の前に「誰が・どこから・どの API を呼んだか」(試行)、
操作の後に「対象と結果」です。試行が書けなければ操作を実行しないので、記録の無い操作は起きません。

操作が触ったレコードは、画面からの保存でも一括取込でもファイル出力でも、**行ごとの Id** で残します ([対象と件数](#対象と件数))。
記録するのは項目名・Id・件数・理由までで、**レコードの値は記録しません** (個人情報を監査ログに複製しない)。メールの宛先のアドレスも残しません。
ユーザーや権限の変更も「誰がどのユーザーの行を変えたか」までです。付けた権限の中身まで残すには、ユーザーモジュールに編集履歴 (EditHistoryField) を置いてください。

### 有効化

appsettings の `AuditLog` セクションに書きます。出力先 (`Database` / `File`) は DB とファイルのどちらか、または両方で、使うものだけ書きます。

```json
"AuditLog": {
  "Enabled": true,
  "FailureMode": "Strict",
  "Database": { "DataSourceName": "Audit", "Table": "audit_log" },
  "File": { "Directory": "/var/log/lowcode/audit" }
}
```

DB に書く場合はテーブルを先に作ります。デザイナの **Tools > 監査ログのセットアップ** (CLI は `audit-log-setup`) が、テーブル作成 DDL と閲覧用のモジュールを生成します
([セットアップ](#セットアップ-閲覧モジュールの生成))。列は固定で、`DatabaseAuditSink.CreateTableSql(DataSourceType, table)` が各 DB 向けの `create table` を、
`CreateIndexSql` が日時のインデックスを返します (SQL Server / PostgreSQL / MySQL / Oracle / SQLite)。

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
  "design_version" varchar(64),
  "targets" text,
  "detail" text
);
-- 閲覧の日時絞り込みと、DB 管理者が古い行を整理するとき用
create index "ix_audit_log_occurred_at" on "audit_log" ("occurred_at_utc");
```

### 上場企業の監査基準への対応

上場企業の内部統制報告 (J-SOX) で IT 全般統制として求められる証跡に対応しています。次の観点で仕様・設計・実装をレビュー済みです。

| 観点 | 監査ログが残すもの |
|---|---|
| アクセス管理 | ログインの成功・失敗・ログアウト、二要素認証のコード不一致、外部 IdP の成立と拒否、権限による拒否 (401/403 とデザインの権限条件による拒否。`result = Denied` で引ける)。ユーザーや権限の変更は「誰がどのユーザーの行を変えたか」(Id) |
| 変更管理 | 全行にその操作が使ったデザインの版 (App.zip の SHA-256)、インスタンスごとの版の切替 (`Design.Loaded`)、送った App.zip の保管 (送信履歴フォルダ) |
| 運用管理 | アプリの起動 (有効な監査設定つき)・停止、管理操作 (再索引) |
| 記録の完全性 | 状態を変える操作・外へ出す操作・認証は操作の前に試行の行を書き、書けなければ (Strict) 操作を実行しない。失敗と拒否は設定に関係なく常に記録 |
| 証跡の保護 | 追記専用は DB の権限分離で担保 (アプリの接続ユーザーは INSERT のみ)、アプリ経路からの変更はサーバーが拒否、ファイル出力を SIEM へ転送して外部に写しを持つ |
| 追跡性 | 触ったレコードは経路によらず行ごとの Id、`RequestId` でアプリのログと突き合わせ、ファイル取込は取り込んだファイルの SHA-256 |

これを満たす構成は次のとおりです (いずれも本書の各節に手順があります)。

- `FailureMode` は `Strict` (既定) のまま
- 監査ログの DB ユーザーは INSERT (と閲覧用の SELECT) だけにする (アプリは監査ログを消さない。古い行の整理は DB 管理者の運用)
- 閲覧モジュールの UserRead 条件で閲覧者を監査役に絞る
- 送信履歴フォルダを指定し、App.zip を監査ログを残す期間と同じだけ保管する
- AI チャットは「誰が・どの画面で・どの Agent に聞いたか」までが残り、Agent が読んだ行は残らない ([監査の対象外](#監査の対象外))。AI が読める範囲は AI 用 DB ユーザー (個人情報を除いたビュー) で固定し、利用の事実は送信の行で示す ([AIChatField](AIChatField.md) の「監査基準への対応」)

暗号学的な改ざん検知 (ハッシュチェーン) は J-SOX / ISMS では求められないため持ちません。PCI DSS のようにログの変更検知を明示的に要求する規格は対象外です。

---

## 詳細

### レコード

| 項目 | 内容 |
|---|---|
| `OccurredAtUtc` | サーバー時刻 (UTC)。試行の行は操作の前、結果の行は操作が終わった時刻 |
| `Category` | 分類 (下記) |
| `Action` | 操作の名前。WebAPI は "Controller.Action" (例 `ModuleData.Submit`、`Account.Login`) |
| `Result` | `Attempt` (操作前の試行) / `Success` / `Failure` / `Denied` (認証・権限で拒否された) / `Continued` (対象の続きの行) |
| `UserId` | 操作したユーザーの Id (ユーザーモジュールの行の Id)。未認証なら空。ログインでは **Cookie を発行した (成立した) ときだけ** そのユーザー。パスワードは合ったが二要素認証のコード待ち・認証アプリの登録待ちの行は HTTP としては成功なので `Success` だが `user_id` は空 (`Detail` の `TwoFactor=...` と `LoginName=...`)。ログインの成立は「`Success` で `user_id` がある行」で数える |
| `ClientIp` / `UserAgent` | 接続元。`ClientIp` は ASP.NET Core が見た接続元 (`RemoteIpAddress`)。Azure App Service は基盤が Forwarded Headers を解決するので利用者の IP が入るが、IIS (ARR) や nginx 等のリバースプロキシを自前で置いた構成では**プロキシの IP が入る**。その構成では `Program.cs` で `UseForwardedHeaders` (`KnownProxies` にプロキシを登録) を `UseAuditLog` より前に足す。テンプレートには入れていない (プロキシの無い構成で有効にすると `X-Forwarded-For` の偽装を信用してしまうため) |
| `RequestId` | ASP.NET Core の TraceIdentifier。アプリのログ (ILogger) と突き合わせる鍵 |
| `Host` | 発生したサーバー名 (複数インスタンス運用での発生元) |
| `DesignVersion` | その操作が使ったデザインの版 (App.zip の SHA-256。小文字の 16 進 64 桁)。試行の行と結果の行は同じ値 |
| `Targets` | 対象のレコードの並び。`Module` / `Id` / `Operation` (Read / Add / Update / Delete / Export / Import / BulkSubmit / Mail / BulkMail / MailTo / Preview / BulkPreview / Download:フィールド名 / Approval:操作 など)。1 レコードに 500 件まで。超えた分は続きの行 |
| `Detail` | 補足。件数 (`Add=3; Update=1; Delete=0`、`Rows=1234` など)、取り込んだファイルのハッシュ (`File=...`)、失敗の理由、試行したログイン名 (`LoginName=...`)、二要素認証の状態など |

### 分類と結果

| 分類 | 記録される操作 (テンプレートの結線) |
|---|---|
| `Authentication` | ログイン (ID/パスワード・外部 IdP への遷移と IdP からの戻り・モバイルのチケット交換)、ログアウト、認証アプリの解除。ログイン失敗と二要素認証のコード不一致は `Denied`。成立した行だけ `user_id` が入る (二要素待ち・IdP への遷移は `Success` でも空) |
| `DataRead` | 一覧・詳細の取得 (誰が・どのモジュールを・何件。`AddRead(..., recordIds: true)` なら返した行ごとに `Read` と Id)、メールのプレビュー (一斉送信のプレビューは描いた宛先の行ごとに `Read`。送らずに宛先の値を見る操作なので参照として残す)、AI チャットの送信 (`AIChat.Send`。対象は AIChatField のモジュールと `AIChat:フィールド名`、`Detail` に `Agent=Agent 名`。発言と Agent が読んだ行は残さない) |
| `DataWrite` | 保存・一括取込・スクリプトの一括保存 (行ごとに Add / Update / Delete と件数)、アップロード、承認フローの操作 |
| `Export` | 一括ファイル出力 (出した行ごとに Export と件数)、Excel → PDF、添付ファイルのダウンロード (レコードとフィールド名。動画の再生も同じ API なので、再生欄を開いた・シークした要求ごとに 1 行残り、要求された範囲が Detail の `Range=` に入る)、メール送信・一斉送信 (送信元のレコード・件数・一斉送信は宛先の行)。画面に表示するだけの参照 (DataRead) と違い、ファイルとして持ち出す操作は既定で記録される |
| `Admin` | 意味検索の再索引 |
| `System` | アプリの起動 (有効な設定を Detail に残す)・停止、デザインの版の切替 (`Design.Loaded`)、初期管理者の作成 (`Account.InitialUserCreated`。ユーザーが 0 件のときテンプレートが作る admin。作った行の Id が対象) |
| `Other` | `[Audit]` を付けていない API (設計の取得、リソース、TOTP 状態、AI チャットのポーリング・中断など)。失敗と拒否だけが残る |

外部 IdP (Entra / Google / Cognito / OIDC) のログインは、IdP からの戻りを `Account.ExternalLoginCallback` として記録します。成立なら `Success` と解決したユーザー、
未登録のユーザー・クレームの不備・IdP 側の失敗は `Denied` で、`Detail` に `Provider=...; Error=...; LoginName=...` が入ります。
この戻りはコントローラのアクションではないので、外部ログインの部品 (`AddExternalLogins`) が直接書きます。ホストの追加の結線は要りません。
ネイティブアプリの流れは、チケットを Cookie に交換する `Account.LoginTicket` が成立の記録です。

### 監査の対象外

次の操作は監査ログに残りません。上場企業の内部統制のように参照の証跡まで求められる環境では、これらを使わない構成にしてください。

- **AI チャットの `RawDataAccessAgent` が読んだ行** (`execute_sql` / `search_records` / `{embed:…}`)。AI が組んだ SQL を AI 用 DB ユーザーの接続で直接実行するので、
  ログインユーザーごとの行制限 (UserRead / DataRead 条件) が効かず、読んだ行をモジュール・Id で記録することもできません。実行は送信とは別のジョブで行われます。
  監査ログに残るのは送信 (`AIChat.Send`。`DataRead`) の「誰が・どの画面の AIChatField を・どの Agent で使ったか」までで、発言の内容と読んだ行は残りません。
  実行した SQL は `RawDataAccessAgent` に `ILoggerFactory` を渡したときにアプリの実行ログ (ILogger) へ出ますが、
  これは調査用のログで監査ログではありません ([AIChatField](AIChatField.md) の「RawDataAccessAgent と DB の権限」)。
  監査が要る環境では、AI 用 DB ユーザーに個人情報を除いたビューだけを許可して「行単位の説明責任があるデータに AI が届かない」構成にし、
  範囲 (DB ユーザーの権限) と利用の事実 (送信の行) で説明します ([AIChatField](AIChatField.md) の「監査基準への対応」)。
  ユーザーごとに見える行が違うデータは DB ユーザーでは表現できないので、AI の範囲に入れません
- **意味検索の再索引 (`SemanticSearch.Start`) が書き直した行**。管理操作として `Admin` の行は残りますが、ジョブが書き直す行 (索引用の書き込み専用列だけ) は記録しません

### 二段の記録 (試行と結果)

状態を変える操作 (`DataWrite` / `Admin`)・外へ出す操作 (`Export`)・認証 (`Authentication`) では、操作の前に `Result = Attempt` の行を書きます。内容は日時・分類・API 名・ユーザー・接続元・RequestId で、対象や結果はまだ入っていません。
操作の後に通常の行 (Success / Failure / Denied、対象と詳細付き) を書き、2 行は `RequestId` で結びます。

- 前段が書けなければ (Strict) 操作を実行しません。この分類の操作に「記録の無い操作」は起きません
- 後段が書けなかったときは前段だけが残ります。「試行はあったが結果が無い」行は、業務データで結果を確かめる手掛かりです (内容は ILogger の Critical にも残ります)
- 操作がロールバックしても前段は残り、後段が `Failure` になります。監査ログは業務のトランザクションと別なので、失敗の記録がロールバックに巻き込まれません
- どの分類が二段かはコードで固定です (`AuditLogger.HasAttempt`)。参照 (`DataRead`) は行数が多いので結果の行だけです
- 業務の DB と監査の DB は別トランザクションです (別 DB・ファイルにも書くため)。「両方成功か両方無し」ではなく「試行は必ず残る」で保証します

結果の決まり方:

- アクションが例外を投げた → `Failure` (Detail に例外メッセージ)
- 本体の権限チェックで拒否された (`LowCodeAccessDeniedException`。アプリアクセス条件・モジュールの UserRead / UserWrite 条件・行の条件・フィールドの権限・デザインが許していない追加 / 更新 / 削除) → `Denied` (Detail にその文言。保存では対象の行も残る)
- HTTP 401 / 403 → `Denied` (認可ミドルウェアの拒否も含む)
- その他の 4xx / 5xx → `Failure`
- 200 でも業務として失敗したもの (保存結果の `ExceptionMessage`、承認の `ErrorMessage`、ログインのコード不一致) はコントローラが `Failure` / `Denied` に上書きする
- ファイルを読みながら返す応答 (添付のダウンロード・動画の再生) は、送り始めた時点で結果が決まる (ストレージから開けなければ `Failure`。送っている途中で切れても `Success` のまま)

何を記録するかは appsettings ではなく **ホストのコード** (コントローラの `[Audit]`) で決まります。分類を宣言したアクションは成功も失敗も記録し、
宣言の無いアクション (`Other`) は失敗と拒否だけが残ります。参照 (`DataRead`) も常に残り、既定は「誰が・どのモジュールを・何件読んだか」(`Targets` にモジュール名、
`Detail` に `Rows=`) です。返した行の Id まで残す (閲覧の証跡) には、`ModuleDataController` の一覧取得で `AddRead(..., recordIds: true)` にします
(行数ぶん大きくなるのでテンプレートの既定は false)。

### 対象と件数

操作が触ったレコードは、経路によらず `Targets` に行ごとの Id で残り、件数が `Detail` の先頭に入ります。

| 操作 | `Targets` | `Detail` |
|---|---|---|
| 一覧・詳細の参照 | モジュール名の `Read` (`recordIds: true` なら行ごとの `Read` と Id) | `Rows=50` |
| 保存 (画面)・一括取込・スクリプトの一括保存 | 行ごとの `Add` / `Update` / `Delete` と Id。条件での削除 (一覧の洗い替え) は `SearchDelete` でモジュール名だけ | `Add=3; Update=1; Delete=0` |
| 一括取込 (ファイル) | 上に加えて、モジュール名の `Import` | 件数に加えて `File=取り込んだファイルの SHA-256` |
| 一括ファイル出力 | 出した行ごとの `Export` と Id | `Rows=1234` |
| メール送信 | 送信元のレコード (`Mail`) | `Total=1; Success=1; Failed=0` |
| 一斉送信 | 送信元のレコード (`BulkMail`) と、宛先になった行 (`MailTo`) | `Total=120; Success=118; Failed=2` |
| メールのプレビュー | 送信元のレコード (`Preview` / `BulkPreview`)。一斉送信のプレビューは描いた宛先の行 (`Read`。配信停止などで除外した行も描くので含む) | `Rows=120` (一斉送信のプレビュー) |

- **どの保存も同じ形で残ります**: 保存の対象は、保存の合流点に置くインターセプタ (`AuditIOInterceptor`) が記録します。画面の保存もファイル取込もスクリプトの一括保存もここを通ります
- **失敗した保存にも対象が残ります**: 権限で弾かれた変更も「誰がどのレコードを変えようとしたか」が分かります (ロールバックした新規行は Id が無いので、モジュール名と件数です)
- **メールの宛先はアドレスではなく行の Id で残ります**: 一斉送信の宛先は検索で引いたレコードなので、Id で「誰に送ったか」を追えます。自由入力の宛先 (単発のメール) は件数だけです。
  宛先ごとの記録が要る場合は、メールの送信履歴を使ってください

#### 対象が多いとき (続きの行)

1 レコードに入れる対象は 500 件までです。超えた分は切り捨てず、同じ `RequestId` の **続きの行** (`Result = Continued`) に分けて書きます。
続きの行は日時・分類・API 名・ユーザーなどが結果の行と同じで、`Targets` だけが続きです (`Detail` は空)。

- 操作の件数を数えるときは `Success` / `Failure` / `Denied` の行だけを数えます (`Attempt` と `Continued` は結果ではありません)
- あるレコードを触った操作を探すときは、`Continued` の行も対象に含めます (下の「参照の仕方」)
- 1 行の大きさが数十 KB に収まるので、DB の 1 回の送信の上限やログ収集基盤の 1 件の上限に当たらず、一覧画面も重くなりません

#### 一括 INSERT で入った新規行

本体は、100 行以上の純粋な追加 (テンプレートの `BulkAddThreshold = 100`) を一括 INSERT で処理します。この経路は採番された Id を返さないので、
その新規行は Id 無しで、モジュールごとに 1 件の `Add` と件数 (`Add=620`) だけが残ります。監査ログのために一括 INSERT を止めることはしません (大量の取込が遅くなるため)。

- 更新と削除は一括 INSERT の対象ではないので、必ず Id が残ります。Id が残らないのは「自動採番の新規行を 100 行以上まとめて入れたとき」だけです
- 100 行未満の取込、追加と更新が混ざった取込、編集履歴 (EditHistoryField) を持つモジュールへの取込は、新規行にも採番 Id が残ります
- Id が無い行は、バッチ単位で追います。ファイル取込なら `Detail` の `File=...` が取り込んだファイルの SHA-256 です。**取込ファイルを保管しておけば**、
  「誰が・いつ・どのファイルを・何件取り込んだか」を証明でき、行の中身はファイルが証拠になります (ハッシュの確かめ方は「デザインの版」の節と同じです)

### デザインの版

デザイン (App.zip) は画面・権限・スクリプトの定義で、プログラムと同じ扱いです。監査ログは「どの版で動いていたか」を次の形で残します。

- **各行の `DesignVersion`**: その操作が使った App.zip の SHA-256 です。サーバーが複数インスタンスでも、行を見ればその行を書いたインスタンスの版が分かります
- **リクエストの間は版が変わりません**: リクエストを受け付けた時点の版を最後まで使います。処理の途中で App.zip が差し替わっても、そのリクエストは元の版で動き、
  試行の行にも結果の行にも元の版が残ります (結果の行が書けなかった場合も、試行の行にある版が実際に使った版です)
- **版の切替 `Design.Loaded`**: インスタンスが新しい版を読み込むと、System の `Design.Loaded` を 1 行書きます。`DesignVersion` が新しい版、`Detail` が `Previous=前の版` です。
  インスタンスごとに書くので、`Host` で並べれば「どのインスタンスがいつ替わったか」「替わっていないインスタンスが無いか」が分かります
- 起動の記録 (`Application.Start`) にも、起動時に読み込んだ版が入ります

監査ログが残すのは版 (ハッシュ) だけです。App.zip そのものの保管と、誰がいつ送ったかの記録は、システムの外で運用します (プログラムのデプロイと同じ考え方です)。

#### 運用: 送った App.zip を保管する

デザイナのデプロイ設定に **送信履歴フォルダ** (`HistoryDirectory`) を指定してください。送信のたびに、送る App.zip をそのフォルダへ保存してから送ります。

- ファイル名は `版の先頭 16 桁_UTC 日時_マシン名.zip` (例 `3f9a1c0b7d2e4a55_20261001T021530Z_DEV-PC01.zip`)
- 保存できなければ送信しません (履歴に無い版が稼働することはありません)
- 本番・検証など、記録を残したい送信先にだけ指定します (ローカル向けは空のままで構いません)。複数の PC から送るなら共有フォルダを指定します
- コマンドライン (`deploy`) から送った場合も同じように保存され、結果の JSON の `designVersion` に版が入ります。CI から送るなら、この値を CI の記録に残してください
- 保管する期間は、監査ログを残す期間 (社内規程) に合わせてください

設定は `designer.settings.Development.json` の `DeployInfo` です (デザイナのデプロイ設定の追加ダイアログでも入力できます)。

```json
"DeployInfo": {
  "production": {
    "DeployMethod": "FTPS",
    "FTPSEndPoint": "ftps://example.ftp.azurewebsites.windows.net/site/wwwroot/Designs",
    "UserName": "...",
    "Password": "...",
    "HistoryDirectory": "\\\\fileserver\\release\\app-design-history"
  }
}
```

#### 運用: 監査ログの行から、その時のデザインを特定する

1. 調べたい行の `DesignVersion` (`design_version` 列) を見る
2. 送信履歴フォルダで、ファイル名がその値の先頭 16 桁で始まる zip を探す
3. その zip の SHA-256 が `DesignVersion` と一致することを確かめる (大文字・小文字の違いは無視します)

```powershell
(Get-FileHash .\3f9a1c0b7d2e4a55_20261001T021530Z_DEV-PC01.zip -Algorithm SHA256).Hash
```

一致すれば、その zip が操作の時点で動いていたデザインそのものです。履歴フォルダの zip を後から差し替えても、監査ログ側の値と食い違うので分かります。

#### 運用: 誰が送ったか

システムは送信者を記録しません。デザイナは送信先へファイルを直接置く (ファイルコピー / FTPS) ので、サーバーからは誰が送ったかを確かめようがなく、
自己申告の名前を残しても証拠にならないためです。送信者は、プログラムのリリースと同じく運用の側で担保してください。

- 送信先に書ける人を絞る (本番の資格情報を配らない・個人ごとの資格情報にする)
- 変更の記録 (変更依頼・リリースの記録) に版 (SHA-256) を書く。デザイナは送信の完了時に版を表示します。履歴のファイル名にあるマシン名と日時は、調べるときの手掛かりです
- 個人まで厳密に残すなら、CI からコマンドラインで送る (実行者と承認者が CI に残ります)
- デザインファイルを git で管理しているなら、コミットしてから送る (履歴の zip の中身とコミットの中身が同じになります)

デザイナを使わずに App.zip を置いた場合も、置いた zip の SHA-256 がそのまま版になります。

### 出力先

| 設定 | 実装 | 内容 |
|---|---|---|
| `AuditLog.Database` (`DataSourceName` / `Table`) | `DatabaseAuditSink` | テーブルへ 1 行ずつ INSERT。書き込みは操作のトランザクションとは別の接続で行う (操作が失敗しても記録は残る) |
| `AuditLog.File` (`Directory`) | `FileAuditSink` | JSON Lines。`audit-{ホスト名}-{yyyyMMdd}.jsonl` に追記。SIEM やログ収集への転送元 |

両方書くと二重に残ります。DB を主、ファイルを外部転送用の写しにする構成を想定しています。
独自の出力先 (SIEM 直送など) は `IAuditSink` を実装してテンプレートの `AuditSinkTable` に足します。

### 失敗時の扱い (FailureMode)

| 値 | 動き |
|---|---|
| `Strict` (既定) | 1 つでも出力先に書けなければその操作を失敗にする (レスポンスは 500)。起動時に書けなければアプリは起動しない。監査ログの障害でアプリを止める運用 |
| `BestEffort` | 書けなくてもアプリのログ (ILogger の Critical) に残して操作は続ける |

後段の書き込みはレスポンスの先頭が出る前に行います。保存のように操作自体は既にコミットされていることがあり、その場合は前段の行だけが残ります
(結果は ILogger の Critical に内容ごと残ります。書けなかったレコードを、ファイル出力と同じ JSON 1 行で出します)。

### 保持と削除

アプリは監査ログを消しません (保持期限の掃除のような機能は持ちません)。何年残すか・古い行をどう整理するかは法令と社内規程で決まる運用で、
DB 管理者が `occurred_at_utc` (セットアップがインデックスを張る列) で行います。ファイル出力は日付ごとのファイルなので、古いファイルを移す・消すだけです。

### 改ざん対策

追記専用であることは **DB の権限** で担保します。アプリ側の防御は補助です。

- DB のユーザーを分け、監査ログのデータソースは UPDATE / DELETE のできないユーザーで繋いでください。必要な権限は INSERT と、閲覧用のモジュールを作るなら SELECT です。
  この構成なら、デザイン (スクリプトや ExecuteSqlField の生 SQL) を含むアプリのどの経路からも消したり書き換えたりできません
- アプリは監査ログを消す機能を持たないので、接続ユーザーに DELETE / UPDATE を与える理由がありません。与えなければ、デザインから監査ログを消す余地もありません
- 監査ログのテーブルに対してモジュールを作ることはできます (閲覧用)。そのモジュールからの追加・更新・削除は `AuditIOInterceptor` (IO インターセプタ) がサーバーで拒否します。
  これは設計ミスで監査ログを普通のデータとして編集してしまうのを防ぐもので、権限分離の代わりにはなりません
- ファイル出力を SIEM などアプリ管理者の手が届かない場所へ転送しておけば、DB 側を直接いじっても写しと食い違うので発覚します

暗号学的な改ざん検知 (ハッシュチェーン) は持ちません。J-SOX / ISMS の要求は「ログの保護」までで、上の権限分離と外部転送で足ります。
PCI DSS のようにログの変更検知が明示的に要る規格には別途の対応が必要です。

### 参照の仕方

専用の画面はありません。監査ログのテーブルに対する一覧モジュール ([セットアップ](#セットアップ-閲覧モジュールの生成) が生成します) を使い、UserRead 条件で監査役に絞ってください
(`targets` / `detail` は文字列なので、検索は LIKE 相当になります)。

あるレコードを触った操作を全部探すときは、`targets` をレコードの形で検索します。続きの行 (`Continued`) にも対象が入っているので、`result` では絞りません。

```sql
-- モジュール Order の Id 123 を触った操作 (参照・追加・更新・削除・出力・メールの宛先)
select * from audit_log where targets like '%{"Module":"Order","Id":"123",%' order by id
```

見つかった行が `Continued` なら、同じ `request_id` の結果の行 (`Success` / `Failure` / `Denied`) に、誰が・何の操作で・結果がどうだったかがあります。

#### 操作者を特定する

`user_id` はユーザーモジュールの行の Id です。名前やログイン名はユーザーモジュールの行から引きます。
退職者のユーザー行を物理削除すると、保持期間内のログの操作者がユーザーモジュールから引けなくなるので、ユーザーモジュールは論理削除にするか、
行を消さずに無効化 (ログインできなくする) してください。

ユーザー行が無くなっていても、ログイン成功の行 (`Authentication` / `Account.Login` または `Account.ExternalLoginCallback` の `Success`) には
その `user_id` と `detail` の `LoginName=...` が残っているので、保持期間内にログインしていれば監査ログの中だけで Id からログイン名を引けます。

```sql
-- user_id 'u-123' のログイン名
select distinct detail from audit_log
 where user_id = 'u-123' and category = 'Authentication' and result = 'Success' and detail like '%LoginName=%'
```

### セットアップ (閲覧モジュールの生成)

デザイナの **Tools > 監査ログのセットアップ**。モジュール名 (既定 AuditLog)・テーブル名 (既定 audit_log)・データソース・操作者リンクのユーザーモジュールと表示名フィールド・
ページリンクを追加するか、を聞いて次を生成する (冪等。既にあるものは触らない):

- 閲覧モジュール (固定列に対応するフィールド・一覧 / 詳細 / 検索レイアウト・作成 / 更新 / 削除できない設定と「誰も書けない」保護条件)。
  日時は UTC で入っているので表示はローカル時刻に直す (`SaveAsUtc`)。分類と結果は候補付きの選択。操作者はユーザーモジュールへのリンク
- PageFrame のページリンク「監査ログ」(新規作成なし・詳細遷移あり・Id の降順)
- テーブル作成 DDL (結果ダイアログでその場で実行できる)。`DatabaseAuditSink.CreateTableSql` と同じ列に、日時絞り込み (と DB 管理者の整理) 用の `occurred_at_utc` のインデックスを足したもの。
  テーブルが既にあれば出さない

テーブル名とデータソースは、ホストの appsettings の `AuditLog.Database` と同じにしてください。操作者の表示名のフィールドは、ユーザーモジュールに `Name` があればそれ、無ければログインアカウント契約の表示名の役割を使います (`--user-name-field` で指定可)。記録の有効化 (appsettings)・閲覧できる人の制限 (UserReadCondition)・
追記専用の担保 (DB ユーザーの権限) は生成しない (結果ダイアログに手順が出る)。生成したモジュールからの追加・更新・削除は `AuditIOInterceptor` が拒否する。

headless CLI:

```
<designer.exe> audit-log-setup "<projectDir>" [--module-name AuditLog] [--table audit_log] [--data-source <name>] [--user-module AppUser] [--user-name-field <表示名フィールド>] [--no-pageframe] [--ddl-out "<path.sql>"]
```

Claude Code でデザインを編集している場合 (デザイナの Tools > Claude Code Workspace)、この手順は `ClaudeCodeForDesigner/_specs/AuditLog.md` として展開されます。
「監査ログを入れて」と頼めば、Claude Code がセットアップの実行・DDL の適用・閲覧条件の設定まで行い、appsettings の有効化 (ホスト側) を案内します。

### ホストの結線

テンプレート (Cookie) には含まれています。既存のアプリに足す場合:

1. `SystemConfig` に `AuditLogSettings` を持ち、`Program.cs` で `AuditLog` セクションを読む (出力先の `Database` / `File` は入れ子)
2. `Services/AuditSinkTable.cs` (設定 → `IAuditSink` の並び) を置く
3. `builder.Services.AddAuditLog(SystemConfig.Instance.AuditLog, AuditSinkTable.Create(), designVersion);`
   `designVersion` はデザインの版を返す関数です。リクエストの中 (HttpContext あり) ではそのリクエストが使う版、外 (null) では今読み込んでいる版を返します。
   テンプレートは `Services/RequestDesign` (スコープ) がリクエストの最初にデザインを 1 つに固定し、コントローラは `DataService.Design` 経由でその版だけを使います
   (`DesignerService` から直接取ると、処理の途中で差し替わった版が混ざります)。渡さなければ版は記録されません
4. `app.UseRouting();` の直後に `app.UseAuditLog();` (認可の前。401/403 も記録するため。認証はミドルウェアが自分で解決するので認証ミドルウェアより前でよい)
5. `CustomizedModuleDataIO` のコンストラクタで `AddInterceptor(new AuditIOInterceptor(designData, SystemConfig.Instance.AuditLog.Database));`
   保存の対象の記録と、監査ログのテーブルの保護を行います。保存の最終結果を記録するので、**他のインターセプタ (編集履歴など) より先に登録します**
6. 各コントローラのアクションに `[Audit(AuditCategory.Xxx)]` を付け、対象や業務上の失敗は `AuditContext` (スコープ) で足す
   (保存の対象は 5 のインターセプタが記録するので、保存のアクションで対象を足す必要はありません)

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

コントローラから離れた処理 (公開メソッドの引数に持ち回れないところ) は、今のリクエストのレコードを `AuditContext.Current` から取れます
(リクエストの外や監査ログが無効のときは null)。

```csharp
var audit = AuditContext.Current;
audit?.AddTarget("Order", id, "Export");
audit?.AddCount("Rows", rows.Count);
```

リクエストの外 (バックグラウンドのジョブ) では `AuditLogger` (シングルトン) に `AuditEvent` を組み立てて `WriteAsync` します。

| 部品 | 役割 |
|---|---|
| `AuditAttribute` | アクションの分類の宣言。クラスに付けると既定になり、アクション側が優先 |
| `AuditContext` | 今のリクエストのレコード。`AddTarget` / `AddCount` (件数) / `AddNote` (補足) / `AddRead` (参照。recordIds で行の Id) / `Fail` / `Deny`、`Event.UserId` (ログイン時)。`AuditContext.Current` で今のリクエストのものを取れる |
| `AuditLogger` | 保存する部品。記録の規則 (分類あり = 全部 / 無し = 失敗と拒否だけ / 試行は分類で固定)・全出力先への書き込み・Strict/BestEffort・デザインの版の記入と切替の記録・対象が多いレコードの分割 |
| `AuditLogMiddleware` | WebAPI を記録 (前段の試行と後段の結果)。`UseAuditLog()` |
| `AuditLogHostedService` | 起動・停止の記録。`AddAuditLog()` が登録する |
| `IAuditSink` / `DatabaseAuditSink` / `FileAuditSink` | 出力先 |
| `AuditIOInterceptor` | 保存の合流点に置く IO インターセプタ。保存の対象 (行ごとの Add / Update / Delete と Id・件数) を記録し、監査ログのテーブルをモジュールの保存から守る |
