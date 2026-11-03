# 認証 (ログイン) の全体像

Codeer.LowCode.Blazor 本体が持つのは**認可** (ログインしたユーザーに何を見せ、何を書かせるか) だけで、
**認証** (誰がログインしたかの確認) はライブラリに含まれません。認証はホストアプリ (Visual Studio テンプレートで作る Server プロジェクト) の担当で、
その実装をこの Extras が **MIT ライセンス**で提供します。動作を変えたい場合はソースをコピーして改変できます。

| 役割 | 担当 | 場所 |
|---|---|---|
| 認可 (CurrentUser・モジュール / 行 / PageFrame の条件・権限フィールド) | Codeer.LowCode.Blazor 本体 | 製品 ([マニュアル: 認証 / 認可の概要](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/authorization/authorization.md)) |
| ログインアカウントの宣言 (ユーザーモジュールの契約フィールド) | Codeer.LowCode.Blazor.Extras | このリポジトリ (MIT) |
| ID/パスワード照合・パスワードのハッシュ化・外部 IdP・二要素認証 | Codeer.LowCode.Blazor.Extras.Server | このリポジトリ (MIT) |
| ログイン画面・Cookie の発行・エンドポイント (`api/account/*`) | ホストアプリ | [Starter リポジトリ](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Starter) の Cookie ホスト (テンプレートに同梱) |

ホストと本体の境界は「ログイン中ユーザーの id を渡す」の 1 点だけです。ホストは認証が済んだユーザーの id (ユーザーモジュールの `Id`) を Cookie に載せ、
本体はそれを `app.clprj` の Current User Module の行に結びつけて `CurrentUser` と認可に使います。
どの方式でログインしても (パスワード / Entra ID / Google / 二要素認証) 認可の設定は変わりません。

ログイン画面はテンプレートの `login.html` が固定の枠を持ち、外部 IdP を設定するとボタンが増えます (パスワード入力の下)。

<img src="images/login_external.png" alt="ログイン画面 (Entra ID を設定した状態)" style="border: 1px solid #ccc;" width="600">

## 部品

### ユーザーモジュールの契約: LoginAccountContractField

ログインユーザーモジュール (`app.clprj` の Current User Module) に 1 つ置き、「この行がログインアカウントとしてどう振る舞うか」を宣言するフィールドです。
サーバーのログイン処理はこの契約だけを見るので、フィールド名や列名は自由に決められます。

| 宣言 | 内容 |
|---|---|
| LoginName (必須) | ログイン ID を持つフィールド (ユーザー名・メールなど) |
| ExternalLoginName | 外部 IdP が確認した本人 (UPN / メール) と突き合わせるフィールド。空なら LoginName |
| IsActive | 有効フラグ。偽ならログイン拒否 (退職・停止) |
| DisplayName | 画面のユーザー表示と認証アプリの QR に使う名前 |
| TwoFactorEmail | メールのワンタイムコードによる二要素認証の送信先 |
| PasswordField | パスワードの入力フィールド (PasswordField)。指定すると保存時にサーバーがハッシュ / ソルトを作って下の列に書く |
| DbColumnPasswordHash / DbColumnPasswordSalt | パスワード照合用の列 (書き込み専用)。両方空なら外部 IdP 専用 |
| DbColumnTotpSecret / DbColumnTotpConfirmed / DbColumnTotpLastTimestep | 認証アプリ (TOTP) の 3 列。そろえて設定すると有効 |
| DbColumnSessionStamp | セッションの印の列 (書き込み専用。TEXT)。設定すると発行済みの Cookie をサーバー側で失効できる (下の「ログイン済みセッションの失効」)。空なら照合しない |

→ 詳細: [LoginAccountContractField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/LoginAccountContractFieldDesign.md)

デザイナのテンプレート (空のプロジェクト / 標準パターン集 / 業務テンプレート) の `AppUser` には配置済みです (`DbColumnSessionStamp` = `session_stamp` も Designer.Standard 0.8.23 以降のテンプレートでは設定済み)。

<img src="images/login_account_contract.png" alt="デザイナで AppUser のログインアカウント契約を選択した状態" style="border: 1px solid #ccc;" width="800">

