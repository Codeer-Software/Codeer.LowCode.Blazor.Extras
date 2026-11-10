# WebApiField - 外部 Web API の呼び出し

**ボタンを押す (またはスクリプトで `Execute()`) と、サーバーが外部の Web API を呼んで結果を返すフィールド**です。
呼ぶ先はサーバーの設定にある「接続先」の名前で指します。接続先の URL と認証のキー・トークンはサーバーの設定だけが持ち、ブラウザには出ません。

## いつ使うか

- 社外のサービス (CRM・会計・在庫など) の API から値を取ってきて画面に出したい
- 画面の値を外部のサービスに登録したい (JsonField のノードに結線した入力欄の値をそのまま送れます。下の「JsonField とつなぐ」。スクリプトで値を `JsonObject` に組んで `Execute(args)` に渡すこともできます)
- 応答の値をスクリプトなしで画面に出したい (応答を JsonField に入れ、ノードに結線したフィールドに出します。配列は一覧の行に出せます)
- API のキーをブラウザに渡したくない (スクリプトオブジェクトの `WebApiService` はブラウザから直接呼ぶので、キーを持たせられません)

## プロパティ

| C#名 | 日本語表示名 | 型 | 既定値 | 説明 |
|---|---|---|---|---|
| **Name** | 名前 | string | `""` | フィールド識別子 |
| **ConnectionName** | 接続先 | string | `""` | サーバーの設定 (`WebApi.Connections`) の接続先の名前 (必須) |
| **Method** | メソッド | WebApiMethod | `Get` | `Get` / `Post` / `Put` / `Delete` |
| **Path** | パス | string | `""` | 接続先の `BaseUrl` からの相対パス。`{名前}` で引数の値を差し込める (例 `users/{id}/orders`) |
| **ArgumentsField** | 引数の JsonField | string | `""` | 同じモジュールの JsonField の名前。ボタンとスクリプトの `Execute()` (引数なし) は、その JsonField の今の内容を引数として送る (下の「JsonField とつなぐ」) |
| **ResultField** | 結果の JsonField | string | `""` | 同じモジュールの JsonField の名前。成功した応答の本文 (空でなければ) を、その JsonField に入れる (下の「JsonField とつなぐ」) |
| **UseUpperCamelCaseNames** | 名前を先頭大文字で送る | bool | `false` | `Execute(args)` で渡した引数の名前の先頭 1 文字を大文字にする。`false` なら小文字にする (下の「名前の大文字小文字」) |
| **ShowButton** | ボタンを表示 | bool | `true` | `false` なら画面には何も出ず、スクリプトの `Execute()` からだけ呼ぶ |
| **ButtonText** | テキスト | string | `""` | ボタンの文言。空なら「実行」 |
| **OnCompleted** | 呼び出した後 | string | `""` | ボタンで呼んだ後に呼ぶスクリプトのメソッド。引数は結果 (`WebApiResult`) |

接続先・メソッド・パスはサーバーがデザインから読みます (画面からは送りません)。

## 動作

### 送る内容

引数は JSON 1 つです。ふつうはオブジェクトですが、配列 (`[...]`) や値だけ (`"ok"`・`123`) も送れます (下の「外側がオブジェクトでない引数」)。

| 呼び方 | 引数 |
|---|---|
| スクリプトの `Execute(args)` | 渡した `JsonObject` (配列の `JsonObject` も可。「引数の JsonField」は使いません) |
| スクリプトの `Execute()` | 「引数の JsonField」の今の内容 (`GetJson()` の結果そのまま。値なしなら `{}`)。設定が無ければ `{}` |
| ボタン | `Execute()` と同じ |

サーバーは引数から URL とボディを組み立てます。

- **パスの差し込み**: `Path` の `{名前}` を、引数の最上位の同じ名前の値 (先頭 1 文字の大文字小文字は区別しません。2 文字目以降は区別します。先頭 1 文字だけが違う名前が両方あるときは完全に一致するほうを使います。文字列・数値・真偽値) で置き換えます。値は URL エスケープします (`/` や `?` もそのまま 1 つの値になります)。値が無い・空・`.`・`..` のときは呼びません
- **Get / Delete**: 差し込みに使わなかった最上位の文字列・数値・真偽値をクエリ文字列にします (オブジェクト・配列・null は載せません)。ボディはありません
- **Post / Put**: 引数の JSON 全体をボディにします (`Content-Type: application/json`)

