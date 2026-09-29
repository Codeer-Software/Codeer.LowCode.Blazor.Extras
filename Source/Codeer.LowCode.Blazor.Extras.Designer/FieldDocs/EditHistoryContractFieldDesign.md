# EditHistoryContractField (編集履歴契約)

編集履歴モジュールに 1 つ置き、「役割 → 自モジュールのフィールド名」のマッピングを宣言するフィールド。
UI もデータも持たない (DB 列不要)。対象モジュールの EditHistoryField はこのモジュールを `HistoryModuleName` で指す。

## セットアップ (履歴モジュールの自動生成)

履歴モジュールは手で作らず**セットアップコマンドで生成する**:

- デザイナ: メニュー Tools > 編集履歴のセットアップ
- CLI (headless): `<designer.exe> edit-history-setup "<projectDir>" [--history-name EditHistory] [--data-source <name>]
  [--user-module <ユーザーモジュール>] [--user-name-field Name] [--no-enum] [--no-pageframe] [--ddl-out <path.sql>]`

生成内容: 履歴モジュール (契約フィールド・復活ボタン・対象レコードを開くリンク・一覧 / 検索 / 詳細レイアウト・書き込めない保護条件) +
対象モジュール enum `EditHistoryTargetModule` (空。`--no-enum` で作らず ModuleName を素の名前で運用) + PageFrame のページリンク +
テーブル作成 DDL ((module_name, data_id) のインデックス込み)。**それだけ**。
**冪等**: 既存のモジュール・enum は生成しない (履歴モジュールは全モジュールで 1 つ共有する。対象モジュールが増えても再実行不要)。
`--user-module` の既定はアプリ設定のカレントユーザーモジュール (未設定なら AppUser)、`--data-source` の既定は先頭のデータソース。
DDL は自動実行されない。CLI は標準出力 (または `--ddl-out` のファイル) に出すので、それを実行してテーブルを作成する。

### 対象モジュール側の手順 (セットアップ後)

1. 履歴を取りたいモジュールに EditHistoryField を置く (`HistoryModuleName` = 生成した履歴モジュール名。詳細レイアウトの右カラムかタブ)
2. 対象モジュール enum `EditHistoryTargetModule` にメンバー (名前 = 対象モジュール名 / 表示 = 画面上の名前) を追加する (enum を作った場合。メンバーが 1 つでもあると、足りない対象モジュールをデザインチェックが指摘する)
3. サーバーの `CustomizedModuleDataIO` に `EditHistoryRecorder` が登録されていることを確認する (アプリテンプレートは登録済み)
4. Id を保った復活が要るなら対象モジュールを論理削除にする

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 必須でない役割は空にできる (= その項目は記録しない)
- 役割プロパティはフィールドのリネームに自動追従する
- 履歴モジュールには他のフィールドを足してもよい (記録側は契約の役割しか書かない)

| 役割 (表示名) | 型 | 内容 | 必須 |
|---|---|---|---|
| ModuleName (対象モジュール名) | Text / Select | 対象モジュールの名前。Select で enum (メンバー名 = モジュール名 / 表示 = 画面上の名前) を指せば、一覧の表示と検索を「受注」のような名前に読み替えられる。enum は任意で、無い・空なら素のモジュール名のまま | ○ |
| DataId (対象レコードの Id) | Text | 対象レコードの Id | ○ |
| ChangeType (変更種別) | Text / Select | `EditHistoryChangeType` (Add / Update / Delete / Restore)。Select なら EnumName に `EditHistoryChangeType` を指定 | ○ |
| Snapshot (レコード JSON) | Text | レコード全体のスナップショット。長くなるので TEXT 型の列に | ○ |
| UserId (変更したユーザー) | Link→ユーザーモジュール / Text | 保存したユーザーの Id | - |
| DateTime (変更日時) | DateTime | 保存日時。フィールドの SaveAsUtc に従う (本体の CreatedAt と同じ) | - |

## 履歴モジュールの構成 (生成物を直すときの要点)

セットアップの生成物はこの形になっている。生成後に手で直すとき (フィールド追加・画面の調整) はこれを崩さない。

- 1 つの履歴モジュールを全モジュールで共有する
- モジュールの CanCreate / CanUpdate は false にする (版はシステムだけが書く)。古い版を消せるようにするなら CanDelete を true にし、消す人に UserWrite を開ける
- 一覧レイアウトに ModuleName / DataId / ChangeType / UserId / DateTime を並べ、検索に ModuleName / ChangeType / UserId / DateTime を置く (ChangeType = 削除 で「削除済みレコード一覧」になる)
- Snapshot は一覧に出さず、検索条件・並びにも使わない (使った読み出しはサーバーが拒否する)
- 詳細レイアウトに EditHistoryUndeleteButtonField (削除したレコードの復活) と、本体の AnchorTagField (ModuleVariable = `ModuleName.Value` / IdVariable = `DataId.Value` で対象レコードを開く) を置く
- ModuleName を Select + enum にすると対象の表示と検索が画面上の名前になる (下の例は Text のまま)
- テーブルには (module_name, data_id) のインデックスを張る

```json
{
  "Name": "EditHistory",
  "DataSourceName": "Main",
  "DbTable": "edit_histories",
  "CanCreate": false,
  "CanUpdate": false,
  "Fields": [
    { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
    { "Name": "ModuleName", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "モジュール", "DbColumn": "module_name" },
    { "Name": "DataId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "レコード Id", "DbColumn": "data_id" },
    { "Name": "ChangeType", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.SelectFieldDesign", "DisplayName": "変更種別", "DbColumn": "change_type", "EnumName": "EditHistoryChangeType" },
    { "Name": "Snapshot", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DisplayName": "内容", "DbColumn": "snapshot" },
    { "Name": "UserId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.LinkFieldDesign", "DisplayName": "変更者", "DbColumn": "user_id", "SearchCondition": { "ModuleName": "AppUser" } },
    { "Name": "DateTime", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.DateTimeFieldDesign", "DisplayName": "変更日時", "DbColumn": "date_time" },
    { "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryContractFieldDesign" },
    { "Name": "UndeleteButton", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryUndeleteButtonFieldDesign" }
  ]
}
```

## 権限

- 書き込み: 履歴は内部経路で書かれるので UserWrite 条件は不要 (書けない設定でよい)。画面・API から版を足す・書き換える送信はサーバーが拒否する (別のモジュールの送信に混ぜた行も同じ)
- 閲覧: UserRead / DataRead 条件で絞る = EditHistoryField の表示もこの権限に従う
- Snapshot の内容は、返すときにサーバーが読む人の権限 (対象モジュールの読めない項目・読めない従属レコード・行の閲覧条件) に落とす。履歴モジュールを一覧・詳細・ダウンロードで直接読んでも生の値は見えない

## 設計チェック

| コード | 内容 |
|---|---|
| `EditHistoryContractFieldDesign:1` | 役割のフィールドの型が違う |
| `EditHistoryContractFieldDesign:2` | ModuleName が enum 付きの Select で、enum にメンバーがあるのに、この履歴モジュールに記録するモジュールのメンバーが無い (enum が無い・空なら指摘しない) |
| `EditHistoryContractFieldDesign:3` | 履歴モジュールの CanCreate / CanUpdate が true |

役割のフィールドが自モジュールに無い・必須の役割が空・同じモジュールに複数置いた、もエラーになる。

## 運用上の注意

- 保持期間の自動削除は持たない。古い版は履歴モジュールの一覧から消す
- 版を消すと、残った版の版番号は詰まる (版番号は閲覧時の連番)
- 対象モジュールの改名には追従しない (版は記録したときのモジュール名で残る)
