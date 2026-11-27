# RichTextField CSS カスタマイズガイド

RichTextFieldComponent の各要素にはCSSクラスが付与されています。
ローコード設定のCSSでこれらのクラスを指定することで、見た目を自由にカスタマイズできます。

## セレクタの書き方

**セレクタの前に `:root` を付けてください**（例: `:root .richtext-toolbar`）。

```css
:root .richtext-toolbar {
  background: #e3f2fd;
}
```

- クラス名だけのセレクタ（`.richtext-toolbar { … }`）だと、既定のスタイルが同じプロパティを指定している所では既定のほうが勝ち、変わりません（ツールバーの背景・ボタンの大きさ・編集領域の文字の大きさなど、ほとんどの所が当てはまります）。
- 下の「CSS クラス一覧」は要素を示すためのクラス名です。CSS を書くときは、前に `:root` を付けます。
- `wwwroot/css` に置いたファイルで変えるときは、`index.html` でそのファイルを `〜.styles.css`（コンポーネントのスタイル）より後に読み込んでください。デザイナーの CSS に書いた分は、後に読み込まれるのでそのまま効きます。
- 無効（入力できない状態）のときのツールバーと編集領域の見た目を変えるときは、`:root .richtext-disabled .richtext-toolbar` のように `.richtext-disabled` を入れます。

## CSS クラス一覧

### エディター全体

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-editor-wrapper` | エディター全体のルート要素 | `border`, `border-radius`, `background` |
| `.richtext-content` | 編集領域（contenteditable） | `background`, `font-size`, `line-height`, `padding` |
| `.richtext-view` | 読み取り専用表示領域 | `font-size`, `line-height`, `padding` |

### ツールバー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-toolbar` | ツールバー全体 | `background`, `border-bottom`, `padding`, `gap` |
| `.richtext-toolbar-group` | ツールバーのボタングループ | `gap` |
| `.richtext-toolbar-btn` | ツールバーボタン共通 | `width`, `height`, `border-radius`, `color`, `background` |
| `.richtext-toolbar-select` | 見出しセレクト | `border`, `border-radius`, `background`, `font-size` |
| `.richtext-toolbar-separator` | グループ間の区切り線 | `width`, `height`, `background`, `margin` |
| `.richtext-toolbar-icon` | ボタン内アイコン（テキスト） | `font-size` |
| `.richtext-toolbar-svg-icon` | ボタン内アイコン（SVG） | `width`, `height` |

### ツールバーボタン（個別）

| クラス名 | 要素 |
|---|---|
| `.richtext-toolbar-btn-bold` | 太字ボタン |
| `.richtext-toolbar-btn-italic` | 斜体ボタン |
| `.richtext-toolbar-btn-underline` | 下線ボタン |
| `.richtext-toolbar-btn-strikethrough` | 取り消し線ボタン |
| `.richtext-toolbar-btn-ul` | 箇条書きボタン |
| `.richtext-toolbar-btn-ol` | 番号付きリストボタン |
| `.richtext-toolbar-btn-align-left` | 左揃えボタン |
| `.richtext-toolbar-btn-align-center` | 中央揃えボタン |
| `.richtext-toolbar-btn-align-right` | 右揃えボタン |
| `.richtext-toolbar-btn-forecolor` | 文字色ボタン |
| `.richtext-toolbar-btn-backcolor` | 背景色ボタン |
| `.richtext-toolbar-btn-link` | リンクボタン |
| `.richtext-toolbar-btn-clear` | 書式クリアボタン |
| `.richtext-toolbar-btn-undo` | 元に戻すボタン |
| `.richtext-toolbar-btn-redo` | やり直しボタン |

### カラーパレット

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-color-wrapper` | カラーボタンのラッパー | — |
| `.richtext-color-icon` | カラーボタン内の「A」アイコン | `font-size` |
| `.richtext-highlight-icon` | 背景色ボタンの「A」アイコン | `border-radius`, `padding` |
| `.richtext-color-palette` | カラーパレットのポップアップ | `background`, `border`, `border-radius`, `box-shadow`, `gap` |
| `.richtext-color-palette-header` | パレットヘッダー（閉じるボタン） | — |
| `.richtext-color-palette-close` | パレット閉じるボタン | `color`, `background` |
| `.richtext-color-swatch` | 色の選択肢（各色マス） | `width`, `height`, `border-radius`, `border` |
| `.richtext-color-swatch.selected` | 選択中の色 | `border` |

### リンクポップアップ

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-link-popup` | リンク入力ポップアップ全体 | `background`, `border`, `border-radius`, `box-shadow` |
| `.richtext-link-input` | URL入力フィールド | `width`, `height`, `border`, `font-size` |
| `.richtext-link-popup-btn` | ポップアップボタン共通 | `height`, `border-radius`, `font-size` |
| `.richtext-link-popup-ok` | OKボタン | `background`, `color` |
| `.richtext-link-popup-cancel` | キャンセルボタン | `color` |

