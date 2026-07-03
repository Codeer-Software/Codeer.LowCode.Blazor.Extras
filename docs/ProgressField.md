# ProgressField - 進捗バー

進捗率をガントチャート風の角丸バーで表示する**表示専用**フィールドです。値を自身で保持せず、**別のフィールドの値を参照**して表示します (DB カラムは持ちません)。

一覧レイアウトのカラムに配置すれば、各行のタスク進捗をバーで一覧表示できます。

## 機能

- **値の参照表示**: `ValueField` に指定した数値フィールドの値をバーで表示
- **色の参照**: `ColorField` に指定したフィールド (Text / ColorPicker) の色をバー色に使用。未設定/空なら `BarColor` (固定色) を使用
- **ガント風の見た目**: 全体は薄い同色、完了部分は濃い同色ベタ塗り (Gantt のタスクバーと同じ)
- **進捗率の重畳表示**: 進捗率をバーの上に重ねて表示。`ShowValueLabel` で表示 ON/OFF
- **見やすい文字色**: ラベルの文字色は背景色に対する YIQ コントラスト色を自動選択 (Gantt と同じロジック。明るい背景→濃色、暗い背景→白)
- **自動追従**: 参照先フィールドの値が変わると自動で再描画 (`IDataDependentField`)

## デザイナー設定プロパティ

「デザイナ表示名」は Designer (日本語環境) で表示されるラベルです。

| プロパティ | デザイナ表示名 | 型 | 必須 | 説明 |
|---|---|---|---|---|
| ValueField | 値フィールド | string | - | 進捗値を取得する数値フィールド (`NumberFieldDesign`) |
| ColorField | 色フィールド | string | - | バー色を取得するフィールド (`TextFieldDesign` / `ColorPickerFieldDesign`)。空なら `BarColor` を使用 |
| BarColor | バー色 | string | - | バー色の既定値 (`ColorField` 未設定/空のとき)。空なら既定色 `#1a73e8` |
| ShowValueLabel | 進捗率を表示 | bool | - | 進捗率をバー上に表示するか。既定 `true` |

`ValueField` の値は**パーセント (0〜100) としてそのまま扱い**、0〜100% にクランプして表示します (Gantt と同じ)。

## 色とコントラスト (Gantt との共通仕様)

- バー色の解決順: `ColorField` の値 → `BarColor` → 既定色 `#1a73e8`
- ラベルの文字色は、解決したバー色から YIQ 輝度を計算し、明るければ濃色 (`#1a1a1a`)、暗ければ白 (`#ffffff`) を選択します
- これによりバー色をどれに設定してもラベルが読みやすくなります

## 使い方の例

### タスク一覧に進捗バーを表示

1. モジュールに進捗値の数値フィールド (例: `Progress`) を用意する
2. `ProgressField` を追加し、`ValueField` に `Progress` を指定
3. 色を行ごとに変えたい場合は `ColorField` に色フィールド (例: `BackgroundColor`) を指定
4. 一覧レイアウトのカラムに `ProgressField` を配置

### 進捗値の更新

進捗値そのものは参照先フィールド (`ValueField`) 側で管理します。スクリプトで進捗を動かす場合は、参照先フィールドの値を変更してください。

```javascript
// 参照先の数値フィールドを更新するとバーも追従する
async function OnStep() {
  Progress.Value = (Progress.Value ?? 0) + 10;
}
```

## CSS クラス

バーの見た目は以下のクラスで上書きできます (バー色は通常デザイナーの `BarColor` / `ColorField` で指定します)。

| クラス | 対象 |
|---|---|
| `.progress-bar` | バー全体 (高さ・角丸など)。既定の高さは `20px` |
| `.progress-fill` | 完了部分 (濃い同色のベタ塗り) |
| `.progress-label` | バー上に重ねた進捗率ラベル |

```css
/* 例: バーを高くして角を丸める */
.progress-bar {
  height: 28px;
  border-radius: 14px;
}
```

## 備考

- 表示専用フィールドのため、このフィールド自体は DB に値を保存しません。進捗値は `ValueField` 側のフィールドで保持してください
- `ShowValueLabel` を `false` にすると、数値を出さずバーのみを表示します
