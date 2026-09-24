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

#### AIChatField - AI チャット UI
- **状態**: 実装済み (docs/AIChatField.md)。サーバー側に標準 Agent (`RawDataAccessAgent`。会話エンジン `ChatClientAgent` は internal) あり。Example の既定はダミー (`Example/.../Extras.Server/AI/DummyAIChatAgent.cs`)、`RawDataAccess` は AISettings (Azure OpenAI) があるときだけ登録
- **機能**: チャット UI だけを担う。入力 (Ctrl+Enter 常に送信 / Shift+Enter 常に改行 / Enter は `SendOnEnter` 既定 true / IME 対応 / 自動伸長。Placeholder / WelcomeMessage は撤去 = 必要なら画面にラベルを置く。`.aichat-messages` は min-height 16rem で初期表示に高さ)、考え中ドット + 途中経過 + 経過秒、逐次表示、エラー + 再送、停止、新しい会話、返事のコピー。返事は HTML をそのまま描画 (SVG グラフ含む)
- **Agent の選択**: デザインの `Agent` (文字列、空 = 既定) を `AIChatSendRequest.Agent` で送り、`AIChatJobStore` がコンストラクタで受けた対応表 `Func<string, IAIChatAgent?>` で解決 (アプリ側の静的クラス。Example は `AI/AIChatAgentTable.cs` = MailSenderTable と同型、名前ごとに Lazy で 1 つ、`Jobs` 静的プロパティに JobStore)。無い名前は error。DI 登録なし。`AIChatJobStore.Start(owner, conv, message, agentName)`
- **通信**: `POST {EndPoint}` → 202 `{requestId}` → `GET {EndPoint}/{requestId}` をポーリング (最初の 10 秒は 1 秒、以後 2.5 秒) → `{status, reply, progress, error}`。`DELETE` で中断。やり取りする型は `AIChat/` (AIChatSendRequest / AIChatSendResponse / AIChatStatusResponse / AIChatJobStatus)
- **サーバー** (Extras.Server `AI/Chat/`。フォルダ = 役割: 直下 = 共有の契約と窓口 (`IAIChatAgent` / `AIChatJobStore` / `ChatReplyHtml`)、`ChatClient/` = 内部の会話エンジン (ChatClientAgent とツールセット機構、Chart ツール。すべて internal)、`RawDataAccess/` = 専用 (Agent / Options / ToolSet / DbSchemaReader)、`DesignKnowledge/` = 設計参照ツール (DesignDescriber = DesignData の要約、DesignKnowledgeToolSet = list_modules / describe_module / read_document)。名前空間もフォルダどおり): `IAIChatAgent` / `AIChatJobStore` (プロセス内ジョブ。プロセスに 1 つ。対応表 Func か単一 Agent を受ける) / `ChatReplyHtml` (Markdig で Markdown→HTML、テキストはエスケープ、HTML 素通し + target=_blank) / `ChatClientAgent` (Microsoft.Extensions.AI `IChatClient`。システムプロンプト + internal な `IAIChatToolSet` 群、会話履歴 `ConversationHistory` (鍵 = 所有者+会話ID (所有者空なら会話IDのみ)。切り詰め 3 段: 古いターンのツール呼出/結果を落とす KeepToolResultsForTurns=2 → ターン数 MaxHistoryTurns=20 → 文字数 MaxHistoryCharacters=40000。いずれもターン境界で)、FunctionInvokingChatClient で往復上限、ストリーミング → ReportPartial、Markdown→HTML→ToolSet.PostProcessHtml) / `RawDataAccess/RawDataAccessToolSet` (get_schema = `DbSchemaReader` で DB 種別ごとのカタログ問い合わせ + モジュール定義の業務名、execute_sql = 1 文 SELECT 判定 `Validate` + DbCommand 直叩き (タイムアウト・MaxRows・MaxResultChars) + ログ) / `ChatClient/ChartToolSet` + `SvgChart` (render_chart → `[[chart:N]]` プレースホルダ → 後処理で `<div class="aichat-chart"><svg>`) / `RawDataAccessAgent` (ChatClientAgent を包含して委譲 + 上 2 ツールセット。継承ではない。ChatClientAgent は sealed)
- **公開面**: public は契約 (`IAIChatAgent` / `AIChatAgentRequest` / `AIChatReply` / `AIChatReplyFormat` / `IAIChatProgress`)、窓口 (`AIChatJobStore` + Options)、標準 Agent (`RawDataAccessAgent` + `RawDataAccessOptions`。会話の設定 (プロンプト・履歴上限・逐次表示) も RawDataAccessOptions に、ILoggerFactory はコンストラクタ) だけ。`ChatClientAgent` / `ChatClientAgentOptions` / `IAIChatToolSet` / `AIChatToolContext` / `RawDataAccessToolSet` / `ChartToolSet` / `SvgChart` / `ChatReplyHtml` / `ConversationHistory` は internal (テストは IVT)。独自ツールの Agent はアプリが `IAIChatAgent` を直接実装する (ToolSet を公開しない = 実装の詳細)
- **設計と文書**: 業務の意味はデザインにある。`RawDataAccessAgent(clientFactory, dbAccessorFactory, Func<DesignData?>? design, Func<IReadOnlyList<AIChatDocument>>? documents, options)`。design があれば DesignKnowledgeToolSet が付き、モデルは list_modules/describe_module で業務語→表・列・候補値・リンク・スクリプトを確かめる (QueryField の SQL は .sql ファイル＝実行エンジン専用のバッファなので出さない)。文書 (`AIChatDocument`) の出所はホスト = デザインの `Resources/{DocumentFolder}/*.md` (フォルダは `AIChatFieldDesign.DocumentFolder` → `AIChatSendRequest.DocumentFolder` → `AIChatAgentRequest.DocumentFolder`。空 = 文書なし。読み出しは本体 1.3.32 の `DesignDataFileManager.GetResourceTexts(dir, folder, ".md", ".txt")` (App.zip の Resources/{folder} 直下) を Example / Starter の対応表が呼ぶ)。`IAIChatToolSet.GetInstructions(context)` (依頼ごとに文書が変わるためプロパティから引数付きに)。Example デザインに `Resources/AIChat/データの見方.md`。文書は合計 8000 字以下なら全文をプロンプトに入れる (実 AI 検証: 一覧だけだと read_document を呼ばず「売上」を全件合計した→全文注入で定義どおり完了分のみになった)、超えたら抜粋 200 字 + read_document
- **画面 URL**: `DesignDescriber.PageUrls` がページフレームのサイドバーリンク (Module 一致 → PageFrame/ModuleUrlSegment) から `/{frame}/{segment}` と `/{frame}/{segment}/{Id}` を組む (リンク無しはルートのフレーム、フレーム無しは省略)。list_modules と describe_module に出し、プロンプトで「行を挙げるときは詳細リンクを付ける」。`ChatReplyHtml.AddLinkTargets` は外部 (http(s)://、//) だけ target=_blank、アプリ内相対リンクは同じタブ
- **トークン節約 (2026-09-09)**: get_schema は 2 段 (引数なし = 表名+列数+モジュールの目次 / `tables` 指定 = その表だけ `table(col TYPE [説明], …)` の 1 行)。方言は `RawDataAccessToolSet.GetInstructions` に常時 (`Dialects()` = DataSource 種別のみ・接続なし)。プロンプトで順序を明示: 文書 → describe_module (表と列が出る) → 足りないときだけ get_schema。列情報は全データソース分を 10 分キャッシュ (`LoadSchemaAsync`)。ツール結果は履歴で直近 2 ターン後に落ちる
- **複数データソース**: `RawDataAccessOptions.DataSourceNames` (IList)。get_schema はデータソースごとに `## データソース X` + 方言、execute_sql は `dataSource` 引数 (1 つなら省略可)。設計の業務名は「モジュールの DataSourceName が一致するもの優先、無ければ表名で全モジュール」
- **方針 (ユーザー決定 2026-09-09)**: 権限管理はライブラリで持たず DB 側の設定 / 接続文字列 (AI 用読み取り専用ユーザー・列 GRANT・ビュー) で。基本的にデータは AI に渡してよい (Azure OpenAI は学習不使用・リージョン内)。気にする顧客 (個人情報・契約・業法) には列 GRANT / ビュー / 閉域・ローカルモデルの 3 手段。docs/AIChatField.md「AI に送られるデータ」に記載。除外列オプション等は要望が出てから
- **権限の考え方**: RawDataAccess は DB を生で読む。見える範囲 = `RawDataAccessOptions.DataSourceNames` の接続 (AI 用の読み取り専用 DB ユーザーを GRANT で絞る) で決める。ログインユーザーごとの行制限は効かないので、置くページの UserReadCondition で使える人を絞る。SELECT 判定は補助
- **IChatClient の作り方**: Azure OpenAI は Extras.Server `AI/AzureOpenAIClients.cs` (`ChatClientFactory(AISettings)`。埋め込みはここではなく IEmbeddingProvider へ (2026-09-24)。設定が欠けていれば null。AzureOpenAIClient は 1 つを使い回し・ApiKeyCredential) が提供 (2026-09-23。それまでは Example/Starter の AIChatAgentTable.CreateAzureOpenAI がホスト側で作っていた)。Agent / AITextAnalyzeService / SemanticSearchIndexer 自体は Microsoft.Extensions.AI の抽象しか受け取らないので、別プロバイダはホストが同じ型を作って渡す
- **結線**: 静的 `AIChatField.EndPoint` (テンプレートは "/api/ai_chat")。デスクトップは `AIChatField.SendCoreAsync` フック。Example の appsettings `AIChat:RawDataAccessDataSource` (既定 SampleSQLite、`SystemConfig.AIChat`)。**テスト環境**: gitignore 済 `appsettings.AIChatTest.json` (キー入り) + プロファイル「Extras.Server (AIChatTest)」https://localhost:7469
- **会話の保持 (0.11.0)**: 会話は `AIChatConversation` (発言・会話 ID・ポーリング) として `AIChatConversationStore` (DI スコープの `Services` に ConditionalWeakTable で紐づく = ホスト登録なしでスコープと同じ寿命。鍵 `{Module}:{Field}`。静的なのは弱参照の索引だけ。★これが正式な形。Extras クライアントはホストに DI 登録を求めない = 登録必須化は既存ホストを壊す破壊的変更なのでやらない) に置き、`AIChatField` は表示だけの薄い層 (Changed/ReplyReceived の委譲、コンポーネント Dispose で `DetachAsync`)。ポーリングは会話側で続くので離脱中に返事が届く。sessionStorage は使わない (リロードで消えるのは意図)。`KeepConversation=false` なら保管庫に置かず離脱で中断。送信時の写し (`AIChatSendRequest.Transcript`: テキストのみ・直近 6 往復・4000 字) はサーバー履歴が消えたときの文脈復元用で、`ChatClientAgent` は履歴が無いときだけ `ConversationHistory.Seed`
- **複数インスタンス**: ジョブと会話履歴はプロセス内 → セッション固定 (ARR アフィニティ) 前提。必要になれば DB テーブルの共有ストアに差し替える (本体の一時ファイル表方式)
- **ローカライズ**: 画面に出る文言 (進捗・未登録エラー・グラフの「データなし」) は Extras.Server の `Properties/Resources(.ja-JP).resx`。JobStore が Start 時のカルチャをバックグラウンドへ引き継ぐ。モデル向けのプロンプトとツール説明は日本語の定数 (ローカライズ対象外)
- **テスト**: `Test/AI/` (JobStore + 対応表の振り分け / ChatClientAgent は台本つき `FakeChatClient` / RawDataAccessToolSet は SQLite 実 DB / SvgChart)
- **インターフェース**: `IFillHeightFieldDesign` (Height=0 で親の高さに合わせる)
- **ファイル**: `Designs/AIChatFieldDesign.cs`, `Fields/AIChatField.cs`, `Components/AIChatFieldComponent.razor(.css)`, `wwwroot/aichat-interop.js`, `AIChat/*.cs`
- **意味検索 (0.14.0)**: コンストラクタ末尾の `Func<IEmbeddingProvider>? embeddingProvider` を渡し、design に SemanticSearchField (3 列あり・PostgreSQL / SQL Server) のモジュールがあれば `SemanticSearch/SemanticSearchToolSet` (internal) が付き `search_records(moduleName, query, top)` が使える (下の SemanticSearchField 参照)

#### SemanticSearchField - 意味検索 (AI / RAG)
- **状態**: 実装済み (docs/SemanticSearchField.md, Extras.Designer FieldDocs/SemanticSearchFieldDesign.md)。Example にはホスト側の結線だけ (`AI/SemanticSearchIndex.cs` = Indexer / Provider (IEmbeddingProvider) / Jobs の静的な持ち物 + `AI/EmbeddingProviderTable.cs` / `Controllers/SemanticSearchController.cs` = 再索引 API / CustomizedModuleDataIO / AIChatAgentTable)。Example のデータは SQLite なので意味検索できるモジュールは置いていない (`Inquiry` は通常モジュール)
- **形**: 書き込み専用列 2 本 (`DbColumnText` 文章 / `DbColumnVector` = `[0.1,-0.2,…]` JSON 配列テキスト) + DB のベクトル型列 `DbColumnVectorSearch` (PG は `search_vector::vector` の生成列、SQL Server 2025 は VECTOR 型列にテキストから暗黙変換で書けるので `DbColumnVector` と同名)。3 列すべて必須 (`HasColumns`)。UI なし (PasswordHashField と同型。`FieldDesignBase` 直下)。本体 (core) は触らない
- **★検索は DB 側だけ (2026-09-23 決定)**: サーバーでの全行メモリ比較 (コサイン類似度) は廃止。`SemanticSearchIndexReader.SearchAsync` (`BuildSearchSql`: PG `<=>`+limit / SQL Server `VECTOR_DISTANCE('cosine')`+top、`semantic_score` = 1-距離、論理削除は `cast(col as integer)=0`) のみ。`SupportsDbSearch(type)` = PostgreSQL / SQLServer。それ以外の DB のモジュールは `SemanticSearchToolSet.SearchableModules` (データソース定義の種別で判定・接続しない) から外れ、AI への説明にも出ない。DB 検索の失敗は AI にエラーとして返す (フォールバック無し)。`SemanticSearchVector` は Encode だけ (Decode / Cosine / 旧 base64 互換は削除)
- **execute_sql の `{embed:…}`**: `RawDataAccessToolSet.SqlPreprocessor` (Func<ds, sql, ct, Task<sql>>) を RawDataAccessAgent が `SemanticSearchToolSet.ExpandEmbeddingsAsync` に結線。SELECT 判定より前に置換 (数値リテラルだけになる)。非対応 DS は例外→AI にエラー。ログは展開前の SQL。AI への説明は GetInstructions が意味検索できるモジュールごとに表・ベクトル列・方言ヒント (`DialectHint`) を列挙
- **文章**: `SemanticSearch/SemanticSearchText.Build` (Extras、クライアント / サーバー共用) が「表示名: 値」を LF 区切りで 1 行ずつ。`SourceFields` 空 = `DefaultSourceFields` (DB 列ある入力フィールド全部。Id / 論理削除 / 楽観ロック / Password / 作成更新記録 / 自身は除く)。候補値は表示名、リンクは DisplayText、日付 ISO、RichText はタグ除去。`MaxTextLength` 既定 8000
- **クライアントが組み立てて送る**: `Fields/SemanticSearchField.GetSubmitData` が新規なら常に、更新は対象フィールドのどれかが `IsModified` のときだけ `SemanticSearchFieldData { Text }` を送る。理由: 更新の Submit は変更フィールドしか来ないのでサーバーでは全行文章を作れない
- **サーバー** (Extras.Server `AI/SemanticSearch/`): `SemanticSearchIndexer.ApplyAsync(designData, data, isNewData)` = CustomizedModuleDataIO の Add/Update から呼び埋め込みを付ける (新規で文章が無ければサーバーで組み立て = 一括取込。埋め込み失敗 / 未設定は Vector null + 警告ログ。索引付けは DB を選ばない。★ベクトル付きで送られてきたら埋め込みを呼ばない = 再索引がまとめて埋め込んだもの)。インスタンスの `ReindexAsync(moduleDataIO, db, design, moduleName, missingOnly, progress, pageSize, ct)` = GetListAsync でページ読み→ページ分の文章を **1 回の GenerateAsync でまとめて埋め込み**→Text+Vector 付きで通常 Submit (権限は通常どおり。論理削除行は出ない。埋め込み失敗は例外)。missingOnly は `SemanticSearchIndexReader.ReadIndexedIdsAsync` (ベクトル列 not null の Id を生 SELECT) で索引済みを飛ばす。`SemanticSearchIndexReader` = 書き込み専用列を IDbAccessor の生 SELECT で読む (LoginAccountStore と同じ割り切り)
- **★再索引はフィールドのスクリプト (2026-09-24 ユーザー決定「ボタンのモードよりフィールドのスクリプト。権限制御もできる」)**: `Fields/SemanticSearchField` に `Reindex()` / `ReindexMissing()` / `CancelReindex()` と `IsReindexing` / `ReindexProcessed` / `ReindexTotal` / `ReindexError`、デザインに `OnReindexCompleted` (ScriptEvent・引数なし・CheckFieldFunctionExistence)。クライアントは AIChatField と同じ形: 静的 `EndPoint` (テンプレ "/api/semantic_search/reindex") + ホストフック `ReindexCoreAsync`、POST→requestId→GET ポーリング (1 秒→2.5 秒・最大 2 時間)→`Module.ExecuteScriptAsync(OnReindexCompleted)`。DTO は `SemanticSearch/SemanticSearchReindex{Request,Response,StatusResponse}` (Status は AIChatJobStatus の値を共用)。サーバーは `SemanticSearchReindexJobStore` (AIChatJobStore と同形・`StartAsync(owner, request, authorizeWith, openScope)` = FieldApiAuthorization で入口検査→Task.Run。同じモジュールの走行中ジョブには合流 (Watchers)。`SemanticSearchReindexScope(ModuleDataIO, IDbAccessor, owner)` = バックグラウンド用に host が開く実行ユーザーの ModuleDataIO+DB。Example は `DataService(string userId)` で HttpContext 無しでもユーザー固定)。ホスト結線 = Example `Controllers/SemanticSearchController.cs` (POST/GET/DELETE) + `SemanticSearchIndex.Jobs` + ServiceInitializer の EndPoint。テスト = `Test/AI/SemanticSearchReindexJobStoreTest` (SQLite・まとめ埋め込みの回数 / missingOnly / 進捗 / 合流と中断 / 入口検査)
- **★埋め込みは IEmbeddingProvider (2026-09-24 ユーザー決定「メールみたいに切り替えたい・固定ライブラリに引っ張られるな」)**: Extras.Server `AI/Embedding/` に `IEmbeddingProvider { ModelId; Dimensions; EmbedAsync(texts) }` と、プロバイダ別の設定+実装 `AzureOpenAIEmbeddingSettings/Provider` (Azure SDK) / `EmbeddingGeneratorProvider` (M.E.AI IEmbeddingGenerator のアダプタ)。★OpenAI / Ollama の実装は書いたが実 API 未検証のためユーザー指示で削除 (2026-09-24「一旦 AzureOpenAI だけでいい」)。必要になれば OpenAIEmbeddingCalls を共有して OpenAI、HttpClient で /api/embed を叩いて Ollama を足す。OpenAI 系は `OpenAIEmbeddingCalls` で 2048 件ずつ・Index で対応づけ・Dimensions>0 なら縮小指定。Indexer は `Func<IEmbeddingProvider?>` を取り、Dimensions 申告と返ったベクトル長の不一致は例外 (`CheckDimensions`)。ホストは対応表 `EmbeddingProviderTable` (メールの MailSenderTable と同形) を appsettings の `SemanticSearch.EmbeddingProvider` の呼び名で引く (Example: SystemConfig.SemanticSearch / AzureOpenAIEmbedding セクション。AISettings と同じく appsettings.Development.json 側)。`AISettings.EmbeddingModel` と `AzureOpenAIClients.EmbeddingGeneratorFactory` は削除。テスト = `Test/AI/EmbeddingProviderTest` (設定必須・アダプタ) + `AzureOpenAIEmbeddingProviderRealTest` ([Explicit]・次元縮小 256 も確認)。FakeEmbeddingGenerator → `FakeEmbeddingProvider`
- **IsModified**: 対象フィールドのどれかが IsModified なら true (自前状態なし。GetSubmitData は `IsNewData || IsModified` で送る)
- **対象フィールドの読み込み**: 詳細の SELECT は「レイアウト + DataOnlyFields (+ IDataDependentField の依存先)」だけ (本体 SelectSqlCreator.GetSelectFields)。デザインは `IDataDependentField.GetDependencyFields() = SourceFields` を実装 (ProgressField / QrCode / MarkerList と同じ)。SourceFields 空は列挙できず何も足さない
- **デザインチェック**: `SemanticSearchFieldDesign:1` = 3 列必須。`:2` = 対象の一部だけ読み込む詳細レイアウト (LayoutDesignCheckInfo・DataOnlyFields を指す。対象を 1 つも読み込まないレイアウトは対象外)。列存在 / SourceFields 存在もチェック。DB 種別 (対応 DB か) は DesignCheckContext に無いのでチェックしない (実行時に対象外になるだけ)
- **テスト**: `Test/AI/` (FakeEmbeddingProvider = 2-gram ハッシュ 128 次元 / SemanticSearchTextTest / SemanticSearchIndexerDbTest (SQLite・索引付けとデザインチェック) / SemanticSearchToolSetTest (接続せず判定と説明) / SemanticSearchDbSearchTest (SQL 文字列) / SemanticSearchVectorDbTest は [Explicit]・実 DB (FakeEmbedding で 保存→再索引→search_records→{embed:} SQL 実行。環境変数 SEMANTIC_SEARCH_PG_CONNECTION = pgvector 済み PG / SEMANTIC_SEARCH_MSSQL_CONNECTION = SQL Server 2025。2026-09-23 に Docker の pgvector/pgvector:pg16 と mcr.microsoft.com/mssql/server:2025-latest で両方緑 = 生成列 ::vector / VECTOR_DISTANCE / cast(is_deleted as integer) の構文確認済) / SemanticSearchRealAITest は [Explicit]・実 Azure OpenAI + pgvector PG (環境変数 AZURE_OPENAI_* と SEMANTIC_SEARCH_PG_CONNECTION))
- **ファイル**: `Designs/SemanticSearchFieldDesign.cs`, `Data/SemanticSearchFieldData.cs`, `Fields/SemanticSearchField.cs`, `Components/SemanticSearchFieldComponent.razor` (空), `SemanticSearch/SemanticSearchText.cs`

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
