## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign`

ボタンを押す (またはスクリプトで `Execute()`) と、サーバーが外部の Web API を呼んで結果を返すフィールド。呼ぶ先はサーバーの設定 (appsettings の `WebApi.Connections`) にある接続先の名前 `ConnectionName` で指し、接続先の URL と認証のキーはサーバーだけが持つ (ブラウザには出ない)。メソッド・パスもサーバーがこのデザインから読む (クライアントから送るのはモジュール名・フィールド名・引数の JSON だけ)。このフィールド自体は保存しない。

- **サーバーの設定が要る** (接続先は appsettings の `WebApi` セクション。デザインファイルでは作れない)。設定の書き方・認証方式は Codeer.LowCode.Blazor.Extras の `docs/WebApiField.md`
- **このフィールドに書けるユーザーが呼べる** (JobTriggerField と同じ)。書けない (PermissionField の書き込み条件・閲覧モード・`CanCreate` / `CanUpdate` が `false` の読み取り専用のモジュール) ときは他のボタンと同じく押せず、`Execute()` も呼ばずに `StatusCode` 0 の結果を返す (読み取り専用のモジュールで使うなら OnLoad のスクリプトでこのフィールドの `IsViewOnly` を `false` にする)。絞るなら **CurrentUser だけで決まる** PermissionField の書き込み条件を使う。行の値を見る条件はサーバーの判定には使われない (モジュールの `DataReadCondition` / `DataWriteCondition` は設計チェック `WebApiFieldDesign:3`、このフィールドを対象にした PermissionField の行条件は `WebApiFieldDesign:4` で検出)
- 見た目は固定 (枠線のボタン)。凝るときは `ShowButton` を `false` にして ButtonField の OnClick から `Execute()` を呼ぶ

### 送る内容

引数は JSON 1 つ。ふつうはオブジェクト。配列 (`[...]`)・値だけ (`"ok"`・`123`) も送れる (下の「外側がオブジェクトでない引数」)。

| 呼び方 | 引数 |
|---|---|
| `Execute(args)` | 渡した `JsonObject` (配列の `JsonObject` も可。`ArgumentsField` は使わない) |
| `Execute()` | `ArgumentsField` の JsonField の `GetJson()` をそのまま (値なしなら `{}`)。`ArgumentsField` が空なら `{}` |
| ボタン | `Execute()` と同じ |

キーの名前:

- `Execute(args)` のときだけ、送る前にすべてのオブジェクトのキー (入れ子・配列の中のオブジェクトも) の先頭 1 文字を小文字にする (`UseUpperCamelCaseNames` が `true` なら大文字)。2 文字目以降は変えない (`UserName` → `userName`、`user_name` → `user_name`)。揃えた結果が同じ名前になるキーは後のものを使う
- `ArgumentsField` の JsonField から送る引数と `{}` はキーを変えない (JsonField の中身の綴りのまま)
- 応答は変えない

サーバーは引数をこう使う (キーは届いた綴りのまま):

- `Path` の `{名前}` は引数の最上位の同じ名前の値 (先頭 1 文字の大文字小文字は区別しない。2 文字目以降は区別する。先頭 1 文字だけ違うキーが両方あれば完全一致のほう。文字列・数値・真偽値) で置き換える (URL エスケープする)。値が無い・空・`.`・`..` なら呼ばずにエラー
- `Get` / `Delete`: 差し込みに使わなかった最上位の文字列・数値・真偽値をクエリ文字列にする (オブジェクト・配列・null は載せない)。ボディなし
- `Post` / `Put`: 引数の JSON 全体をボディにする (`application/json`)

外側がオブジェクトでない引数 (配列・値だけ) は、取り出せる名前と値が無い:

- `Post` / `Put`: 引数をそのままボディにする
- `Get` / `Delete`: クエリもボディも付けない (`Path` に書いたクエリはそのまま)
- `Path` に `{名前}` があれば値が無いので呼ばずにエラー (差し込みの値が無いときと同じ)

スクリプトの `JsonObject` に `args.id = 1` と書いても `args.Id = 1` と書いても、`Execute(args)` が送るのは既定で `{"id":1}` (クエリなら `id=1`)。先頭が大文字の名前 (PascalCase) を取る API には `UseUpperCamelCaseNames` を `true` にする (`{"Id":1}`)。

### JsonField とつなぐ (スクリプトなし)

- `ArgumentsField`: ボタン・`Execute()` の引数を、同じモジュールの JsonField の今の内容 (`GetJson()` = JsonField の値にノードのフィールドの今の値を当てたもの。未結線のキーも残る) にする
- `ResultField`: 成功 (`IsSuccess`) した応答の本文が空でなければ、そのまま同じモジュールの JsonField に `SetJson` で入れる。ノードのフィールドに値が配られる (配られるのはキーを `JsonFieldSettings.JsonNodes` に宣言したノードだけ)。本文の外側 (オブジェクト・配列・値だけ) は WebApiField では見ない。その JsonField の形に合うかは `SetJson` が決める。ボタン・`Execute()`・`Execute(args)` のどれでも同じ
  - 本文が空 (空白だけ) のとき、失敗のときは何もしない (JsonField はそのまま)
  - `SetJson` がエラー (外側の形が合わない・値がノードの型に合わない・JSON として読めない) のときは JsonField を変えず、`WebApiResult.ErrorMessage` にその理由を入れる (`StatusCode` / `JsonObject` / `Body` は応答のまま)。ボタンならエラーを通知する
  - ボタンの `OnCompleted` は `ResultField` に入れた後に呼ぶ
