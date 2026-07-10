# AITextAnalyzerField - AI 帳票解析

帳票ファイル (PDF・画像など) や自由テキストを AI で解析し、モジュールのフィールドへ値を自動入力する入力補助フィールドです。
ファイルは Azure Document Intelligence でテキストと表を抽出し、Azure OpenAI がモジュール定義 (フィールド名・表示名・型) に合わせて値を割り当てます。

画面にはファイルアップロードボタンと「Text」ボタン (自由テキストを貼り付けるモーダル) が表示されます。

## 動作の流れ

1. ユーザーがファイルを選択、またはテキストを入力する
2. `FileField` を指定している場合、選択したファイルをその FileField に格納する (解析元ファイルがレコードに添付される)
3. サーバーで AI 解析し、モジュールの各フィールドに対応する値を抽出する
4. 抽出結果が画面のフィールドに反映される (**未保存の状態**。ユーザーが内容を確認して保存する)
5. `DataImportCompleted` に設定したスクリプトが実行される

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| Remarks | string (複数行) | - | AI への補足指示。表の読み方・値の解釈ルール・単位の扱いなどを日本語で書く。抽出精度のチューニングポイント |
| DataImportCompleted | string (スクリプトイベント) | - | 取込完了後に実行するスクリプトメソッド名 |
| FileField | string (フィールド参照) | - | 解析したファイルを格納する同一モジュール内の `FileField` 名 |

## 値が入る対象フィールド型

モジュール内の次の型のフィールドに値が入ります。値が見つからなかった項目はスキップされます (既存値は変更されません)。

- Text / Number / Boolean / Date / DateTime / Time
- Id (`IsManualInput: true` のときのみ)
- Select / Link — 候補リストやマスタ参照の候補と AI がマッチングし、値を解決します
- List — 子モジュールの行としてネスト抽出します (明細表の取込)

フィールドの表示名 (DisplayName) は抽出時の項目名として使われるため、帳票上の見出しに近い日本語を設定すると精度が上がります。

## サーバー側設定

このフィールドはサーバーの AI 解析エンドポイント (`/api/ai_text_analyze`、アプリテンプレートに設定済み) を呼び出します。
動作にはサーバーの `appsettings.json` に `AISettings` セクションが必要です。

```json
"AISettings": {
  "OpenAIEndPoint": "https://xxx.openai.azure.com/",
  "OpenAIKey": "...",
  "ChatModel": "gpt-4o",
  "DocumentAnalysisEndPoint": "https://xxx.cognitiveservices.azure.com/",
  "DocumentAnalysisKey": "..."
}
```

- `OpenAIEndPoint` / `OpenAIKey` / `ChatModel` — Azure OpenAI (項目抽出・候補マッチング)
- `DocumentAnalysisEndPoint` / `DocumentAnalysisKey` — Azure Document Intelligence (ファイルのテキスト・表の抽出。テキスト入力のみ使う場合は不要)
- `FileField` を併用する場合は FileField のサーバー要件 (一時ファイルテーブル・ファイルストレージ設定) も必要です

サーバー側の解析ロジックは `Codeer.LowCode.Blazor.Extras.Server` パッケージの `AITextAnalyzeService` が提供します。
プロンプトや解析処理を変えたい場合は、ソース (MIT) をコピーして改変してください。

## スクリプト

取込完了は `DataImportCompleted` プロパティに設定したメソッドで受け取ります。

```csharp
// DataImportCompleted に "Analyzer_DataImportCompleted" を設定した場合
void Analyzer_DataImportCompleted()
{
    // 取込直後の補正・既定値設定・ユーザーへの案内
    if (Status.Value == null)
    {
        Status.Value = "下書き";
    }
    Toaster.Info("AI取込が完了しました。内容を確認して保存してください。");
}
```

## 備考

- このフィールド自体は DB に値を保存しません (取り込んだ値は各フィールドが保持します)
- 解析は失敗することがあります (リトライで成功することがあります)。取込結果はユーザーが確認してから保存する前提の機能です
