# MailSender

Web アプリ (Codeer.LowCode.Blazor + Extras) でダウンロードしたメールの**プレビュー HTML** を開き、
**本人の Gmail アカウント**から送る Windows アプリです。
Gmail のトークンは本人の PC にだけ置きます (サーバーには置かない)。

## 使い方

1. **OAuth クライアントを用意する** (組織で 1 回)
   - Google Cloud コンソール > API とサービス > 認証情報 > 「OAuth クライアント ID を作成」
   - アプリケーションの種類: **デスクトップ アプリ**
   - Gmail API を有効にしておく
   - 作成されたクライアント ID (`xxxx.apps.googleusercontent.com`) を控える (client_secret は使いません)
2. **設定**: MailSender の「設定」でクライアント ID を入力
3. **トークンを発行**: 「トークンを発行」→ ブラウザで Google アカウントを選んで許可 → アプリに戻る
   - 許可するのは「メールの送信」(gmail.send) とアカウントのメールアドレスの確認だけ
   - 発行されたトークンはこの PC の Windows アカウントでしか復号できない形で保存されます
4. **送信**: Web アプリのメール画面で「プレビュー」をダウンロードし、その HTML を MailSender で開く
   (「プレビュー HTML を開く」/ ウィンドウにドロップ / exe の引数に渡す) → 内容を確認 → 「送信」
   - 差出人は発行したトークンのアカウントになります
   - 一斉送信の除外行 (配信停止 / アドレスなし) は送りません
   - Gmail の上限 (Workspace: 2,000 通/日、無料: 500 通/日) に達すると残りは失敗として止まります
5. **トークンを破棄**: 使わなくなったら「トークンを破棄」(Google 側で無効化し、この PC から削除)

## ファイルの場所

`%LOCALAPPDATA%\Codeer\MailSender\`

| ファイル | 内容 |
|---|---|
| `settings.json` | クライアント ID |
| `token.bin` | リフレッシュトークン + アカウント (DPAPI で暗号化。他の Windows アカウントでは読めない) |
| `logs\yyyyMMdd.log` | 送信ログ (日時 / 宛先 / 件名 / 結果 / 元ファイル) |

## 発行 (単一 exe)

```
dotnet publish Tools\MailSender\MailSender.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o Tools\MailSender\publish
```

`publish\MailSender.exe` を配布します (.NET 8 デスクトップ ランタイムが必要)。成果物は git に入れず GitHub Releases に置きます。

## 仕組み

- 送信パッケージ = プレビュー HTML の `<script id="data" type="application/json">` (Extras.Server の `MailPreviewDocument`)。
  宛先・件名・本文は Web 側で解決済み、添付は Base64 で同梱
- Gmail API / OAuth / MIME は `Codeer.Mail.Gmail/` (このアプリ専用。製品側の Extras とは独立)
- 同意は PKCE + ループバック (`http://127.0.0.1:<空きポート>/`)。デスクトップ種別なので client_secret は不要
