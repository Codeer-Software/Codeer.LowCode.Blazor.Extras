# EditHistoryContractField (編集履歴契約)

編集履歴モジュールに 1 つ置き、「役割 → 自モジュールのフィールド名」のマッピングを宣言するフィールド。
UI もデータも持たない (DB 列不要)。対象モジュールの EditHistoryField はこのモジュールを `HistoryModuleName` で指す。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無い・型が違うとデザインチェックがエラーにする
- 役割プロパティはフィールドのリネームに自動追従する
- 同じモジュールに複数置くとエラー
- ModuleName が enum 付きの Select で、enum にメンバーがあるのに、この履歴モジュールに記録するモジュールのメンバーが無いとエラー (enum が無い・空なら指摘しない)
- 必須でない役割は空にできる (= その項目は記録しない)。監査用に項目を増やしたい場合は履歴モジュールに自由にフィールドを足してよい (記録側は契約の役割しか書かない)

| 役割 (表示名) | 型 | 内容 | 必須 |
|---|---|---|---|
| ModuleName (対象モジュール名) | Text / Select | 対象モジュールの名前。Select で enum (メンバー名 = モジュール名 / 表示 = 画面上の名前) を指せば、一覧の表示と検索を「受注」のような名前に読み替えられる。enum は任意で、無い・空なら素のモジュール名のまま (承認待ち一覧の申請種別と同じ作り) | ○ |
| DataId (対象レコードの Id) | Text | 対象レコードの Id | ○ |
| ChangeType (変更種別) | Text / Select | `EditHistoryChangeType` (Add / Update / Delete)。Select なら EnumName に `EditHistoryChangeType` を指定 | ○ |
| Snapshot (レコード JSON) | Text | レコード全体のスナップショット。長くなるので TEXT 型の列に | ○ |
| UserId (変更したユーザー) | Link→ユーザーモジュール / Text | 保存したユーザーの Id | - |
| DateTime (変更日時) | DateTime | 保存日時 | - |

## 履歴モジュールの例

1 つの履歴モジュールを全モジュールで共有するのが簡単。一覧レイアウトに ModuleName / DataId / ChangeType / UserId / DateTime を並べ、
検索に ModuleName / ChangeType / UserId / DateTime を置く (ChangeType = 削除 で「削除済みレコード一覧」になる)。Snapshot は一覧に出さない。
ModuleName を Select + enum にすると対象の表示と検索が画面上の名前になる (下の例は Text のまま)。

```json
{
  "Name": "EditHistory",
  "DataSourceName": "Main",
  "DbTable": "edit_histories",
  "Fields": [
    { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
    { "Name": "ModuleName", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "モジュール", "DbColumn": "module_name" },
    { "Name": "DataId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "レコード Id", "DbColumn": "data_id" },
    { "Name": "ChangeType", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.SelectFieldDesign", "DisplayName": "変更種別", "DbColumn": "change_type", "EnumName": "EditHistoryChangeType" },
    { "Name": "Snapshot", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "内容", "DbColumn": "snapshot" },
    { "Name": "UserId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.LinkFieldDesign", "DisplayName": "変更者", "DbColumn": "user_id", "SearchCondition": { "ModuleName": "AppUser" } },
    { "Name": "DateTime", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.DateTimeFieldDesign", "DisplayName": "変更日時", "DbColumn": "date_time" },
    { "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryContractFieldDesign" }
  ]
}
```

権限: 履歴は内部経路で書かれるので UserWrite 条件は不要 (書けない設定でよい)。
閲覧は UserRead / DataRead 条件で絞る = EditHistoryField の表示もこの権限に従う。
