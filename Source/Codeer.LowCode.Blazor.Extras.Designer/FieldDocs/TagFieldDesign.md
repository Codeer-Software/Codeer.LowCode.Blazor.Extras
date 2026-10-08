## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力・表示・検索して保存するフィールド。タグはタグ付けモジュール (`TagLinkContractField`。タグを付けるモジュールごとに 1 つ) の行に持つ: 1 行 = このレコードに付いたタグ 1 つ (OwnerId + タグ名)。タグのマスタは無い。
タグ付けモジュールを従属レコードとして持つ素のフィールド (Calendar / Gantt / TaskBoard / MarkerList と同じ形)。設定はタグ付けモジュール名 1 つで、結び付き (`OwnerId.Value = Id.Value`・並び `Id`・読む列) はタグ付けモジュールの契約から組み立てて本体に渡す (JSON には出ない)。画面ではチップ (× で外せる) で表示する。

モジュールとテーブルは手で作らず **タグのセットアップ** で生成する:

- デザイナ: メニュー Tools > タグのセットアップ (タグを付けるモジュールを選ぶだけ)
- CLI (headless): `<designer.exe> tag-setup "<projectDir>" --target <Module> [--ddl-out <path.sql>]`

生成内容: タグ付けモジュール `<Module>Tags` + 対象モジュールの TagField `Tags` (`TagModuleName` を入れる。同名の結び付きなしの TagField があれば結び付ける) + DDL。画面への配置はデザイナで行う。

- タグの確定は Enter。打った文字に「,」「、」「，」が入っていれば (貼り付け・変換で複数が一度に入ったとき) そこで分ける。スペースでは区切らない (日本語のタグにはスペースが入ることがある)。区切りの集合は `TagField.Separators` の 1 か所。IME の変換中のキーは無視する。入力欄の外へフォーカスが移ったときも、打ちかけの文字をタグにする。入力欄が空のときの Backspace は右端のチップを外す
- タグ名は 200 文字まで (入力欄・タグ付けモジュールの `Name` の MaxLength・DDL の列の長さが同じ値 `TagField.MaxTagLength`)。長すぎるタグは足さずにエラーを出す (同じ操作でほかのタグが足されても残る)
- 同一判定は完全一致 (大文字小文字を区別する)。前後の空白は落とす
- 足し外しは何も書かない。レコードの保存で一緒に書く (保存せずにやめれば何も残らない)。レコードを消したときのタグの削除は DDL の外部キー (ON DELETE CASCADE)
- 候補: 打った文字を含むタグを、よく使われている順 (同じ件数なら名前順) に最大 10 件。タグ付けモジュールを本体の集計で 1 回引く。付いているタグは出さない。効くのはタグ付けモジュールの読み取り条件で、タグを付けるモジュール (親) の行の条件は効かない
- 検索: 選んだタグを「すべて含む (`ContainsAll`) / いずれかを含む (`In`)」で組み合わせる。どちらも SQL で判定する。タグは丸ごとの完全一致。親の検索に子のタグを置いたとき (`LinkFieldNames` で 社員.タグ など) の「すべて含む」は、親の子の行のタグを合わせて判定する
- 一覧レイアウトにも置ける (ページの行の分は一覧の読み込みに同梱される)
- レコードをコピーすると、タグもコピーされる (新しい行として保存する)

### プロパティ

> 共通プロパティ（Name, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `TagModuleName` | string | `""` | タグ付けモジュール (`TagLinkContractField` を置いたモジュール)。セットアップが入れる。 |
| `DisplayName` | string | `""` | 表示名。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `IsSimpleSearchParameter` | bool | `false` | 検索欄に一致の選択を出さない。 |
| `SearchMatchDefaultValue` | enum | `All` | 検索の一致の既定。`All` = すべて含む、`Any` = いずれかを含む。 |
| `OnDataChanged` | string | `""` | タグが変わったときのスクリプトイベント名。 |

### 必要なモジュール構成 (セットアップの生成物)

```json
{ "Name": "ContactTags", "DbTable": "contact_tags", "Fields": [
  { "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "id" },
  { "Name": "OwnerId", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign", "DbColumn": "owner_id", "IsManualInput": false },
  { "Name": "Name", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign", "DbColumn": "name", "IsRequired": true, "MaxLength": 200 },
  { "Name": "TagLinkContract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TagLinkContractFieldDesign", "OwnerId": "OwnerId", "TagName": "Name" } ] }
```

- タグ付けモジュールに一覧レイアウトは要らない (TagField が結び付きで読む列を決める)
- テーブル: (owner_id, name) の一意インデックス、name のインデックス (SQL Server / MySQL は name に大文字小文字を区別する照合順序)、レコードへの外部キー (ON DELETE CASCADE。論理削除のレコードのタグ行は残る)
- SQLite は接続文字列に `Foreign Keys=True` を付ける。無いと外部キーが効かずタグ付け行が残る

### 設計チェック

| コード | 内容 |
|---|---|
| `TagFieldDesign:1` | `TagModuleName` が空か、そのモジュールに TagLinkContractField が無い (モジュールが無いときは本体のチェックも出る) |

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

タグの変更はレコードを保存したときに書かれる。スクリプトの `ModuleSearcher` で読んだレコードのタグを読む (`Tags` / `HasTag`) ときは `Select` で TagField を指定する (指定しないとタグ付け行は読まれない)。足し外しは、読んでいなければその前に 1 回読むので `Select` が無くても二重にならない。

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