#### 外側がオブジェクトでない引数

引数が配列や値だけのときは、取り出せる名前と値がありません。

- **Post / Put**: 引数をそのままボディにします (`[{"code":"A"},{"code":"B"}]` ならそれがボディ)
- **Get / Delete**: クエリもボディも付けません (`Path` に書いたクエリはそのまま)
- `Path` に `{名前}` があるときは差し込む値が無いので呼びません (「パスの差し込み 'X' の値がありません。」)

#### 名前の大文字小文字

**スクリプトの `Execute(args)` で渡した引数**は、送る前にすべてのオブジェクトの名前 (入れ子・配列の中のオブジェクトも) の先頭 1 文字を小文字にします。クエリ文字列の名前にもボディの名前にも同じ規則が掛かります。

- `args.id = 1` と書いても `args.Id = 1` と書いても、送るのは `{"id":1}` (クエリなら `id=1`) です
- 2 文字目以降は変えません (`UserName` は `userName`、`user_name` は `user_name`)
- 先頭が大文字の名前 (PascalCase) を取る API には「名前を先頭大文字で送る」(`UseUpperCamelCaseNames`) を `true` にします (`{"Id":1}`)
- 揃えた結果が同じ名前になる名前が同じオブジェクトにあるとき (`Id` と `id`) は、後のものを送ります

**「引数の JsonField」から送る引数**は名前を変えません。JsonField の中身 (ノードの `Key` と、ノードに結線していないキー) をその綴りのまま送ります。

応答 (結果の `JsonObject`・`Body`・「結果の JsonField」に入る内容) の名前は変えません。

### 結果

| メンバー | 型 | 説明 |
|---|---|---|
| `StatusCode` | int | 相手の HTTP の状態コード。4xx / 5xx もそのまま入ります。呼び出せなかったときは `0` |
| `IsSuccess` | bool | `StatusCode` が 200〜299 |
| `JsonObject` | JsonObject | 応答のボディ。配列なら要素の並び (`foreach` で回せます)。JSON でない応答・数値や文字列だけの応答は空 |
| `Body` | string | 応答のボディの文字列そのまま。無ければ空文字列 |
| `ErrorMessage` | string | 呼び出せなかったときの説明 (下の表)。「結果の JsonField」に入れられなかった (JsonField が受け付けない) ときもその理由が入ります。それ以外は空 |

ボタンで呼んだときは、失敗 (`IsSuccess` でない) か `ErrorMessage` が空でないときにエラーの通知を出します (`ErrorMessage`、空なら「Web API の呼び出しに失敗しました (HTTP 状態コード)」)。成功は通知しません。そのあと「呼び出した後」のスクリプトを呼びます (「結果の JsonField」に入れた後です)。
スクリプトの `Execute()` は通知を出しません (結果は戻り値で見ます)。

### JsonField とつなぐ

「引数の JsonField」(`ArgumentsField`) と「結果の JsonField」(`ResultField`) に同じモジュールの JsonField を指定すると、スクリプトなしで画面の値を送り、応答を画面に出せます。テーブルを持たないモジュールの JsonField でも使えます。

- **引数**: ボタンとスクリプトの `Execute()` は、「引数の JsonField」の `GetJson()` (JsonField の値に、ノードに結線したフィールドの今の値を当てた JSON) を名前を変えずに送ります。JsonField が値なしなら `{}` です
- **結果**: 成功 (`IsSuccess`) した応答の本文が空でなければ、そのまま「結果の JsonField」に `SetJson` で入れます。ノードに結線したフィールドに値が出ます (値が配られるのは、キーを JsonField の `JsonNodes` に宣言したノードだけです)。本文の外側がオブジェクトか配列か値だけかは見ません。その JsonField の形に合うかは JsonField が決めます (下の「配列が返る API・配列を送る API」)
  - 本文が空 (空白だけを含む) のとき、失敗したときは何もしません (JsonField はそのまま)
  - JsonField が受け付けない (外側の形が合わない・応答の値がノードの型に合わない・JSON として読めない) ときは JsonField を変えず、`ErrorMessage` に理由が入ります (`StatusCode`・`JsonObject`・`Body` は応答のまま。ボタンならエラーを通知します)
  - ボタン・`Execute()`・`Execute(args)` のどれで呼んでも同じです
