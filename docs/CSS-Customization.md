# CSS カスタマイズガイド

Codeer.LowCode.Blazor.Extras の各フィールドは、CSSクラスを使って見た目を自由にカスタマイズできます。このガイドでは、CSSの適用方法と各フィールドのカスタマイズ方法を説明します。

## CSSの適用方法

### デザイナーから設定する場合

1. デザイナーでモジュールのレイアウトを開く
2. レイアウトの **CSS** 欄にカスタムCSSを記述する

この方法で記述したCSSは、そのレイアウト内のフィールドに適用されます。

### wwwroot/css に配置する場合

アプリケーション全体に共通のスタイルを適用したい場合は、`wwwroot/css/` にCSSファイルを配置し、`index.html` で読み込みます。

```html
<link href="css/extras-custom.css" rel="stylesheet" />
```

## カスタマイズの基本

各フィールドのコンポーネントには、要素ごとにCSSクラスが付与されています。これらのクラスをセレクタとして指定し、スタイルを上書きできます。

### 例：CalendarField の背景色を変更する

```css
.calendar-container {
  background: #f0f0f0;
}
.calendar-toolbar {
  background: #e0e0e0;
}
```

### 例：GanttField のタスクバーの色を変更する

```css
.gantt-task-bar {
  fill: #34a853;
}
```

### 例：TaskBoardField のカードスタイルを変更する

```css
.taskboard-card {
  background: #fffde7;
  border: 1px solid #f9a825;
  border-radius: 4px;
}
```

### 例：RichTextField のツールバーを変更する

```css
.richtext-toolbar {
  background: #e3f2fd;
  border-bottom: 2px solid #1a73e8;
}
```

## 注意事項

- **inline style の優先順位**: フィールドのデザイナー設定（例: CalendarField の `ColorField`、TaskBoardField の `Color` / `BackgroundColor`）で指定された色は、inline style として適用されるため CSS クラスよりも優先されます。CSSで統一したい場合は、デザイナー側の色設定を空にしてください。
- **SVG要素のスタイル**: GanttField のタイムライン部分はSVGで描画されています。SVG要素のスタイルには `background` / `border` ではなく `fill` / `stroke` プロパティを使用してください。

## フィールド別 CSS クラス一覧

各フィールドの全CSSクラスとカスタマイズ例は、以下の個別ドキュメントを参照してください。

| フィールド | CSSカスタマイズドキュメント |
|---|---|
| CalendarField | [CalendarField CSS カスタマイズ](CalendarField-CSS-Customization.md) |
| GanttField | [GanttField CSS カスタマイズ](GanttField-CSS-Customization.md) |
| TaskBoardField | [TaskBoardField CSS カスタマイズ](TaskBoardField-CSS-Customization.md) |
| RichTextField | [RichTextField CSS カスタマイズ](RichTextField-CSS-Customization.md) |
| ColorPickerField | [ColorPickerField CSS カスタマイズ](ColorPickerField-CSS-Customization.md) |
