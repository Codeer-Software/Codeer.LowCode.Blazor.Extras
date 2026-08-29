# メール送信

ローコードアプリからメールを送るための機能群です。**フィールドを置くだけ**で送信ボタンになり、
宛先・件名・本文はレコードの値から組み立てられます。スクリプトから動的に送ることもできます。

- [概略](#概略) — 何ができるか、最短の使い方
- [詳細](#詳細) — 各フィールド・設定・サーバー側の仕組み

---

## 概略

### できること

| やりたいこと | 使うもの |
|---|---|
| レコードの内容で 1 通送る (受付通知・依頼メールなど) | **MailField** |
| 名簿 (リスト) の全員に一斉送信する (キャンペーン・お知らせ) | **BulkMailField** |
| 送信した記録を残す | 送信履歴モジュール (**メールのセットアップ**で生成) |

差出人は常に**送信インフラ設定のシステム送信者** (`SenderMailAddress` / `SenderDisplayName`) です。
本人のアカウント (担当者個人の Gmail / Microsoft 365 / SMTP) から送る用途は、トークンを各自の PC にだけ置く Windows アプリ [MailSender](MailSender.md) が担います (サーバーにはトークンを置かない)。

### 最短の使い方 (MailField)

1. モジュールの詳細レイアウトに **MailField** を置く
2. プロパティに宛先と文面を設定する
   - 宛先変数: `Email.Value` (自レコードのフィールド。リンク先なら `Customer.Email.Value`)
   - 件名: `注文確認: {OrderNo.Value}`
   - 本文: `{Customer.Name.Value} 様\nご注文を受け付けました。`
3. サーバーの `appsettings.json` に送信インフラの設定を書く (後述)

これで詳細画面に送信アイコンのボタンが現れ、押すと確認ダイアログ (件名・宛先数) の後にそのレコードの値でメールが送られます。
文字付きのボタンにしたい場合は ButtonField を置いてスクリプトから `フィールド名.Send()` を呼びます。
`{変数}` は自レコードの値で置き換えられます (数値・日付はフィールドの書式で整形)。

### 一斉送信 (BulkMailField)

「配信 (1 レコード)」と「配信対象の名簿 (リスト)」を持つモジュールに **BulkMailField** を置き、
宛先リストのフィールドを指定します。名簿の**全行** (画面のページングとは無関係) に送信され、
文面の `{変数}` は**宛先の行ごと**に解決されます。宛先アドレスはサーバー側で解決されるため
クライアントには渡りません。

### セットアップ (デザイナ)

**Tools > メールのセットアップ** で、送信履歴モジュール (＋送信明細) と
サーバー設定の案内をまとめて用意できます (使うものだけ選べます)。
コマンドラインからは `<designer.exe> mail-setup "<projectDir>"` で同じことができます。

---

## 詳細

### MailField (単発送信)

レイアウトに置くと送信ボタン、置かなくてもスクリプトの `Send()` から使えます。

各項目は **値** と **変数** のペアで指定します。**値が入っていれば値、空なら変数**を自レコードで解決します。

| ペア | 説明 |
|---|---|
| 宛先 / 宛先変数 | 固定アドレス (カンマ・セミコロン区切りで複数可) か、`Email.Value` のような変数 (リンクパス可) |
| Cc / Cc変数、Bcc / Bcc変数 | 宛先と同じ規則 |
| 件名 / 件名変数 | 件名テンプレート。どちらの経路でも `{変数}` が自レコードで解決される |
| 本文 / 本文変数 | 本文テンプレート (件名と同じ規則) |
| 返信先 / 返信先変数 | 返信先アドレス |

| プロパティ | 説明 |
|---|---|
| HTML本文 | 本文を HTML として送る |
| メールインフラ名 | どの送信インフラで送るかの呼び名。**通常は空**にしてサーバー設定の既定を使う |
| プレビューボタンを表示 | 送信アイコンの横にプレビュー (HTML ダウンロード) を出す (既定 ON) |

- 宛先 (値か変数) と、件名・本文のどちらかは必須 (デザインチェックが検証)
- 変数はデザインチェックで存在検証され、フィールドのリネームに追従します

#### スクリプト

```csharp
// デザインどおりに送る (ButtonField の OnClick 等から)
var result = ReceiptMail.Send();              // MailSendResult (IsSuccess / Failures)
if (!result.IsSuccess) Toaster.Error("送信失敗: " + result.Failures[0].Error);

// 動的に組み立てて送る (設定した値は変数より優先)
ReceiptMail.To = "sato@example.com;suzuki@example.com";
ReceiptMail.Subject = "月次レポート {Month.Value}";       // 値も {変数} が解決される
ReceiptMail.Body = "今月のレポートを添付します。";
ReceiptMail.AddAttachment("report.xlsx", excel);           // Excel オブジェクトを添付
ReceiptMail.AddTextAttachment("memo.txt", "テキスト添付");
ReceiptMail.Send();                                        // 添付は送信後にクリアされる
```

### BulkMailField (一斉送信)

| プロパティ | 説明 |
|---|---|
| 宛先リスト | 同一モジュール上の List / DetailList / TileList のフィールド名。その先のモジュールに**一斉送信の宛先契約**が必要 |
| 件名 / 件名変数、本文 / 本文変数 | MailField と同じ規則。`{変数}` は**宛先行**で解決される (`{Contact.Name.Value}` のようなリンクパス可) |
| HTML本文 / 返信先 / メールインフラ名 / プレビューボタンを表示 | MailField と同じ |

動作:

1. ボタン押下 → 対象件数の確認ダイアログ
2. サーバーが宛先リストの検索条件から宛先を解決して送信 (読み取り権限・行条件が効く)
3. 宛先契約の **配信停止 (OptOut)** が true の行と、アドレスが空の行はスキップ
4. 未保存の変更があるレコード・新規レコードからは送れない (保存済みの状態が送信対象)

典型構成 (キャンペーン + 名簿):

```
メール配信 (MailCampaign)             … 1 レコード = 1 配信
├── Title (Text)                     … 件名テンプレート
├── Body (Text 複数行)                … 本文テンプレート
├── Members (DetailList → 配信対象)   … 送る相手そのもの
└── BulkMail (BulkMailField)  宛先リスト = Members、件名変数 = Title.Value、本文変数 = Body.Value

配信対象 (CampaignMember)             … 一斉送信の宛先契約を置く (Email = Contact.Email.Value / OptOut = Contact.メール拒否.Value)
├── Campaign (Link → メール配信)
└── Contact (Link → 担当者)

担当者 (Contact)
├── Email (Text)
└── メール拒否 (Boolean)              … 配信停止。どの配信でも常に尊重される
```

スクリプトからは `Send()` (確認ダイアログなし・戻り値 `MailSendResult`) が使えます。

### プレビュー (送らずに「送るとこうなる」を確認する)

MailField / BulkMailField の送信ボタンの横に **プレビュー** ボタンがあり (プロパティ「プレビューボタンを表示」で非表示にできます)、
押すと**自己完結の HTML ファイル**がダウンロードされます。ブラウザで開くだけで、外部への通信はしません。
文面と宛先の解決は**送信と同じサーバー経路**なので、プレビューと実際の送信内容は一致します。

- **単発 (MailField)**: To / Cc / Bcc / 返信先 / 添付 / 件名 / 本文を 1 枚で表示
- **一斉 (BulkMailField)**: 左に宛先一覧、右に選んだ宛先で解決した件名・本文。**除外された行も理由付き** (配信停止 / アドレスなし) で一覧に残り、
  「除外のみ」の絞り込み・検索・キーボード移動 (↑↓、`n` = 次の除外) ができます。1 万件でも軽く動きます
- **変数ハイライト**: テンプレートの `{変数}` が入った箇所に色が付き、原文の変数名がツールチップで見えます (空になった変数は「(空)」と表示)
- HTML 本文は sandbox 内で描画。「ソースを表示」で切替
- 一覧に人の名前を出すには、宛先契約の任意役割 **DisplayName** (例: `Contact.Name.Value`) を設定します
- 一斉送信のプレビューは保存済みの内容 (テンプレート・名簿) で作られます。未保存の変更は反映されません
- スクリプトからは `フィールド名.Preview()` で同じ HTML をダウンロードできます

### 契約フィールド (どの値を使うかの宣言)

UI もデータも持たない「宣言用」のフィールドです。役割 → フィールド (変数) の対応を宣言します。
必須の役割は表示名に「(必須)」が付き、それ以外は空にすれば「使わない」宣言になります。

| 契約 | 置く先 | 役割 | 使う機能 |
|---|---|---|---|
| **一斉送信の宛先契約** (BulkMailRecipientContractField) | 一斉送信の宛先リストが指すモジュール | Email (必須) / OptOut / DisplayName | BulkMailField の宛先解決・プレビューの一覧表示 |
| **メール履歴契約** (MailHistoryContractField) | 送信履歴モジュール | SentAt (必須) / MailInfraName / Subject / TotalCount / SuccessCount / FailureDetails / SourceModule / SourceId / Details | 送信履歴の記録 |
| **メール送信明細契約** (MailHistoryDetailContractField) | 送信明細モジュール (履歴の Details 一覧の参照先) | History (必須) / To (必須) / Subject / Body / IsSuccess / Error | 宛先ごとの明細 (任意) |

役割の値はフィールドのリネームに追従し、不在ならデザインチェックがエラーにします。

### 送信履歴

サーバー設定 `Mail.HistoryModuleName` に履歴モジュール名を書くと、**単発・一斉すべての送信**が
そのモジュールに 1 送信 1 レコードで記録されます (失敗明細は JSON)。
書き込みは操作ユーザーの権限に依存しないサーバー内部経路で行われ、履歴の失敗が送信を止めることはありません。
履歴モジュールは**メールのセットアップ**で生成できます (一覧画面・ページリンク付き)。

**送信明細 (任意)**: 履歴モジュールに明細モジュールへの一覧を置き、履歴契約の「送信明細の一覧」に設定すると、
**1 宛先 1 行**で「宛先・**その宛先で解決した後の**件名と本文・成否・失敗理由」が残ります。
テンプレートの変数が正しく展開されたか、誰に何を送ったかを後から確認できます。
セットアップの「送信明細モジュールも生成」(既定 ON) で明細モジュールごと揃います。
宛先アドレスと本文が残るので、履歴・明細モジュールの閲覧権限は管理者などに絞ってください。

### 差出人の考え方

- 差出人は常に**送信インフラ設定のアドレス・表示名** (システム送信者)。部署共通アドレスなどはインフラ (呼び名) を分けて表現する
- 差出人アドレスを指定する手段はありません (なりすましの構造的排除)。クライアントが載せた From はサーバーで常に破棄される
- 本人のアカウント (担当者個人の Gmail / Microsoft 365 / SMTP) から送る用途は、トークンを各自の PC にだけ置く Windows アプリ **MailSender** が担います (サーバーにはトークンを置かない)

### 担当者本人のアカウントから送る (MailSender アプリ)

本人名義の送信は Windows アプリ **MailSender** (`Tools/MailSender`) で行います。

1. Web で MailField / BulkMailField の **プレビュー** をダウンロードする (宛先・変数は解決済み。添付の内容も同梱される)
2. そのプレビュー HTML を MailSender で開く (ダブルクリック / ドラッグ & ドロップ)
3. 内容を確認して「送信」→ 本人の PC に DPAPI で保存したトークン (Gmail / Microsoft 365) または SMTP アカウントで送る。サーバーは関与しない

トークンの発行・破棄、Web アプリのシステム送信者用トークンの発行もこのアプリで行います。
使い方・ビルド方法は [MailSender](MailSender.md) を参照してください。

### サーバー設定 (appsettings.json)

```json
"Mail": {
  "DefaultInfraName": "Gmail",          // 単発送信の既定インフラ (呼び名)
  "DefaultBulkInfraName": "Gmail",      // 一斉送信の既定インフラ (省略時は単発と同じ)
  "HistoryModuleName": "MailHistory",   // 送信履歴モジュール (空 = 記録しない)
  "DebugRedirectAllTo": ""              // 開発時: 全メールをこのアドレスへ転送 (空 = 無効)
},
"Smtp": {
  "SenderMailAddress": "notify@your-domain.example",
  "SenderDisplayName": "業務システム",
  "Host": "smtp.your-domain.example",
  "Port": "587",
  "SSL": "true",                        // true: 465 は SSL 接続、他は STARTTLS / false: サーバーが対応していれば STARTTLS
  "UserName": "",                       // 空 = SenderMailAddress で認証
  "Password": "",
  "MaxBulkCount": 10000
},
"GraphApi": {
  "SenderMailAddress": "notify@your-domain.example",
  "SenderDisplayName": "業務システム",
  "TenantId": "",                       // クライアントシークレット認証では必須
  "ClientId": "",                       // クライアントシークレット認証では必須
  "ClientSecret": "",                   // 空 = DefaultAzureCredential (Managed Identity 等) で認証
  "MaxBulkCount": 10000
},
"Gmail": {
  "SenderMailAddress": "notify@your-domain.example",
  "SenderDisplayName": "業務システム",
  "ClientSecret": "client_secret.json のパス、または JSON そのもの",
  "TokenSecret": "システム送信者のトークン (JSON のパス、または JSON / トークン文字列そのもの)",
  "MaxBulkCount": 500
}
```

- `Mail` は共通設定、送信インフラごとの設定 (`Smtp` / `GraphApi` / `Gmail` など) は独立したセクションです。使うものだけ書きます
- `ClientSecret` / `TokenSecret` は値が `.json` で終わればファイルパス、それ以外は中身そのものとして扱います。
  秘密をファイルで置きたくない場合は環境変数 (`Gmail__ClientSecret` / `Gmail__TokenSecret`) に
  JSON やトークン文字列を直接入れてください
- 提供している送信インフラは **SMTP**、**GraphApi (Microsoft Graph)**、**Gmail (Gmail API)** です
- **SMTP** は社内メールサーバーや各種メールサービスの SMTP エンドポイントに送ります (MailKit)。一斉送信は 1 接続で逐次送信します。
  `Password` は環境変数 (`Smtp__Password`) やユーザーシークレットに置いてください。
  開発時は [smtp4dev](https://github.com/rnwood/smtp4dev) (`dotnet tool install -g Rnwood.Smtp4dev`) をローカルに立て、`Host: localhost` / `Port: 25` / `SSL: false` で送ると
  実際のメールを出さずにブラウザ (http://localhost:5000) で受信内容を確認できます
- **GraphApi** は Microsoft 365 (Exchange Online) のメールボックスから Microsoft Graph の `sendMail` で送ります。
  Entra ID にアプリを登録し、Microsoft Graph の**アプリケーションの許可** `Mail.Send` に管理者の同意を与えてください
  (差出人を特定のメールボックスに限定するには Exchange Online の Application Access Policy を使います)。
  送ったメールは差出人の「送信済みアイテム」に残ります。認証は 2 通り:
  - `ClientSecret` を設定 → クライアントシークレット認証 (`TenantId` / `ClientId` / `ClientSecret`)。シークレットは環境変数 (`GraphApi__ClientSecret`) やユーザーシークレットに置いてください
  - `ClientSecret` が空 → `DefaultAzureCredential`。App Service 等では **Managed Identity** で動くので設定にシークレットを持ちません
    (MI のサービスプリンシパルに `Mail.Send` のアプリロールを付与します。ポータルの UI は無く PowerShell の `New-MgServicePrincipalAppRoleAssignment` で行います)。
    ローカル開発では Azure CLI / Visual Studio のログインが使われます (別テナント既定なら `TenantId` を指定)
  Exchange Online のレート制限があるため大量の一斉送信には向きません (逐次送信、429 は Retry-After に従って再試行)
- **Gmail** は Gmail の上限 (Workspace: 1 ユーザー 1 日 2,000 通 / 無料: 500 通、約 2.5 通/秒) に合わせて、
  一斉送信は 400ms 間隔で逐次送信し、レート制限 (429 / 503) は指数バックオフ (2s→32s・最大 5 回) で再試行、日次上限に達したら残りを打ち切って失敗として返します。
  `MaxBulkCount` の既定は 500。数千通規模の配信は配信サービス系のインフラ (`IMailSender` 実装) を使ってください
- 独自の送信手段 (社内メールゲートウェイなど) は `IMailSender` を実装し、アプリの
  `MailSenderTable` (呼び名 → 実装の対応表) に 1 行足すだけで使えます

### 0.5.0 のメール API から移行する

0.5.0 のテンプレートにあった `MailService` (スクリプトオブジェクト) / `MailMessage` / サーバーの `SmtpMailService` / `MailSettings` (appsettings の `MailSettings` セクション) は互換のために残してあり、
そのままビルド・動作します (`SmtpMailService` の中身は `SmtpMailSender` になりました)。新しく作る画面では MailField / BulkMailField を使ってください。

### サーバー側の結線 (アプリテンプレートに含まれるもの)

新しいアプリテンプレートには最初から入っています。既存アプリに足す場合の要点:

- `MailController` — `MailTransport.SendMailEndPoint` (`/api/mail`) / `BulkSearchMailEndPoint` (`/api/mail/bulk_search`) / `PreviewMailEndPoint` (`/api/mail/preview`) / `BulkPreviewMailEndPoint` (`/api/mail/bulk_preview`) の受け口 (プレビューは `MailPreviewBuilder` が HTML を作る)
- `MailSenderTable` — 呼び名 → `IMailSender` の対応表
- クライアント起動時: `MailTransport.SendMailEndPoint` / `BulkSearchMailEndPoint` / `PreviewMailEndPoint` / `BulkPreviewMailEndPoint` に URL を設定

### デザインチェックで検出されるもの

- MailField / BulkMailField: 宛先未設定、件名・本文とも空、変数の不在
- BulkMailField の宛先リストの先に宛先契約が無い
- 契約の必須役割が空、名指ししたフィールドの不在

### 関連

- 承認フローの「順番が回ってきた人への通知」は、承認メンバーモジュールに置いた MailField を
  テンプレートとして使います → [承認フロー](ApprovalFlow.md)