### パスワードの保存

契約の `PasswordField` を指定すると、ユーザーを保存するときにサーバー (`PasswordHashingModuleDataIO`。テンプレートの `CustomizedModuleDataIO` はこれを継承済み) が
平文からハッシュ / ソルト (PBKDF2-HMAC-SHA256) を作って契約の列に書きます。ユーザーモジュールに別のフィールドは要りません。
契約の無いモジュール (同じテーブルを参照するパスワード変更ダイアログなど) では [PasswordHashField](PasswordHashField.md) を使います。

### ID/パスワードのログイン: PasswordLogin / LoginAccountStore

`Codeer.LowCode.Blazor.Extras.Server.Auth.PasswordLogin` がログインの検証の流れ (契約の確認 → 総当たり対策 → パスワード照合 → 二要素認証) を 1 回の呼び出しで行い、
結果 (使えない / ロック中 / 不一致 / 二要素認証の途中 / 成功) を返します。テンプレートの `AccountController.Login` はこれを呼んで、結果を HTTP (404 / 429 / 401 / 200) と監査ログに写し、
成功したら Cookie を発行します。パスワード照合そのものは `LoginAccountStore` がデザインの契約からテーブル・列を引いて行います (有効フラグが偽の行は拒否)。
初回起動でユーザーが 0 件なら `admin` / `admin` を作ります (テンプレートの `CookieAuthentication.cs`)。

### ログイン済みセッションの失効: セッションの印

パスワードの流出・退職・停止のときに、発行済みの Cookie をサーバー側でその場で全部使えなくする仕組みです。
Cookie は状態を持たない (サーバーは発行した Cookie を覚えていない) ので、ユーザー行に「セッションの印」(乱数) を 1 つ持ち、
ログインで Cookie に同じ値を積み、リクエストごとに今の行の値と照合します。印を新しい値に書き換えると、それまでの Cookie は全部照合に落ちます
(ASP.NET Identity の SecurityStamp と同じ考え方)。

**有効にする**: 契約の `DbColumnSessionStamp` に列を 1 本指定します (書き込み専用列なので、デザイナの DDL 生成に載ります。型は TEXT)。
空なら照合しません (従来どおり、Cookie は有効期限まで使えます)。テンプレートは Cookie 認証の `OnValidatePrincipal` に `LoginSessionValidation.ValidatePrincipalAsync` を結線しているので、列を足すだけで動きます。

**印が書き換わる (= 失効する) とき**

| きっかけ | 書き換える所 |
|---|---|
| パスワードの変更 (契約の PasswordField / 同じ表を書く別モジュールの PasswordHashField) | 保存 (`PasswordHashingModuleDataIO`)。行の更新と同じトランザクション |
| 停止 (ユーザーモジュールの保存で IsActive が偽) | 同上。有効に戻しても停止前の Cookie は生き返らない |
| 全端末ログアウト (管理者が特定のユーザーを) | [SessionRevokeButtonField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/SessionRevokeButtonFieldDesign.md)。印を NULL にする (次のログインで採番) |

管理者が特定のユーザーを全端末からログアウトさせるのは、ユーザーモジュールの詳細画面に置く SessionRevokeButtonField です (テンプレートの `AppUser` に設置済み)。列は契約の `DbColumnSessionStamp` と同じ列にし、通常の保存と同じ権限 (その行を編集できる人だけ) で動きます。

表示名など他の項目だけの保存では書き換えません。照合では行が無い (論理削除したユーザーを含む。ログインも通りません)・IsActive が偽・印が合わない Cookie を無効にします (`RejectPrincipal` と Cookie の削除。保護された API は 401)。
外部 IdP のログイン・ネイティブアプリのチケットで発行した Cookie も同じ印を持つので、同じく失効の対象です。

**保証の範囲と限界**