### メンション

`@` を打つと出る候補の枠と、本文に入ったメンションの部品です（メンションの設定は [RichTextField](RichTextField.md) の「メンション」）。

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-mention-popup` | 候補の枠全体 | `min-width`, `max-width`, `background`, `border`, `border-radius`, `box-shadow`, `font-size` |
| `.richtext-mention-item` | 候補の 1 行 | `padding`, `gap` |
| `.richtext-mention-item.active` | 選択中の候補（↑↓ やマウスで選んでいる行） | `background`, `color` |
| `.richtext-mention-item-all` | 「全員」の行（`.richtext-mention-item` と一緒に付く） | `border-bottom` |
| `.richtext-mention-name` | 候補の表示名 | `font-weight`, `color` |
| `.richtext-mention-sub` | 補助の表示名（表示名の横の小さい文字） | `color`, `font-size` |
| `.richtext-content span.mention` / `.richtext-view span.mention` | 本文に入ったメンションの部品（編集領域・表示領域） | `background`, `color`, `border-radius`, `padding`, `font-weight` |

- 候補の枠の位置（`position`・`top`・`left`）と高さ（`max-height`）は、画面に収まるようにプログラムが決めています。CSS で変えないでください。
- 「全員」のメンションだけを変えるときは `span.mention[data-mention-all]`、人のメンションだけなら `span.mention[data-mention-id]` を使います。

### コンテンツ内の要素（編集領域・表示領域共通）

エディター内のHTML要素です（CSS を書くときは、ほかと同じく前に `:root` を付けます）。

| セレクタ | 要素 | カスタマイズ例 |
|---|---|---|
| `.richtext-content h1` / `.richtext-view h1` | 見出し1 | `font-size`, `margin`, `color` |
| `.richtext-content h2` / `.richtext-view h2` | 見出し2 | `font-size`, `margin`, `color` |
| `.richtext-content h3` / `.richtext-view h3` | 見出し3 | `font-size`, `margin`, `color` |
| `.richtext-content ul, ol` / `.richtext-view ul, ol` | リスト | `padding-left`, `margin` |
| `.richtext-content a` / `.richtext-view a` | リンク | `color`, `text-decoration` |
| `.richtext-content p` / `.richtext-view p` | 段落 | `margin` |

## カスタマイズ例

### 枠線と角丸を変更する

```css
:root .richtext-editor-wrapper {
  border: 2px solid #1a73e8;
  border-radius: 8px;
}
```

### ツールバーの外観を変更する

```css
:root .richtext-toolbar {
  background: #e3f2fd;
  border-bottom: 2px solid #1a73e8;
}
:root .richtext-toolbar-btn {
  border-radius: 50%;
  width: 32px;
  height: 32px;
}
:root .richtext-toolbar-btn:hover {
  background: #bbdefb;
}
```

### 編集領域のフォントを変更する

```css
:root .richtext-content {
  font-family: "游ゴシック", "Yu Gothic", sans-serif;
  font-size: 16px;
  line-height: 1.8;
  background: #fafafa;
}
```

### 見出しの色を変更する

```css
:root .richtext-content h1,
:root .richtext-view h1 {
  color: #1a73e8;
}
:root .richtext-content h2,
:root .richtext-view h2 {
  color: #1557b0;
}
```

### リンクの色を変更する

```css
:root .richtext-content a,
:root .richtext-view a {
  color: #e91e63;
}
```

### メンションの色を変更する

```css
/* 本文に入ったメンション */
:root .richtext-content span.mention,
:root .richtext-view span.mention {
  background: #fff3cd;
  color: #664d03;
}
/* 「全員」だけ太字にする */
:root .richtext-content span.mention[data-mention-all],
:root .richtext-view span.mention[data-mention-all] {
  font-weight: 600;
}
/* 候補の枠で選択中の行 */
:root .richtext-mention-item.active {
  background: #fff3cd;
}
```

### ツールバーを非表示にする（表示専用風）

```css
:root .richtext-toolbar {
  display: none;
}
```

### カラーパレットのサイズを変更する

```css
:root .richtext-color-swatch {
  width: 24px;
  height: 24px;
}
:root .richtext-color-palette {
  gap: 3px;
  padding: 8px;
}
```
