## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力・表示・検索する値フィールド。値はタグを「, 」でつないだ 1 つの文字列として DB カラムに保存し、画面ではチップ (× で外せる) で表示する。`DbValueFieldDesignBase` を継承し (本体の TextField と同じ、検索できる DB 列のフィールド)、値の型は `string`。

- 入力は Enter・「,」・「、」で確定 (`ConfirmOnSpace` でスペースも)。IME の変換中のキーは無視し、変換の確定後に区切りがあれば分ける。入力欄の外へフォーカスが移ったときも、打ちかけの文字をタグにする
- 候補: その列に既に入っているタグのうち、打った文字を含むものを最大 10 件 (多く付いている順)。打つまで出さず、付いているタグは出さない
- 検索レイアウトに置くと、選んだタグごとの部分一致 (本体の Like) を「すべて含む / いずれかを含む」で組み合わせる。タグを選んでいなければ絞らない
- 読むときは「,」「、」「，」のどれでも区切り、前後の空白・空のタグ・重複を落とす。保存済みのカンマ区切りテキストをそのまま使える

### プロパティ

> 共通プロパティ（Name, IgnoreModification, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。
> 値フィールド共通プロパティ（DisplayName, IsRequired, OnDataChanged）と DB 列フィールド共通プロパティ（IsUpdateProtected, IsSimpleSearchParameter, AllowEmptySearch, OnSearchDataChanged）は [_FieldCommon.md](_FieldCommon.md) を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DbColumn` | string | `""` | タグの文字列を保存する DB カラム名。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `ConfirmOnSpace` | bool | `false` | `true` にするとスペース (全角含む) でもタグを確定する。既定で確定しないのは、日本語のタグにスペースが入ることがあるため。 |
| `TextEditEmptyType` | enum | `StringEmpty` | タグが 1 つも無いときに保存する値。`StringEmpty` = 空文字、`Null` = NULL (本体 TextField と同じ)。 |
| `CandidateModuleName` | string | `""` | 候補を読むモジュール。空ならこのフィールドのモジュールの同じ列から読む。テーブルを持たない画面 (一括登録の入力欄など) で、別のモジュールのタグを候補にするときに指定する。 |
| `CandidateFieldName` | string | `""` | `CandidateModuleName` の中で候補を読むフィールド (TagField か TextField)。空ならこのフィールドと同じ名前。 |
| `SearchMatchDefaultValue` | enum | `All` | 検索レイアウトでの一致の既定。`All` = 選んだタグをすべて含む (AND)、`Any` = いずれかを含む (OR)。画面の選択で切り替えられる (`IsSimpleSearchParameter` が `true` なら選択は出ない)。 |

### 必要な DB 構成

タグをまとめて入れられる長さのテキスト型カラムを用意する。

```sql
tags NVARCHAR(400) NULL
```

### 候補の読み方

- 最初にフォーカスしたときに 1 回だけ、候補のモジュールの新しい行から 1000 件 (タグの列だけ) を読んで、タグごとの件数を数える
- 読めるのは通常の一覧と同じ (そのモジュールの閲覧権限と行の条件に従う)。候補のモジュールにテーブルが無い、または閲覧できないときは候補を出さない
- ほかの行で足したタグは、画面を読み込み直すと候補に入る

### 配置の注意

- 一覧 (ListField) のセルにも置ける。編集できるセルではチップと入力欄、閲覧専用ならチップだけを表示する
- 一括ダウンロード / 一括更新 (BulkFileTransfer) では列の値 (「, 」区切りの文字列) をそのまま出し入れする。一括更新はタグを足すのではなく、列の値を置き換える
- タグの同一判定は大文字小文字を区別しない (`DXPO` と `dxpo` は同じタグ。先に入っていた表記を残す)。候補の絞り込みも区別しない。検索だけは DB の照合順序に従う

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Value` | 保存する文字列 (get / set)。「, 」区切り |
| `Tags` | 付いているタグ (`List<string>`、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `HasTag(string tag)` | そのタグが付いているか |
| `SearchTags` | 検索レイアウトで選んでいるタグ (`List<string>`、get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

### 使用例

```csharp
// 登録する人全員にタグを足す
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
