# TagField - タグ入力

タグを入力・表示・検索する値フィールドです。値は **タグを「, 」でつないだ 1 つの文字列** として DB カラムに保存し、画面ではチップ (× で外せる) で表示します。保存形式は TextField に入れたカンマ区切りのテキストと同じなので、既存の列をそのまま TagField に切り替えられ、Excel の入出力や意味検索もそのまま使えます。

## 機能

- **入力**: 文字を打って Enter・「,」・「、」で確定 (設定でスペースでも)。× でタグを外す。入力欄の外へフォーカスが移ると、打ちかけの文字もタグになる (ウィンドウの切り替えでは確定しない)
- **日本語入力**: IME の変換中のキーは無視する。変換を確定する Enter でタグは作られず、確定後に「、」があれば分ける
- **候補**: その列に既に入っているタグのうち、打った文字を含むものを最大 10 件 (多く付いている順) 表示。↑↓ と Enter、またはクリックで選ぶ。付いているタグは出ない
- **検索**: 検索条件に置くと、選んだタグごとの部分一致 (「展示会」で「展示会2026」も当たる) を「すべて含む / いずれかを含む」で組み合わせる。タグを選んでいなければ絞らない
- **閲覧 (読取専用)**: チップだけを表示。一覧のセルにも置ける
- **検証**: 必須 (`IsRequired`) = 1 つ以上のタグ
- **正規化**: 読むときは「,」「、」「，」のどれでも区切り、前後の空白・空のタグ・重複を落とす

## デザイナー設定プロパティ

| プロパティ | デザイナ表示名 | 型 | 必須 | 説明 |
|---|---|---|---|---|
| DbColumn | DBカラム | string | ○ | タグの文字列を保存する DB カラム名 |
| Placeholder | プレースホルダ | string | - | タグが無いときの入力欄のプレースホルダ |
| ConfirmOnSpace | スペースでも確定 | bool | - | スペースでもタグを確定する。既定 false (日本語のタグにはスペースが入ることがあるため) |
| TextEditEmptyType | 空のときの値 | enum | - | タグが無いときに保存する値。`StringEmpty` (既定) / `Null`。TextField と同じ |
| CandidateModuleName | 候補のモジュール | string | - | 候補を読むモジュール。空ならこのフィールドの列。テーブルを持たない画面 (一括登録の入力欄など) で別のモジュールのタグを候補にするときに指定 |
| CandidateFieldName | 候補のフィールド | string | - | 候補のモジュールの中で読むフィールド (TagField か TextField)。空ならこのフィールドと同じ名前 |
| SearchMatchDefaultValue | 検索の一致の既定 | enum | - | 検索条件での一致の既定。`All` = すべて含む (既定) / `Any` = いずれかを含む。画面で切り替えられる |

DB 列フィールド共通の `IsUpdateProtected` / `IsSimpleSearchParameter` (一致の選択を出さない) / `AllowEmptySearch` / `OnSearchDataChanged`、値フィールド共通の `DisplayName` / `IsRequired` / `OnDataChanged` も使えます。

### DB カラム

タグをまとめて入れられる長さのテキスト型を用意してください。

```sql
tags NVARCHAR(400) NULL
```

## 候補の読み方

- 最初にフォーカスしたときに 1 回だけ、候補のモジュールの新しい行から 1000 件 (タグの列だけ) を読み、タグごとの件数を数えます
- 読めるのは通常の一覧と同じで、そのモジュールの閲覧権限と行の条件に従います。テーブルの無いモジュール・閲覧できないモジュールからは読みません
- ほかの行や別の人が足したタグは、画面を読み込み直すと候補に入ります

## スクリプト API

| メンバ | 説明 |
|---|---|
| `Value` | 保存する文字列 (get / set)。「, 」区切り |
| `Tags` | 付いているタグ (`List<string>`、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない |
| `RemoveTag(string tag)` | タグを外す |
| `HasTag(string tag)` | そのタグが付いているか |
| `SearchTags` | 検索条件で選んでいるタグ (get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

```csharp
// 登録する人全員にタグを足す
void Register_OnClick()
{
    foreach (var person in People.Rows)
    {
        foreach (var tag in NewTags.Tags) person.Tags.AddTag(tag);
    }
}
```

## 配置の注意

- 一括ダウンロード / 一括更新 (BulkFileTransfer) では列の値をそのまま出し入れします。一括更新はタグを足すのではなく列の値を置き換えます
- タグの同一判定は大文字小文字を区別します (`DXPO` と `dxpo` は別のタグ)。候補の絞り込みは区別しないので、既存の表記を選べます
- 検索条件の一致の選択を出したくないときは `IsSimpleSearchParameter` を `true` にします (TextField の比較の選択と同じ)

## CSS カスタマイズ

チップは `.tag-chip`、入力欄全体は `.tag-editor`、閲覧表示は `.tag-view` です。色や角丸を変えたいときは app.css で上書きしてください。

```css
.tag-chip { background: #eef3ff; border-color: #c7d7ff; }
```
