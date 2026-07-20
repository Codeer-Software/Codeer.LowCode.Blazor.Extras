# Codeer.LowCode.Blazor.Extras

[Codeer.LowCode.Blazor](https://github.com/Codeer-Software/Codeer.LowCode.Blazor) に高機能UIコンポーネントと、
アプリケーション開発でよく使うクライアント/サーバーサービスを追加する拡張ライブラリです。
ソースは MIT で公開しているため、動作を変えたい場合はクラスを丸ごと差し替えたり、ソースをコピーして改変できます。

[![NuGet](https://img.shields.io/nuget/v/Codeer.LowCode.Blazor.Extras)](https://www.nuget.org/packages/Codeer.LowCode.Blazor.Extras)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## パッケージ構成

| パッケージ | 内容 |
|---|---|
| Codeer.LowCode.Blazor.Extras | 拡張フィールド、スクリプトオブジェクト (Excel / WebApi / Toaster / Mail)、クライアントサービス |
| Codeer.LowCode.Blazor.Extras.Designer | デザイナ統合 (ツールボックス登録・AI 用ドキュメント登録) |
| Codeer.LowCode.Blazor.Extras.Server | サーバーサービス (AI ドキュメント解析 / SMTP メール送信 / マルチDBアクセス / ファイルストレージ / ASP.NET Core ヘルパ) |

## 提供フィールド

| フィールド | 説明 |
|---|---|
| [CalendarField](docs/CalendarField.md) | 月・週・日表示のカレンダー。イベントの表示・追加・編集が可能 |
| [GanttField](docs/GanttField.md) | SVGベースのガントチャート。タスクのドラッグ移動・リサイズ、依存関係の管理が可能 |
| [TaskBoardField](docs/TaskBoardField.md) | カンバンボード。ドラッグ&ドロップでステータス変更が可能 |
| [RichTextField](docs/RichTextField.md) | 書式付きテキストエディタ。太字・色・リンクなどのHTMLフォーマットに対応 |
| [ColorPickerField](docs/ColorPickerField.md) | カラーピッカー。HTML5ネイティブカラーピッカーで色をHEX文字列として保存 |
| [MarkerListField](docs/MarkerListField.md) | 画像上にマーカー(ピン)を配置・操作するフィールド |
| [QrCodeField](docs/QrCodeField.md) | 文字列をQRコード画像として表示する表示専用フィールド (外部ライブラリ QRCoder を使用) |
| [ProgressField](docs/ProgressField.md) | 進捗率を横バー / 半円メーターで表示する表示専用フィールド。値・色を別フィールドから参照 |
| [EnterFocusMoveField](docs/EnterFocusMoveField.md) | Enterキーでモジュール内の次の入力要素にフォーカスを移動させるユーティリティフィールド |
| [PasswordHashField](docs/PasswordHashField.md) | パスワードを Submit 時にハッシュ + ソルトへ変換して DB に書き込む補助フィールド (サーバサイド実装が必要) |
| [OrientationLockField](docs/OrientationLockField.md) | タッチ端末で画面の向き(横/縦)が指定と異なるとき、全画面オーバーレイで回転を促すフィールド |
| [AITextAnalyzerField](docs/AITextAnalyzerField.md) | 帳票ファイルや自由テキストを AI で解析し、モジュールのフィールドへ自動入力する入力補助フィールド (Azure OpenAI + Document Intelligence を使用) |

## スクリプトオブジェクト

スクリプト (*.mod.cs) から利用できるサービス・型です。`ExtrasClientInitializer.Initialize` で一括登録されます。

| オブジェクト | 説明 |
|---|---|
| Excel | Excel ファイルの読み書き・テンプレートへの値書き込み・xlsx / PDF ダウンロード |
| WebApiService | 外部 API への HTTP リクエスト (Get / Post / Put / Delete) |
| Toaster | トースト通知の表示 (Success / Info / Warn / Error) |
| MailService | メール送信。`CreateMessage()` で宛先複数・Cc / Bcc・HTML 本文・添付付きのメールを組み立てられる |

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
- SmtpMailService — MailMessage 対応の SMTP 送信
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

// スクリプトオブジェクト (Excel / WebApi / Toaster / Mail) も使う場合
ExtrasClientInitializer.Initialize(this, http, logger, toaster);
```

メール送信・Excel PDF 変換・AI 解析を使う場合は、エンドポイント URL を起動時に設定します
(URL はアプリのコントローラに合わせて変更してください)。

```csharp
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;

MailService.SendMailEndPoint = "/api/mail";
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

#### LowCodeApp.Designer

`App.xaml.cs` に以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras.Designer;

// OnStartup メソッド内
ExtrasDesignerInitializer.Initialize(BlazorRuntime);
```

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
- [ColorPickerField - カラーピッカー](docs/ColorPickerField.md)
- [MarkerListField - 画像マーカー](docs/MarkerListField.md)
- [QrCodeField - QRコード](docs/QrCodeField.md)
- [ProgressField - 進捗バー / メーター](docs/ProgressField.md)
- [EnterFocusMoveField - Enterキーでフォーカス移動](docs/EnterFocusMoveField.md)
- [PasswordHashField - パスワードハッシュ](docs/PasswordHashField.md)
- [OrientationLockField - 画面の向き制御](docs/OrientationLockField.md)
- [AITextAnalyzerField - AI 帳票解析](docs/AITextAnalyzerField.md)

## CSS カスタマイズ

各フィールドの見た目はCSSで自由にカスタマイズできます。詳しくは **[CSS カスタマイズガイド](docs/CSS-Customization.md)** を参照してください。

## ライセンス

[MIT License](LICENSE)
