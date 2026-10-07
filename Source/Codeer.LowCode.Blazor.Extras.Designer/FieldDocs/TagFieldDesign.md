## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力・表示・検索して保存するフィールド。タグはタグ付けモジュール (`TagLinkContractField`。タグを付けるモジュールごとに 1 つ) の行に持つ: 1 行 = このレコードに付いたタグ 1 つ (OwnerId + タグ名)。タグのマスタは無い。
`ListFieldDesignBase` を継承する一覧フィールドで、タグ付けモジュールを子の一覧として持つ。読み込み・保存 (レコードの保存に一緒に載る)・削除の連鎖・権限・編集履歴は本体の一覧と同じ。画面ではチップ (× で外せる) で表示する。

モジュールとテーブルは手で作らず **タグのセットアップ** で生成する:

- デザイナ: メニュー Tools > タグのセットアップ
- CLI (headless): `<designer.exe> tag-setup "<projectDir>" --target <Module> [--field Tags | --no-field] [--link-name <Module>Tags] [--link-table <table>] [--owner-column owner_id] [--data-source <name>] [--ddl-out <path.sql>]`

生成内容: タグ付けモジュール + 対象モジュールの TagField (検索条件を入れる。同名の結び付きなしの TagField があれば結び付ける) + DDL。画面への配置はデザイナで行う。

- 入力は Enter・「,」・「、」で確定 (`ConfirmOnSpace` でスペースも)。IME の変換中のキーは無視する。入力欄の外へフォーカスが移ったときも、打ちかけの文字をタグにする。入力欄が空のときの Backspace は右端のチップを外す
- タグ名は 200 文字まで (入力欄・タグ付けモジュールの `Name` の MaxLength・DDL の列の長さが同じ値 `TagField.MaxTagLength`)
- 同一判定は完全一致 (大文字小文字を区別する)。前後の空白は落とす
- 足し外しは何も書かない。レコードの保存で一緒に書く (保存せずにやめれば何も残らない)
- 候補: 打った文字を含むタグを、よく使われている順 (同じ件数なら名前順) に最大 10 件。タグ付けモジュールを本体の集計で 1 回引く。付いているタグは出さない。効くのはタグ付けモジュールの読み取り条件で、タグを付けるモジュール (親) の行の条件は効かない
- 検索: 選んだタグを「すべて含む (`ContainsAll`) / いずれかを含む (`In`)」で組み合わせる。どちらも SQL で判定する。タグは丸ごとの完全一致。親の検索に子のタグを置いたとき (`LinkFieldNames` で 社員.タグ など) の「すべて含む」は、親の子の行のタグを合わせて判定する
- 一覧レイアウトにも置ける (ページの行の分は一覧の読み込みに同梱される)

### プロパティ

> 共通プロパティ（Name, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。一覧フィールドの表示用の設定 (ページング・行の追加削除ボタン等) は出ない。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `SearchCondition` | SearchCondition | 空 | タグ付けモジュール + `FieldVariableMatchCondition` (`OwnerId.Value` = `Id.Value`) + 並び (`Id` 昇順)。セットアップが入れる。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `ConfirmOnSpace` | bool | `false` | スペース (全角含む) でもタグを確定する。 |
| `IsRequired` | bool | `false` | 1 つ以上のタグが必要。 |
| `IsSimpleSearchParameter` | bool | `false` | 検索欄に一致の選択を出さない。 |
| `SearchMatchDefaultValue` | enum | `All` | 検索の一致の既定。`All` = すべて含む、`Any` = いずれかを含む。 |
| `DisplayName` / `OnDataChanged` / `OnSearchDataChanged` | | | 一覧フィールド共通。 |

### 必要なモジュール構成 (セットアップの生成物)

```json
{ "Name": "ContactTags", "DbTable": "contact_tags", "Fields": [
  { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
  { "Name": "OwnerId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "owner_id", "IsManualInput": false },
  { "Name": "Name", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DbColumn": "name", "IsRequired": true, "MaxLength": 200 },
  { "Name": "TagLinkContract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TagLinkContractFieldDesign", "OwnerId": "OwnerId", "TagName": "Name" } ],
  "ListLayouts": { "": { "DataOnlyFields": [ "OwnerId", "Name" ] } } }
```

- タグ付けの一覧レイアウト (`""`) は `OwnerId` とタグ名を読むこと (レイアウトに無いフィールドは値が空で届く)
- テーブル: (owner_id, name) の一意インデックス、name のインデックス (SQL Server / MySQL は name に大文字小文字を区別する照合順序)、レコードへの外部キー (ON DELETE CASCADE。ふだん消すのは本体の DeleteTogether で、外部キーは SQL で直接消したときの保険)

### 設計チェック

| コード | 内容 |
|---|---|
| `TagFieldDesign:1` | 検索条件のモジュールが空か、TagLinkContractField が無い |
| `TagFieldDesign:2` | 検索条件にこのレコードへの結び付き (`OwnerId.Value = Id.Value`) が無い |
| `TagFieldDesign:3` | タグ付けの一覧レイアウトが OwnerId / タグ名を読まない |

### 配置の注意

- 一括ダウンロード / 一括更新 (BulkFileTransfer) はタグを扱わない (レコードの列ではないため)
- 意味検索 (SemanticSearchField) の `SourceFields` に入れると、タグ名を並べた行が文章に入る
- 見せたくないタグがあるなら、タグ付けモジュールに読み取り条件を書く (候補の集計はタグ付けモジュールに対して走る)

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Tags` | 付いているタグ (`List<string>`、付けた順、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `SetTags(List<string> tags)` | タグを置き換える |
| `HasTag(string tag)` | そのタグが付いているか (完全一致) |
| `SearchTags` | 検索レイアウトで選んでいるタグ (`List<string>`、get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

タグの変更はレコードを保存したときに書かれる。スクリプトの `ModuleSearcher` で読んだレコードのタグを使うときは `Select` で TagField を指定する (指定しないと子の一覧は読まれない)。

### 使用例

```csharp
// 条件に合う人全員に、タグを足して保存する
void AddTag_OnClick()
{
    var ps = new ModuleSearcher<顧客企業社員>();
    ps.AddEqual(e => e.企業Id.Value, 企業Id.Value);
    ps.Select(e => e.Tags);
    var changed = new List<Module>();
    foreach (var p in ps.Execute())
    {
        if (p.Tags.HasTag("展示会2026")) continue;
        p.Tags.AddTag("展示会2026");
        changed.Add(p);
    }
    if (changed.Count > 0) this.Submit(changed);
}

// 条件に合う人だけ別のタグを付ける
void Member_OnDataChanged()
{
    if (Member.HasTag("VIP")) Member.AddTag("優先対応");
}
```
