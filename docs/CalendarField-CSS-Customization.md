# CalendarField CSS カスタマイズガイド

CalendarFieldComponent の各要素にはCSSクラスが付与されています。
ローコード設定のCSSでこれらのクラスを指定することで、見た目を自由にカスタマイズできます。

## CSS クラス一覧

### コンテナ・ツールバー

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.calendar-container` | カレンダー全体のルート要素 | `border-radius`, `border`, `background`, `font-family` |
| `.calendar-toolbar` | ツールバー（ナビゲーション + ビュー切替） | `background`, `border-bottom`, `padding` |
| `.toolbar-btn` | ツールバーのボタン共通 | `border-radius`, `border`, `color` |
| `.toolbar-btn-today` | 「今日」ボタン | `background`, `font-weight` |
| `.toolbar-btn-prev` | 前へボタン (◀) | `background` |
| `.toolbar-btn-next` | 次へボタン (▶) | `background` |
| `.toolbar-title` | タイトル（年月表示） | `font-size`, `color` |
| `.view-switcher` | ビュー切替ボタングループ | `border-radius`, `border` |
| `.view-btn` | ビュー切替ボタン（日/週/月） | `background`, `color`, `border-radius` |
| `.view-btn.active` | 選択中のビューボタン | `background`, `color` |

### 月表示

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.month-view` | 月表示のコンテナ | `background` |
| `.month-header` | 曜日ヘッダー行 | `border-bottom`, `background` |
| `.month-header-cell` | 曜日ヘッダーの各セル | `color`, `font-size` |
| `.month-grid` | カレンダーグリッド全体 | `background` |
| `.month-row` | 1週間の行 | — |
| `.month-cell` | 日のセル | `border`, `background`, `padding` |
| `.month-cell-other-month` | 前後月のセル | `background`, `opacity` |
| `.month-cell-today` | 今日のセル | `background` |
| `.month-date` | 日付の数字 | `color`, `font-size` |
| `.month-date-today` | 今日の日付（丸背景） | `background`, `color`, `border-radius` |

### 月表示 - イベント

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.month-event` | イベント共通 | `border-radius`, `font-size`, `padding` |
| `.month-event-allday` | 終日/複数日イベント | `background`, `color` |
| `.month-event-timed` | 時刻指定イベント | `background`, `color` |
| `.month-event-dot` | 時刻イベントの丸マーカー | `background`, `width`, `height` |
| `.month-event-time` | イベントの時刻テキスト | `color`, `font-size` |
| `.month-event-text` | イベントのタイトルテキスト | `font-size`, `color` |

### 週表示

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.week-view` | 週表示のコンテナ | `background` |
| `.week-header` | ヘッダー（曜日 + 日付） | `border-bottom`, `background` |
| `.week-header-cell` | ヘッダーの各曜日セル | `padding`, `background` |
| `.week-header-cell-today` | 今日の曜日セル | `background` |
| `.week-header-dayname` | 曜日名 | `color`, `font-size` |
| `.week-header-date` | 日付の数字 | `color`, `font-size` |
| `.week-header-date-today` | 今日の日付（丸背景） | `background`, `color` |

### 日表示

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.day-view` | 日表示のコンテナ | `background` |
| `.day-header` | ヘッダー | `border-bottom`, `background` |
| `.day-header-cell` | ヘッダーセル | `padding` |
| `.day-header-dayname` | 曜日名 | `color`, `font-size` |
| `.day-header-date` | 日付の数字 | `color`, `font-size` |
| `.day-header-date-today` | 今日の日付（丸背景） | `background`, `color` |

### 終日イベントセクション（週/日共通）

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.allday-section` | 終日イベント行全体 | `background`, `border-bottom` |
| `.allday-gutter` | 時刻ガター部分 | `background` |
| `.allday-cell` | 終日イベントのセル | `padding`, `border-left` |
| `.allday-event` | 終日イベント | `background`, `color`, `border-radius` |

### タイムグリッド（週/日共通）

| クラス名 | 要素 | カスタマイズ例 |
|---|---|---|
| `.time-grid-scroll` | スクロール領域 | — |
| `.time-grid` | タイムグリッド全体 | `background` |
| `.time-gutter` | 時刻表示列 | `width`, `background` |
| `.time-gutter-cell` | 時刻の各行 | `height` |
| `.time-label` | 時刻ラベル (例: "9:00") | `color`, `font-size` |
| `.time-column` | 日の列 | `border-left`, `background` |
| `.time-column-today` | 今日の列 | `background` |
| `.time-cell` | 1時間のセル | `height`, `border-bottom` |
| `.time-event` | 時間配置イベント | `background`, `color`, `border-radius`, `border` |
| `.time-event-title` | イベントタイトル | `font-size`, `font-weight`, `color` |
| `.time-event-time` | イベント時刻テキスト | `font-size`, `opacity` |

## カスタマイズ例

### 角を全て直角にする

```css
.calendar-container {
  border-radius: 0;
}
.view-switcher {
  border-radius: 0;
}
.toolbar-btn {
  border-radius: 0;
}
.month-event,
.allday-event,
.time-event {
  border-radius: 0;
}
```

### 背景色を変更する

```css
.calendar-container {
  background: #f0f0f0;
}
.calendar-toolbar {
  background: #e0e0e0;
}
.month-cell-other-month {
  background: #e8e8e8;
}
```

### イベントのデフォルト色を変更する

```css
.month-event-allday,
.allday-event,
.time-event {
  background: #e91e63;
}
.month-event-dot {
  background: #e91e63;
}
.month-date-today,
.week-header-date-today,
.day-header-date-today {
  background: #e91e63;
}
```

### 罫線を消す

```css
.month-cell {
  border: none;
}
.time-cell {
  border-bottom: none;
}
.time-column {
  border-left: none;
}
```

> **Note:** `ColorField` でイベントごとに個別の色が指定されている場合、inline style の `background` が CSS クラスより優先されます。