- 名前のフィールドが実行時に無い・JsonField でないときは、引数は `{}`、結果は何もしない (設計チェック `WebApiFieldDesign:5`)
- `SetJson` で入れた未結線のキーは、画面からの保存では DB に入らない (JsonField の保存の規則。JsonFieldDesign の Docs)。行を親の列に保存する一覧の行モジュールの JsonField では、未結線のキーも保存される
- テーブルを持たないモジュールの JsonField でも使える (JsonField の `DbColumn` は空、ノードのフィールドの `DbColumn` は `JsonField名/キー`)

テーブルを持たないモジュールで、Id を入力して取得し、応答の `name` / `email` を出す例 (`QueryId` / `UserName` / `UserEmail` をレイアウトに置き、`UserQuery` / `UserJson` は `DataOnlyFields` に入れる):

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

### 配列が返る API・配列を送る API (スクリプトなし)

JsonField の `NodeType: Array` のノードに一覧フィールドを `DbColumn` = `JsonField名/キー` で結線すると、配列の要素が一覧の行になる。

- 応答が `{"items":[...]}`: 普通の JsonField に `items` を Array のノードとして宣言する
- 応答が `[...]`: JsonField を `UseNodeValueAsJson: true` にし、Array のノードを 1 つだけ宣言する (JsonField の JSON = 配列そのもの)
- 一覧の行モジュールはテーブルなし。行モジュールの JsonField が要素のキーと型を決め、行のフィールドはキーに結線する
- 同じ JsonField を `ArgumentsField` にすると、編集後の行を配列で送れる (`Post` / `Put` のボディ)
- 規則の詳細 (読み込み・型が合わないとき・設計チェック) は JsonFieldDesign の Docs (「一覧をノードに結線する」「ノードの値だけを JSON にする」) と ListFieldDesign の Docs (「列モード」)

応答 `[{"id":1,"name":"Leanne Graham"},{"id":2,"name":"Ervin Howell"}]` を一覧 `Users` に出す例 (`UserList` は `DataOnlyFields` に入れる):

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

行モジュール `UserRow` (テーブルなし。`ListLayouts` に `UserNo` / `UserName` を並べる):

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

応答が `{"total":2,"items":[...]}` なら `UserList` の `UseNodeValueAsJson` を外す (他は同じ)。

### 結果

`WebApiResult` (`WebApiService` と同じ型):

| メンバー | 型 | 説明 |
|---|---|---|
| `StatusCode` | int | 相手の HTTP の状態コード (4xx / 5xx もそのまま)。呼び出せなかった (接続先が無い・通信できない・タイムアウト・URL が接続先の外・差し込みの値が無い・実行できない状態) ときは `0` |
| `IsSuccess` | bool | `StatusCode` が 200〜299 |
| `JsonObject` | JsonObject | 応答のボディ。配列なら要素の並び (`foreach` で回せる)。JSON でない・数値や文字列だけの応答は空 |
| `Body` | string | 応答のボディの文字列そのまま。無ければ空文字列 |
| `ErrorMessage` | string | 呼び出せなかったときの説明 (利用者に見せてよい文言)。`ResultField` に入れられなかった (`SetJson` がエラー) ときもその理由。それ以外は空 |

ボタンで呼んだときは、失敗 (`IsSuccess` でない) か `ErrorMessage` が空でなければエラーの通知を出す (`ErrorMessage`、空なら「Web API の呼び出しに失敗しました (HTTP 状態コード)」)。成功は通知しない。その後 `OnCompleted` のスクリプトを結果を付けて呼ぶ。スクリプトの `Execute()` は通知を出さず、`OnCompleted` も呼ばない。

### C# クラス定義 (真実の源)

