# Gmail 送信アプリ (Windows) 仕様 — 案 (2026-08-28)

## 目的

「担当者本人の Gmail から送りたい」要件を、**本人のトークンを本人の PC の外に出さずに**満たす。
サーバー側には本人のトークンもクライアントシークレットも置かない。

背景: サーバーにユーザー単位のリフレッシュトークンを暗号化保存する方式 (GmailTokenField) は
「10 人分の送信資格情報が 1 か所・1 鍵に集まる」構造で、リフレッシュトークンは client_id だけで
アクセストークンに交換できる (Google の現行仕様では client_secret は Optional) ため、トークン単体が
送信権限そのもの。組織として許容しにくい、という判断で 0.7.0 で削除した。

## 全体の流れ

1. Web アプリ (CLB) で MailField / BulkMailField の **プレビュー** をダウンロードする (既存機能。宛先・変数は解決済み)
2. ダウンロードしたプレビュー HTML を **Gmail 送信アプリで開く** (ダブルクリック / D&D / ファイル選択)
3. アプリが内容を表示 → 「送信」→ 本人のトークンで Gmail API へ送る。サーバーは関与しない

Web 側の役割は「送信パッケージを作って渡す」だけ。送信履歴はサーバーに残らない (v1 はアプリ側のローカルログ)。

## Web (CLB / Extras) 側の変更

- プレビュー HTML に機械可読データを同梱する:
  `<script type="application/json" id="clb-mail-package">` に次を入れる (人が読む HTML はそのまま)
  ```json
  {
    "Version": 1,
    "CreatedAt": "2026-08-28T10:00:00+09:00",
    "Messages": [
      { "To": ["a@x"], "Cc": [], "Bcc": [], "ReplyTo": "", "Subject": "...", "Body": "...", "IsBodyHtml": false,
        "Attachments": [ { "FileName": "a.txt", "ContentType": "text/plain", "Base64": "..." } ] }
    ]
  }
  ```
  - 単発は 1 件、一斉は宛先ごとに解決済みの本文で N 件 (除外された宛先は含めない)
  - 添付は Base64 で同梱 (合計 10MB を上限とし、超える場合は同梱せずプレビューに警告を出す)
  - 宛先一覧は既に本人の PC に落ちているものなので、追加の露出は無い
- 差出人の指定は無い (本人のアカウントから送るので From は本人になる。表示名は Gmail 側の設定)

## アプリ

- 置き場所: Extras リポジトリ `Tools/GmailSender/` (WPF・.NET 8・CLB 非依存)。配布は GitHub Releases の zip (単一 exe 発行)。git に成果物は入れない
- 機能
  1. **トークン発行**: Google の同意画面 (システム既定ブラウザ) → ループバック `http://127.0.0.1:<port>/` で認可コードを受ける → PKCE で交換。
     OAuth クライアントは **デスクトップアプリ種別** (client_secret 不要。client_id はアプリ設定に持つ)。スコープは `gmail.send` + `openid email`
  2. **トークン保管**: リフレッシュトークンを **DPAPI (CurrentUser)** で暗号化し `%LOCALAPPDATA%\Codeer\GmailSender\token.bin` に保存。同意したアカウントのアドレスも保持して画面に表示
  3. **トークン破棄**: Google の revoke エンドポイント呼び出し + ローカルファイル削除
  4. **送信**: プレビュー HTML を開く → パッケージを表示 (件数・宛先・件名・本文) → 「送信」→ 1 通ずつ Gmail API `users.messages.send`。
     レート制御・再試行・日次上限打ち切りは Extras.Server の GmailApiMailSender と同じ規則。結果 (成功 / 失敗と理由) を一覧表示し、ローカルログ (`%LOCALAPPDATA%\Codeer\GmailSender\logs\`) に残す
- 送信ロジックの共有: MIME 組み立て (MailKit)・Gmail REST 呼び出し・レート制御を小さなライブラリ
  `Codeer.LowCode.Blazor.Extras.Gmail` (仮) に切り出し、Extras.Server とアプリの両方が参照する (二重実装しない)

## セキュリティ上の位置づけ

- トークンは本人の Windows アカウントでしか復号できない (DPAPI)。PC を離れない
- サーバー・DB・バックアップに送信資格情報が存在しない。事故は本人の PC 1 台に閉じる
- 本人がいつでも「破棄」でき、Google アカウント側の「アプリのアクセス削除」でも無効化できる
- client_secret はどこにも無い (デスクトップ種別 + PKCE)

## v1 で割り切ること

- サーバーの送信履歴 (MailHistory) には残らない。必要になれば「結果ファイルを Web にアップロードして履歴に反映」を後で足す
- Web での「送信」ボタンから直接アプリを起動する連携 (カスタム URL スキーム) はやらない。ファイル経由のみ
- Windows のみ

## 未決

- 添付の上限値 / 添付非対応にするか
- アプリ名 (仮: Codeer Gmail Sender)
- Extras.Server の GmailApiMailSender からの切り出し範囲 (MIME + REST + レート制御まで)