- 指定した名前のフィールドが無い・JsonField でないときは、引数は `{}`、結果は何もしません (デザインチェック `WebApiFieldDesign:5` で検出します)
- 「結果の JsonField」に入れた内容の保存は JsonField の規則に従います (ノードに結線していないキーは画面からの保存では DB に入りません。行を親の列に保存する一覧の行モジュールの JsonField では、結線していないキーも保存されます)

例: Id を入力して「取得」を押すと `users/{id}` を呼び、応答の `name` と `email` を画面に出す (`QueryId`・`UserName`・`UserEmail` をレイアウトに置き、JsonField の `UserQuery`・`UserJson` はレイアウトの「表示しないデータ項目」に入れます)

```json
[
  {
    "JsonFieldSettings": { "JsonNodes": [ { "Key": "id", "NodeType": "Text" } ] },
    "DbColumn": "",
    "Name": "UserQuery",
    "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.JsonFieldDesign"
  },
  { "DbColumn": "UserQuery/id", "Name": "QueryId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
  {
    "JsonFieldSettings": { "JsonNodes": [ { "Key": "name", "NodeType": "Text" }, { "Key": "email", "NodeType": "Text" } ] },
    "DbColumn": "",
    "Name": "UserJson",
    "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.JsonFieldDesign"
  },
  { "DbColumn": "UserJson/name", "Name": "UserName", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
  { "DbColumn": "UserJson/email", "Name": "UserEmail", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
  {
    "ConnectionName": "Crm",
    "Method": "Get",
    "Path": "users/{id}",
    "ArgumentsField": "UserQuery",
    "ResultField": "UserJson",
    "ShowButton": true,
    "ButtonText": "取得",
    "Name": "UserApi",
    "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign"
  }
]
```

#### 配列が返る API・配列を送る API

JsonField の `NodeType: Array` のノードに一覧フィールドを結線すると、配列の要素が一覧の行になります (一覧の `DbColumn` は `JsonField名/キー`)。スクリプトなしで配列を受け取って一覧に出せます。

- 応答が `{"items":[...]}` (オブジェクトの中の配列): 普通の JsonField に `items` を Array のノードとして宣言します
- 応答が `[...]` (配列から始まる): JsonField の `UseNodeValueAsJson` を `true` にし、Array のノードを 1 つだけ宣言します (JsonField の JSON がそのノードの値 = 配列そのものになります)
- 一覧の行モジュールはテーブルを持たず、行モジュールの JsonField が要素の形 (キーと型) を決めます。要素のキーに結線したフィールドに値が出ます
- 同じ JsonField を「引数の JsonField」にすると、画面で編集した後の行を配列で送れます (`Post` / `Put` のボディ。上の「外側がオブジェクトでない引数」)

JsonField と一覧の規則の詳細は、本体の JsonField の Docs (「一覧をノードに結線する」「ノードの値だけを JSON にする」) と ListField の Docs (「列モード」) を見てください。

例: 「一覧を取得」を押すと `users` を呼び、応答 `[{"id":1,"name":"Leanne Graham"},{"id":2,"name":"Ervin Howell"}]` の要素を一覧 `Users` の行に出す (`UserList` はレイアウトの「表示しないデータ項目」に入れます)

```json
[
  {
    "JsonFieldSettings": { "JsonNodes": [ { "Key": "items", "NodeType": "Array" } ] },
    "DbColumn": "",
    "UseNodeValueAsJson": true,
    "Name": "UserList",
    "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.JsonFieldDesign"
  },
  {
    "DbColumn": "UserList/items",
    "SearchCondition": { "ModuleName": "UserRow" },
    "Name": "Users",
    "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.ListFieldDesign"
  },
  {
    "ConnectionName": "Crm",
    "Method": "Get",
    "Path": "users",
    "ResultField": "UserList",
    "ShowButton": true,
    "ButtonText": "一覧を取得",
    "Name": "UsersApi",
    "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign"
  }
]
```