```csharp
public class WebApiFieldDesign : FieldDesignBase
{
    public string ConnectionName { get; set; } = string.Empty;
    public WebApiMethod Method { get; set; } = WebApiMethod.Get;   // Get / Post / Put / Delete
    public string Path { get; set; } = string.Empty;
    public string ArgumentsField { get; set; } = string.Empty;    // 同じモジュールの JsonField の名前
    public string ResultField { get; set; } = string.Empty;       // 同じモジュールの JsonField の名前
    public bool UseUpperCamelCaseNames { get; set; }
    public bool ShowButton { get; set; } = true;
    public string ButtonText { get; set; } = string.Empty;
    public string OnCompleted { get; set; } = string.Empty;        // void Xxx(WebApiResult result)
    // 親 FieldDesignBase から継承: Name（IgnoreModification / OnValidateInput はデザイナ非表示）
}
```

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `ConnectionName` | string | `""` | 接続先の名前 (サーバーの appsettings の `WebApi.Connections` の `Name`)。必須 (空ならデザインチェックのエラー `WebApiFieldDesign:1`) |
| `Method` | WebApiMethod | `Get` | `Get` / `Post` / `Put` / `Delete` |
| `Path` | string | `""` | 接続先の `BaseUrl` からの相対パス。`{名前}` で引数の値を差し込める (例 `users/{id}/orders`)。`?` 以降にクエリも書ける。`{` と `}` が対になっていなければ `WebApiFieldDesign:2`。`BaseUrl` の外 (`..` で上・別のホスト) は指せない |
| `ArgumentsField` | string | `""` | 同じモジュールの JsonField の名前。ボタン・`Execute()` はその内容を引数として送る (上の「JsonField とつなぐ」)。JsonField の名前でなければ `WebApiFieldDesign:5` |
| `ResultField` | string | `""` | 同じモジュールの JsonField の名前。成功した応答の本文 (空でなければ) をそこに入れる (上の「JsonField とつなぐ」)。JsonField の名前でなければ `WebApiFieldDesign:5` |
| `UseUpperCamelCaseNames` | bool | `false` | `Execute(args)` で渡した引数のキー (クエリ・ボディ。入れ子も) の先頭 1 文字を大文字にする。`false` なら小文字にする (上の「送る内容」)。`ArgumentsField` から送る引数には掛からない |
| `ShowButton` | bool | `true` | ボタンを出す。`false` なら画面には何も出ず、スクリプトの `Execute()` からだけ呼ぶ (そのときはレイアウトの `DataOnlyFields` に入れる) |
| `ButtonText` | string | `""` | ボタンの文言。空なら「実行」 |
| `OnCompleted` | string | `""` | ボタンで呼んだ後に呼ぶスクリプトのメソッド名。引数は結果 `WebApiResult result` |

### JSON例

```json
{
  "ConnectionName": "Crm",
  "Method": "Get",
  "Path": "customers/{code}",
  "ArgumentsField": "",
  "ResultField": "",
  "UseUpperCamelCaseNames": false,
  "ShowButton": false,
  "ButtonText": "",
  "OnCompleted": "",
  "Name": "CustomerApi",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign"
}
```

### 設計チェック

| コード | 内容 |
|---|---|
| `WebApiFieldDesign:1` | `ConnectionName` が空 |
| `WebApiFieldDesign:2` | `Path` の `{` `}` の対応が壊れている (閉じていない・`}` だけ・`{}` の中が空・入れ子) |
| `WebApiFieldDesign:3` | モジュールの `DataReadCondition` / `DataWriteCondition` は呼び出しの可否に効かない (画面でボタンが押せなくなるだけ) |
| `WebApiFieldDesign:4` | このフィールドを対象にした PermissionField の行データを参照する条件は呼び出しの可否に効かない。CurrentUser の条件を使う |
| `WebApiFieldDesign:5` | `ArgumentsField` / `ResultField` が空でなく、同じモジュールの JsonField の名前でない |

### 検索

検索には対応しない。DB列マッピングなし。

### ホストの結線 (ユーザーの作業)

- クライアント: 設定不要 (共通の API `ExtrasTransport.EndPoint` = `/api/extras` に送る)。HTTP を使わないホストは `ExtrasTransport.Handler` を設定する (Extras.Server の `ExtrasApi` を直接呼ぶ)
- サーバー: ホストの共通の API の対応表 (`ExtrasApiTable`) に `new WebApiApi(WebApiSettings.Load(configuration.GetSection("WebApi")))` を並べる。独自の認証方式は `IWebApiAuthenticator` を実装してコンストラクタの後ろに渡す

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Execute(args)` | WebApiResult | `JsonObject` (配列も可) を引数にして呼ぶ (キーの先頭 1 文字を揃える)。通知は出さない |
| `Execute()` | WebApiResult | `ArgumentsField` の JsonField の内容 (空なら `{}`) を引数にして呼ぶ。通知は出さない |
| `IsRunning` | bool | 呼び出しを頼んでいる間 `true` (その間の `Execute()` とボタンは呼ばずに `StatusCode` 0) |

ButtonField から引数を付けて呼ぶ例 (`ShowButton` を `false` にしてこのフィールドを `DataOnlyFields` に入れる):

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

ボタンで呼んだ後の処理 (`OnCompleted` = `"ApiCompleted"`):

```csharp
void ApiCompleted(WebApiResult result)
{
    if (result.IsSuccess) Message.Value = "登録しました";
}
```

値・データ系メソッドは公開しない (`IsModified` は常に `false`。保存には何も載せない)。

---

## CSS

```html
<button type="button" class="btn btn-outline-primary" data-system="web-api">実行</button>
```

`ShowButton` が `false` なら何も描かない。
