# MailSender — 担当者本人の Gmail アカウント名義でメールを送る

[メール送信](Mail.md) (MailField / BulkMailField) の差出人は、通常はサーバーに設定した**システム送信者**です。
**MailSender** は、それを**担当者本人の Gmail アカウント名義**で送るための Windows アプリです (`Tools/MailSender`)。

- Web アプリの「プレビュー」でダウンロードした HTML を MailSender で開き、内容を確認してから本人のアカウントで送信します
- Gmail のトークンは本人の PC にだけ保存され、サーバーには置きません (なりすましの構造的排除と両立させるための設計)
- Web アプリのシステム送信者用のトークンを発行する管理ツールとしても使います

![MailSender](images/mailsender_main.png)

## 動作の流れ

```
Web アプリ (MailField / BulkMailField)
    │  「プレビュー」をダウンロード (宛先・件名・本文・添付を含む HTML)
    ▼
MailSender で開く → 宛先ごとに内容を確認 → 送るものにチェック
    │
    ▼
本人の Gmail アカウントで送信 (Gmail API)。サーバーは関与しない
```

プレビュー HTML はブラウザでそのまま見られる確認用ファイルであり、同時に MailSender の**送信パッケージ**でもあります
(`<script id="data" type="application/json">` に `packageVersion` / `items` の宛先・件名・本文 / `attachmentFiles` / `replyTo` / `isBodyHtml` を持つ)。

## 事前準備 (組織で 1 回)

Google Cloud で OAuth クライアントを作ります。