一覧の行モジュール `UserRow` (テーブルなし。`ListLayouts` に `UserNo`・`UserName` を並べます):

```json
[
  {
    "JsonFieldSettings": { "JsonNodes": [ { "Key": "id", "NodeType": "Number" }, { "Key": "name", "NodeType": "Text" } ] },
    "DbColumn": "",
    "Name": "Json",
    "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.JsonFieldDesign"
  },
  { "DbColumn": "id", "Name": "UserNo", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.NumberFieldDesign" },
  { "DbColumn": "name", "Name": "UserName", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" }
]
```

応答が `{"total":2,"items":[...]}` の形なら、`UserList` の `UseNodeValueAsJson` を外します (他は同じ。`total` を出すなら Number のノードを足して結線します)。

### エラーの返り方

呼び出せなかったときは例外にはならず、`StatusCode` が `0` で `ErrorMessage` に説明が入ります。説明に認証のキー・トークン・キー入りの URL は入りません。

| 場合 | ErrorMessage |
|---|---|
| 接続先の名前が設定に無い | 接続先 'X' が設定にありません。 |
| 認証方式の名前が無い | 接続先 'X' の認証方式 'Y' がありません。 |
| 認証の設定が足りない (キーが空など) | 接続先 'X' の認証の設定が足りません。 |
| トークンを取得できない (OAuth2) | 接続先 'X' の認証に失敗しました。 |
| BaseUrl が正しくない | 接続先の BaseUrl が正しくありません。 |
| パスの `{` `}` が壊れている | WebApiField のパスの書き方が正しくありません。 |
| 差し込みの値が無い | パスの差し込み 'id' の値がありません。 |
| URL が BaseUrl の外を指す | 組み立てた URL が接続先の BaseUrl の外を指しています。 |
| 引数が JSON として読めない | 引数が JSON として読めません。 |
| タイムアウト | 接続先 'X' から時間内に応答がありませんでした。 |
| 接続できない | 接続先 'X' に接続できませんでした。 |
| 応答が上限 (10MB) を超えた | 接続先 'X' の応答が大きすぎます。 |
| その他 (独自の認証方式の例外など) | 接続先 'X' の呼び出しに失敗しました。 |
| 実行中・書き込めない・無効 (画面側) | 実行できない状態のため呼び出しませんでした (実行中・書き込み不可・無効)。 |

相手が 4xx / 5xx を返したときは、その状態コードとボディがそのまま返ります (`ErrorMessage` は空)。

### URL の制限

