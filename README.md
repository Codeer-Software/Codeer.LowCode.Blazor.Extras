# Codeer.LowCode.Blazor.Extras

[Codeer.LowCode.Blazor](https://github.com/Codeer-Software/Codeer.LowCode.Blazor) に高機能UIコンポーネントを追加する拡張フィールドライブラリです。

[![NuGet](https://img.shields.io/nuget/v/Codeer.LowCode.Blazor.Extras)](https://www.nuget.org/packages/Codeer.LowCode.Blazor.Extras)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## 提供フィールド

| フィールド | 説明 |
|---|---|
| [CalendarField](docs/CalendarField.md) | 月・週・日表示のカレンダー。イベントの表示・追加・編集が可能 |
| [GanttField](docs/GanttField.md) | SVGベースのガントチャート。タスクのドラッグ移動・リサイズ、依存関係の管理が可能 |
| [TaskBoardField](docs/TaskBoardField.md) | カンバンボード。ドラッグ&ドロップでステータス変更が可能 |
| [RichTextField](docs/RichTextField.md) | 書式付きテキストエディタ。太字・色・リンクなどのHTMLフォーマットに対応 |
| [MarkerListField](docs/MarkerListField.md) | 画像上にマーカー(ピン)を配置・操作するフィールド |

## セットアップ

### 1. NuGet パッケージのインストール

`LowCodeApp.Client.Shared` プロジェクトに NuGet から次のパッケージをインストールしてください。

```
Codeer.LowCode.Blazor.Extras
```

### 2. コードの修正

ライブラリを使用するために、以下の各プロジェクトに初期化コードを追加する必要があります。

#### LowCodeApp.Client.Shared

`Services/AppInfoService.cs` の `AppInfoService` コンストラクタに以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras;

// AppInfoService コンストラクタ内
ExtrasClientInitializer.Initialize(this);
```

#### LowCodeApp.Server

`Program.cs` に以下のコードを追加してください。

```csharp
using Codeer.LowCode.Blazor.Extras;

ExtrasServerInitializer.Initialize();
```

### 3. セットアップ完了

以上でセットアップは完了です。Designer からExtras のフィールドが配置できるようになります。

## 各フィールドの詳細

各フィールドの詳しい説明は以下のドキュメントを参照してください。

- [CalendarField - カレンダー](docs/CalendarField.md)
- [GanttField - ガントチャート](docs/GanttField.md)
- [TaskBoardField - カンバンボード](docs/TaskBoardField.md)
- [RichTextField - リッチテキストエディタ](docs/RichTextField.md)
- [MarkerListField - 画像マーカー](docs/MarkerListField.md)

## ライセンス

[MIT License](LICENSE)
