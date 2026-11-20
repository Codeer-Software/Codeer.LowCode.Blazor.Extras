# AITextAnalyzer 精度改善 — レビュー結果 (元の作業をしている方へ)

2026-11-20。`AITextAnalyzer精度改善_変更意図.md` の変更へのレビュー結果です。

## 結論

Extras に取り込みます。帳票によらず効く改善なので、プロジェクト独自のエンジンには分けません。
Extras の作業ツリーにある変更は、この結果を反映した状態になっています (未コミット・版は 0.34.1 のまま)。これ以上 Extras 側は直さないでください。

## 変えたところ

| 項目 | 変更案 | 取り込んだ形 |
|---|---|---|
| JSON スキーマ | `AISettings.TextAnalyzeJsonSchema`・既定 true | **常に有効 (設定なし)** |
| 画像も渡す | `AISettings.TextAnalyzeSendImage`・既定 true | **AITextAnalyzerField のプロパティ `SendImage`・既定 false** |
| strict の指定 | `AdditionalProperties["strictJsonSchema"] = true` | **`["strict"] = true` に修正** |
| 推論の強さ (`TextAnalyzeReasoningEffort`) | 設定を追加 | **削除** |

### JSON スキーマは常に有効にしました

キーをフィールド名にするのは元からの期待値で、スキーマはそれを API に守らせるだけなので、設定にはせず常に使います。
受け付けないモデル / プロバイダでは 1 回失敗してから今までの形 (JSON モード) でやり直します。

### 画像はフィールドのプロパティにして、既定は無効にしました

appsettings (`AISettings`) の設定はやめ、デザイナで AITextAnalyzerField ごとに有効にします (帳票ごとに効果を確かめて決められる)。
既定を無効にしたのは、パッケージを上げただけで費用と結果が変わらないようにするためです。効果を測ったのはミルシートだけで、他の帳票では
画像の読み違いが文字認識の正しい値を上書きする悪化があり得ます。画像の分だけトークン (費用・応答時間) も増えます。

### strict のキーが違っていました

Microsoft.Extensions.AI.OpenAI 10.7 が見るキーは `"strict"` です (逆コンパイルで確認: `OpenAIClientExtensions.StrictKey = "strict"`)。
`"strictJsonSchema"` は無視されるので、変更案では strict が OpenAI に届いていませんでした。
送信される JSON に `response_format.json_schema.strict: true` が載ることをテスト (`スキーマはstrictでOpenAIに送られる`) で押さえています。

- 検証ツールも同じキーを使っていたなら、実測の「スキーマ＋画像」は strict なしの数字です。キーを直して測り直すと結果が変わる可能性があります (良くなる見込みですが未確認)
- 「スキーマ外のキーが出なくなった」のは strict が効いたからではなく、スキーマを渡したことによる効果だったと思われます

### 推論の強さは外しました

実測で効果が安定しなかった (CH2F2 で 65 の回と 203 の回) ので、ライブラリの設定にはしません。
そのプロジェクトで使いたい場合は、`AITextAnalyzeService` をアプリにコピーし (MIT)、`ChatOptions.Reasoning` を足してください (Temperature は送らない)。
appsettings に `TextAnalyzeReasoningEffort` を書いていたら、消しておいてください (無視されるだけで害はありません)。

## そちらのプロジェクトでやること

- 新しい Extras.Server が公開されたら上げてください。デザイナも新しい版にして、ミルシートの AITextAnalyzerField の `SendImage` を true にしてください (既定は false で、そのままだと画像は渡しません)。JSON スキーマは上げるだけで効きます。appsettings に `TextAnalyzeJsonSchema` / `TextAnalyzeSendImage` を書いていたら消してください (無視されるだけで害はありません)
- ミルシートが PDF のままなら画像は付きません (画像を渡すのは jpg / png / gif / webp だけ)。効果を出すには、ページごとの画像にしてから取り込んでください
- Remarks の書き方が原因の誤り (原因 3: 規格の記号を結果の列に写す) はライブラリでは直しません。Remarks の側で詰めてください

## 確かめてほしいこと (こちらでは未確認)

- 新しい Extras.Server を載せたホストで、画面からの取り込みが今までどおり動くこと
- 誤りのセル数を strict が効いた状態で測り直した結果 (今の表は strict なしの数字のはずです)
- 画像を付けたときに増えるトークン数と応答時間 (目安があれば教えてください)
