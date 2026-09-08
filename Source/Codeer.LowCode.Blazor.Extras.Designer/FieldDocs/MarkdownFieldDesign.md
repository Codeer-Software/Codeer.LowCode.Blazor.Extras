## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.MarkdownFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

Markdown を入力・表示する値フィールド。値は Markdown のテキストそのままで DB カラムに保存し、閲覧時 (IsViewOnly) は HTML に描画して表示する。`ValueFieldDesignBase` を継承し、値の型は `string`。

備考・仕様・手順・議事メモなど構造のある長文で、書き手が記法を知っている人か AI のときに使う。記法を知らない利用者に書式付きで入力させるなら [RichTextField](RichTextFieldDesign.md) (WYSIWYG、HTML を保存)。

- 保存するのはプレーンテキストなので、DB の検索・CSV や Excel への出力でもそのまま読める
- 描画時は Markdown 中の生 HTML を無効化する (文字として見える)。利用者の入力をそのまま描いても script は動かない
- 表・チェックリスト・コードブロック・自動リンクなどの拡張記法に対応。単独の改行は改行として表示される (Enter がそのまま改行になる)
- リンクは別タブで開く

### プロパティ

> 共通プロパティ（Name, IgnoreModification, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。
> 値フィールド共通プロパティ（DisplayName, IsRequired, OnDataChanged）は [_FieldCommon.md](_FieldCommon.md) の「ValueFieldDesignBase」を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DbColumn` | string | `""` | Markdown テキストを保存する DB カラム名。 |
| `Placeholder` | string | `""` | 編集欄のプレースホルダ。 |
| `Rows` | int | `8` | 編集欄の行数 (高さ)。利用者はドラッグで縦に伸ばせる。 |
| `MaxLength` | int? | `null` | 最大文字数 (Markdown の文字数)。null なら制限なし。 |
| `PreviewMode` | enum | `Tab` | 編集中のプレビューの出し方 (デザイナ表示名「プレビュー（読取専用時はプレビューで表示）」)。`Tab` = 編集 / プレビューのタブで切替、`Split` = 左に編集・右にプレビュー、`None` = 編集中はプレビューを出さない。**読取専用 (IsViewOnly) のときはこの設定に関係なく常にプレビュー (描画結果) だけを表示する**。 |
| `ShowToolbar` | bool | `true` | 見出し・太字・斜体・取り消し線・箇条書き・番号付き・チェックリスト・引用・リンク・コード・コードブロック・表を挿入するツールバーを出す。 |

### 必要な DB 構成

長さ無制限のテキスト型カラムを用意する。

```sql
body TEXT NULL
```

### 配置の注意

- 一覧 (ListField) のセルにも置けるが、閲覧表示は描画結果をそのまま出すので行が高くなる。一覧には置かず詳細画面で使うか、一覧側には別の TextField を出すのが無難
- 検索条件 (SearchLayout) には対応していない。検索したいキーワードは別の TextField に持たせる

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Value` | Markdown のテキスト (get / set) |
| `Html` | 現在の値を描画した HTML (読み取り専用)。MarkupStringField への転記などに |
| `PlainText` | 記法を落としたプレーンテキスト (読み取り専用) |
| `AppendLine(string line)` | 末尾に 1 行追加する |

### 使用例

```csharp
// 承認時に履歴を追記する
void Approve_OnClick()
{
    Notes.AppendLine("- " + DateTime.Now.ToString("yyyy/MM/dd HH:mm") + " " + CurrentUser.Name.Value + " が承認");
}

// AI の返事 (Markdown) をそのまま保存する
void Chat_OnReplyReceived(string reply)
{
    Summary.Value = reply;
}
```
