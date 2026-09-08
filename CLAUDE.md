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

#### MarkdownField - Markdown エディタ / ビューア
- **状態**: 実装済み (docs/MarkdownField.md)
- **機能**: Markdown をプレーンテキストのまま DB 列に保存し、閲覧時 (IsViewOnly) は HTML に描画。編集時はツールバー (見出し/太字/斜体/取り消し線/箇条書き/番号付き/チェックリスト/引用/リンク/コード/コードブロック/表) と `PreviewMode` (Tab=タブ切替 / Split=左右並び / None。**編集中だけの設定で、読取専用時は常にプレビュー表示**。表示名は「プレビュー（読取専用時はプレビューで表示）」= 「なし」なのに閲覧で描画されて見える誤読への対策 2026-09-08)。`MaxLength` / `Placeholder` / `ShowToolbar`。必須と最大文字数の検証。**高さの設定は持たない** (0.9.1 で Rows 撤去。JS `attach`/`autoSize` が中身に合わせて伸ばす=最小 6 行。FillAvailable の最終行や行 Height で外から高さが決まっているときは「textarea を 0 にしても .markdown-body が縮まない」で判定して伸ばさず stretch+内部スクロール。RichText と同じ振る舞い)
- **描画**: `Markdown/MarkdownRenderer` (Markdig 0.37.0、AdvancedExtensions + 単独改行を `<br>` + **`DisableHtml` で生 HTML 無効化** = 信頼できないテキストを描く前提) + リンクに target=_blank。表示 CSS は `.markdown-view ::deep`。閲覧・プレビューで共通
- **編集の同期**: textarea はローカル `_text` を oninput で持ち、blur (onchange) / ツールバー操作 / タブ切替で `Field.SetValueAsync` (キー入力ごとに OnDataChanged を起こさない)。外部からの値変更は `Field.OnDataChangedAsync` で `_text` へ戻す。ツールバーは `wwwroot/markdown-interop.js` の `applyAction` が選択範囲を書き換えて全文を返す
- **RichTextField との住み分け**: 書き手が記法を知っている人か AI なら Markdown、記法を知らない利用者なら RichText (HTML 保存)
- **未対応**: 検索条件 (SearchLayout)、一覧セル向けの 1 行表示、画像アップロード
- **スクリプト**: `Value` / `Html` / `PlainText` / `AppendLine(string)`
- **ファイル**: `Designs/MarkdownFieldDesign.cs` (enum `MarkdownPreviewMode` 同居), `Data/MarkdownFieldData.cs`, `Fields/MarkdownField.cs`, `Components/MarkdownFieldComponent.razor(.css)`, `Markdown/MarkdownRenderer.cs`, `wwwroot/markdown-interop.js`

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

#### LoginAccountContractField - ログインアカウント契約
- **状態**: 実装済み (Extras.Designer FieldDocs/LoginAccountContractFieldDesign.md)
- **機能**: ユーザーモジュール (CurrentUserModule) に置き、行がログインアカウントとしてどう振る舞うかを宣言する契約フィールド。役割 (フィールド参照): LoginName (必須) / ExternalLoginName (外部 IdP の突き合わせ。空なら LoginName) / IsActive (偽なら拒否) / DisplayName (Cookie の Name と TOTP の QR) / TwoFactorEmail (メール二要素の送信先)。ログイン用の書き込み専用列: DbColumnPasswordHash + Salt (照合用。空 = パスワードログイン無し)、DbColumnTotp* 3 列 (認証アプリの二要素。空 = 無効)。テンプレートのログイン (Extras.Server `LoginAccountStore` / `TotpLogin` / `EmailOtpLogin`) はこの契約だけを見る。PasswordHashField は「書く側」として別に置く
- **ファイル**: `Designs/LoginAccountContractFieldDesign.cs` (`ContractFieldDesignBase` 派生。UI・データ無し)

#### EmailOtpLogin - 二要素認証 (メールのワンタイムコード)
- **状態**: 実装済み (docs/TwoFactorLogin.md)。Extras.Server `Auth/EmailOtpLogin.cs` (フィールドは無し)
- **機能**: LoginAccountContractField の TwoFactorEmail (送信先フィールド) を設定するとパスワード成功後に 6 桁コードをメールで送り入力を求める。コードはサーバーのキャッシュ (IDistributedCache) に有効期限付き。試行超過で破棄。契約の TOTP 列があれば認証アプリ優先
- **設定**: appsettings `EmailOtpLogin` (MailInfraName / Subject / Body / CodeLifetimeMinutes / MaxAttempts、すべて任意)

#### TotpResetButtonField - 認証アプリ解除ボタン (管理者用)
- **状態**: 実装済み (Extras.Designer FieldDocs/TotpResetButtonFieldDesign.md)
- **機能**: ログインユーザーモジュールの詳細画面に置き、表示中の行のユーザーの認証アプリ (TOTP) 登録を解除する。TOTP の 3 列を `DbColumn(IsWriteOnly)` で持ち、解除は「3 列を空にするデータ」を通常の Submit で書く (SubmitButtonField と同じく Module.SubmitAsync)。権限は CLB の権限モデル (UserWrite / DataWrite 条件) がそのまま効くのでサーバーに専用の入口は無い。列は契約と同じでなければならない (デザインチェック)。登録済みかは表示しない (書き込み専用列は SELECT されない)
- **ファイル**: `Designs/TotpResetButtonFieldDesign.cs`, `Data/TotpResetButtonFieldData.cs`, `Fields/TotpResetButtonField.cs`, `Components/TotpResetButtonFieldComponent.razor`

#### MyTotpResetButtonField - 自分の認証アプリ解除ボタン
- **状態**: 実装済み (Extras.Designer FieldDocs/MyTotpResetButtonFieldDesign.md)
- **機能**: ログイン中の自分の認証アプリ (TOTP) 登録を解除するボタン。表示中の行とは無関係で、どのモジュールにも置ける (設定画面・マイページ)。状態はサーバー問い合わせ (列は書き込み専用)
- **結線**: 静的 `TotpResetClient.StatusEndPoint` / `ResetEndPoint` (テンプレートは api/account/totp/status, /reset。ログイン中のユーザーが対象)
- **ファイル**: `Designs/MyTotpResetButtonFieldDesign.cs`, `Fields/MyTotpResetButtonField.cs`, `Fields/TotpResetClient.cs`, `Components/MyTotpResetButtonFieldComponent.razor`

#### LoginAccountContractField のパスワード書き込み (2026-09-07)
- 契約の `PasswordField` (同モジュールの PasswordField 参照) を指定すると、`PasswordHashHelper.ApplyPasswordHash` が契約のデータ (`LoginAccountContractFieldData` = PasswordHash / PasswordSalt) を差し込み、本体が契約の書き込み専用列へ書く。ユーザーモジュールに PasswordHashField は不要 (併用はデザインチェック エラー・ヘルパーも契約を優先)
- TOTP の 3 列は契約の列名プロパティだけ (DbColumn 属性なし)。本体の書き込みは「送られたフィールドの DbColumn メンバーを全部書く」ので、同じデータに載せるとパスワード保存で NULL 上書きされるため。`ContractFieldDesignBase.GetRoleProperties` は CandidateType.DbColumn のプロパティを役割から除外する

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
