# PasswordHashField - パスワードハッシュ

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

`PasswordHashField` は **Field 側 (クライアント側) だけでは動きません**。Submit されたデータの `Password` フィールドを取り出してハッシュ化し、`PasswordHashFieldData` (Hash + Salt) として書き戻す処理を **サーバ側で実装する必要があります**。

### Extras 本体に組み込み済の `PasswordHashHelper`

ハッシュ計算 + 適用ロジックは Extras パッケージに同梱されています:

- **名前空間**: `Codeer.LowCode.Blazor.Extras.Services.PasswordHashHelper`
- **アルゴリズム**: PBKDF2-HMAC-SHA256 / 100,000 iter / 32-byte salt + 32-byte hash / base64 出力
- **公開 API**:
  - `CreateHash(string password)` → `PasswordHashFieldData`
  - `VerifyHash(string password, string hash, string salt)` → `bool`
  - `ApplyPasswordHash(ModuleDesign moduleDesign, ModuleData data)` → モジュール内の `PasswordHashFieldDesign` をすべて検出して自動で hash + salt をセット

### 最小限の組み込み手順

`ModuleDataIO` を継承したクラスを作って、`AddAsync` / `UpdateAsync` の冒頭で `PasswordHashHelper.ApplyPasswordHash(...)` を呼ぶだけです (in-tree に helper をコピーする必要はありません)。

```csharp
using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Extras.Services;  // ← Extras 本体の Helper を使う

public class CustomizedModuleDataIO : ModuleDataIO
{
    readonly DesignData _designData;

    public CustomizedModuleDataIO(DesignData designData, IAuthenticationContext auth,
        IDbAccessor db, ITemporaryFileManager tfm)
        : base(designData, auth, db, tfm)
    {
        _designData = designData;
    }

    protected override async Task<string> AddAsync(Guid txn, Guid submitId, ModuleData data)
    {
        var moduleDesign = _designData.Modules.Find(data.Name)
            ?? throw LowCodeException.Create("invalid design");
        PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
        return await base.AddAsync(txn, submitId, data);
    }

    protected override async Task UpdateAsync(Guid txn, Guid submitId, ModuleData data)
    {
        var moduleDesign = _designData.Modules.Find(data.Name)
            ?? throw LowCodeException.Create("invalid design");
        PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
        await base.UpdateAsync(txn, submitId, data);
    }
}
```

最後に `Program.cs` 等で `ModuleDataIO` を `CustomizedModuleDataIO` に差し替えて DI 登録すれば完了です。

完全な動く例は `Source/Example/Extras/Extras/Extras.Server/Services/CustomizedModuleDataIO.cs` を参照してください。

### ログイン検証 (Cookie 認証等) でハッシュを照合する場合

`PasswordHashHelper.VerifyHash(password, hash, salt)` を使えば、DB の hash/salt と入力パスワードを照合できます。

```csharp
var ok = PasswordHashHelper.VerifyHash(inputPassword, rowHash, rowSalt);
```

## ハッシュアルゴリズムを変えたい場合

Extras 標準の PBKDF2-SHA256 100k iter で要件を満たさない場合は、`PasswordHashHelper` を呼ばずに自前ロジックを `CustomizedModuleDataIO` の中に書いてください (Field 側の `PasswordHashFieldDesign` / `PasswordHashFieldData` はアルゴリズム非依存で、Hash と Salt をただの文字列として保持するだけ)。Argon2 / bcrypt / scrypt 等への置き換えは、`PasswordHashFieldData` を作って `data.Fields[hashFieldDesign.Name]` にセットするコードを書くだけで完結します。

## スクリプト API

このフィールドはクライアント側で値を読み書きできません (書き込み専用)。スクリプトから直接操作する API はありません。