- 送り先は必ず接続先の `BaseUrl` の配下です。同じ scheme・ホスト・ポートで、パスが `BaseUrl` のパスで始まる URL にしか送りません。`..` で上の階層に出る・別のホストを指す (`https://...`・`//host`・`@host`) パスや値は、配下のパスとして扱われるか、送らずにエラーになります。差し込んだ値も含めてパスを復号したとき (`%2F`・`%5C`・`%2E` や二重の符号化も解いたとき) に `.` / `..` の区間 (`/` か `\` で区切ったもの) があれば送りません (`a/b`・`v1.2` のような値は送ります)
- `BaseUrl` は `http` / `https` の絶対 URL で、クエリ・ユーザー情報は書けません
- リダイレクトは追いません (3xx はそのまま返ります)
- 応答のボディは 10MB までです

## 権限

- **このフィールドに書き込めるユーザーが呼べます** (JobTriggerField と同じ)。書き込めないとき (権限の書き込み条件・閲覧モード・新規作成/更新ができないモジュール) は、ほかのボタンと同じく押せず、`Execute()` も呼びません
- 呼べるユーザーを絞るときは、PermissionField の書き込み条件を **ログインユーザー (CurrentUser) だけで決まる条件** にしてください。行の値を見る条件はサーバーでの判定には使われません (デザインチェックで警告が出ます)
- 監査ログを有効にしているときは「誰が・どのモジュールのどの接続先を・相手の状態コード」が 1 件記録されます。`Get` は参照 (DataRead)、`Post` / `Put` / `Delete` は変更 (DataWrite。呼ぶ前に試行の行も書きます) の区分です。相手の 4xx / 5xx と呼び出せなかったときは失敗として残ります

## 接続先の設定

サーバーの `appsettings.json` の `WebApi` セクションに接続先を並べます。キー・パスワードは環境変数やユーザーシークレットなど、リポジトリに入らない場所に置いてください。

```json
"WebApi": {
  "Connections": [
    {
      "Name": "Crm",
      "BaseUrl": "https://api.example.com/v1/",
      "TimeoutSeconds": 30,
      "Authentication": "ApiKey",
      "ApiKey": { "HeaderName": "X-Api-Key", "Key": "..." }
    },
    {
      "Name": "Billing",
      "BaseUrl": "https://billing.example.com/",
      "Authentication": "OAuth2ClientCredentials",
      "OAuth2ClientCredentials": { "TokenUrl": "https://login.example.com/oauth2/token", "ClientId": "...", "ClientSecret": "...", "Scope": "billing.read" }
    }
  ]
}
```

| 項目 | 説明 |
|---|---|
| `Name` | 接続先の名前 (フィールドの「接続先」に書く。大文字小文字は区別しない) |
| `BaseUrl` | 呼ぶ URL の土台。フィールドの `Path` はこの下だけを指せます |
| `TimeoutSeconds` | 応答を待つ秒数 (既定 30) |
| `Authentication` | 認証方式の名前 (下の表)。空なら `None` |
| 方式と同じ名前の子 | その方式の設定 (`"ApiKey": {...}` など) |

## 認証方式

| Authentication | 設定 | 付くもの |
|---|---|---|
| `None` (または空) | なし | なし |
| `ApiKey` | `HeaderName`・`Key`・`QueryName` | `QueryName` があればクエリ `QueryName=Key`、無ければヘッダー `HeaderName: Key` |
| `Bearer` | `Token` | `Authorization: Bearer Token` (固定のトークン) |
| `Basic` | `UserName`・`Password` | `Authorization: Basic base64(UserName:Password)` |
| `OAuth2ClientCredentials` | `TokenUrl`・`ClientId`・`ClientSecret`・`Scope` | `TokenUrl` に `grant_type=client_credentials` (と `client_id`・`client_secret`・`scope`) をフォームで POST して得た `access_token` を `Authorization: Bearer` で付ける |

`OAuth2ClientCredentials` のトークンは期限 (`expires_in`。無ければ 3600 秒) の 1 分前まで接続先ごとにサーバーのメモリに持ちます。同時に来た呼び出しでもトークンの取得は 1 回です。相手が 401 を返したら 1 回だけ取り直して送り直します。

## 独自の認証方式

署名を計算する・独自のトークンを取るなど、標準に無い方式は `IWebApiAuthenticator` (Codeer.LowCode.Blazor.Extras.Server) を実装します。

```csharp
public interface IWebApiAuthenticator
{
    string Name { get; }   // appsettings の Authentication に書く名前
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken);
}
```

- `request` にヘッダーやクエリを付けて `next(request)` で送ります。送ったあとの応答を見て、付け直して `next(request)` をもう一度呼べます (`next` は `request` の写しを送るので、同じ `request` を何度でも渡せます)
- トークンの取得など、別の URL への要求も `next` で送れます (タイムアウトは接続先の設定)
- `cancellationToken` は接続先のタイムアウトです (`next` で送る要求には掛かっています)。応答の本文を読む (`ReadAsStringAsync(cancellationToken)` など)・待つ処理には渡してください。渡さないと、相手が本文の途中で止まったときに戻りません
- 方式の設定は `connection.GetAuthenticationSettings<T>()` で読みます (接続先の中の `Authentication` と同じ名前の子セクションを `T` として読む)
- 例外を投げると、呼び出しは `StatusCode` 0・「接続先 'X' の呼び出しに失敗しました。」になります (例外の文言は画面に出しません)

```csharp
public class HmacSettings
{
    public string KeyId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public class HmacAuthenticator : IWebApiAuthenticator
{
    public string Name => "Hmac";

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
    {
        var settings = connection.GetAuthenticationSettings<HmacSettings>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.Secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + request.RequestUri!.PathAndQuery)));
        request.Headers.Add("X-Key-Id", settings.KeyId);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Signature", signature);
        return await next(request);
    }
}
```

```json
{ "Name": "Partner", "BaseUrl": "https://partner.example.com/api/", "Authentication": "Hmac",
  "Hmac": { "KeyId": "...", "Secret": "..." } }
