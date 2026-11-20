# AITextAnalyzer 抽出精度の改善 — 変更の意図 (レビュー用)

2026-11-20 作成。対象は `Codeer.LowCode.Blazor.Extras.Server` の `AITextAnalyzeService`。
変更は main の作業ツリーにあり、未コミット・バージョン未更新です。

## 何を直したいのか

ガスメーカーの分析証明書 (ミルシート) 11 形式・56 ページを AITextAnalyzerField で取り込み、紙面と照合しました。
約 11,500 セル中 420〜500 セルが誤りで、同じ入力でも実行ごとに 1 割以上揺れます。原因は次の 3 つでした。

| # | 原因 | 例 | どこで起きるか |
|---|---|---|---|
| 1 | 文字認識 (Document Intelligence) が記号や似た字を取り違える | `<` → `V`、`≥` → `>`、`6` → `8`、`0` → `D` | OCR。モデルは OCR の結果しか見ていないので直せない |
| 2 | モデルが JSON のキーにフィールド名ではなく表示名を使う | 子モジュールの `規格記号` と `結果記号` の表示名がどちらも「記号」。モデルが `"記号"` をキーにして返し、`CreateModule` でフィールドが見つからず値が捨てられる | モデルの出力。OCR には正しい値が入っているのに 1 ページ分の記号が全部消える |
| 3 | モデルが補足指示 (Remarks) の決まりを守り切れない | 規格の記号を結果の記号の列に写す | モデル。Remarks の書き方の問題で、今回のコード変更の対象外 |

1 と 2 はライブラリ側でしか直せないので、この変更で対応します。

## 変更内容

### 1. 抽出結果を JSON スキーマで受け取る (原因 2)

- `ChatResponseFormat.Json` の代わりに `ChatResponseFormat.ForJsonSchema(...)` を使い、キーをフィールド名に固定します
- スキーマは `CreateJsonSchema` が作ります。対象のフィールドは既存の `CreateJsonExplanation` と同じ (`IsSupportedType`)
  - キー = フィールド名、表示名は `description` (フィールド名と同じなら付けない)
  - 値はすべて null 可。Number は `number` / `string` の両方 (Remarks で「帳票の表記のまま文字列で」と指示する使い方があるため)
  - List は子モジュールのスキーマを `items` に持つ `array`
  - `additionalProperties: false`、全キー `required`
- `AdditionalProperties["strictJsonSchema"] = true` で OpenAI 系アダプタの strict を有効にします
- 既存の 2 通目のメッセージ (`CreateJsonExplanation` の項目指示) はそのまま残しています

### 2. 画像ファイルは画像そのものもモデルに渡す (原因 1)

- `AnalyzeFileAsync` で、ファイルが jpeg / png / gif / webp なら (`GetImageMediaType`。先頭バイトで判定)、OCR 結果のテキストと同じメッセージに `DataContent` で画像を付けます
- システムプロンプトに「JSON は文字認識の結果なので記号・小数点・似た字を画像で確かめ、食い違うときは画像を正とする。表の行と列の対応は JSON を手がかりにする」を足します (`CreateImageSourcePrompt`)
- PDF・Office 文書は対象外で、従来どおり OCR 結果だけを渡します。20MB を超える画像も付けません
- OCR は今までどおり実行します。表の行・列の構造は OCR のほうが安定しているためです

### 3. 推論の強さを設定できるようにする

- `TextAnalyzeReasoningEffort` (`low` / `medium` / `high`) を指定すると `ChatOptions.Reasoning` に渡し、`Temperature` は送りません
- 既定は空 (指定しない = 従来どおり `Temperature = 0`)

### 設定 (`AISettings` に追加)

| プロパティ | 既定 | 意味 |
|---|---|---|
| `TextAnalyzeJsonSchema` | `true` | 1 を使う |
| `TextAnalyzeSendImage` | `true` | 2 を使う |
| `TextAnalyzeReasoningEffort` | `""` | 3。空なら指定しない |

### 失敗時のやり直し

スキーマ・画像・推論のいずれかを付けた呼び出しが例外になるか、返った文字列が JSON オブジェクトでなければ、従来の形 (OCR 結果だけ・JSON モード・Temperature 0) で 1 回やり直します。
スキーマや画像を受け付けないモデル / プロバイダ (`Func<IChatClient>` でホストが渡したもの) でも動かすためです。

## 変更したファイル

