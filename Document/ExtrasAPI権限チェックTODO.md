# Extras の API 経由処理の権限チェック — 現状と方針 (TODO)

2026-09-10 の棚卸し。Extras.Server が提供する API のうち、本体の ModuleDataIO を通らない入口は
「ログイン済み ([Authorize])」以上のチェックが無い。方針は **「ModuleName + FieldName を渡し、そのフィールドが
デザインに存在し、そのモジュールを読める人にだけ提供する」に全部揃える** (承認・AITextAnalyze は既にこの形)。実装はまだ。

## 現状

| 入口 (テンプレの Controller) | ログイン以外のチェック | 評価 |
|---|---|---|
| メール単発 `POST /api/mail` (`MailDispatcher.SendAsync(MailSendRequest)`) | なし。To/Cc/Bcc/件名/本文/添付/MailInfraName は全部クライアント任せ。From だけシステム送信者に固定。SourceModule/SourceId は履歴用で未検証 | **穴**: ログインできる人なら誰宛にでも何でも送れる |
| メール一斉 `bulk_search` / `bulk_preview` (`MailBulkSearch`) | 宛先は `ModuleDataIO.GetListAsync` → アプリアクセス・UserRead・DataRead が効く。本文は自由 | 概ね妥当 |
| 承認 `POST /api/approval` (`ApprovalEngine`) | `ResolveContextAsync` で `CheckAppAuthorization` + 現在ユーザー。申請は親を正規経路 (権限込み) で保存。承認 = `FindWaitingApprover(自分)`、再申請/取り下げ = 申請者本人。フロー/メンバー行の読みも GetListAsync | 妥当 (却下/差し戻し/確認は Approve と同型のはず。**未読**・実装時に確認) |
| AIChat `POST /api/ai_chat` (`AIChatJobStore` / `RawDataAccessAgent`) | ジョブ所有者チェックのみ。`CheckAppAuthorization` 無し。RawDataAccess は AI 用 DB ユーザーで直接読む = UserRead/DataRead/フィールド読取権限を素通り (設計上 DB 側権限)。DesignKnowledge (list_modules/describe_module) は全モジュールの定義・スクリプト・画面 URL をモジュール権限に関係なく返す。`DocumentFolder` はクライアント送信値 (Resources の任意フォルダを読める。zip 内に限られるので外には出ない) | **要対応**。`RawDataAccessDataSources: []` (空 = 全データソース) はアプリ本体の接続で全表が読める |
| AITextAnalyze `POST /api/ai_text_analyze/*` | moduleName + fieldName でデザインを引く (`GetRemarks`)。候補解決は GetListAsync (権限効く)。`CheckAppAuthorization` 無し。結果は保存しない | コスト面だけ。モジュール権限チェックを足せば方針どおり |
| Excel→PDF `POST /api/excel/pdf` | なし (データも触らない) | コスト面だけ |
| ModuleData 拡張 `list_file` / `submit_by_file` / `bulk_submit` (`BulkFileTransfer`) | ModuleDataIO 経由で権限が効く。`parse_file` / `list_file_by_data` は DB を触らない | 妥当 |
| TOTP `api/account/totp/status|reset` | 本人のみ | 妥当 |
| `login_options` (匿名) | プロバイダ名を返すだけ | 妥当 |

共通の抜け: **AppAccessConditions (IsActive 等) を見るのはデザイン取得と ModuleDataIO だけ**。停止したユーザーでも Cookie が
生きていればメール単発・AIChat・AITextAnalyze・Excel は使える。

## 方針: ModuleName + FieldName を渡す

サーバー側の共通手順 (Extras.Server に 1 つのヘルパーとして置き、各入口の先頭で呼ぶ):

1. `ModuleDataIO.CheckAppAuthorization()` (停止ユーザーの穴が閉じる)
2. モジュールが存在し、フィールドが存在し、期待した型 (`AIChatFieldDesign` / `MailFieldDesign` / `BulkMailFieldDesign` / `AITextAnalyzerFieldDesign` …) であること
3. そのモジュールの `UserReadCondition` を評価 (本体 `AuthorizationChecker.CheckUserCondition` と同じロジック。公開 API が無ければ「Id 指定なしの GetListAsync を LimitCount 0 で呼ぶ」等で代用せず、本体に薄い公開経路を足すか検討 → [[feedback_fix_at_right_layer_no_internal_exposure]] に注意)
4. 行に紐づく操作 (メールの SourceId) は Id で `GetListAsync` して DataReadCondition まで通す

副次効果: **クライアントが送っている設定値をデザインから引き直せる**。AIChat の `DocumentFolder` と `Agent`、メールの `MailInfraName`・
宛先契約はフィールドのデザインが持つので、リクエストは ModuleName/FieldName (+ 入力値) だけにして改ざんの余地を消す。

AIChat の補足: この方式で決まるのは「誰がチャットできるか」。「AI が何を読めるか」は引き続き DB ユーザー側。
AIChatField を置いたモジュールに UserReadCondition (管理者のみ等) を書けば役割で絞れる。

## 決めること

- **スクリプトからの送信** (Mail スクリプトオブジェクト、BulkFileTransferService) はフィールドが無い。
  - 案 A: 呼び出し元モジュール名をスクリプト実行文脈から取って ModuleName にする (既存デザインが動き続ける)
  - 案 B: 「送るならそのモジュールに MailField を置く」を要件にする (デザインに能力を宣言させる。筋は良いが既存デザインが止まる)
  - 穏当なのは A で始めて B を designcheck の警告にする
- チェックする条件は UserRead で足りるか (MailField は「操作」なので UserWrite にすべき場面があるか)。まずは Read で統一し、必要になったら MailFieldDesign にプロパティを足す
- DesignKnowledge の list_modules/describe_module を利用者の UserRead で絞る (別項目。RawDataAccess の DB 側権限とは独立)
- `RawDataAccessDataSources` 空 = 全部、をやめて「読み取り専用 DB ユーザーのデータソースを明示しないと動かない」に倒すか (docs に太字で前提を書く)

## 実装順 (効果順)

1. AIChat: リクエストに ModuleName/FieldName → 共通チェック → DocumentFolder/Agent をデザインから
2. メール単発: SourceModule/SourceId を必須化 + 共通チェック + 行の DataRead + MailInfraName をデザインから
3. AITextAnalyze: 既に名前は来ているので共通チェックを足すだけ
4. Excel→PDF・その他: `CheckAppAuthorization` だけ
5. 承認の却下/差し戻し/確認の本人チェックを読んで確認 (足りなければ Approve と同型に)

## 互換性

- リクエスト DTO に ModuleName/FieldName が増えるので Extras と Extras.Server は同時に版を上げる (今と同じ運用)
- テンプレの Controller は 1 行の差し替えで済むよう、チェックは Extras.Server 側 (MailDispatcher / AIChatJobStore の入口) に置く。旧シグネチャは 1 版だけ `[Obsolete]` で残す ([[feedback_public_api_removal_compat_check]])
- 既存デザイン: フィールド経由の操作は変更なし。スクリプト送信は上の「決めること」次第

## 検証

- Extras.Test: 各入口で「停止ユーザー」「UserReadCondition 不成立」「存在しないフィールド」「型違いのフィールド」が拒否されること、正規ユーザーは通ること
- Selenium (Extras SeleniumDrivers) の既存 e2e (メール/承認/AIChat) が緑のまま
