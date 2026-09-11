# サーバー API の権限チェック (フィールド起点の API)

Extras.Server が提供する API のうち、本体の通常経路 (一覧取得・保存) を通らないもの — メール送信、一斉送信、承認、AI チャット、AI 帳票解析 — は、
リクエストに **「どのモジュールのどのフィールドから呼ばれたか」** を載せ、サーバーは **そのフィールドが今のユーザーに見えるときだけ** 応じます。
送信インフラの呼び名、Agent 名、補足文書のフォルダ、補足指示といった設定値はリクエストではなくフィールドのデザインから取るので、クライアントが値を書き換えても効きません。

「誰がこの機能を使えるか」は、フィールドを置くモジュールの UserReadCondition と、PermissionField でそのフィールドを見せる相手で決めます。
画面で見えない人は API を直接叩いても使えません。

Codeer.LowCode.Blazor 1.3.33 / Codeer.LowCode.Blazor.Extras 0.12.0 / Codeer.LowCode.Blazor.Extras.Server 0.12.0 以降。

## 共通ルール

判定は本体の `ModuleDataIO.CheckUserReadAuthorization(moduleName, fieldName)` です。ログインユーザーの情報だけで決まる条件を見て、**レコード (行) は読みません**。

| 条件 | 検査 | 意味 |
|---|---|---|
| アプリのアクセス条件 (AppAccessConditions) | する | 停止したユーザー等はアプリに入れないので、どの API も使えない |
| フィールドを置いたモジュールの UserReadCondition | する | そのモジュールを開けない人は使えない |
| フィールドの読取権限 (PermissionField の読取条件のうち、ユーザーだけで偽と確定するもの) | する | PermissionField で隠された人は使えない |
| PermissionField の行に依存する読取条件 | しない | 行を読まないので判定できず、素通し (新規行の判定と同じ規則) |
| モジュールの UserWriteCondition / DataWriteCondition | しない | これらの API は書き込みではない。書き込みを伴う操作 (承認の申請書保存) は通常の保存経路で効く |
| フィールドを置いたモジュールの DataReadCondition (行) | しない (承認だけ例外) | 行を読まないので、未保存のレコードや DB に繋がっていないモジュールからも使える |

検査を通らないときは `LowCodeException` (HTTP エラー) になり、クライアントはエラー表示になります。
リクエストのモジュール名・フィールド名がデザインに無い、または期待した型のフィールドでない場合も同じです。

## フィールドごとのルール

| フィールド | API | リクエストが運ぶ場所 | 共通ルールに加えて見るもの | デザインから取る値 |
|---|---|---|---|---|
| MailField ([メール送信](Mail.md)) | `POST /api/mail` (送信) / `/api/mail/preview` | `SourceModule` / `FieldName` (`SourceId` は履歴の記録用) | なし。宛先・件名・本文・添付はクライアントが組む。差出人は常に送信インフラ設定のシステム送信者 | `MailInfraName` |
| BulkMailField ([メール送信](Mail.md)) | `/api/mail/bulk_search` (送信) / `/api/mail/bulk_preview` | `SourceModule` / `FieldName` / `Condition` (宛先リストの検索条件) | 宛先の解決は本体の一覧取得 (GetListAsync) なので、**宛先モジュール**の UserReadCondition・DataReadCondition・フィールド読取権限が効く。リンク越しの値 (`{Contact.Email.Value}` 等) もリンク先モジュールの権限で読む。読めない行は宛先にならず、読めない列は空 (アドレスなら「アドレスなし」で除外) | `MailInfraName` |
| ApprovalFlowField ([承認フロー](ApprovalFlow.md)) | `POST /api/approval` | `TargetModuleName` / `FieldName` / `FlowId` | (1) 既存フローへの操作 (承認・却下・差し戻し・取り下げ・回覧確認・再申請) では**申請書の行を読めること** (DataReadCondition) (2) 本人性: 承認・却下・差し戻しは今順番の来ている承認者本人、取り下げ・再申請は申請者本人、回覧確認は到達済みの回覧者本人 (3) フローは申請書モジュールと組で識別 (別モジュール名を添えた要求は「フローなし」) (4) 申請・再申請の保存データはそのモジュールのもの、再申請はそのフローの申請書レコードの更新に限る (5) 申請・再申請の申請書保存は通常の保存経路 (UserWrite / DataWrite / 編集ロック) | `WithdrawPolicy` 等の運用設定 |
| AIChatField ([AI チャット](AIChatField.md)) | `POST /api/ai_chat` | `ModuleName` / `FieldName` | ジョブの取得・中断はそのジョブの所有者 (ログイン ID) だけ。**AI が何を読めるか** (RawDataAccessAgent) は AI 用 DB ユーザーの権限で決まり、ここでは決めない | `Agent` / `DocumentFolder` |
| AITextAnalyzerField ([AI 帳票解析](AITextAnalyzerField.md)) | `POST /api/ai_text_analyze/file` / `/text` | クエリの `moduleName` / `fieldName` | なし。検査は AI 呼び出し (ファイルの読み取り) より前 | `Remarks` (補足指示) |

### 一斉送信で取得するデータ

一斉送信でサーバーが読むデータ (宛先行、アドレス・配信停止・表示名の列、テンプレート変数、リンク越しの値、多段リンク) はすべて本体の一覧取得を通るので、
送る人が画面で見られる範囲のデータしか宛先にも文面にも使われません。送信元 (BulkMailField を置いた配信レコード) の行だけは読みません (件名・本文テンプレートはクライアントが自モジュールの値から組む。MailField と同じ扱い)。

### スクリプトからの送信

フィールドを介さずに送る API はありません。スクリプトから送るときは画面に置いた MailField / BulkMailField の `Send()` を呼びます (0.5.0 の Mail スクリプトオブジェクトは 0.12.0 で削除)。
`Send()` は画面の送信ボタンと同じリクエストを送るので、同じ検査を受けます。

## ホスト側の結線

各 Controller は `DataService.ModuleDataIO` (ログインユーザーで作った ModuleDataIO) を Extras.Server の入口に渡します。アプリテンプレートには結線済みです。

| 入口 | 渡し方 |
|---|---|
| メール単発 | `MailDispatcher.SendAsync(request, moduleDataIO)` |
| メール一斉 / プレビュー | `new MailBulkSearch(dispatcher, moduleDataIO, designData).SendAsync(request)` / `new MailPreviewBuilder(dispatcher, moduleDataIO, designData)` |
| 承認 | `new ApprovalEngine(designData, moduleDataIO, db, addInternal, updateInternal).ExecuteAsync(command)` |
| AI チャット | `AIChatJobStore.StartAsync(owner, request, moduleDataIO)` |
| AI 帳票解析 | `AITextAnalyzeService.AnalyzeFileAsync / AnalyzeTextAsync(moduleDataIO, modules, moduleName, fieldName, …)` |

システムが送るメール (二要素認証のコードなど、画面のフィールドを介さないもの) は `MailDispatcher.SendAsync(mailInfraName, message)` を使います。こちらはフィールドの検査を持ちません (ホストのコードだけが呼べる)。

## 関連

- [メール送信](Mail.md) / [承認フロー](ApprovalFlow.md) / [AIChatField](AIChatField.md) / [AITextAnalyzerField](AITextAnalyzerField.md)
- [認証の全体像](Authentication.md) (ログインと CurrentUser の結びつけ)
- [マニュアル: 認証 / 認可の概要](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/authorization/authorization.md)
