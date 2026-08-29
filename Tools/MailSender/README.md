# MailSender

Web アプリ (Codeer.LowCode.Blazor + Extras) でダウンロードしたメールの**プレビュー HTML** を開き、
**本人のアカウント** (Gmail / Microsoft 365 / SMTP サーバー) から送る Windows アプリです。
トークンや SMTP パスワードは本人の PC にだけ置きます (サーバーには置かない)。利用者向けの説明は `docs/MailSender.md`。

## 使い方

1. **アプリ登録を用意する** (組織で 1 回。SMTP だけ使うなら不要)
   - Gmail: Google Cloud コンソール > API とサービス > 認証情報 > 「OAuth クライアント ID を作成」(種類 **デスクトップ アプリ**、Gmail API を有効化)。**クライアント ID** と**シークレット**を控える
   - Microsoft 365: Entra ID > アプリの登録。認証にプラットフォーム「モバイル アプリケーションとデスクトップ アプリケーション」+ `http://localhost`、「パブリック クライアント フローを許可する」= はい。API のアクセス許可に委任 `Mail.Send` / `User.Read`。**アプリケーション (クライアント) ID** を控える (シークレット不要)
2. **設定**: MailSender の「設定」に上の ID を貼る (Gmail は JSON があれば「JSON を読み込む」)
3. **アカウントを追加**: 「アカウントを追加」→ メニューで種類を選ぶ
   - Gmail / Microsoft 365: ブラウザでアカウントを選んで許可 → アプリに戻る。許可するのはメールの送信と本人のアドレス確認だけ
   - SMTP: 差出人アドレスとサーバー (ホスト / ポート / 暗号化 / ユーザー名 / パスワード) を入力。「接続テスト」あり
   - 複数のアカウントを種類をまたいで登録でき、ドロップダウンで差出人を切り替えられます。「再発行」は選択中アカウントのトークンを取り直し (SMTP は「編集」)
   - トークン・パスワードはこの PC の Windows アカウントでしか復号できない形で保存されます
4. **送信**: Web アプリのメール画面で「プレビュー」をダウンロードし、その HTML を MailSender で開く
   (「プレビュー HTML を開く」/ ウィンドウにドロップ / exe の引数に渡す) → 内容を確認 → 「送信」
   - 差出人はドロップダウンで選んでいるアカウントになります
   - 一斉送信の除外行 (配信停止 / アドレスなし) は送りません
   - Gmail の上限 (Workspace: 2,000 通/日、無料: 500 通/日) に達すると残りは失敗として止まります。Microsoft 365 は 2 秒間隔 (30 通/分の制限)。SMTP は 1 接続で逐次、接続失敗は全件失敗
   - 右側の本文プレビューは、HTML メールなら WebView2 で描画します (Windows 11 / 最近の Windows 10 は WebView2 ランタイム同梱。無い環境ではソース表示になります)
   - プレビューでは**外部画像・リンク先を一切読み込みません**。開封検知用の追跡画像を踏んで「開封済み」を誤発火させないためです (Web 側のプレビュー HTML も同じ)
5. **破棄**: 使わなくなったアカウントは選んで「破棄」(この PC から削除。Gmail は Google 側でも無効化)
   - **トークンを書き出す** (Gmail のみ): 選択中アカウントのリフレッシュトークンを `{"refresh_token":"..."}` の JSON に保存します。Web アプリの共通送信者 (`Gmail.TokenSecret`) に設定する用途です。平文なので取り扱いに注意

## Web アプリの共通送信者のトークンを作る

リフレッシュトークンは**発行した OAuth クライアントに紐づく**ので、Web アプリの `Gmail.ClientSecret` に置くクライアントと同じもので発行します。

- **デスクトップ種別を共用する場合**: 上の手順のまま。MailSender で発行 → 「トークンを書き出す」→ Web の `Gmail.TokenSecret`。同じ client_secret.json を Web の `Gmail.ClientSecret` に
- **Web アプリには「ウェブ アプリケーション」種別を使う場合** (推奨。ウェブ種別の client_secret はサーバーだけに置く):
  1. Google Cloud でウェブ種別クライアントを作り、「承認済みのリダイレクト URI」に `http://localhost:53682/` を登録。クライアント ID とシークレットを控える
  2. 管理者の MailSender の「設定」の「ウェブ アプリケーション」欄に ID とシークレットを貼る (デスクトップ欄と両方登録してよい)。「JSON を書き出す」で Web アプリの `Gmail.ClientSecret` 用 JSON (client_secret.json 相当) を保存できます
  3. 「アカウントを追加」→ メニューで「ウェブ アプリケーション で追加」を選び、共通送信者のアカウントで同意 → 「トークンを書き出す」
  4. Web の `Gmail.ClientSecret` に 2 で書き出したクライアント JSON、`Gmail.TokenSecret` に 3 で書き出したトークン

  ウェブ種別で発行したアカウントは一覧に「[ウェブ]」と表示されます (同じアドレスをデスクトップ用・ウェブ用の両方で持てます)。アドレスの下に発行クライアントが表示され、再発行・書き出し・破棄はそのアカウントに対して行われます。送信時も発行したクライアントを自動で使うので、設定を切り替える必要はありません。

Web アプリ側は同意フローを実行しない (リフレッシュトークンからアクセストークンを取るだけ) ので、Web アプリのドメインのリダイレクト URI は不要です。

## ファイルの場所

`%LOCALAPPDATA%\Codeer\MailSender\`

| ファイル | 内容 |
|---|---|
| `settings.json` | Gmail の OAuth クライアント (デスクトップ / ウェブ それぞれの client_id / client_secret)、Microsoft 365 のアプリケーション ID / テナント、画面の拡大率 |
| `accounts.bin` | 登録アカウントごとのリフレッシュトークン / SMTP サーバー情報とパスワード + 選択中アカウント (DPAPI で暗号化。他の Windows アカウントでは読めない) |
| `logs\yyyyMMdd.log` | 送信ログ (日時 / 宛先 / 件名 / 結果 / 元ファイル) |

環境変数 `MAILSENDER_DATA_FOLDER` でこのフォルダを差し替えられます (検証やスクリーンショット撮影で本番の設定・トークンと分けるため)。

## 発行 (単一 exe)

```
dotnet publish Tools\MailSender\MailSender.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o Tools\MailSender\publish
```

`publish\MailSender.exe` を配布します (.NET 10 デスクトップ ランタイムが必要)。成果物は git に入れません (配布パッケージは未整備。当面はソースからビルドして使う。利用者向けの説明は `docs/MailSender.md`)。

## 仕組み

- 送信パッケージ = プレビュー HTML の `<script id="data" type="application/json">` (Extras.Server の `MailPreviewDocument`)。
  宛先・件名・本文は Web 側で解決済み、添付は Base64 で同梱
- 送信部品は `Codeer.Mail/` (このアプリ専用。製品側の Extras とは独立): `Gmail/` = Gmail API + Google OAuth、`Graph/` = Microsoft Graph `/me/sendMail` + Entra v2.0 OAuth (パブリック クライアント)、`Smtp/` = MailKit。共通 = MIME 組み立て / PKCE / ループバック受信
- 同意は PKCE + ループバック (Google は `http://127.0.0.1:<空きポート>/`、Microsoft は `http://localhost:<空きポート>/`)。デスクトップ種別なので client_secret は不要
- Microsoft はリフレッシュのたびに新しいリフレッシュトークンが返ることがあるので、送信中に差し替わったら accounts.bin に保存し直す
