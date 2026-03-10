# GanttField - ガントチャート

モジュールデータをガントチャート形式で表示するフィールドです。タスクの期間をバーで可視化し、ドラッグによる移動・リサイズ、タスク間の依存関係の管理が可能です。

## 機能

- **SVGベースのタイムライン描画**: 日・週・月の3つのビューモードとカスタム範囲指定
- **タスクバーのドラッグ操作**: バー全体の移動、左端・右端のリサイズ
- **進捗表示**: バー内に進捗率を視覚的に表示
- **依存関係の管理**: タスク間の依存線を矢印で表示。右クリックメニューから追加、Deleteキーで削除
- **レスポンシブ対応**: コンテナ幅に合わせた自動調整 (FitToWidth)
- Date型 / DateTime型のどちらにも対応

## デザイナー設定プロパティ

### タスクモジュール設定 (SearchCondition)

| プロパティ | 型 | 説明 |
|---|---|---|
| DisplayName | string | フィールドの表示名 |
| SearchCondition | SearchCondition | タスクデータの取得元モジュールと検索条件 |
| TextField | string | タスク名として表示するフィールド (Text型) |
| StartField | string | タスクの開始日時フィールド (DateTime型 または Date型) |
| EndField | string | タスクの終了日時フィールド (DateTime型 または Date型) |
| ProgressField | string | 進捗率フィールド (Number型、0〜100。省略可) |
| IdField | string | タスクの一意識別子フィールド (Id型) |
| ProcessingCounterField | string | 楽観ロック用カウンターフィールド (Number型、省略可) |
| DetailLayoutName | string | 編集・追加時に表示するDetailレイアウト名 |

### 依存関係モジュール設定 (DependenciesModule)

タスク間の依存関係を管理するための別モジュールです。省略可能です。

| プロパティ | 型 | 説明 |
|---|---|---|
| DependenciesModule | SearchCondition | 依存関係データの取得元モジュールと検索条件 |
| DependencySourceIdField | string | 依存元タスクIDフィールド (Id型 または Link型) |
| DependencyDestinationIdField | string | 依存先タスクIDフィールド (Id型 または Link型) |

### 表示設定

| プロパティ | 型 | 説明 |
|---|---|---|
| EnableDayView | bool | 日表示を有効にする (デフォルト: true) |
| EnableWeekView | bool | 週表示を有効にする (デフォルト: true) |
| EnableMonthView | bool | 月表示を有効にする (デフォルト: true) |
| CustomRange | bool | カスタム範囲表示を有効にする (デフォルト: false) |
| CustomRangeEditable | bool | カスタム範囲の開始・終了日を編集可能にする (デフォルト: true) |
| FitToWidth | bool | タイムラインをコンテナ幅に合わせる (デフォルト: false) |
| ShowDetailHeader | bool | タイムラインの詳細ヘッダーを表示する (デフォルト: true) |
| ShowToolbar | bool | ツールバーを表示する (デフォルト: true) |
| OnDataChanged | string | データ変更時に呼び出すスクリプトイベント |

## 必要なモジュール構成

### タスクモジュール

| 用途 | 必須 | 対応する型 |
|---|---|---|
| タスク名 | 必須 | TextField |
| 開始日時 | 必須 | DateTimeField または DateField |
| 終了日時 | 必須 | DateTimeField または DateField |
| タスクID | 必須 | IdField |
| 進捗率 | 任意 | NumberField (0〜100) |
| 処理カウンター | 任意 | NumberField |

### 依存関係モジュール (省略可)

| 用途 | 必須 | 対応する型 |
|---|---|---|
| 依存元タスクID | 必須 | IdField または LinkField |
| 依存先タスクID | 必須 | IdField または LinkField |

## 操作方法

| 操作 | 動作 |
|---|---|
| バーをドラッグ | タスクの期間を移動 |
| バーの左端/右端をドラッグ | 開始日/終了日を変更 |
| バーをダブルクリック | 編集ダイアログを表示 |
| ツールバーの「+」ボタン | 新規タスクを追加 |
| バーを右クリック | 依存関係の追加メニューを表示 |
| 依存線を選択 → Delete | 依存関係を削除 |

## スクリプトAPI

| メンバー | 種別 | 説明 |
|---|---|---|
| ViewMode | プロパティ | 現在の表示モード (GanttViewMode) |
| Reload() | メソッド | データを再読み込み |
| SetViewMode(mode) | メソッド | 表示モードを変更 |
| SetCustomRange(start, end) | メソッド | カスタム範囲を設定 |

### GanttViewMode

| 値 | 説明 |
|---|---|
| Day | 日単位表示 |
| Week | 週単位表示 |
| Month | 月単位表示 |
| CustomRange | カスタム範囲表示 |
