# EnterFocusMoveField - Enterキーでフォーカス移動

モジュール内で Enter キーを押すと、次の入力要素にフォーカスを移動させるユーティリティフィールドです。
配置するだけでモジュール全体に機能が適用され、UI には表示されません (デザインモードではプレースホルダが表示されます)。

## 機能

- **Enter キーでフォーカス移動**: モジュール内の入力要素間を Enter キーで順に移動
- **末尾から先頭へループ**: 最後の要素で Enter を押すと先頭の要素に戻る
- **全選択**: 移動先が `<input>` の場合は文字列を全選択し、上書き入力しやすくする
- **tabindex 順序を尊重**: 正の tabindex を持つ要素を先に、その後 DOM 順で移動
- **IME 対応**: 日本語入力中 (変換中) の Enter は無視する
- **Submit ボタンは通常動作**: `type="submit"` のボタン/input では既定動作を維持

## デザイナー設定プロパティ

設定項目はありません。モジュール内に 1 つ配置するだけで有効になります。

## フォーカス移動対象

以下のセレクタに該当する要素が対象になります。

| 要素 | 条件 |
|---|---|
| `<input>` | `type="hidden"` / `disabled` / `readonly` を除く |
| `<select>` | `disabled` を除く |
| `<button>` | `disabled` を除く |
| `[contenteditable="true"]` | 常に対象 |
| `[tabindex]` | `tabindex="-1"` を除く |

非表示の要素 (`offsetParent === null` かつ `position !== fixed`) は自動的に除外されます。

## Enter キーが移動しないケース

以下の場合、Enter の既定動作を優先し、フォーカス移動は行いません。

| ケース | 理由 |
|---|---|
| `<textarea>` にフォーカスがある | 改行入力を優先 |
| `contenteditable` 要素にフォーカスがある | 改行入力を優先 |
| 要素 (または祖先) に `data-consumes-enter` 属性がある | カスタムフィールドが Enter を利用している宣言 |
| `<button type="submit">` / `<input type="submit">` | フォーム送信の既定動作を維持 |
| IME 変換中 (`isComposing`) | 変換確定の Enter と競合しないため |

### data-consumes-enter による除外

独自フィールドが Enter キーをキャンセル動作や候補選択などに使う場合、ルート要素に `data-consumes-enter` 属性を付けることで、EnterFocusMoveField の処理から除外できます。

```razor
<div data-consumes-enter>
  <!-- Enter を独自処理する UI -->
</div>
```

## 配置方法

モジュールのレイアウトに `EnterFocusMoveField` を 1 つ配置してください。見た目を持たないため、どこに置いても構いません。
同一モジュールに 2 つ以上配置しても問題なく動作しますが、キーイベントが多重にバインドされるため 1 つで十分です。

## 動作範囲

EnterFocusMoveField は、最も近い以下の要素をモジュールのルートとしてイベントを登録します。

1. `[data-module]` 要素 (DetailPageComponent)
2. `[data-module-design]` 要素 (ModuleDialog / ModulePanel)
3. 上記がなければ祖先の `<form>`
4. それもなければ親要素

このため、ダイアログ内・パネル内・ページ内いずれのモジュールでも、そのモジュール内に限定してフォーカス移動が行われます。

## スクリプト API

このフィールドはスクリプトから利用できる API を公開していません。
