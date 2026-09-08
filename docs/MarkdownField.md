# MarkdownField - Markdown エディタ / ビューア

Markdown を入力・表示する値フィールドです。値は **Markdown のテキストそのまま**で DB カラムに保存し、閲覧時 (IsViewOnly) は HTML に描画して表示します。編集時は「編集 / プレビュー」のタブ切替か左右並びでプレビューできます。

描画には [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause) を使います。純 C# 実装で WebAssembly 上で動くため、サーバー往復なしにプレビューできます。

## RichTextField との使い分け

| | MarkdownField | RichTextField |
|---|---|---|
| 保存形式 | Markdown (プレーンテキスト) | HTML |
| 書き手 | 記法を知っている人、AI | 記法を知らない利用者 (WYSIWYG) |
| DB 検索 / CSV・Excel 出力 | そのまま読める | タグが混ざる |
| 向く内容 | 備考・仕様・手順・議事メモ、AI の出力 | 見た目を細かく作る文書 |

## 機能

- **編集**: ツールバーから見出し・太字・斜体・取り消し線・箇条書き・番号付き・チェックリスト・引用・リンク・インラインコード・コードブロック・表を挿入。選択範囲を囲む / 外す、行頭記法の付け外しに対応
- **プレビュー**: 編集中の見え方を `PreviewMode` で「タブ切替 (既定)」「左右並び」「なし」から選ぶ
- **閲覧 (読取専用)**: `PreviewMode` に関係なく、常に描画結果 (プレビューと同じ見え方) だけを表示。表はフィールド幅を超えたら表だけ横スクロール
- **安全**: Markdown 中の生 HTML は無効化される (文字として見える)。利用者や外部から来たテキストをそのまま描いても script は動かない
- **記法**: GitHub 風の拡張 (表・チェックリスト・自動リンク・取り消し線 など)。単独の改行は改行として表示 (Enter がそのまま改行になる)
- **リンク**: 別タブで開く
- **検証**: 必須 (`IsRequired`) と最大文字数 (`MaxLength`)

## デザイナー設定プロパティ

| プロパティ | デザイナ表示名 | 型 | 必須 | 説明 |
|---|---|---|---|---|
| DbColumn | DBカラム | string | ○ | Markdown テキストを保存する DB カラム名 |
| Placeholder | プレースホルダ | string | - | 編集欄のプレースホルダ |
| Rows | 行数 | int | - | 編集欄の行数 (高さ)。既定 8。利用者はドラッグで縦に伸ばせる |
| MaxLength | 最大文字数 | int? | - | Markdown の文字数の上限。未設定なら制限なし |
| PreviewMode | プレビュー（読取専用時はプレビューで表示） | enum | - | 編集中のプレビューの出し方。`Tab` (編集 / プレビューのタブ) / `Split` (左右に並べる) / `None` (編集中は出さない)。既定 `Tab`。読取専用 (IsViewOnly) のときはこの設定に関係なく常にプレビュー (描画結果) だけを表示する |
| ShowToolbar | ツールバーを表示 | bool | - | 既定 true |

値フィールド共通の `DisplayName` / `IsRequired` / `OnDataChanged` も使えます。

### DB カラム

長さ無制限のテキスト型を用意してください。

```sql
body TEXT NULL
```

## スクリプト API

| メンバ | 説明 |
|---|---|
| `Value` | Markdown のテキスト (get / set) |
| `Html` | 現在の値を描画した HTML (読み取り専用) |
| `PlainText` | 記法を落としたプレーンテキスト (読み取り専用) |
| `AppendLine(string line)` | 末尾に 1 行追加する |

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

## 配置の注意

- 一覧 (ListField) のセルにも置けますが、閲覧表示は描画結果をそのまま出すので行が高くなります。一覧には置かず詳細画面で使うか、一覧側には別の TextField を出すのが無難です
- 検索条件 (SearchLayout) には対応していません。検索したいキーワードは別の TextField に持たせてください
- 画像は URL 指定の `![alt](https://...)` だけ描画されます。FileField との連携はありません

## CSS カスタマイズ

描画結果は `.markdown-view` の下に入ります。見出し・表・コードの見え方を変えたいときは app.css でこのクラス配下を上書きしてください。

```css
.markdown-view h2 { color: #1a73e8; }
.markdown-view table th { background: #eef3ff; }
```
