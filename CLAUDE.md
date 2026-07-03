# Codeer.LowCode.Blazor.Extras - Project Guide

## プロジェクト概要

Codeer.LowCode.Blazor 向けの拡張フィールドライブラリ。
カレンダー、ガントチャート、カンバンボードなど、ローコードフレームワークに高機能UIコンポーネントを追加する。
NuGetパッケージ `Codeer.LowCode.Blazor.Extras` (MIT) として配布。

- **リポジトリ**: https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Extras
- **ターゲット**: .NET 8.0
- **言語**: C# (Nullable有効, ImplicitUsings有効)
- **ライセンス**: MIT
- **本体リポジトリ**: `C:\tfs\codeer\Codeer.LowCode.Blazor` (参照用、直接編集しない)

## リポジトリ構成

```
Codeer.LowCode.Blazor.Extras/
├── Source/
│   ├── Codeer.LowCode.Blazor.Extras/   # メインライブラリ (Razor Class Library)
│   │   ├── Components/                   # Blazor UIコンポーネント (.razor + .razor.css)
│   │   ├── Designs/                      # フィールドデザインクラス (JSON定義)
│   │   └── Fields/                       # ランタイムフィールドクラス
│   └── Example/Extras/                   # 動作確認用サンプルアプリ
├── Experiments/                          # 実験的プロトタイプ (統合前の検証用)
│   ├── BlazorMyCalendarApp/              # カレンダーUI実験
│   └── GanttBlazor/                      # ガントチャートUI実験 (SVGベース)
├── bk/                                   # バックアップ
│   └── GanttBlazor/                      # ガントチャート旧版
└── LICENSE, README.md
```

## 依存関係

```xml
<PackageReference Include="Codeer.LowCode.Blazor" Version="1.2.45" />
<PackageReference Include="Microsoft.AspNetCore.WebUtilities" Version="8.0.8" />
<PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="8.0.8" />
<PackageReference Include="Microsoft.Extensions.Http" Version="8.0.0" />
```

本体 `Codeer.LowCode.Blazor` はNuGetパッケージとして参照。

## ビルド

```bash
dotnet build Source/Codeer.LowCode.Blazor.Extras/Codeer.LowCode.Blazor.Extras.csproj
```

`GeneratePackageOnBuild: True` でビルド時にNuGetパッケージを自動生成。

## 既存フィールド

すべて実装済み。各フィールドのユーザー向け詳細は `docs/Xxx.md` を参照。

### 表示・可視化系

#### CalendarField - カレンダー
- **状態**: 実装済み (docs/CalendarField.md)
- **機能**: 月・週・日表示のカレンダー。イベントの表示・追加・編集、モジュールデータとの連携
- **インターフェース**: `IDisplayName`, `ISearchResultsViewFieldDesign`, `IFillHeightFieldDesign`
- **ファイル**: `Designs/CalendarFieldDesign.cs`, `Fields/CalendarField.cs`, `Components/CalendarFieldComponent.razor`

#### GanttField - ガントチャート
- **状態**: 実装済み (docs/GanttField.md)
- **機能**: SVGベースのガントチャート。タスクのドラッグ移動・リサイズ、依存関係の管理
- **インターフェース**: `IDisplayName`, `ISearchResultsViewFieldDesign`, `IFillHeightFieldDesign`
- **ファイル**: `Designs/GanttFieldDesign.cs`, `Fields/GanttField.cs`, `Components/GanttFieldComponent.razor`

#### TaskBoardField - カンバンボード
- **状態**: 実装済み (docs/TaskBoardField.md)
- **機能**: カンバンボード。ドラッグ&ドロップでステータス変更
- **インターフェース**: `IDisplayName`, `ISearchResultsViewFieldDesign`, `IFillHeightFieldDesign`
- **ファイル**: `Designs/TaskBoardFieldDesign.cs`, `Fields/TaskBoardField.cs`, `Components/TaskBoardFieldComponent.razor`

#### MarkerListField - 画像マーカー
- **状態**: 実装済み (docs/MarkerListField.md)
- **機能**: 画像上にマーカー(ピン)を配置・操作。クリック/ダブルクリックイベント
- **インターフェース**: `ISearchResultsViewFieldDesign`, `IDataDependentField`, `IFillHeightFieldDesign`
- **ファイル**: `Designs/MarkerListFieldDesign.cs`, `Fields/MarkerListField.cs`, `Components/MarkerListFieldComponent.razor`

