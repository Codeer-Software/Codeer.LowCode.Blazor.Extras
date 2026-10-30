# PasswordHashField - パスワードハッシュ

> ログインユーザーモジュール (AppSettings.CurrentUserModuleDesignName) では、LoginAccountContractField の `PasswordField` を指定すれば契約がハッシュ / ソルトを書くので、このフィールドは要りません。このフィールドは契約の無いモジュール (同じテーブルを参照するパスワード変更ダイアログなど) 向けです。

平文の `PasswordField` を受け取り、Submit 時に **ハッシュ + ソルト** に変換して 2 つの DB カラムへ書き込む補助フィールドです。フィールド自体に UI はなく (描画されません)、登録/更新フローの裏で動きます。

## 機能

- **PasswordField とペア**: 同じ Module 内の `PasswordField` を `PasswordFieldName` で指定して紐づけ
- **ハッシュ + ソルト保存**: `DbColumnHash` / `DbColumnSalt` で書き込み先 DB カラムを指定 (どちらも書き込み専用)
- **読み込まれない (Read 不可)**: クライアントには Hash/Salt 値を返さない (`IsWriteOnly = true`)
- **UI なし**: レイアウトに置いても何も描画されない (隠しフィールド扱い)

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 (例: `Hash` / `ハッシュ`) |
| PasswordFieldName | string | ○ | 同じ Module 内の `PasswordField` の Name (例: `Password`) |
| DbColumnHash | string | ○ | ハッシュ値を書き込む DB カラム名 (例: `hash`) |
| DbColumnSalt | string | ○ | ソルト値を書き込む DB カラム名 (例: `salt`) |

## 必要な DB 構成

ハッシュ・ソルトとも base64 文字列で 44 〜 64 文字程度になります。可変長文字列カラムを 2 つ用意してください。

```sql
CREATE TABLE app_users (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    user_name    TEXT NOT NULL,
    name         TEXT,
    hash         TEXT,
    salt         TEXT,
    role         TEXT
);
```

`hash` / `salt` カラムは初期登録時には空で OK。クライアントから送信された平文パスワードを **サーバ側で**ハッシュ化したものが書き込まれます (詳細は後述)。

## モジュール JSON 例

`AppUser` モジュールに Password と PasswordHash をセットで配置します。`PasswordHashField` 自体はレイアウトに含めなくても OK ですが、Field 配列 (`Fields`) には必ず含めてください。

```json
{
  "Name": "AppUser",
  "DataSourceName": "Main",
  "DbTable": "app_users",
  "Fields": [
    {
      "DbColumn": "id",
      "Name": "Id",
      "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign"
    },
    {
      "DbColumn": "user_name",
      "Name": "UserName",
      "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign"
    },
    {
      "Name": "Password",
      "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.PasswordFieldDesign"
    },
    {
      "PasswordFieldName": "Password",
      "DbColumnHash": "hash",
      "DbColumnSalt": "salt",
      "Name": "Hash",
      "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.PasswordHashFieldDesign"
    }
  ]
}
```

## サーバサイド実装が必須

`PasswordHashField` は **Field 側 (クライアント側) だけでは動きません**。Submit されたデータの `Password` フィールドを取り出してハッシュ化し、`PasswordHashFieldData` (Hash + Salt) として書き戻す処理を **サーバ側で結線する必要があります** (下記の `PasswordHashingModuleDataIO` の継承)。

### Extras 本体に組み込み済の `PasswordHashHelper`

ハッシュ計算 + 適用ロジックは Extras パッケージに同梱されています:

- **名前空間**: `Codeer.LowCode.Blazor.Extras.Services.PasswordHashHelper`
- **アルゴリズム**: PBKDF2-HMAC-SHA256 / 100,000 iter / 32-byte salt + 32-byte hash / base64 出力
- **公開 API**:
  - `CreateHash(string password)` → `PasswordHashFieldData`
  - `VerifyHash(string password, string hash, string salt)` → `bool`
  - `ApplyPasswordHash(ModuleDesign moduleDesign, ModuleData data)` → モジュール内の `PasswordHashFieldDesign` をすべて検出して自動で hash + salt をセット

### 最小限の組み込み手順

`ModuleDataIO` の派生 (テンプレートの `CustomizedModuleDataIO`) を、`ModuleDataIO` ではなく `Codeer.LowCode.Blazor.Extras.Server` の `PasswordHashingModuleDataIO` (`Codeer.LowCode.Blazor.Extras.Server.Auth`) から継承するだけです (アプリテンプレートは継承済み)。本体の行書き込み `AddAsync` / `UpdateAsync` / `BulkAddAsync` を override して、書く行に `PasswordHashHelper.ApplyPasswordHash(...)` を当てます。

```csharp
using Codeer.LowCode.Blazor.Extras.Server.Auth;

public class CustomizedModuleDataIO : PasswordHashingModuleDataIO
{
    public CustomizedModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
        : base(designData, authenticationContext, dbAccess, temporaryFileManager)
    {
        // 監査・編集履歴・意味検索などのインターセプタの登録はここ
    }
}
```

- ハッシュ化は「行を書く直前の変換」なので、保存 (Submit) 単位のインターセプタではなく行書き込みの口に置いています。保存だけでなく、システム権限の内部書き込み (`ModuleDataIOSystemAccess` の `AddAsync` / `UpdateAsync`、デスクトップホストの `AddSystemRecordAsync`) も通ります
- 本体のフィールド書き込み権限の検査の後に入るので、ハッシュ列の書き込み権限は見ません (ユーザーが書くのは `PasswordField`)
- `CustomizedModuleDataIO` で `ModuleDataIO` の virtual (`AddAsync` など) を override するときは base を呼んでください (呼ばないと基底のハッシュ化が飛びます)
- `AddAsync` / `UpdateAsync` を override して `ApplyPasswordHash` を呼ぶ従来の形でも動きます

従来の形 (override で `ApplyPasswordHash` を呼ぶ) の例は `Source/Example/Extras/Extras/Extras.Server/Services/CustomizedModuleDataIO.cs` にあります。

### ログイン検証 (Cookie 認証等) でハッシュを照合する場合

`PasswordHashHelper.VerifyHash(password, hash, salt)` を使えば、DB の hash/salt と入力パスワードを照合できます。

```csharp
var ok = PasswordHashHelper.VerifyHash(inputPassword, rowHash, rowSalt);
```

## ハッシュアルゴリズムを変えたい場合

Extras 標準の PBKDF2-SHA256 100k iter で要件を満たさない場合は、`CustomizedModuleDataIO` を `PasswordHashingModuleDataIO` ではなく `ModuleDataIO` から継承し、`AddAsync` / `UpdateAsync` / `BulkAddAsync` を override して自前ロジックを書いてください (Field 側の `PasswordHashFieldDesign` / `PasswordHashFieldData` はアルゴリズム非依存で、Hash と Salt をただの文字列として保持するだけ)。Argon2 / bcrypt / scrypt 等への置き換えは、`PasswordHashFieldData` を作って `data.Fields[hashFieldDesign.Name]` にセットするコードを書くだけで完結します。

## スクリプト API

このフィールドはクライアント側で値を読み書きできません (書き込み専用)。スクリプトから直接操作する API はありません。
