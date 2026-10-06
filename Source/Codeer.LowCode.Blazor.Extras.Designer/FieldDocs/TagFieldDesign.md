## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力・表示・検索するフィールド。タグはテーブルに持つ (CLB の多対多の形): タグのマスタ (`TagContractField`) と、タグを付けるモジュールごとのタグ付けモジュール (`TagLinkContractField`)。
`ListFieldDesignBase` を継承する一覧フィールドで、タグ付けモジュールを子の一覧として持つ。読み込み・保存 (レコードの保存に一緒に載る)・子のパスでの検索は本体の一覧と同じ。画面ではチップ (× で外せる) で表示する。

モジュールとテーブルは手で作らず **タグのセットアップ** で生成する:

- デザイナ: メニュー Tools > タグのセットアップ
- CLI (headless): `<designer.exe> tag-setup "<projectDir>" --target <Module> [--field Tags | --no-field] [--link-name <Module>Tags] [--master-name Tag] [--data-source <name>] [--no-pageframe] [--ddl-out <path.sql>]`

生成内容: マスタ (既にあれば使う) + タグ付けモジュール + 対象モジュールの TagField (検索条件を入れる。同名の結び付きなしの TagField があれば結び付ける) + DDL。画面への配置はデザイナで行う。

- 入力は Enter・「,」・「、」で確定 (`ConfirmOnSpace` でスペースも)。IME の変換中のキーは無視する。入力欄の外へフォーカスが移ったときも、打ちかけの文字をタグにする
- 候補: マスタのうち打った文字を含むものを最大 10 件 (名前順)。付いているタグは出さない
- マスタに無いタグは、保存のときマスタに行を足す (同じトランザクション)。`AllowNewTags = false` ならマスタにあるタグだけ
- 検索: 選んだタグを「すべて含む / いずれかを含む」で組み合わせる。タグは丸ごと一致、大文字小文字は区別しない
- 一覧レイアウトにも置ける (ページの行の分をまとめて 1 回で読む)
- 検索条件が空なら保存しない入力欄 (候補は `TagModuleName` のマスタ)

### プロパティ

> 共通プロパティ（Name, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。一覧フィールドの表示用の設定 (ページング・行の追加削除ボタン等) は出ない。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `SearchCondition` | SearchCondition | 空 | タグ付けモジュール + `FieldVariableMatchCondition` (`OwnerId.Value` = `Id.Value`) + 並び (`Id` 昇順)。セットアップが入れる。空なら保存しない入力欄。 |
| `TagModuleName` | string | `""` | タグのマスタ。保存しない入力欄では必須。結び付きありでは空でよい (書くならタグ付けのリンクの先と同じ)。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `ConfirmOnSpace` | bool | `false` | スペース (全角含む) でもタグを確定する。 |
| `AllowNewTags` | bool | `true` | マスタに無いタグを入力できる (保存のときマスタに足す)。 |
| `CandidateRowCount` | int | `1000` | 候補に読むマスタの行数 (名前順)。これを超えるタグも入力すればマスタから引く。 |
| `IsRequired` | bool | `false` | 1 つ以上のタグが必要。 |
| `IsSimpleSearchParameter` | bool | `false` | 検索欄に一致の選択を出さない。 |
| `SearchMatchDefaultValue` | enum | `All` | 検索の一致の既定。`All` = すべて含む、`Any` = いずれかを含む。 |
| `DisplayName` / `OnDataChanged` / `OnSearchDataChanged` | | | 一覧フィールド共通。 |

### 必要なモジュール構成 (セットアップの生成物)

```json
{ "Name": "Tag", "DbTable": "tags", "Fields": [
  { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
  { "Name": "Name", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DbColumn": "name", "IsRequired": true },
  { "Name": "TagContract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TagContractFieldDesign", "TagName": "Name" } ] }

{ "Name": "ContactTags", "DbTable": "contact_tags", "Fields": [
  { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
  { "Name": "OwnerId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "contact_id", "IsManualInput": false },
  { "Name": "Tag", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.LinkFieldDesign", "DbColumn": "tag_id",
    "SearchCondition": { "ModuleName": "Tag" }, "ValueVariable": "Id.Value", "DisplayTextVariable": "Name.Value" },
  { "Name": "TagLinkContract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TagLinkContractFieldDesign" } ],
  "ListLayouts": { "": { "DataOnlyFields": [ "OwnerId", "Tag" ] } } }
```

- タグ付けの一覧レイアウト (`""`) は `OwnerId` と `Tag` を読むこと (レイアウトに無いフィールドは値が空で届く)
- タグ付けの `Tag` の表示文字列はマスタのタグ名 (`DisplayTextVariable = "Name.Value"`)。TagField はこれをタグ名として使う

### 設計チェック

| コード | 内容 |
|---|---|
| `TagFieldDesign:1` | 検索条件のモジュールに TagLinkContractField が無い |
| `TagFieldDesign:2` | 検索条件にこのレコードへの結び付き (`OwnerId.Value = Id.Value`) が無い |
| `TagFieldDesign:3` | タグ付けの一覧レイアウトが OwnerId / Tag を読まない |
| `TagFieldDesign:4` | 保存しない入力欄で TagModuleName が空 |
| `TagFieldDesign:5` | TagModuleName のモジュールに TagContractField が無い |
| `TagFieldDesign:6` | TagModuleName がタグ付けのリンクの先と違う |
| `TagFieldDesign:7` | CandidateRowCount が 1 未満 |

### 配置の注意

- 一括ダウンロード / 一括更新 (BulkFileTransfer) はタグを扱わない (レコードの列ではないため)
- 意味検索 (SemanticSearchField) の `SourceFields` に入れると、タグ名を並べた行が文章に入る
- 外部キーは削除を制限する (使われているタグはマスタから消せない)。タグを付けたレコードを消すとタグ付け行も一緒に消える

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Tags` | 付いているタグ (`List<string>`、付けた順、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `SetTags(List<string> tags)` | タグを置き換える |
| `HasTag(string tag)` | そのタグが付いているか (大文字小文字を区別しない) |
| `SearchTags` | 検索レイアウトで選んでいるタグ (`List<string>`、get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

タグの変更はレコードを保存したときに書かれる。

### 使用例

```csharp
// 登録する人全員に、入力欄 (保存しない TagField) で選んだタグを足す
void Register_OnClick()
{
    foreach (var person in People.Rows)
    {
        foreach (var tag in NewTags.Tags) person.Tags.AddTag(tag);
    }
}

// 条件に合う人だけ別のタグを付ける
void Member_OnDataChanged()
{
    if (Member.HasTag("VIP")) Member.AddTag("優先対応");
}
```
