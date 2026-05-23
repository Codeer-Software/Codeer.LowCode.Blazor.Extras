# OrientationLockField - 画面の向き制御

端末の画面の向き (横 / 縦) が指定と異なるとき、全画面オーバーレイで「正しい向きに回転してください」と促すユーティリティフィールドです。
タブレットやスマートフォンなどのタッチ端末でのみ動作し、マウス操作の PC では何も表示されません。

## 機能

- **許可する向きを指定**: 横向き (Landscape) / 縦向き (Portrait) のどちらかを許可
- **許可外の向きでオーバーレイ表示**: 許可外の向きになると全画面の半透明オーバーレイ + 回転アイコン (⟳) を表示し、操作をブロック
- **タッチ端末限定**: `(pointer: coarse)` のメディアクエリで判定するため、マウス主体の PC では表示されない
- **任意のメッセージ**: `Message` を設定するとアイコンの下に表示 (未設定ならアイコンのみ)
- **JS 不要**: 向きの判定と表示切り替えはすべて CSS のメディアクエリで完結

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| AllowedOrientation | enum | - | 許可する向き。`Landscape` (横) / `Portrait` (縦)。既定は `Landscape` |
| Message | string | - | オーバーレイに表示するメッセージ (例: `横向きにしてご利用ください`) |

## 動作仕様

| AllowedOrientation | 端末の向き | 表示 |
|---|---|---|
| Landscape (横) | 横向き | 通常表示 (オーバーレイなし) |
| Landscape (横) | 縦向き | オーバーレイ表示 (回転を促す) |
| Portrait (縦) | 縦向き | 通常表示 (オーバーレイなし) |
| Portrait (縦) | 横向き | オーバーレイ表示 (回転を促す) |

いずれの場合も `(pointer: coarse)` を満たすタッチ端末でのみオーバーレイが表示されます。マウス操作の PC (fine pointer) では、向きに関わらずオーバーレイは表示されません。

デザインモードでは、配置位置に `OrientationLock (Landscape)` のようなプレースホルダが表示されます。

## モジュール JSON 例

```json
{
  "AllowedOrientation": "Landscape",
  "Message": "横向きにしてご利用ください",
  "Name": "OrientationLock",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.OrientationLockFieldDesign"
}
```

## 配置方法

モジュールのレイアウトに `OrientationLockField` を 1 つ配置してください。オーバーレイは `position: fixed; inset: 0; z-index: 100000` で画面全体を覆うため、レイアウト上のどこに置いても表示位置は変わりません。

## CSS カスタマイズ

オーバーレイは以下の CSS クラスで構成されています。背景色・アイコン・メッセージのスタイルを上書きできます。

| CSS クラス | 対象 |
|---|---|
| `.extras-orientation-overlay` | オーバーレイ全体 (背景・配置) |
| `.extras-orientation-overlay--require-landscape` | 横向き要求時に付与される修飾クラス |
| `.extras-orientation-overlay--require-portrait` | 縦向き要求時に付与される修飾クラス |
| `.extras-orientation-overlay__content` | アイコン + メッセージのコンテナ |
| `.extras-orientation-overlay__icon` | 回転アイコン (⟳)。`extras-orientation-rotate` アニメーションで回転 |
| `.extras-orientation-overlay__message` | メッセージテキスト |

### 例: オーバーレイの背景色とメッセージ色を変更する

```css
.extras-orientation-overlay {
  background: rgba(26, 115, 232, 0.9);
}
.extras-orientation-overlay__message {
  color: #fff;
  font-size: 1.2rem;
}
```

## スクリプト API

このフィールドは値を持たず、スクリプトから利用できる API を公開していません。
