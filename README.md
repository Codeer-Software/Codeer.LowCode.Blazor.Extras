# Codeer.LowCode.Blazor.Extras

[Codeer.LowCode.Blazor](https://github.com/Codeer-Software/Codeer.LowCode.Blazor) に高機能UIコンポーネントと、
アプリケーション開発でよく使うクライアント/サーバーサービスを追加する拡張ライブラリです。
ソースは MIT で公開しているため、動作を変えたい場合はクラスを丸ごと差し替えたり、ソースをコピーして改変できます。

[![NuGet](https://img.shields.io/nuget/v/Codeer.LowCode.Blazor.Extras)](https://www.nuget.org/packages/Codeer.LowCode.Blazor.Extras)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## パッケージ構成

| パッケージ | 内容 |
|---|---|
| Codeer.LowCode.Blazor.Extras | 拡張フィールド (メール送信・承認フローを含む)、スクリプトオブジェクト (Excel / WebApi / Toaster)、クライアントサービス |
| Codeer.LowCode.Blazor.Extras.Designer | デザイナ統合 (ツールボックス登録・AI 用ドキュメント登録) |
| Codeer.LowCode.Blazor.Extras.Server | サーバーサービス (認証: ID/パスワード照合・外部 IdP・二要素認証 / メール送信 / 承認フローエンジン / AI ドキュメント解析 / ファイルストレージ / ASP.NET Core ヘルパ) |

## 提供フィールド

| フィールド | 説明 |
|---|---|
| [CalendarField](docs/CalendarField.md) | 月・週・日表示のカレンダー。イベントの表示・追加・編集が可能 |
| [GanttField](docs/GanttField.md) | SVGベースのガントチャート。タスクのドラッグ移動・リサイズ、依存関係の管理が可能 |
| [TaskBoardField](docs/TaskBoardField.md) | カンバンボード。ドラッグ&ドロップでステータス変更が可能 |
| [RichTextField](docs/RichTextField.md) | 書式付きテキストエディタ。太字・色・リンクなどのHTMLフォーマットに対応 |
| [MarkdownField](docs/MarkdownField.md) | Markdown エディタ / ビューア。Markdown をプレーンテキストのまま保存し、閲覧時は HTML に描画。ツールバーとプレビュー (タブ / 左右並び) 付き。生 HTML は無効化 (外部ライブラリ Markdig を使用) |
| [ColorPickerField](docs/ColorPickerField.md) | カラーピッカー。HTML5ネイティブカラーピッカーで色をHEX文字列として保存 |
| [MarkerListField](docs/MarkerListField.md) | 画像上にマーカー(ピン)を配置・操作するフィールド |
| [QrCodeField](docs/QrCodeField.md) | 文字列をQRコード画像として表示する表示専用フィールド (外部ライブラリ QRCoder を使用) |
| [ProgressField](docs/ProgressField.md) | 進捗率を横バー / 半円メーターで表示する表示専用フィールド。値・色を別フィールドから参照 |
| [FileStorage](docs/FileStorage.md) | FileField のファイル保存先。FileSystem / Azure Blob / Amazon S3 (S3互換含む) と独自プロバイダ |
| [EnterFocusMoveField](docs/EnterFocusMoveField.md) | Enterキーでモジュール内の次の入力要素にフォーカスを移動させるユーティリティフィールド |
| [LoginAccountContractField](Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/LoginAccountContractFieldDesign.md) | ユーザーモジュールに置く契約フィールド。ログイン ID・外部 IdP の突き合わせ・有効フラグ・表示名・メール二要素の送信先・パスワード入力欄を役割で、パスワード照合用の列と認証アプリ (TOTP) の列を宣言する。サーバーのログイン処理はこの契約だけを見る ([認証の全体像](docs/Authentication.md)) |
| [二要素認証](docs/TwoFactorLogin.md) | LoginAccountContractField の TOTP 列 (認証アプリ) か TwoFactorEmail (メールのワンタイムコード) を設定すると、パスワード成功後に 6 桁コードの入力を求める (Extras.Server の TotpLogin / EmailOtpLogin) |
| [TotpResetButtonField](Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/TotpResetButtonFieldDesign.md) | 表示中のユーザーの認証アプリ (TOTP) 登録を解除するボタン (管理者用)。ユーザーモジュールの詳細画面に置く。通常の保存と同じ権限で制御される |
| [MyTotpResetButtonField](Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/MyTotpResetButtonFieldDesign.md) | ログイン中の自分の認証アプリ登録を解除するボタン (本人用)。設定画面などどのモジュールにも置ける |
| [PasswordHashField](docs/PasswordHashField.md) | パスワードを Submit 時にハッシュ + ソルトへ変換して DB に書き込む補助フィールド (サーバサイド実装が必要)。ログインユーザーモジュールでは LoginAccountContractField の PasswordField で代替できる |
| [OrientationLockField](docs/OrientationLockField.md) | タッチ端末で画面の向き(横/縦)が指定と異なるとき、全画面オーバーレイで回転を促すフィールド |
| [AITextAnalyzerField](docs/AITextAnalyzerField.md) | 帳票ファイルや自由テキストを AI で解析し、モジュールのフィールドへ自動入力する入力補助フィールド (Azure OpenAI + Document Intelligence を使用) |

## 認証 (ログイン)

Codeer.LowCode.Blazor 本体が持つのは認可だけで、認証 (ログイン) はライブラリに含まれません。認証はホストアプリの担当で、その実装をこの Extras が MIT で提供します。
セッションは Cookie、ログインアカウントの宣言はユーザーモジュールの契約フィールド。パスワード / Entra ID / Google / AWS Cognito / OpenID Connect / 二要素認証のどれでログインしても、本体の認可 (CurrentUser・モジュール / 行 / PageFrame の条件) は変わりません。

| ドキュメント | 内容 |
|---|---|
| [認証の全体像](docs/Authentication.md) | 本体 (認可) とホスト・Extras (認証) の役割分担、部品の一覧、ホストに入っているもの |
| [外部ログイン](docs/ExternalLogin.md) | Entra ID / Google / AWS Cognito / 汎用 OpenID Connect。appsettings だけで有効化。MAUI 対応 |
| [二要素認証](docs/TwoFactorLogin.md) | 認証アプリ (TOTP) とメールのワンタイムコード。解除ボタン |
| [LoginAccountContractField](Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/LoginAccountContractFieldDesign.md) | ユーザーモジュールに置く契約フィールドの仕様 |

## 業務機能

| 機能 | 説明 |
|---|---|
| [メール送信](docs/Mail.md) | MailField (単発送信ボタン) / BulkMailField (名簿への一斉送信) / 送信履歴 / プレビュー。宛先・文面はレコードの値から組み立てる |
| [MailSender](docs/MailSender.md) | 担当者本人のアカウント (Gmail / Microsoft 365 / SMTP) 名義で送る Windows アプリ (`Tools/MailSender` をビルドして使う)。Web のプレビュー HTML を開いて送信。トークンは本人の PC にだけ置く。Web アプリのシステム送信者用トークンの発行にも使う |
| [承認フロー](docs/ApprovalFlow.md) | ApprovalFlowField を申請書に置くだけで申請・承認・却下・差し戻し・取り下げ・再申請・回覧。承認データは通常のモジュール。状態遷移はサーバーが検証 |

どちらもデザイナの **Tools > メールのセットアップ / 承認フローのセットアップ** (または CLI の `mail-setup` / `approval-setup`) で
必要なモジュール群を生成できます。

## スクリプトオブジェクト

スクリプト (*.mod.cs) から利用できるサービス・型です。`ExtrasClientInitializer.Initialize` で一括登録されます。

| オブジェクト | 説明 |
|---|---|
| Excel | Excel ファイルの読み書き・テンプレートへの値書き込み・xlsx / PDF ダウンロード |
| WebApiService | 外部 API への HTTP リクエスト (Get / Post / Put / Delete) |
| Toaster | トースト通知の表示 (Success / Info / Warn / Error) |

各オブジェクトの正確なシグネチャと使用例は、デザイナの入力補完、またはデザイナ CLI の
`script-catalog` サブコマンドが生成するカタログで確認できます。

## クライアントサービス

アプリテンプレートの DI に登録して使う実装です。インターフェース (`IHttpService` / `IToastService` 等) で
登録するため、自前実装への丸ごと差し替えができます。

- HttpService — ローディング表示・エラー通知付きの HTTP 通信ラッパー
- UIService — ファイルダウンロード・通知トースト
- Logger — ブラウザコンソール + トースト通知のロガー
- ToastService — Sotsera.Blazor.Toaster ベースのトースト実装
- LocalizeService — TSV リソースによるローカライズ

## サーバーサービス (Codeer.LowCode.Blazor.Extras.Server)

- AITextAnalyzeService — Azure Document Intelligence + Azure OpenAI による帳票・テキスト解析 (AITextAnalyzerField のサーバー側)
- 認証 — LoginAccountStore (ID/パスワード照合・ユーザー行の解決) / 外部 IdP (OidcLoginProvider と Entra / Google / Cognito 実装) / TotpLogin・EmailOtpLogin (二要素認証)。[認証の全体像](docs/Authentication.md)
- メール送信 — MailDispatcher (テンプレート解決・一斉送信・送信履歴) と SMTP / Microsoft Graph / SendGrid / Gmail API 送信。独自の送信手段は IMailSender で追加
- 承認フロー — ApprovalEngine (状態遷移の検証と実行)
- StorageAccess / TemporaryFileManager — ファイルストレージ (ファイルシステム / Azure Blob) と一時ファイル管理
- CustomFontResolver — Excel PDF 変換用のフォントリゾルバ
- Web ヘルパ — ETag 付きファイル応答 (FileWithETag)、ホットリロード (HotReloadHub / FileWatcherService)

## セットアップ

新しいアプリテンプレートで作成したプロジェクトには最初から組み込まれています。
既存プロジェクトに追加する場合は以下の手順で設定してください。

### 1. NuGet パッケージのインストール

| インストール先プロジェクト | パッケージ名 |
|---|---|
| LowCodeApp.Client.Shared | Codeer.LowCode.Blazor.Extras |
| LowCodeApp.Designer | Codeer.LowCode.Blazor.Extras.Designer |
| LowCodeApp.Server (サーバーサービスを使う場合) | Codeer.LowCode.Blazor.Extras.Server |

### 2. コードの修正

#### LowCodeApp.Client.Shared

`Services/AppInfoService.cs` の `AppInfoService` コンストラクタに以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras;

// フィールドのみ使う場合
ExtrasClientInitializer.Initialize(this);

// スクリプトオブジェクト (Excel / WebApi / Toaster) も使う場合
ExtrasClientInitializer.Initialize(this, http, logger, toaster);
```

メール送信・承認フロー・Excel PDF 変換・AI 解析を使う場合は、エンドポイント URL を起動時に設定します
(URL はアプリのコントローラに合わせて変更してください)。

```csharp
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Approval;

MailTransport.SendMailEndPoint = "/api/mail";
MailTransport.BulkSearchMailEndPoint = "/api/mail/bulk_search";
MailTransport.PreviewMailEndPoint = "/api/mail/preview";
MailTransport.BulkPreviewMailEndPoint = "/api/mail/bulk_preview";
ApprovalTransport.EndPointBase = "/api/approval";
Codeer.LowCode.Blazor.Extras.ScriptObjects.Excel.ConvertPdfEndPoint = "api/excel/pdf";
AITextAnalyzerField.FileToModuleDataEndPoint = "/api/ai_text_analyze/file";
AITextAnalyzerField.TextToModuleDataEndPoint = "/api/ai_text_analyze/text";
```

#### LowCodeApp.Server

`Program.cs` に以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras;

ExtrasServerInitializer.Initialize();
```

メール送信・承認フローを使う場合は、受け口となるコントローラ (`MailController` / `ApprovalController`) と
送信インフラの対応表 (`MailSenderTable`) が必要です。新しいアプリテンプレートには含まれています。
詳細は [メール送信](docs/Mail.md) / [承認フロー](docs/ApprovalFlow.md) を参照してください。

#### LowCodeApp.Designer

`App.xaml.cs` に以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras.Designer;

// OnStartup メソッド内 (base.OnStartup(e) より前)
ExtrasDesignerInitializer.Initialize(BlazorRuntime);

// base.OnStartup(e) の後 (Tools メニュー: 承認フローのセットアップ / メール履歴モジュールの生成)
ExtrasDesignerInitializer.Setup(DesignerEnvironment);
```

セットアップメニューは承認フロー・メール履歴に必要なモジュール群をテンプレートから生成し、
申請書モジュールへの結線とテーブル作成 DDL の提示まで行います
(headless CLI の `approval-setup` / `mail-history-setup` verb からも同じ生成を実行できます)。

### 3. セットアップ完了

以上でセットアップは完了です。Designer から Extras のフィールドが配置できるようになります。

## カスタマイズ

ソースは MIT で公開しています。動作を変えたい場合は次のいずれかで対応できます。

- インターフェースで登録するサービス (`IHttpService` / `IToastService` / `ITemporaryFileManager` 等) は、自前実装を DI 登録して丸ごと差し替える
- それ以外のクラスは、このリポジトリのソースをコピーしてアプリ内で改変し、元のクラスの代わりに使う

## 各フィールドの詳細

各フィールドの詳しい説明は以下のドキュメントを参照してください。

- [CalendarField - カレンダー](docs/CalendarField.md)
- [GanttField - ガントチャート](docs/GanttField.md)
- [TaskBoardField - カンバンボード](docs/TaskBoardField.md)
- [RichTextField - リッチテキストエディタ](docs/RichTextField.md)
- [MarkdownField - Markdown エディタ / ビューア](docs/MarkdownField.md)
- [ColorPickerField - カラーピッカー](docs/ColorPickerField.md)
- [MarkerListField - 画像マーカー](docs/MarkerListField.md)
- [QrCodeField - QRコード](docs/QrCodeField.md)
- [ProgressField - 進捗バー / メーター](docs/ProgressField.md)
- [EnterFocusMoveField - Enterキーでフォーカス移動](docs/EnterFocusMoveField.md)
- [認証の全体像 (ログインアカウント契約 / パスワード / 外部 IdP / 二要素認証)](docs/Authentication.md)
- [外部ログイン (Entra ID / Google / AWS Cognito / OpenID Connect)](docs/ExternalLogin.md)
- [二要素認証 (認証アプリ TOTP / メールのワンタイムコード)](docs/TwoFactorLogin.md)
- [PasswordHashField - パスワードハッシュ](docs/PasswordHashField.md)
- [OrientationLockField - 画面の向き制御](docs/OrientationLockField.md)
- [AITextAnalyzerField - AI 帳票解析](docs/AITextAnalyzerField.md)

## CSS カスタマイズ

各フィールドの見た目はCSSで自由にカスタマイズできます。詳しくは **[CSS カスタマイズガイド](docs/CSS-Customization.md)** を参照してください。

## ライセンス

[MIT License](LICENSE)

### 使用している OSS

各パッケージが NuGet 参照で取り込む第三者ライブラリとそのライセンスです (Microsoft.* / Azure.* / System.* の Microsoft 製パッケージはすべて MIT)。
いずれも商用利用・再配布が可能なライセンスで、NuGet パッケージ自体がライセンス文を同梱しているため、利用側で追加の手続きは要りません。

| パッケージ | ライブラリ | ライセンス | 用途 |
|---|---|---|---|
| Codeer.LowCode.Blazor.Extras | [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause | MarkdownField の描画 |
| Codeer.LowCode.Blazor.Extras | [QRCoder](https://github.com/codebude/QRCoder) | MIT | QrCodeField の QR 生成 |
| Codeer.LowCode.Blazor.Extras | [Sotsera.Blazor.Toaster](https://github.com/sotsera/sotsera.blazor.toaster) | MIT | トースト通知 (ToastService) |
| Codeer.LowCode.Blazor.Extras / .Server | [Excel.Report.PDF](https://github.com/Codeer-Software/Excel.Report.PDF) (Codeer) | MIT | Excel の読み書き・PDF 変換。推移的に [ClosedXML](https://github.com/ClosedXML/ClosedXML) (MIT)、[PdfSharp](https://github.com/empira/PDFsharp) (MIT)、[SixLabors.Fonts](https://github.com/SixLabors/Fonts) 1.0 (Apache-2.0) を含む |
| Codeer.LowCode.Blazor.Extras.Server | [MailKit](https://github.com/jstedfast/MailKit) | MIT | SMTP 送信 |
| Codeer.LowCode.Blazor.Extras.Server | [AWSSDK.S3](https://github.com/aws/aws-sdk-net) | Apache-2.0 | FileField の S3 保存先 |
| Codeer.LowCode.Blazor.Extras.Server | Azure.AI.FormRecognizer / Azure.AI.OpenAI / Azure.Identity / Azure.Storage.Blobs | MIT | AI 帳票解析、Azure Blob 保存先、Entra ID |
| Codeer.LowCode.Blazor.Extras.Server | Microsoft.AspNetCore.Authentication.OpenIdConnect | MIT | 外部ログイン |
| Codeer.LowCode.Blazor.Extras.SeleniumDrivers | Codeer.LowCode.Blazor.SeleniumDrivers (Codeer) | MIT | 本体フィールドのドライバ |

本体 (Codeer.LowCode.Blazor / .Designer) は Codeer の商用ライセンスです。
