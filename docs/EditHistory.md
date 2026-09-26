# 編集履歴

モジュールに **EditHistoryField** を 1 つ置くと、そのモジュールのレコードの保存 (作成・更新・削除) ごとに
レコード全体 (明細を含む) のスナップショットが履歴モジュールへ記録されます。
詳細画面には版の一覧が出て、変更されたフィールドの「旧 → 新」、その版のレコード全体の表示、
その版の内容へのフォームの巻き戻しができます。履歴は通常のモジュールに保存されるため、
一覧・検索・権限はいつものローコードのやり方でできます。

- [概略](#概略) — 何ができるか、導入の流れ
- [詳細](#詳細) — 記録、表示、復元、権限、契約、サーバーの結線

---

## 概略

### できること

- **保存ごとにレコード全体を記録**。従属レコード (明細やガントチャートのタスクなど) も一緒に 1 版になる (明細だけの変更も親の 1 版)
- **削除も記録** (削除前の内容)。履歴モジュールの一覧を ChangeType = Delete で絞れば「削除済みレコード一覧」
- **削除したレコードの復活** — 履歴モジュールの詳細に EditHistoryRestoreButtonField を置く。論理削除のモジュールなら Id を保ったまま明細ごと戻る (リンクは切れない)。物理削除なら新しいレコードとして作り直す
- **版の一覧** — 版番号・変更種別・変更者・日時と、変更されたフィールドだけの「旧 → 新」。明細は行の追加 / 削除 / 変更
- **この版を表示** — その版のレコード全体を表示専用のダイアログで表示。変更されたフィールドを強調
- **この版に戻す** — その版の内容を編集中のフォームへ反映。**保存はユーザーが行う** (保存前に確認・一部だけ直す、ができる)。保存すると新しい版として記録される (履歴は巻き戻らない)
- CSV / Excel の一括取込や API からの保存も記録される (保存経路が同じ)

### 導入の流れ

1. **履歴モジュール**を作る (全モジュールで 1 つ共有してよい)。フィールドは ModuleName / DataId / ChangeType / Snapshot (必須) と UserId / DateTime (任意)。
   **EditHistoryContractField** を 1 つ置く (既定名でフィールドを作れば設定不要)。テーブル定義例:

   ```sql
   CREATE TABLE edit_histories (
     id          BIGSERIAL PRIMARY KEY,
     module_name TEXT NOT NULL,
     data_id     TEXT NOT NULL,
     change_type TEXT NOT NULL,
     snapshot    TEXT NOT NULL,
     user_id     TEXT,
     date_time   TIMESTAMP
   );
   CREATE INDEX ix_edit_histories_target ON edit_histories (module_name, data_id);
   ```

2. 履歴を取りたいモジュールに **EditHistoryField** を置き、`HistoryModuleName` に履歴モジュールを設定。詳細レイアウトの右カラムかタブに配置
3. サーバー側は `Codeer.LowCode.Blazor.Extras.Server` の **EditHistoryRecorder** を `CustomizedModuleDataIO.SubmitAsync` に結線 (アプリテンプレートは結線済み。[サーバーの結線](#サーバーの結線))

---

## 詳細

### 記録

- サーバーの `EditHistoryRecorder` が `ModuleDataIO.SubmitAsync` を包む。トランザクションのルートレコード 1 件につき履歴 1 行
- 作成・更新: base の保存が成功した後、保存後のレコードを本体の `ModuleDataIO.GetWithOwnedRecordsAsync` で読み直して記録。読む範囲は「レコード本体 ＋ 従属レコードの宣言 (`IOwnedRecordsFieldDesign`) の先」。何が従属かは各フィールドが決める (一覧は「親と一緒に消える一覧」、Gantt / Calendar / TaskBoard は自分の子レコード)。従属でない一覧は含めない。子・孫も再帰
- 削除: base の前に削除前のレコードを読んでおき、成功後に記録
- 読み直しは操作ユーザーの権限で行う。そのユーザーに読めない列はスナップショットに入らない (復元でもその列は変わらない)
- 変更種別は `EditHistoryChangeType` (Add / Update / Delete) のメンバー名。デザイン enum として公開されるので、履歴モジュールの ChangeType を SelectField (EnumName = `EditHistoryChangeType`) にすれば表示名付きで一覧・検索できる
- 履歴の記録に失敗すると保存も失敗 (ロールバック) になる。EditHistoryField があるのに履歴モジュール・契約が無い設計 (デザインチェックが指摘する不備) も同様
- 一括取込の一括 INSERT 経路 (`BulkAddThreshold` 以上の純追加) は採番された Id が返らないため記録されない (ログに出る)。1 行ずつの経路 (既定) は記録される
- 添付ファイルはファイル名とキーだけ記録し、実体は履歴に残さない
- **Gantt / Calendar / TaskBoard** のように別モジュールのレコードを自分で読み書きする拡張フィールドは、`IOwnedRecordsFieldDesign` で子レコードを宣言しているので、そのまま親の版に入る (一覧フィールドを別途置く必要はない)。独自の拡張フィールドで子レコードを持つものは同じインターフェースを実装する。復元 (この版に戻す) は、ランタイム側が `IOwnedRecordsField` を実装しているフィールドが差し替えられる (Gantt / Calendar / TaskBoard / MarkerList は実装済み)
- **ExecuteSqlField** も記録される (SQL は同じ SubmitAsync の中で走る)。Update / Delete タイミングは通常どおり保存後・削除前の内容。Create タイミングは `NewId` で採番 Id が返る設定のときだけ記録される (返らないと読み直せない = ログに出る)。Standalone (Add / Update / Delete の無い送信で SQL だけ実行) は送信前後のレコードを比べ、レコード自体が変わったときだけ 1 版にする (他のテーブルだけを変える SQL は履歴にならない)
- 「変更なしで保存」は Submit 自体が起きないので版は増えない

### 表示

- 版番号は保存せず、閲覧時に件数から採番する (古い方から 1, 2, ...)。並びは DateTime 役割があれば日時、無ければ Id の降順
- 各版には前の版との差分だけを出す。値フィールドは「表示名: 旧 → 新」(候補・リンクは表示名、日付・数値はフィールドの書式)。明細は行 Id で突き合わせて「追加 n 件 / 削除 n 件 / 変更 n 件」と行ごとの内訳。作成の版は値のある項目、削除の版は「レコードが削除されました」
- 作成の版は「レコードが作成されました」だけ (全項目を並べても読めない。内容は「この版を表示」)。明細は「追加 n 件 / 削除 n 件 / 変更 n 件」の要約だけを出し、行ごとの内訳はクリックで開く
- 「旧 → 新」の文字列を出すのは文字列・数値・真偽・日付時刻・候補・リンク・ファイル (名前) のフィールドだけ。それ以外の型 (独自のデータクラスを持つフィールド) は変わったかどうか (JSON 比較) だけを「変更あり」として名前で出し、内容は「この版を表示」(本物のコンポーネントで描く) で見る。一覧は要約、全体は版表示、の二層構造
- 「この版を表示」は自モジュールの詳細レイアウト (`LayoutName`、空なら既定) をそのまま使って表示専用のダイアログに出し、変更フィールドを緑の枠で強調する。従属レコードを宣言した拡張フィールド (Gantt / Calendar / TaskBoard / MarkerList) は `IOwnedRecordsField.ShowOwnedRecordsAsync` で版の行をそのまま表示する (DB は読まない・強調は無し)
- `PageSize` (既定 20) ずつ読み、「さらに表示」で次を読む
- 未保存のレコード・一覧の行では読まない

### 復元 (この版に戻す)

- 値フィールドは変更扱いで反映 (OnDataChanged スクリプトも動く)。ユーザーが保存して確定する = 権限・検証・楽観ロックは通常の保存と同じ
- 従属レコードは行 Id で突き合わせ、既存行は更新、余った行は削除。無い行は、明細モジュールが**論理削除**なら Id を保ったまま復活 (保存時に「削除の取り消し」が同梱され、同じトランザクションで戻る)、物理削除なら新しい行として追加 (Id は振り直し)。孫の明細も同様
- 対象外: システムフィールド (Id / 楽観ロック / 作成・更新・削除の記録 / 論理削除)、リンク越しの派生値、従属でない一覧、添付ファイル、書き込み権限のないフィールド
- 削除したレコードの復活は履歴モジュール側の EditHistoryRestoreButtonField で行う ([FieldDocs](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/EditHistoryRestoreButtonFieldDesign.md))。論理削除なら Id を保って明細ごと (ChangeType = Restore の版になる)、物理削除なら新しいレコードとして (作成の版になる)
- 論理削除の取り消しは本体の `ModuleDataIO.UndeleteAsync` (Codeer.LowCode.Blazor 1.3.37) が行い、EditHistoryRecorder が base の Submit の前に呼ぶ。権限は削除と同じ (CanDelete と UserWrite 条件)
- **退避 (削除テーブルへの移動) とは併用できない** (デザインチェック エラー)。履歴を入れるモジュールの削除は論理削除か物理削除にする。Id を保った復活が要るなら論理削除
- ExecuteSqlField を使うモジュールでは、戻るのはレコード (と明細) の内容だけ。SQL が他のテーブルや他のレコードに書いたものは履歴に無いので戻らない (履歴は「このレコードがどう変わったか」の記録として使う)

### 権限

- 履歴の読み取りは通常のモジュールデータ API = **履歴モジュールの UserRead / DataRead 条件がそのまま効く**。履歴を見せたくないユーザーには履歴モジュールを読めなくすればよい (EditHistoryField は「履歴はありません」になる)
- 対象モジュール側でフィールド単位の閲覧権限 (PermissionField) を使っている場合、差分表示はそのフィールドを出さない。ただし履歴モジュールを直接読めるユーザーには Snapshot (JSON) の生の値が見える。列を伏せたい相手には履歴モジュールを読ませないこと
- 履歴の書き込みは内部経路 (操作ユーザーの書き込み権限に依存しない)。履歴モジュールの UserWrite は「誰も書けない」でよい
- 復元は対象モジュールの編集権限 (表示専用なら「この版に戻す」は出ない)

### 契約フィールド (フィールド名の対応表)

履歴モジュールに `EditHistoryContractField` を置き、役割 → 自モジュールのフィールド名を宣言する。既定名で作れば設定不要。

| 役割 | 型 | 内容 | 必須 |
|---|---|---|---|
| ModuleName | Text / Select | 対象モジュール名。Select + enum (メンバー名 = モジュール名 / 表示 = 画面上の名前) で一覧の表示と検索を読み替えられる。enum は任意 (無い・空なら素のモジュール名)。enum にメンバーがあるのに記録元モジュールが無いとデザインチェックがエラー | ○ |
| DataId | Text | 対象レコードの Id | ○ |
| ChangeType | Text / Select | Add / Update / Delete | ○ |
| Snapshot | Text | レコード全体の JSON | ○ |
| UserId | Link (ユーザー) / Text | 保存したユーザー | - |
| DateTime | DateTime | 保存日時 | - |

型が合わない・フィールドが無い・必須役割が空はデザインチェックがエラーにする。監査用の項目 (IP アドレス等) を足したい場合は履歴モジュールに自由にフィールドを追加してよい (記録側は役割しか書かない)。

### サーバーの結線

アプリテンプレートの `CustomizedModuleDataIO` に入っている形:

```csharp
public class CustomizedModuleDataIO : ModuleDataIO
{
    readonly EditHistoryRecorder _editHistory;

    public CustomizedModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
        : base(designData, authenticationContext, dbAccess, temporaryFileManager)
    {
        //編集履歴 (EditHistoryField を置いたモジュールの保存ごとに履歴モジュールへスナップショットを書く)
        _editHistory = new EditHistoryRecorder(designData, this, AddSystemRecordAsync);
    }

    public override Task<List<ModuleSubmitResult>> SubmitAsync(Guid transactionId, List<ModuleSubmitData> transactionData)
        => _editHistory.SubmitAsync(transactionData, () => base.SubmitAsync(transactionId, transactionData));

    internal async Task<string> AddSystemRecordAsync(ModuleData data)
        => await AddAsync(Guid.NewGuid(), Guid.NewGuid(), data);
}
```

`EditHistoryRecorder(designData, io, addInternalAsync, logError)` の `logError` に `ILogger` の Warning 等を渡すと、記録をスキップした理由がログに出る。
