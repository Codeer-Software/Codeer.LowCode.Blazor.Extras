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

## 既存フィールド (作りかけ含む)

### CalendarField - カレンダー表示
- **状態**: 作りかけ (UIプロトタイプ段階)
- **機能**: 月表示カレンダー、イベント表示、モジュールデータとの連携
- **インターフェース**: `ISearchResultsViewField`, `ISearchResultsViewFieldDesign`
- **ファイル**: `Designs/CalendarFieldDesign.cs`, `Fields/CalendarField.cs`, `Components/CalendarFieldComponent.razor`

### MarkerListField - 画像マーカー
- **状態**: 作りかけ
- **機能**: 画像上にマーカー(ピン)を配置、クリック/ダブルクリックイベント
- **インターフェース**: `IDataDependentField`
- **ファイル**: `Designs/MarkerListFieldDesign.cs`, `Fields/MarkerListField.cs`, `Components/MarkerListFieldComponent.razor`

### EnterFocusMoveField - Enterキーでフォーカス移動
- **状態**: 実装済み
- **機能**: モジュール内で Enter キー押下時に次の入力要素にフォーカスを移動 (末尾→先頭ループ、tabindex尊重、IME対応)
- **UI**: なし (デザインモードでのみ `EnterFocusMove` ラベルを表示)
- **JS interop**: `wwwroot/enterfocusmove-interop.js` でモジュールルート (`[data-module]` / `[data-module-design]`) に keydown をバインド
- **除外**: `<textarea>`, `contenteditable`, `data-consumes-enter` 属性を持つ要素, Submit ボタン
- **ファイル**: `Designs/EnterFocusMoveFieldDesign.cs`, `Fields/EnterFocusMoveField.cs`, `Components/EnterFocusMoveFieldComponent.razor`, `wwwroot/enterfocusmove-interop.js`

### 実験済みプロトタイプ (Experiments/)
- **タスクガントチャート** (`TaskGantt.razor`): SVGベース、ドラッグ移動/リサイズ、タスク依存線、スナップ
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

値を持つフィールドは `XxxFieldData.cs` も必要 (4ファイル構成)。

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

- **ガントチャート** (GanttField) - タスク管理/設備稼働。SVGベースのプロトタイプあり (Experiments/)
- **カンバンボード** (KanbanField) - ドラッグ&ドロップでステータス変更
- **リッチテキストエディタ** (RichTextEditorField) - HTML/書式付きテキスト編集
- **チャート/ダッシュボード** (ChartField) - 各種グラフ表示
- **署名パッド** (SignatureField) - 手書き署名キャプチャ
- **地図** (MapField) - 位置データ可視化
