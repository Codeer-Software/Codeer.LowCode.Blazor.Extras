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
| 操作した本人の名前・アドレスで送る | 「自分を差出人にする」＋ **差出人契約** |
| Gmail で本人のメールボックスから送る | **GmailTokenField** |

### 最短の使い方 (MailField)

1. モジュールの詳細レイアウトに **MailField** を置く
2. プロパティに宛先と文面を設定する
   - 宛先変数: `Email.Value` (自レコードのフィールド。リンク先なら `Customer.Email.Value`)
   - 件名: `注文確認: {OrderNo.Value}`
   - 本文: `{Customer.Name.Value} 様\nご注文を受け付けました。`
3. サーバーの `appsettings.json` に送信インフラの設定を書く (後述)

これで詳細画面に「メールを送る」ボタンが現れ、押すとそのレコードの値でメールが送られます。
`{変数}` は自レコードの値で置き換えられます (数値・日付はフィールドの書式で整形)。

### 一斉送信 (BulkMailField)

「配信 (1 レコード)」と「配信対象の名簿 (リスト)」を持つモジュールに **BulkMailField** を置き、
宛先リストのフィールドを指定します。名簿の**全行** (画面のページングとは無関係) に送信され、
文面の `{変数}` は**宛先の行ごと**に解決されます。宛先アドレスはサーバー側で解決されるため
クライアントには渡りません。

### セットアップ (デザイナ)

**Tools > メールのセットアップ** で、送信履歴モジュール・差出人契約・Gmail トークン欄と、
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
| 自分を差出人にする | ON = 操作ユーザー本人のアドレス・表示名が差出人になる (サーバーが解決)。差出人アドレスの直接指定はできない (なりすまし防止)。ユーザーモジュールに**差出人契約**が必要 |
| HTML本文 | 本文を HTML として送る |
| メールインフラ名 | どの送信インフラで送るかの呼び名。**通常は空**にしてサーバー設定の既定を使う |
| ボタンテキスト | 送信ボタンの文言 |

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
| 自分を差出人にする / HTML本文 / 返信先 / メールインフラ名 / ボタンテキスト | MailField と同じ |

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

### 契約フィールド (どの値を使うかの宣言)

UI もデータも持たない「宣言用」のフィールドです。役割 → フィールド (変数) の対応を宣言します。
必須の役割は表示名に「(必須)」が付き、それ以外は空にすれば「使わない」宣言になります。

| 契約 | 置く先 | 役割 | 使う機能 |
|---|---|---|---|
| **差出人契約** (MailSenderContractField) | 現在のユーザーのモジュール (AppUser 等) | Email (必須) / DisplayName | 「自分を差出人にする」、Gmail トークン検索 |
| **一斉送信の宛先契約** (BulkMailRecipientContractField) | 一斉送信の宛先リストが指すモジュール | Email (必須) / OptOut | BulkMailField の宛先解決 |
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

- 既定の差出人は**送信インフラ設定のアドレス** (システムのアドレス)
- 「自分を差出人にする」を ON にすると、操作ユーザーの**差出人契約**が宣言するアドレス・表示名が使われる
- 差出人アドレスを直接指定する手段はありません (なりすましの構造的排除)

### GmailTokenField (Gmail で本人名義に送る)

Gmail では「管理者権限なしで本人名義で送る」ために本人の OAuth 同意で得たトークンが必要です。
ユーザーモジュールにこのフィールドを置くと、ユーザーが自分のトークンを登録でき、
「自分を差出人にする」送信が**本人の Gmail** から送られます (送信済みも本人に残る)。

- トークンは**書き込み専用**の列に AES-GCM で暗号化して保存され、クライアントには一切返さない
- 鍵は `appsettings` の `Gmail.TokenEncryptionKey` (環境変数 `Gmail__TokenEncryptionKey` で与えるのが安全)。鍵未設定では保存できない
- 入力欄は毎回空から始まり、空のまま保存すれば既存トークンを維持 (パスワード変更欄と同じ)。「登録を解除」で削除
- 未登録のユーザーはシステム送信者にフォールバック
- 誰が登録できるかは、そのモジュールの書き込み権限がそのまま効く (本人だけが自分の行を編集できる設計にする)

### サーバー設定 (appsettings.json)

```json
"Mail": {
  "DefaultInfraName": "Gmail",          // 単発送信の既定インフラ (呼び名)
  "DefaultBulkInfraName": "Gmail",      // 一斉送信の既定インフラ (省略時は単発と同じ)
  "HistoryModuleName": "MailHistory",   // 送信履歴モジュール (空 = 記録しない)
  "DebugRedirectAllTo": ""              // 開発時: 全メールをこのアドレスへ転送 (空 = 無効)
},
"Gmail": {
  "SenderMailAddress": "notify@your-domain.example",
  "SenderDisplayName": "業務システム",
  "ClientSecret": "client_secret.json のパス",
  "TokenSecret": "システム送信者のトークン (JSON のパス)",
  "TokenEncryptionKey": "(環境変数で与える)",
  "MaxBulkCount": 10000
}
```

- `Mail` は共通設定、送信インフラごとの設定 (`Gmail` など) は独立したセクションです
- 現在提供している送信インフラは **Gmail (Gmail API)** です
- 独自の送信手段 (社内メールゲートウェイなど) は `IMailSender` を実装し、アプリの
  `MailSenderTable` (呼び名 → 実装の対応表) に 1 行足すだけで使えます

### サーバー側の結線 (アプリテンプレートに含まれるもの)

新しいアプリテンプレートには最初から入っています。既存アプリに足す場合の要点:

- `MailController` — `MailTransport.SendMailEndPoint` (`/api/mail`) と `BulkSearchMailEndPoint` (`/api/mail/bulk_search`) の受け口
- `MailSenderTable` — 呼び名 → `IMailSender` の対応表
- `CustomizedModuleDataIO` — GmailTokenField を使う場合、保存前に `GmailTokenHelper.ProtectGmailTokens(...)` を呼ぶ (トークンの暗号化)
- クライアント起動時: `MailTransport.SendMailEndPoint` / `BulkSearchMailEndPoint` に URL を設定

### デザインチェックで検出されるもの

- MailField / BulkMailField: 宛先未設定、件名・本文とも空、変数の不在
- 「自分を差出人にする」が ON なのに現在のユーザーのモジュールに差出人契約が無い
- GmailTokenField を置いたモジュールに差出人契約が無い
- BulkMailField の宛先リストの先に宛先契約が無い
- 契約の必須役割が空、名指ししたフィールドの不在

### 関連

- 承認フローの「順番が回ってきた人への通知」は、承認メンバーモジュールに置いた MailField を
  テンプレートとして使います → [承認フロー](ApprovalFlow.md)