#### QrCodeField - QRコード
- **状態**: 実装済み (docs/QrCodeField.md)
- **機能**: 文字列をQRコード画像として表示する表示専用フィールド。参照フィールド(`SourceField`)の値、固定文字列(`Text`)、またはスクリプト(`Field.Text`)で内容を設定。ECCレベル・前景/背景色を指定可能
- **依存**: `QRCoder` (MIT, 純C#・System.Drawing非依存)。C#側でPNGを生成し base64 data URI で `<img>` 表示 (JS不要)
- **インターフェース**: `IDataDependentField` (`SourceField` 変更に自動追従)
- **ファイル**: `Designs/QrCodeFieldDesign.cs`, `Fields/QrCodeField.cs`, `Fields/QrCodeEccLevel.cs`, `Components/QrCodeFieldComponent.razor`

#### ProgressField - 進捗バー
- **状態**: 実装済み (docs/ProgressField.md)
- **機能**: 進捗率をガント風の角丸バーで表示する**表示専用**フィールド。値は別フィールド(`ValueField`)から、色も別フィールド(`ColorField`)または固定色(`BarColor`)から取得。進捗率をバー上に重ねて表示 (`ShowValueLabel` でON/OFF)、文字色は背景色に対する YIQ コントラスト色を自動選択 (Ganttと同じ)。リストに入れて各行の進捗表示に使える
- **インターフェース**: `IDataDependentField` (`ValueField`/`ColorField` の変更に自動追従)
- **ファイル**: `Designs/ProgressFieldDesign.cs`, `Fields/ProgressField.cs`, `Components/ProgressFieldComponent.razor`

### 入力系 (値を持つ / `ValueFieldDesignBase`)

#### RichTextField - リッチテキストエディタ
- **状態**: 実装済み (docs/RichTextField.md)
- **機能**: 書式付きテキストエディタ。太字・色・リンクなどのHTMLフォーマットに対応
- **ファイル**: `Designs/RichTextFieldDesign.cs`, `Fields/RichTextField.cs`, `Data/RichTextFieldData.cs`, `Components/RichTextFieldComponent.razor`

#### ColorPickerField - カラーピッカー
- **状態**: 実装済み (docs/ColorPickerField.md)
- **機能**: HTML5ネイティブカラーピッカー。色をHEX文字列として保存
- **ファイル**: `Designs/ColorPickerFieldDesign.cs`, `Fields/ColorPickerField.cs`, `Data/ColorPickerFieldData.cs`, `Components/ColorPickerFieldComponent.razor`

### ユーティリティ系 (UIなし / 補助)

#### EnterFocusMoveField - Enterキーでフォーカス移動
- **状態**: 実装済み (docs/EnterFocusMoveField.md)
- **機能**: モジュール内で Enter キー押下時に次の入力要素にフォーカスを移動 (末尾→先頭ループ、tabindex尊重、IME対応)
- **UI**: なし (デザインモードでのみ `EnterFocusMove` ラベルを表示)
- **JS interop**: `wwwroot/enterfocusmove-interop.js` でモジュールルート (`[data-module]` / `[data-module-design]`) に keydown をバインド
- **除外**: `<textarea>`, `contenteditable`, `data-consumes-enter` 属性を持つ要素, Submit ボタン
- **ファイル**: `Designs/EnterFocusMoveFieldDesign.cs`, `Fields/EnterFocusMoveField.cs`, `Components/EnterFocusMoveFieldComponent.razor`, `wwwroot/enterfocusmove-interop.js`

#### PasswordHashField - パスワードハッシュ
- **状態**: 実装済み (docs/PasswordHashField.md)
- **機能**: 平文 `PasswordField` を Submit 時にハッシュ+ソルトへ変換し、2つのDBカラムへ書き込む書き込み専用フィールド。UIなし
- **要件**: サーバ側で `PasswordHashHelper.ApplyPasswordHash(...)` を呼ぶ実装が必須 (Helper は Extras 本体に同梱)
- **ファイル**: `Designs/PasswordHashFieldDesign.cs`, `Fields/PasswordHashField.cs`, `Data/PasswordHashFieldData.cs`, `Components/PasswordHashFieldComponent.razor`

#### OrientationLockField - 画面の向き制御
- **状態**: 実装済み (docs/OrientationLockField.md)
- **機能**: タッチ端末で画面の向き(横/縦)が指定と異なるとき、全画面オーバーレイで回転を促す。CSSメディアクエリ (`pointer: coarse`) のみで制御 (JS不要)
- **UI**: デザインモードでは `OrientationLock (...)` プレースホルダを表示
- **ファイル**: `Designs/OrientationLockFieldDesign.cs`, `Fields/OrientationLockField.cs`, `Components/OrientationLockFieldComponent.razor`

### 実験済みプロトタイプ (Experiments/)
- **タスクガントチャート** (`TaskGantt.razor`): SVGベース、ドラッグ移動/リサイズ、タスク依存線、スナップ。→ `GanttField` として製品化済み
- **設備稼働ガントチャート** (`DeviceStateGantt.razor`): SVGベース、ステータス色分け(正常/警告/異常)、時間軸

---

## フィールド型の実装パターン

### ファイル構成

Extrasでは以下の3ファイル構成が基本 (データなしフィールドの場合):

| ファイル | 配置先 | 役割 |
|---|---|---|
| `XxxFieldDesign.cs` | `Designs/` | デザイン時定義 (JSON永続化) |
| `XxxField.cs` | `Fields/` | ランタイムロジック |
| `XxxFieldComponent.razor` | `Components/` | Blazor UI + `.razor.css` |

値を持つフィールドは `Data/XxxFieldData.cs` も必要 (4ファイル構成。データクラスは `Data/` に配置)。

### 名前空間

```
Codeer.LowCode.Blazor.Extras.Designs     # デザインクラス
Codeer.LowCode.Blazor.Extras.Fields      # ランタイムクラス
Codeer.LowCode.Blazor.Extras.Components  # Blazorコンポーネント
```

### デザインクラスのテンプレート

```csharp
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    public class XxxFieldDesign() : FieldDesignBase(typeof(XxxFieldDesign).FullName!)
    {
        // デザイナーで設定可能なプロパティ
        [Designer(CandidateType = CandidateType.Field)]
        public string SomeField { get; set; } = string.Empty;

        [Designer(CandidateType = CandidateType.ScriptEvent)]
        public string OnSomeEvent { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(XxxFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldDataBase? CreateData() => null;
        public override FieldBase CreateField() => new XxxField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            // フィールド参照の存在チェック
            context.CheckFieldFieldExistence(Name, nameof(SomeField), SomeField).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
            => context.Builder(base.ChangeName(context))
                .AddField(SomeField, x => SomeField = x)
                .Build();
    }
}
```

### ランタイムクラスのテンプレート

```csharp
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class XxxField(XxxFieldDesign design) : FieldBase<XxxFieldDesign>(design)
    {
        public override bool IsModified => false;
        public override FieldDataBase? GetData() => null;
        public override FieldSubmitData GetSubmitData() => new();
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            // 初期化ロジック
        }
    }
}
```

### Blazorコンポーネントのテンプレート

```razor
@using Codeer.LowCode.Blazor.Components
@using Codeer.LowCode.Blazor.Extras.Fields
@inherits FieldComponentBase<XxxField>

<div>
  <!-- UI実装 -->
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }
}
```

## 本体の主要クラス階層 (参照情報)

### フィールドデザイン基底クラス

```
FieldDesignBase                        # 全フィールドの基底 (データなし)
├── ValueFieldDesignBase               # 値を持つフィールド
│   └── DbValueFieldDesignBase         # DB列にマッピングされるフィールド
└── ListFieldDesignBase                # リスト系フィールド
```

### フィールドランタイム基底クラス

```
FieldBase : IUIContext                 # 全フィールドの基底
└── FieldBase<TDesign>                 # デザイン型付き
    └── ValueFieldBase<TDesign, TData, TValue>  # 値あり (Source/Current/IsModified)
```

### 主要インターフェース

- **`ISearchResultsViewFieldDesign`** - 検索条件を持つフィールドデザイン (`SearchCondition`)
- **`ISearchResultsViewField`** - 検索結果を表示するフィールド (`SetAdditionalConditionAsync`, `ReloadAsync`)
- **`IDataDependentField`** - 他フィールドのデータに依存するフィールド (`GetDependencyFields()`)
- **`IDisplayName`** - 表示名を持つフィールド

### 本体の主要フィールド型 (参照用)

TextField, NumberField, BooleanField, DateField, DateTimeField, TimeField,
SelectField, RadioGroupField, LinkField, IdField, FileField,
ListField, DetailListField, TileListField, SearchField,
ButtonField, SubmitButtonField, LabelField, ImageViewerField,
ModuleField, ProCodeField, ExecuteSqlField, QueryField

### データフロー

```
ModuleData → Module.InitializeDataAsync() → Field.InitializeDataAsync()
→ ユーザー操作
→ GetSubmitData() → ModuleDataService.SaveAsync()
```

### 子モジュール操作 (カレンダー等で使用)

```csharp
// 検索してモジュール一覧取得
var items = await this.GetChildModulesAsync(searchCondition, ModuleLayoutType.Detail, layoutName);

// 新規モジュール作成
var mod = await this.CreateChildModuleAsync(moduleName, ModuleLayoutType.Detail, layoutName);

// ダイアログ表示
var result = await mod.ShowDialogAsync("OK", "Cancel");

// 保存
await mod.SubmitAsync();
```

## Designer属性の使い方

```csharp
[Designer]                                          // 基本プロパティ (デザイナーUIに表示)
[Designer(Scope = DesignerScope.All)]               // 全スコープで表示
[Designer(CandidateType = CandidateType.Field)]     // フィールド名の候補リスト
[Designer(CandidateType = CandidateType.ScriptEvent)]  // スクリプトイベント
[Designer(CandidateType = CandidateType.DetailLayout)] // DetailLayout候補
[Designer(CandidateType = CandidateType.Resource)]  // リソースパス候補

[ModuleMember(Member = "...")]                      // どのモジュールのメンバーか
[TargetFieldType(Types = [typeof(TextFieldDesign)])] // 対象フィールド型を制限
[Layout(ModuleNameMember = "...")]                  // レイアウト候補のモジュール指定
[ScriptMethod(ArgumentTypes = ["string"], ArgumentNames = ["id"])]  // スクリプトイベントの引数定義
```

## Script関連属性

```csharp
[ScriptHide]                           // スクリプトから非表示
[ScriptName("Reload")]                 // スクリプト内での名前 (メソッド名からAsyncを除く等)
[ScriptMethodToProperty("Value")]      // 非同期メソッドをプロパティとして公開
```

## コード規約

- **privateフィールド**: `_camelCase`
- **publicプロパティ**: `PascalCase`
- **非同期メソッド**: `Async` サフィックス
- C#: インデント4スペース / Razor, CSS: インデント2スペース
- 改行: CRLF、末尾改行あり
- コードビハインドファイルは使わない (`.razor` 内の `@code {}` に記述)
- CSSスコープファイル (`.razor.css`) を各コンポーネントに配置

## 本体ソースの参照先

フィールド実装の参考にする場合、本体ソースは以下から読める:

```
C:\tfs\codeer\Codeer.LowCode.Blazor\Source\Codeer.LowCode.Blazor\
├── Repository\Design\          # デザインクラスの実装例
├── OperatingModel\             # ランタイムクラスの実装例
├── Components\Fields\          # Blazorコンポーネントの実装例
├── Repository\Data\            # データクラスの実装例
└── DataIO\                     # FieldBase拡張メソッド (GetChildModulesAsync等)
```

特に参考になるファイル:
- `ListField` 系: `ListFieldDesignBase.cs`, `ListField.cs`, `ListFieldComponent.razor`
- `CalendarField` の元になったパターン: `ISearchResultsViewField` の実装

## 今後追加予定のフィールド

(ガントチャート / カンバンボード / リッチテキストエディタ は実装済み。「既存フィールド」参照)

- **チャート/ダッシュボード** (ChartField) - 各種グラフ表示
- **署名パッド** (SignatureField) - 手書き署名キャプチャ
- **地図** (MapField) - 位置データ可視化