1. [Google Cloud コンソール](https://console.cloud.google.com/) でプロジェクトを作り、**Gmail API** を有効にする
2. 「API とサービス > OAuth 同意画面」でアプリ名などを設定する (同意画面に表示される名前はプロジェクトで 1 つ。クライアント名ではありません)
3. 「API とサービス > 認証情報 > OAuth クライアント ID を作成」で、種類 **デスクトップ アプリ** のクライアントを作る
4. 作成直後に表示される **クライアント ID** と **クライアント シークレット** を控える (JSON をダウンロードできる場合はそれでも可)

> デスクトップ種別のクライアント シークレットは、Google の仕様上「秘密として扱わない」ものです (PKCE と併用します)。
> それでも配布物や公開リポジトリには含めないでください。

## 使い方

### 1. 設定

「設定」を開き、「デスクトップ アプリ」欄にクライアント ID とシークレットを貼って OK を押します。
Google Cloud から JSON をダウンロードした場合は「JSON を読み込む」で取り込めます。

![設定](images/mailsender_settings.png)

設定・トークン・送信ログは `%LOCALAPPDATA%\Codeer\MailSender\` に保存されます (「フォルダを開く」で開けます)。

| ファイル | 内容 |
|---|---|
| `settings.json` | OAuth クライアントの ID / シークレット、画面の拡大率 |
| `accounts.bin` | 登録したアカウントのトークン (Windows の DPAPI で暗号化。同じ PC の同じ Windows アカウントでしか復号できません) |
| `logs\yyyyMMdd.log` | 送信ログ (日時 / 宛先 / 件名 / 結果 / 元ファイル) |

環境変数 `MAILSENDER_DATA_FOLDER` でこのフォルダを差し替えられます (検証用に本番の設定・トークンと分けたいとき)。

### 2. アカウントを追加 (Google アカウントの同意)

「アカウントを追加」を押すとブラウザが開き、Google アカウントの選択と許可を求められます。
許可するのは「メールの送信 (gmail.send)」とアカウントのメールアドレスの確認だけです。

許可するとアプリに戻り、差出人の一覧にそのアカウントが追加されます。複数のアカウントを登録して、ドロップダウンで切り替えられます。

- ブラウザを途中で閉じてしまった場合は、画面右下の「中止」で待機を抜けられます
- 「再発行」は選択中アカウントのトークンを取り直します。「破棄」は Google 側でトークンを無効化し、この PC から削除します
- 再発行・トークンの書き出し・破棄は、いずれも**ドロップダウンで選択中のアカウント**に対して行われます (アドレスの下に対象が表示されます)

### 3. 送信パッケージを開く

Web アプリのメール画面で「プレビュー」を押して HTML をダウンロードし、MailSender の「プレビュー HTML を開く」で開きます
(ウィンドウへのドロップや、ファイルをダブルクリックして「プログラムから開く」でも可)。

- 左に宛先の一覧、右に選択した宛先の件名・本文が表示されます
- 一斉送信で除外された宛先 (配信停止 / アドレスなし) は淡色で表示され、送信対象から外れています
- 「送信」列のチェックで送る宛先を選べます。除外行もチェックを付ければ送れます (アドレスが無い行は不可)

HTML メールの場合は本文を実際の見え方でプレビューします。

![HTML メールのプレビュー](images/mailsender_html.png)

> プレビューでは**外部の画像やリンク先を一切読み込みません**。
> SFA/MA などが埋め込む開封検知用の画像を踏んで「開封済み」を誤って記録させないためです (Web アプリ側のプレビュー HTML も同じ動作です)。

### 4. 送信

「送信 (N 件)」を押し、確認ダイアログで差出人を確かめて OK を押すと、チェックした宛先へ 1 通ずつ送信します。
結果は一覧の「状態」列と送信ログに残ります。「中止」で送信中の 1 通が終わったところで止められます。

Gmail の送信上限 (Google Workspace: 2,000 通/日、無料 Gmail: 500 通/日) に達すると、残りは失敗として止まります。
数千通規模の配信はサーバーの配信サービス系インフラで行ってください。

### 画面の拡大縮小

Ctrl + マウスホイールで 70%〜200% に拡大縮小できます。倍率は保存されます。

## Web アプリのシステム送信者のトークンを作る

Web アプリの `Gmail` インフラ (ユーザー同意モード) は、`ClientSecret` に OAuth クライアントの JSON、`TokenSecret` にリフレッシュトークンを設定します
([サーバー設定](Mail.md#サーバー設定-appsettingsjson))。このリフレッシュトークンは MailSender で発行できます。

Web アプリ用には、Google Cloud で種類 **ウェブ アプリケーション** のクライアントを別に作ることを推奨します
(ウェブ種別のシークレットはサーバーにだけ置けます)。デスクトップ種別を共用しても動作します。

1. Google Cloud でウェブ種別のクライアントを作り、「承認済みのリダイレクト URI」に **`http://localhost:53682/`** を登録する。クライアント ID とシークレットを控える
   - リダイレクト URI は、同意フローを実行する MailSender が受けるためのものです。Web アプリ自身は同意フローを実行しない
     (リフレッシュトークンから送信するだけ) ので、Web アプリの URL を登録する必要はありません
2. MailSender の「設定」の「ウェブ アプリケーション」欄に ID とシークレットを貼る。
   「JSON を書き出す」で Web アプリの `Gmail.ClientSecret` 用の JSON (client_secret.json 相当) を保存する
3. 「アカウントを追加」→ メニューで **「ウェブ アプリケーション で追加」** を選び、システム送信者にするアカウントで同意する

   ![アカウントを追加のメニュー](images/mailsender_add_menu.png)

4. そのアカウントを選んで「トークンを書き出す」→ `{"refresh_token":"..."}` 形式の JSON を保存する (平文なので取り扱いに注意)
5. Web アプリ (Server プロジェクト) の `appsettings.Development.json` または環境変数 (`Gmail__ClientSecret` / `Gmail__TokenSecret`) に設定する

   ```json
   "Gmail": {
     "SenderMailAddress": "noreply@example.com",
     "SenderDisplayName": "サンプル株式会社",
     "ClientSecret": "C:\\secrets\\client_secret_web.json",
     "TokenSecret": "C:\\secrets\\gmail_token_noreply_example.com.json"
   }
   ```

   これらの値は `appsettings.json` に書かずに、git 管理外の `appsettings.Development.json` や環境変数に置いてください。

ウェブ種別で発行したアカウントは一覧に「[ウェブ]」と表示され、同じアドレスをデスクトップ用・ウェブ用の両方で持つことができます。
送信時はそのアカウントを発行したクライアントが自動で使われるので、設定を切り替える必要はありません。

## 配布とビルド

ソースは `Tools/MailSender` にあります。.NET 10 SDK でビルドし、単一の exe として発行します。

```
dotnet publish Tools\MailSender\MailSender.csproj -c Release -r win-x64 -p:PublishSingleFile=true -o Tools\MailSender\publish
```

`Tools\MailSender\publish\MailSender.exe` を配布します。実行には **.NET 10 デスクトップ ランタイム** と、
HTML メールのプレビューに **WebView2 ランタイム** (Windows 11 / 最近の Windows 10 には同梱) が必要です。
WebView2 が無い環境では本文がソース表示になります。成果物は git に入れず GitHub Releases に置きます。

| フォルダ | 内容 |
|---|---|
| `Tools/MailSender` | WPF アプリ本体 (WPF-UI / WebView2)。Codeer.LowCode.Blazor には依存しない |
| `Tools/MailSender/Codeer.Mail.Gmail` | Gmail API / OAuth (PKCE・ループバック受信) の送信部品。UI に依存しないため CLI などに転用できる |

## 関連

- [メール送信](Mail.md) — MailField / BulkMailField / プレビュー / サーバー設定