- 印を書き換えた保存のコミット後の、次の認証チェックから拒否します。処理中のリクエストは止めません
- 失効は「そのユーザーの全端末」一括です。端末ごとの一覧・個別の失効はありません
- ブラウザ内の Cookie を遠隔で消すのではなく、届いた Cookie をサーバーが受け付けなくなる仕組みです
- 全端末ログアウトは新しいログインを止めません。止めるなら IsActive を偽にします
- リクエストごとにユーザー行を主キーで 1 行読みます。キャッシュしないので失効の遅れはなく、複数インスタンスでも DB が正本です。照合の読みに失敗したとき (DB の障害) はエラー (500) になり、Cookie は消しません (復旧後はそのまま使えます)
- 印の無い Cookie (列を足す前のログイン) は無効です。列を足した時点で全員が再ログインになります。既存ユーザーの印は最初のログインで採番するので、移行スクリプトは要りません

**通常のログアウトとの違い**: `api/account/logout` はその端末 (ブラウザ) の Cookie を消すだけで、印は変えません。
その Cookie を他所に写し取られていた場合、有効期限までは使えます。パスワードの変更・停止・SessionRevokeButtonField は印を書き換えるので、他の端末と写し取られた Cookie もまとめて使えなくなります。

**直接 SQL で変えるとき (契約)**: パスワードのハッシュや有効フラグをアプリの保存を通さずに SQL で書き換えるときは、印の列も新しい乱数 (他と重ならない文字列) に書き換えてください。
書き換えなければ発行済みの Cookie は有効のままです。

**外部 IdP の注意**: Entra ID 等の IdP 側でアカウントを停止したりパスワードを変えたりしても、アプリの Cookie は自動では失効しません
(IdP は本人確認の手段で、セッションはアプリの Cookie のため)。アプリ側でも IsActive を偽にするか、SessionRevokeButtonField で失効させます。

### 総当たり対策: LoginLockout

`Codeer.LowCode.Blazor.Extras.Server.Auth.LoginLockout` がアカウントごとにログインの失敗 (パスワード・二要素認証のコードの不一致) を数え、
続けて `MaxFailedAttempts` 回 (既定 10) に達したら、最後の失敗から `LockoutMinutes` 分 (既定 5) そのアカウントのログインを受け付けません (正しいパスワードでも)。
ロックが繰り返されるたびに長さは倍になり (5 分 → 10 分 → 20 分 …)、上限は `MaxLockoutMinutes` (既定 1440 = 1 日) です。サインインできたら全部数え直します。
二要素認証のコードを求めた段階、メールのコードの期限切れ (`code_expired`)、照合中の例外 (DB の障害など) は失敗に数えません。既定では、1 つのアカウントに試せるのは最初の 24 時間で 90 回、以降は 1 日 10 回ほどです (1 インスタンスのとき。記録は最後の試行から 30 日残るので、黙って待っても段階は戻りません)。

- 鍵はアカウントの Id です (`LoginAccountStore.FindByLoginNameAsync` で引く)。DB の照合順序で同じ行に当たる別表記 (大文字小文字・全角半角など) も 1 つに数えます
- 存在しないログイン名もログイン名ごとに同じように数えてロックします (ロックの応答でアカウントの有無が分からないように)。大文字小文字・前後の空白は区別しません。空のログイン名は数えません
- ロック中の `api/account/login` は 429 を返し、ログイン画面は「しばらくしてからやり直してください」を出します
- 照合の前に 1 回分を数えるので、同時に大量に送られても回数を超えて照合しません (1 つのプロセスの中での保証)
- 回数はサーバー側のキャッシュ (`IDistributedCache`) に置くので DB 列は要りません。記録は最後の試行から 30 日で消えます。キャッシュが使えないとき (共有キャッシュの障害など) は、ロックを判定できないのでパスワードのログインはエラーになります
- 複数インスタンスなら共有キャッシュ (Redis 等) にします。読んで書くまでの排他はインスタンスの中だけなので、インスタンスをまたいで同時に送られたときの回数は保証しません (数え漏らしが起きる)。厳密に抑えたいときは前段の IP 単位の制限と併用します。共有キャッシュを複数のアプリ (本番と検証など) で使うときは、`InstanceName` でアプリごとに分けます (鍵はアカウントの Id なので、分けないと別のアプリの同じ Id と回数を共有します。メールのワンタイムコードも同じキャッシュに置きます。こちらは送信先が違うコードを通さないので取り違えは起きませんが、発行したコードが上書きされて再送が要ることがあります)
- ロックは時間が経つまで解けません (管理者がパスワードを設定し直しても解けない)。急いで解くときは、キャッシュのキー `codeer.loginlockout.<アカウントの Id>` (存在しないログイン名は `codeer.loginlockout.name:<大文字にしたログイン名>`) を消すか、既定のメモリキャッシュならサーバーを再起動します
- ログイン ID を知っていれば、他人がそのアカウントをロックさせ続けられます (ロックアウト方式に共通の性質)。また 1 つのパスワードを多数のアカウントに試す攻撃 (パスワードスプレー) はアカウントごとの数えでは止まりません。どちらも前段 (WAF・リバースプロキシの IP 単位の制限) で補います
- appsettings の `LoginLockout` で変えます。`MaxFailedAttempts` を 0 にすると無効です

