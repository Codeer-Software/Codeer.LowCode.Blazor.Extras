# MailSender

Web アプリ (Codeer.LowCode.Blazor + Extras) でダウンロードしたメールの**プレビュー HTML** を開き、
**本人の Gmail アカウント**から送る Windows アプリです。
Gmail のトークンは本人の PC にだけ置きます (サーバーには置かない)。

## 使い方

1. **OAuth クライアントを用意する** (組織で 1 回)
   - Google Cloud コンソール > API とサービス > 認証情報 > 「OAuth クライアント ID を作成」
   - アプリケーションの種類: **デスクトップ アプリ**
   - Gmail API を有効にしておく
   - 作成したクライアントの行の右端から JSON (client_secret.json) をダウンロードする
2. **設定**: MailSender の「設定」で「JSON を選ぶ...」からその JSON を選ぶ
3. **アカウントを追加**: 「アカウントを追加」→ ブラウザで Google アカウントを選んで許可 → アプリに戻る
   - 複数のアカウントを登録でき、ドロップダウンで差出人を切り替えられます。「再発行」は選択中アカウントのトークンを取り直します
   - 許可するのは「メールの送信」(gmail.send) とアカウントのメールアドレスの確認だけ
   - 発行されたトークンはこの PC の Windows アカウントでしか復号できない形で保存されます
4. **送信**: Web アプリのメール画面で「プレビュー」をダウンロードし、その HTML を MailSender で開く
   (「プレビュー HTML を開く」/ ウィンドウにドロップ / exe の引数に渡す) → 内容を確認 → 「送信」
   - 差出人はドロップダウンで選んでいるアカウントになります
   - 一斉送信の除外行 (配信停止 / アドレスなし) は送りません
   - Gmail の上限 (Workspace: 2,000 通/日、無料: 500 通/日) に達すると残りは失敗として止まります
   - 右側の本文プレビューは、HTML メールなら WebView2 で描画します (Windows 11 / 最近の Windows 10 は WebView2 ランタイム同梱。無い環境ではソース表示になります)
   - プレビューでは**外部画像・リンク先を一切読み込みません**。開封検知用の追跡画像を踏んで「開封済み」を誤発火させないためです (Web 側のプレビュー HTML も同じ)
5. **破棄**: 使わなくなったアカウントは選んで「破棄」(Google 側で無効化し、この PC から削除)
   - **トークンを書き出す**: 選択中アカウントのリフレッシュトークンを `{"refresh_token":"..."}` の JSON に保存します。Web アプリの共通送信者 (`Gmail.TokenSecret`) に設定する用途です。同じ JSON (client_secret.json) を Web 側の `Gmail.ClientSecret` にも設定してください。平文なので取り扱いに注意

## ファイルの場所

`%LOCALAPPDATA%\Codeer\MailSender\`

| ファイル | 内容 |
|---|---|
| `settings.json` | OAuth クライアント (client_secret.json から取り込んだ client_id / client_secret) |
| `accounts.bin` | 登録アカウントごとのリフレッシュトークン + 選択中アカウント (DPAPI で暗号化。他の Windows アカウントでは読めない) |
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
