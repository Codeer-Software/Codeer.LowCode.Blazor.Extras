## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TagInputFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

タグを入力するだけで保存しない欄。値はタグ名のリスト (メモリに持つ。DB 列不要)。取り込み画面・一括でタグを付ける画面の「付けるタグ」を選ぶ欄など。
選んだタグはスクリプトで TagField に足す (`TagField.AddTag` / `SetTags`)。入力部品 (チップ・IME・候補) と候補の設定は TagField と同じ。保存するタグは `TagFieldDesign`。

### プロパティ

> 共通プロパティ（Name, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DisplayName` | string | `""` | 表示名。 |
| `Placeholder` | string | `""` | タグが 1 つも無いときの入力欄のプレースホルダ。 |
| `ConfirmOnSpace` | bool | `false` | スペース (全角含む) でもタグを確定する。 |
| `AllowNewTags` | bool | `true` | 候補に無いタグを入力できる。false なら候補にあるタグだけ (足すときに確かめる)。 |
| `CandidateSource` | enum | `Module` | 候補の出どころ。`Module` = `CandidateModuleName` の `CandidateFieldName` (TextField か TagField。TagField ならそのタグ付けのタグ) / `Values` = `CandidateValues`。自分のタグ付けを持たないので `TagRows` は使えない。 |
| `CandidateModuleName` / `CandidateFieldName` | string | `""` | `Module` のときの候補の列。例: `顧客企業社員` / `タグ` (TagField)。 |
| `CandidateValues` | string | `""` | `Values` のときの決まったタグ (1 行 1 つ、書いた順)。 |
| `IsRequired` | bool | `false` | 1 つ以上のタグが必要。 |
| `OnDataChanged` | string | `""` | タグが変わったときのスクリプト。 |

### 設計チェック

| コード | 内容 |
|---|---|
| `TagInputFieldDesign:1` | `CandidateSource = Module` で候補のモジュール・フィールドが空 |
| `TagInputFieldDesign:2` | 候補のフィールドが TextField でも TagField でもない |
| `TagInputFieldDesign:3` | `CandidateSource = Values` で決まったタグが 1 つも無い |
| `TagInputFieldDesign:4` | `CandidateSource = TagRows` (この欄では使えない) |

## Script

| メンバ | 説明 |
|---|---|
| `Tags` | 入力されたタグ (`List<string>`、入れた順、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `SetTags(List<string> tags)` | タグを置き換える |
| `HasTag(string tag)` | そのタグが入っているか (大文字小文字を区別しない) |
| `Clear()` | タグを全部外す |

```csharp
// 選んだ人全員に、この欄のタグを足す
void Apply_OnClick()
{
    foreach (var person in People.Rows)
    {
        foreach (var tag in NewTags.Tags) person.Tags.AddTag(tag);
    }
    NewTags.Clear();
}
```