| ファイル | 内容 |
|---|---|
| `Source/Codeer.LowCode.Blazor.Extras.Server/AI/AISettings.cs` | 設定 3 つを追加 |
| `Source/Codeer.LowCode.Blazor.Extras.Server/AI/AITextAnalyzeService.cs` | `DocumentAnalysisByText` を「拡張した呼び出し → 失敗なら従来の呼び出し」に分割 (`GetExtractionAsync`)。`CreateJsonSchema` / `CreateImageContent` / `GetImageMediaType` / `CreateImageSourcePrompt` を追加。`AnalyzeCoreAsync` に画像を受けるオーバーロードを追加 (既存のシグネチャは残してある) |
| `Source/Codeer.LowCode.Blazor.Extras.Test/AI/AITextAnalyzeExtractionTest.cs` | 新規。スキーマの形 (表示名が同じフィールドが別のキーになること) と画像の判定 |
| `docs/AITextAnalyzerField.md` | 設定 3 つとやり直しの説明を追記 |

クライアント側 (`Codeer.LowCode.Blazor.Extras`)・デザイナ・フィールドのデザイン (`AITextAnalyzerFieldDesign`) は変えていません。変更は Extras.Server だけです。

## 効果 (実測)

`AITextAnalyzeService` と同じプロンプト・同じ OCR 処理を再現した検証ツールで、56 ページを流して正解データと自動採点した誤りセル数です。
モデルは `gpt-5.4-mini`。現行は 2 回、ほかは 1 回ずつの実行です。

| | 現行 | スキーマ＋画像 |
|---|---|---|
| 全 11 形式の合計 (約 11,480 セル) | 423 / 498 | 256 |
| うち記号の列 (約 2,350 セル) | 244 / 306 | 115 |
| DCS高純度シリコン (原因 2 が出ていた形式) | 27 / 48 | 1 |
| CH2F2リンガス (原因 1 が多い形式) | 378 / 391 | 203 |
| AsH3マチソン | 52 / 56 | 30 |

- スキーマだけ (画像なし) では合計はほぼ変わりません (514)。原因 2 が出る形式だけに効きます
- 推論 `medium` は効果が安定しませんでした (CH2F2 で 65 の回と 203 の回)。そのため既定は指定なしにしています

## レビューで見てほしい点

1. **既定値を `true` にしてよいか** (`TextAnalyzeJsonSchema` / `TextAnalyzeSendImage`)。パッケージを上げただけで挙動が変わります
   - 画像を渡すとトークン (費用) と応答時間が増えます。増え方は計測していません
   - 失敗時は従来の形にやり直すので動かなくなることはない想定ですが、失敗した分は呼び出しが 2 回になります
2. **やり直しの `catch` が広い**こと。レート制限や通信断のような一時的な失敗でも従来の形で 1 回やり直します。原因の例外は握りつぶしていて、ログには出していません
3. **`strictJsonSchema` のキー名**。Microsoft.Extensions.AI.OpenAI 10.7 での動作は検証ツールで確認しましたが (スキーマ外のキーは出なくなった)、strict が実際に効いているかまでは確かめていません。ほかのプロバイダのアダプタでは無視される可能性があります
4. **設定の置き場所**。`AISettings` に `TextAnalyze` 接頭辞で足しました。AITextAnalyzer 専用の設定クラスに分ける、フィールドのデザインのプロパティにする (フィールドごとに画像を渡すか決める) という案もあります。後者はクライアント・デザイナの変更が要ります
5. **スキーマの再帰**。List が自分自身や循環するモジュールを指すと無限再帰になります。既存の `CreateJsonExplanation` も同じ作りなので、今回は合わせています

## 確認済み / 未確認

確認済み:
- Extras.Server のビルド (エラー 0)
- `dotnet test --filter FullyQualifiedName~AITextAnalyze` で 10 件成功 (新規 3 件を含む)
- 検証ツールでの効果 (上の表)。検証ツールは同じプロンプト・スキーマ・画像の渡し方を再現したもので、Extras のコードそのものではありません

未確認:
- **変更後の Extras.Server をホストに載せて、画面から取り込む確認はしていません** (検証したホストは Extras.Server 0.30.0)
- やり直しの経路 (スキーマや画像を受け付けないモデルでの動作) は実機で通していません
- テスト全体 (`AITextAnalyze` 以外) は流していません
- PDF をそのまま解析する使い方 (画像が付かない経路) は、コード上は従来と同じ呼び出しにスキーマが付くだけですが、実測はしていません

## パッケージ化のときに

- バージョンは上げていません (`Codeer.LowCode.Blazor.Extras.Server` は 0.34.1 のまま)
- リポジトリの `CLAUDE.md` の AITextAnalyzer の記述には追記していません
- Starter テンプレートの appsettings に新しい設定を並べるかは未対応です (既定値で動くので必須ではありません)
