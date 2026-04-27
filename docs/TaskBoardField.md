# TaskBoardField - カンバンボード

モジュールデータをカンバンボード形式で表示するフィールドです。ステータスごとのカラムにカードを配置し、ドラッグ&ドロップでステータス変更や並べ替えが可能です。

## 機能

- **複数カラム表示**: ステータスごとにカラムを表示し、各カラム内にモジュールデータをカードとして表示
- **ドラッグ&ドロップ**: カードをカラム間で移動してステータスを変更。カラム内での並べ替えにも対応
- **カードの追加・編集**: カラムヘッダーの「+」ボタンで新規追加、カードのダブルクリックで編集 (無効化可能)
- **カード表示とポップアップでレイアウトを分離**: カード上に表示するレイアウトと、追加・編集ダイアログのレイアウトを別々に指定可能
- **カスタムカラー**: ステータスごとにヘッダーの文字色・背景色を設定可能
- **ソート順の自動管理**: 並べ替え時にソートインデックスを自動更新

## デザイナー設定プロパティ

| プロパティ | 型 | 説明 |
|---|---|---|
| DisplayName | string | フィールドの表示名 |
| SearchCondition | SearchCondition | データ取得元のモジュールと検索条件 |
| Statuses | TaskBoardStatuses | ボードのカラム定義 (後述) |
| StatusField | string | ステータス値を持つフィールド (Select型 または Text型) |
| CardLayoutName | string | カード上に表示するDetailレイアウト名 |
| PopupLayoutName | string | 追加・編集ダイアログで使用するDetailレイアウト名 |
| EnableDoubleClickPopup | bool | ダブルクリックで編集ポップアップを表示するか (デフォルト: true) |
| SortIndexField | string | 並び順を持つフィールド (Number型) |
| OnDataChanged | string | データ変更時に呼び出すスクリプトイベント |

## ステータス設定 (TaskBoardStatuses)

Statusesプロパティで、ボードに表示するカラムを定義します。各ステータスには以下の設定があります。

| プロパティ | 型 | 説明 |
|---|---|---|
| DisplayText | string | カラムヘッダーに表示するテキスト |
| Value | string | ステータスの値 (空の場合はDisplayTextが使われる) |
| Color | string | ヘッダーの文字色 (CSSカラー値) |
| BackgroundColor | string | ヘッダーの背景色 (CSSカラー値) |
| CanAdd | bool | このカラムに新規カードを追加できるか (デフォルト: true) |

## 必要なモジュール構成

| 用途 | 必須 | 対応する型 |
|---|---|---|
| ステータス | 必須 | SelectField または TextField |
| ソート順 | 任意 | NumberField |

## 操作方法

| 操作 | 動作 |
|---|---|
| カードをドラッグ | 別のカラムやカラム内の位置に移動 |
| カードをダブルクリック | 編集ダイアログを表示 (`EnableDoubleClickPopup` が true のときのみ) |
| 「+」ボタン | 指定ステータスで新規カードを追加 |

## スクリプトAPI

| メンバー | 種別 | 説明 |
|---|---|---|
| Reload() | メソッド | データを再読み込み |

## CSS カスタマイズ

カンバンボードの見た目はCSSクラスで自由にカスタマイズできます。全CSSクラス一覧とカスタマイズ例は **[TaskBoardField CSS カスタマイズガイド](TaskBoardField-CSS-Customization.md)** を参照してください。