```

作った方式は `WebApiApi` のコンストラクタに渡します。標準と同じ名前を付けると、標準の方式の代わりにそちらが使われます。

```csharp
new WebApiApi(WebApiSettings.Load(configuration.GetSection("WebApi")), new HmacAuthenticator())
```

## スクリプト

| メンバー | 型 | 説明 |
|---|---|---|
| `Execute(args)` | WebApiResult | `JsonObject` (配列も可) を引数にして呼ぶ (名前の先頭 1 文字を揃える)。通知は出さない |
| `Execute()` | WebApiResult | 「引数の JsonField」の内容 (設定が無ければ `{}`) を引数にして呼ぶ |
| `IsRunning` | bool | 呼び出しを頼んでいる間 `true` |

ButtonField から引数を付けて呼ぶ例 (「ボタンを表示」を `false` にし、このフィールドをレイアウトの「表示しないデータ項目」に入れます):

```csharp
void SearchButton_OnClick()
{
    var args = new JsonObject();
    args.Code = CustomerCode.Value;
    var ret = CustomerApi.Execute(args);
    if (!ret.IsSuccess)
    {
        Toaster.Error("取得できませんでした: " + ret.ErrorMessage);
        return;
    }
    CustomerName.Value = ret.JsonObject.Name;
}
```

画面の値を登録する例 (「メソッド」を `Post` にしたフィールド。送りたい値を引数に組みます):

```csharp
void RegisterButton_OnClick()
{
    var args = new JsonObject();
    args.Code = CustomerCode.Value;
    args.Name = CustomerName.Value;
    var ret = RegisterApi.Execute(args);
    if (!ret.IsSuccess) Toaster.Error("登録できませんでした: " + ret.ErrorMessage);
}
```

配列が返る API をスクリプトで回す例 (スクリプトなしで一覧に出すなら上の「配列が返る API・配列を送る API」):

```csharp
void LoadButton_OnClick()
{
    var ret = OrdersApi.Execute();
    if (!ret.IsSuccess) return;
    foreach (var e in ret.JsonObject)
    {
        var row = new OrderRow();
        row.OrderNo.Value = e.OrderNo;
        Orders.AddRow(row);
    }
}
```

ボタンで呼んだ後の処理 (「呼び出した後」に `ApiCompleted`):

```csharp
void ApiCompleted(WebApiResult result)
{
    if (result.IsSuccess) Message.Value = "登録しました";
}
```

## ホストの結線

- クライアント: 設定は不要です (共通の API `ExtrasTransport.EndPoint` = `/api/extras` に送ります)。HTTP を使わないホストは `ExtrasTransport.Handler` に送り方 (Extras.Server の `ExtrasApi` を直接呼ぶ) を設定します
- サーバー: 共通の API の対応表 (`ExtrasApiTable`) に `WebApiApi` を並べます。`WebApiApi` が `WebApiExecuteRequest` (`Codeer.LowCode.Blazor.Extras.WebApi`) を受け、このフィールドへの書き込み権限を確かめて接続先を呼び、`WebApiExecuteResponse` (`StatusCode` / `Body` / `ErrorMessage`) を返します

```csharp
// Program.cs
SystemConfig.Instance.WebApi = WebApiSettings.Load(builder.Configuration.GetSection("WebApi"));

// ExtrasApiTable
new WebApiApi(SystemConfig.Instance.WebApi),
```

`WebApiApi` は HttpClient を 1 つ持って使い回すので、アプリで 1 つ作って使い続けてください (要求ごとに作らない)。

## 関連

- [JobTriggerField](JobTriggerField.md) — サーバーの C# の処理をボタンから動かす