```json
"LoginLockout": { "MaxFailedAttempts": 10, "LockoutMinutes": 5, "MaxLockoutMinutes": 1440 }
```

### 外部 IdP: Entra ID / Google / AWS Cognito / OpenID Connect

appsettings に IdP の設定を書くだけでログイン画面にボタンが増えます。IdP は本人確認の手段で、セッションは Cookie のままです。
確認できた本人を契約の ExternalLoginName (空なら LoginName) の列でユーザー行に解決してから Cookie を発行します (事前登録制)。

→ 詳細: [外部ログイン](ExternalLogin.md)

### 二要素認証: 認証アプリ (TOTP) / メールのワンタイムコード

契約の TOTP 3 列を設定すると認証アプリ (RFC 6238)、`TwoFactorEmail` を設定するとメールのワンタイムコードが、パスワード成功後の 2 段階目になります。
認証アプリの登録を解除するボタンは [TotpResetButtonField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/TotpResetButtonFieldDesign.md) (管理者用。ユーザーモジュールに置き、通常の保存と同じ権限で制御) と
[MyTotpResetButtonField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/MyTotpResetButtonFieldDesign.md) (本人用。どこにでも置ける) の 2 つです。

→ 詳細: [二要素認証](TwoFactorLogin.md)

### ネイティブアプリ (MAUI)

MAUI クライアントも同じサーバーの `api/account/*` を使います。外部 IdP のログインだけは、システムブラウザで IdP に行き、使い捨てチケットでアプリに戻る形です
([外部ログイン](ExternalLogin.md) の「ネイティブアプリ」)。

## ホストに入っているもの (Starter の Cookie テンプレート)

| ファイル | 役割 |
|---|---|
| `Controllers/AccountController.cs` | `login_options` / `login` (検証の流れは `PasswordLogin`。結果を HTTP と監査に写して Cookie を発行する) / `login/{provider}` / `logout` / `current_user` |
| `Infrastructure/CookieAuthentication.cs` | Cookie 認証 (有効期間はスライディングで「ログイン状態を保持する」が 14 日・それ以外は 8 時間。`OnValidatePrincipal` でセッションの印を照合) と外部 IdP の登録、初回 admin の作成 |
| `Extras/ExternalLoginTable.cs` | appsettings の IdP 設定 → プロバイダの対応表 |
| `Infrastructure/ExternalLoginUserResolver.cs` | 外部 IdP の本人 → ユーザー行 (事前登録制) |
| `Infrastructure/CustomizedModuleDataIO.cs` | 保存時のパスワードハッシュ化 (`PasswordHashingModuleDataIO` を継承) |
| `wwwroot/login.html` + `login.js` | ログイン画面 (見た目は html を直接編集) |

これらはテンプレートが生成するソースなので、必要に応じて書き換えられます。認証を外す (前段で認証済みなど) 手順は Starter の `CLAUDE.md`「認証を外す」にあります。

## 関連

- [外部ログイン (Entra ID / Google / AWS Cognito / OpenID Connect)](ExternalLogin.md)
- [二要素認証 (認証アプリ TOTP / メールのワンタイムコード)](TwoFactorLogin.md)
- [PasswordHashField](PasswordHashField.md)
- [マニュアル: 認証 / 認可の概要](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/authorization/authorization.md) / [ログインとユーザーの初期設定](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Manual/blob/main/JP/authorization/auth_getting_started.md)
