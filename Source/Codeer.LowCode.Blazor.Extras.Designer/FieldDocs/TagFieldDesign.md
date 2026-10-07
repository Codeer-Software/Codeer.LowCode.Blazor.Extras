## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力・表示・検索して保存するフィールド。タグはタグ付けモジュール (`TagLinkContractField`。タグを付けるモジュールごとに 1 つ) の行に持つ: 1 行 = このレコードに付いたタグ 1 つ (OwnerId + タグ名)。タグのマスタは無い。
`ListFieldDesignBase` を継承する一覧フィールドで、タグ付けモジュールを子の一覧として持つ。読み込み・保存 (レコードの保存に一緒に載る)・削除の連鎖・権限・編集履歴は本体の一覧と同じ。画面ではチップ (× で外せる) で表示する。
保存しない入力欄 (取り込み画面などで「付けるタグ」を選ぶ欄) は `TagInputFieldDesign`。

モジュールとテーブルは手で作らず **タグのセットアップ** で生成する:

- デザイナ: メニュー Tools > タグのセットアップ
- CLI (headless): `<designer.exe> tag-setup "<projectDir>" --target <Module> [--field Tags | --no-field] [--link-name <Module>Tags] [--link-table <table>] [--owner-column owner_id] [--data-source <name>] [--ddl-out <path.sql>]`

生成内容: タグ付けモジュール + 対象モジュールの TagField (検索条件を入れる。同名の結び付きなしの TagField / TagInputField があれば結び付けた TagField にする) + DDL。画面への配置はデザイナで行う。

- 入力は Enter・「,」・「、」で確定 (`ConfirmOnSpace` でスペースも)。IME の変換中のキーは無視する。入力欄の外へフォーカスが移ったときも、打ちかけの文字をタグにする
- 足し外しは何も書かない。レコードの保存で一緒に書く (保存せずにやめれば何も残らない)
- 候補: 打った文字を含むタグを、よく使われている順に最大 10 件。サーバーの集計 (タグ名でグループ化・件数の多い順・上位 20 件・部分一致) を 1 回引く。付いているタグは出さない
- 表記: 大文字小文字を区別しない。新しく足すタグは、既に使われている表記があればそれに寄せる
- 検索: 選んだタグを「すべて含む (`ContainsAll`) / いずれかを含む (`In`)」で組み合わせる。どちらも SQL で判定する。タグは丸ごと一致
- 一覧レイアウトにも置ける (ページの行の分をまとめて 1 回で読む)

### プロパティ

> 共通プロパティ（Name, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。一覧フィールドの表示用の設定 (ページング・行の追加削除ボタン等) は出ない。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `SearchCondition` | SearchCondition | 空 | タグ付けモジュール + `FieldVariableMatchCondition` (`OwnerId.Value` = `Id.Value`) + 並び (`Id` 昇順)。セットアップが入れる。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `ConfirmOnSpace` | bool | `false` | スペース (全角含む) でもタグを確定する。 |
| `AllowNewTags` | bool | `true` | 候補に無いタグを入力できる。false なら候補にあるタグだけ。 |
| `CandidateSource` | enum | `TagRows` | 候補の出どころ。`TagRows` = このフィールドのタグ付けのタグ / `Module` = `CandidateModuleName` の `CandidateFieldName` (TextField か TagField) / `Values` = `CandidateValues`。 |
| `CandidateModuleName` / `CandidateFieldName` | string | `""` | `Module` のときの候補の列。 |
| `CandidateValues` | string | `""` | `Values` のときの決まったタグ (1 行 1 つ、書いた順)。 |
| `IsRequired` | bool | `false` | 1 つ以上のタグが必要。 |
| `IsSimpleSearchParameter` | bool | `false` | 検索欄に一致の選択を出さない。 |
| `SearchMatchDefaultValue` | enum | `All` | 検索の一致の既定。`All` = すべて含む、`Any` = いずれかを含む。 |
| `DisplayName` / `OnDataChanged` / `OnSearchDataChanged` | | | 一覧フィールド共通。 |

### 必要なモジュール構成 (セットアップの生成物)

```json
{ "Name": "ContactTags", "DbTable": "contact_tags", "Fields": [
  { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
  { "Name": "OwnerId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "owner_id", "IsManualInput": false },
  { "Name": "Name", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DbColumn": "name", "IsRequired": true },
  { "Name": "TagLinkContract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TagLinkContractFieldDesign", "OwnerId": "OwnerId", "TagName": "Name" } ],
  "ListLayouts": { "": { "DataOnlyFields": [ "OwnerId", "Name" ] } } }
```

- タグ付けの一覧レイアウト (`""`) は `OwnerId` とタグ名を読むこと (レイアウトに無いフィールドは値が空で届く)
- テーブル: (owner_id, name) の一意インデックス (大文字小文字を区別しない)、name のインデックス、レコードへの外部キー (削除は連鎖)

### 設計チェック

| コード | 内容 |
|---|---|
| `TagFieldDesign:1` | 検索条件のモジュールが空か、TagLinkContractField が無い |
| `TagFieldDesign:2` | 検索条件にこのレコードへの結び付き (`OwnerId.Value = Id.Value`) が無い |
| `TagFieldDesign:3` | タグ付けの一覧レイアウトが OwnerId / タグ名を読まない |
| `TagFieldDesign:4` | `CandidateSource = Module` で候補のモジュール・フィールドが空 |
| `TagFieldDesign:5` | 候補のフィールドが TextField でも TagField でもない |
| `TagFieldDesign:6` | `CandidateSource = Values` で決まったタグが 1 つも無い |

### 配置の注意

- 一括ダウンロード / 一括更新 (BulkFileTransfer) はタグを扱わない (レコードの列ではないため)
- 意味検索 (SemanticSearchField) の `SourceFields` に入れると、タグ名を並べた行が文章に入る
- PostgreSQL では検索と表記寄せの比較が大文字小文字を区別する

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Tags` | 付いているタグ (`List<string>`、付けた順、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `SetTags(List<string> tags)` | タグを置き換える |
| `HasTag(string tag)` | そのタグが付いているか (大文字小文字を区別しない) |
| `LoadTags()` | このレコードのタグを読む (まだなら)。スクリプトの ModuleSearcher で読んだレコードは子の一覧を読まないので、`Tags` を見る前に呼ぶ。`AddTag` / `RemoveTag` / `SetTags` は自分で読む |
| `SearchTags` | 検索レイアウトで選んでいるタグ (`List<string>`、get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

タグの変更はレコードを保存したときに書かれる。

### 使用例

```csharp
// 登録する人全員に、入力欄 (TagInputField) で選んだタグを足す
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
