# ApprovalFlowField (承認フロー)

申請書モジュールに1つ置くと、申請・承認・却下・差し戻し・取り下げ・再申請・回覧確認と、
ステッパー形式の進捗表示・コメント・履歴表示を提供するフィールド。

## セットアップ (承認モジュール群の自動生成)

動作には承認データモジュール群 (フロー / メンバー / 履歴、任意で経路マスタ) が必要。
手で作らず**セットアップコマンドで生成する**:

- デザイナ: メニュー Tools > 承認フローのセットアップ
- CLI (headless): `<designer.exe> approval-setup "<projectDir>" [--data-source <name>]
  [--route standard|none] [--user-module <ユーザーモジュール>] [--user-name-field Name] [--user-email-field Email]
  [--no-mail] [--no-pageframe] [--ddl-out <path.sql>]`

生成内容: 承認モジュール群 (フロー / メンバー / 履歴 + 検索用の承認待ち・承認状況 + 任意で経路マスタ 3 つ) +
承認対象モジュール enum `ApprovalTargetModule` (空。一覧の「申請種別」列でモジュール名を申請書の名前に読み替える) + PageFrame のページリンク + テーブル作成 DDL。**それだけ**。
**冪等**: 既存モジュールは生成しない (承認モジュール群は 1 セット。申請書が増えても共有する)。
DDL は自動実行されないため、生成後にテーブルを作成すること。

通知メールを含める (既定) 場合、メール側の準備 (差出人契約・送信履歴・サーバー設定) は
**メールのセットアップ (Tools > メールのセットアップ / `mail-setup`) を先に**実行しておく。
ユーザーモジュールに差出人契約が既にあれば、メールアドレス・表示名はその宣言に従う (`--user-*-field` は不要)。

### 申請書側の手順 (セットアップ後にデザイナで行う)

1. 申請書モジュールに ApprovalFlowField を置く (FlowModuleName = `ApprovalFlow`、FK 列 (例 `approval_id`) を DB に追加)
2. 申請書のスクリプトに `OnBuildRoute` を書き、フィールドの「経路組み立て」に設定する (下記「経路の組み立て」)
3. 編集ロック: 申請書の DataWriteCondition に「(フィールド名).Status が 未申請(null) / Returned / Withdrawn / Rejected」の Or 条件
4. 承認対象モジュール enum `ApprovalTargetModule` にメンバー (名前 = 申請書モジュール名 / 表示 = 申請書の名前) を追加

## Design

### 考え方

- 状態遷移はすべてサーバーの command API (`/api/approval`) が検証・実行する。
  クライアントの表示制御はスクリプトで外せるが、サーバーが拒否する (権限と同じ大原則)。
- 承認データは通常のモジュール (フロー / メンバー / 履歴の3つ) に保存される。
  各承認モジュールには**契約フィールド** (ApprovalXxxContractField) を1つ置き、
  「役割 → 自モジュールのフィールド名」のマッピングを宣言する (初期値は既定名。後からリネーム可)。
  申請書側で指定するのはフローモジュール名だけで、メンバー・履歴モジュールは
  フロー契約の Members / Histories 一覧の参照先として自動的に決まる。
- 複数の申請書モジュールが同じ承認モジュールを共有するのが標準。
  一覧画面 (承認待ち / 承認状況) が全申請種別を横断する。
- フロー・メンバー・履歴モジュールはエンジンが読み書きするデータと契約だけで UI を持たない。
  一覧は QueryField の検索用モジュールで作る (Example の MyApprovalList / ApprovalStatusList)。
  承認待ちは予約パラメータ `current_user_id` で自分が Waiting のメンバー行に絞る (サーバーが束縛するので権限として使える)。
  「開く」ボタンで TargetModuleName / TargetId から申請書へ遷移する。
- 経路 (誰が承認するか) は申請時にスクリプトで組み立てて渡す。
  メンバーは解決済みのユーザー Id を渡す。指定した経路は履歴に不変記録される。
- FK (承認フロー行への参照) はサーバーだけが書く。クライアントからは送信されない。

### 動作

- 申請 = 申請書の保存 + フロー生成 + FK 設定 + 履歴を同一トランザクションで実行
- 組み込みボタンのアクション成功後はフィールド表示だけを再読込する。編集ロックの表示
  (クライアント評価はページ読込時のデータ基準) は開き直しで反映される。
  その場で表示を切り替えたいアプリはボタンを外付けにして自分のスクリプトで行う (下記 Script 参照)
- ステップは直列。ステップ内は複数承認者 (完了条件 = RequiredMembers / All / Any)
- `Confirmation` (回覧) ステップはフローの進行をブロックしない
- 再申請は経路を再解決し、試行番号 (AttemptNo) で世代を分けて旧メンバーを温存する
- 楽観ロック: フローモジュールの `OptimisticLocking` フィールドで二重承認・同時操作を防止

### デザイナー設定プロパティ

| プロパティ | 説明 |
|---|---|
| DB列 | 承認フロー行への FK 列 (このフィールドが自テーブルに持つ列はこの1本だけ) |
| 承認フローモジュール | フロー本体のモジュール名 (既定 ApprovalFlow)。メンバー・履歴モジュールはフロー契約の Members / Histories 一覧の参照先として決まるため指定不要 |
| 取り下げ許可範囲 | BeforeFirstApproval (既定・承認が始まる前のみ) / Anytime (進行中ならいつでも)。業務ポリシー |
| 進捗を表示 / 履歴を表示 / コメント欄を表示 / アクションボタンを表示 | 標準 UI の表示切り替え。アクションボタンを OFF にすると ButtonField ＋ スクリプト API でアプリ独自の承認 UI に置き換えられる (サーバーの検証はどの UI からでも同じ) |
| 経路組み立て | ApprovalRouteData を返すスクリプト (null で申請中止)。設定すると組み込みの申請・再申請ボタンが出て、スクリプト API の Submit() / Resubmit() も使える |

### 契約フィールド (インターフェイス) と必須フィールド

各承認モジュールに契約フィールドを1つ置く (UI もデータも持たない設定フィールド。DB列不要):

- フローモジュール: `ApprovalFlowContractFieldDesign`
- メンバーモジュール: `ApprovalMemberContractFieldDesign`
- 履歴モジュール: `ApprovalHistoryContractFieldDesign`

契約フィールドの各プロパティ (役割) に自モジュールのフィールド名をマッピングする。
初期値は既定名なので、既定名でフィールドを作れば設定は不要。フィールド名を変えたい場合は
マッピングも合わせて変える (フィールドのリネームには自動追従する)。
役割のフィールドがモジュールに無ければデザインチェックがエラーにする (= 契約の実装漏れ検出)。

役割一覧 (括弧は推奨フィールド型。名前は変更可):

- フロー契約: `Status` (SelectField 推奨) / `TargetModuleName` / `TargetId` /
  `Applicant` (ユーザーモジュールへの LinkField。申請時にエンジンが書き込む) /
  `AttemptNo` / `CurrentStepNo` (Number) /
  `Members` / `Histories` (それぞれメンバー・履歴モジュールの ListField。バインド `Flow.Value == Id.Value`)。
  ほかに `OptimisticLocking` (OptimisticLockingField、IncrementVersion 推奨) が必須
- メンバー契約: `Flow` (Link→フロー) / `AttemptNo` / `StepNo` / `StepName` / `StepType` / `CompletionPolicy` /
  `IsCommentRequiredOnReject` / `ReturnScope` / `ApproverUser` (Link→ユーザー) / `IsRequired` / `IsFinalStep` / `Status` / `ActedAt`
- 履歴契約: `Flow` / `AttemptNo` / `Action` / `ActorUser` (Link→ユーザー) / `Comment` / `ActedAt`

デザインファイル (JSON) では次の1エントリを Fields に足すだけ (マッピングは既定値):

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalFlowContractFieldDesign" }
```

### 権限設定 (必須)

- 承認モジュール3つの「書き込み可能ユーザー条件」は誰も満たさない条件にする
  (承認データはサーバーの内部経路だけが書く。未設定だと正規の保存 API で改ざんできてしまう)
- 申請書モジュールの「データによる認可 書き込み」に編集ロックを宣言する:
  `(フィールド名).Status.Value が null (未申請 = フロー行なし・null 検索) / Returned / Withdrawn / Rejected`
  のときだけ書き込み可。状態・申請者はフロー行が正で、条件は**リンク越し参照**
  (`Approval.Status.Value` / `Approval.Applicant.Value`) で書く。フィールド宣言は不要
  (条件で使えばドット列が自動合成される)
- クライアント側でも権限の見た目を正しく出すには、申請書詳細レイアウトの DataOnlyFields に
  条件で使うパス (`Approval.Status` / `Approval.Applicant` / `Approval.Members`) を登録する
  (未登録でもサーバー強制は正しい。クライアント表示が保守側に倒れるだけ)
- アプリの Current User Module 設定が必須 (承認者・申請者の判定に使う)

### 条件エディタでの書き方

条件エディタの対象フィールド候補には、フロー行へのリンク越しパスが並ぶ。専用の UI は無く、
他のフィールドと同じ操作で条件を組み立てる:

- **状態**: 対象 = `(フィールド名).Status`。値は InProgress / Completed / Rejected / Returned / Withdrawn。
  **未申請 = フロー行なし = null 検索** (`Status` が null)。
  状態値の enum (ApprovalFlowStatus / ApprovalMemberStatus / ApprovalStepType) は
  **Extras に組み込みのコード定義 enum** で、enum 定義ファイルなしで使える。
  フローの `Status`・メンバーの `Status` / `StepType` を `EnumName` 付きの SelectField にすれば、
  条件エディタで値候補 (承認中/承認待ち 等) が選べる (サンプルはこの構成)
- **申請者**: 対象 = `(フィールド名).Applicant`、変数 `CurrentUser.Id.Value` との比較
- **現在の承認待ち**: And グループに
  `Status == "InProgress"` / `Members.StepType == "Approval"` / `Members.Status == "Waiting"` /
  `Members.ApproverUser == CurrentUser.Id.Value` を並べる
  (And で同じ一覧を指す条件は自動的に「同一行が全条件を満たす」存在条件になる)
- **最終承認の番**: 上の `StepType == "Approval"` を `Members.IsFinalStep == true` に替える
  (査定額のような「最終承認者だけが記入できるフィールド」の PermissionField に使う)

「現在の承認待ち」「最終承認の番」はフローモジュールの `Members` 一覧へのリンク越し存在条件
(`Approval.Members.～`) として組み立てられる。申請書モジュール側に一覧フィールドを複製する必要はない:

- フローモジュールの ListField `Members` (必須フィールド) が参照先。バインド `Flow.Value == Id.Value`
- クライアント側でも権限の見た目を正しく出すには、申請書モジュールの詳細レイアウトの
  DataOnlyFields に `(フィールド名).Members` (例 `Approval.Members`) を登録する
  (サーバーがメンバー行を応答に同梱し、クライアントの条件評価が使う。未登録でもサーバー強制は正しい)
- メンバーモジュールのリストレイアウトの DataOnlyFields に `StepType` / `IsFinalStep` を登録する
  (同梱される列はリンク先レイアウトで決まるため。
  未登録だとクライアント評価が false に倒れ、承認者の番でも見た目が読み取り専用になる)

### 応用: 状態・役割による細かい編集制御 (購買申請サンプルの構成)

- 行条件 (データによる認可 書き込み) = [状態: 未申請/差し戻し/取り下げ/却下] OR [現在の承認待ち]
- 申請内容の PermissionField = [状態: 未申請] OR [申請者 = 現在ユーザー]
- 査定額の PermissionField = [最終承認の番]
- 申請時のデータ正当性チェックは OnBuildRoute で行い、null を返して申請を中止する
  (フィールド必須は IsRequired、複合条件・業務ルールは OnBuildRoute、の分担)

デザインファイル (JSON) を直接書く場合は条件エディタの行モデルにする
(1行 = 1ターゲットの葉条件。`FieldMatchCondition` で複数ターゲットを包まない):

- 状態 = `MultiMatchCondition` (Or) の直下に `Approval.Status.Value` への葉条件を状態の数だけ並べる。
  値ありは `FieldValueMatchConditionNonNull`、未申請 (null 検索) だけ `FieldValueMatchCondition`+`NullValue`
- 現在の承認待ち / 最終承認の番 = `MultiMatchCondition` (And) の直下に
  `[Approval.Status.Value == "InProgress", (Approval.Members.StepType.Value == "Approval" または
  Approval.Members.IsFinalStep.Value == true),
  Approval.Members.Status.Value == "Waiting", Approval.Members.ApproverUser.Value == CurrentUser.Id.Value]`
  の葉条件を並べる。And で同じ一覧を指す条件は「同一行が全条件を満たす」= SQL の exists と同じ意味で評価される
- 申請者 = `FieldVariableMatchCondition` (`Approval.Applicant.Value` と `CurrentUser.Id.Value`)

### 状態値

- フロー: InProgress / Completed / Rejected / Returned / Withdrawn (未申請 = フロー行なし)
- 「取消」状態は無い。完全にやめる場合は取り下げてからレコードを削除する
- 取り下げは既定で承認が始まる前のみ (承認後は承認者に差し戻してもらう。「取り下げ許可範囲」で変更可)
- メンバー: Pending / Waiting / Approved / Rejected / Confirmed / Skipped。
  **Waiting は「本当に今待っている人」だけ** (未到達ステップは Pending。到達時にエンジンが昇格させる。
  ステップが完了したら残った未処理の承認メンバー = Any / 任意の相方は Skipped になり、承認できなくなる)。
  「自分の番」判定は `Status == Waiting` だけで書ける
- 完了条件: RequiredMembers (必須全員。必須ゼロなら任意1人) / All / Any

## Script

```csharp
// 経路の組み立て (プロパティ「経路組み立て」に設定。組み込みの申請/再申請ボタンが呼ぶ)
ApprovalRouteData OnBuildRoute()
{
    var route = new ApprovalRouteData();
    var step1 = route.AddStep("課長承認");
    step1.AddMember(課長選択.Value, true);       // 解決済みユーザー Id を渡す
    var step2 = route.AddStep("経理回覧");
    step2.StepType = "Confirmation";              // 回覧 (フローをブロックしない)
    step2.AddMember(経理担当.Value);
    return route;                                  // null を返すと申請中止
}

// 外付けボタン (ButtonField の OnClick) から申請する場合も同じ経路を通る
var result = 承認.Submit();                       // OnBuildRoute を呼んで申請 (保存と同一トランザクション)
承認.Resubmit();                                  // 再申請 (差し戻し・取り下げ・却下後)
if (!result.IsSuccess) Logger.Error(result.ErrorMessage);

// 経路マスタから読む。マスタはただのユーザー定義モジュール (契約なし・形は自由) で、
// 承認フロー側はこのスクリプトが返す経路しか見ない。名指し / 役職や部署からの解決 / 決め打ちロジック、
// どれも OnBuildRoute の書き方の違いでしかない (マスタ自体が必須ではない)。
// マスタを読む処理は経路マスタモジュール側のスクリプトに共通化し、申請書からはモジュールを new して呼ぶ
ApprovalRouteData OnBuildRouteFromMaster()
{
    return new ApprovalRoute().Load("経費ルート");   // 読んだ後に AddStep / AddMember で加工してから返してもよい
}

// 例: 経路 (ApprovalRoute: RouteName) → ステップ (ApprovalRouteStep: Route / StepNo / StepName / StepType /
//     CompletionPolicy / ReturnScope / IsCommentRequiredOnReject) → ステップ承認者 (ApprovalRouteStepMember:
//     Step / ApproverUser / IsRequired) の3段マスタを読む共通処理 (ApprovalRoute.mod.cs に置く)。
//     申請できない経路 (マスタに無い / 申請者自身が承認者) のエラー表示もここに集約する。
//     セットアップ (承認フローのセットアップ) はこのスクリプトも生成する
ApprovalRouteData Load(string routeName)
{
    var routes = new ModuleSearcher<ApprovalRoute>();
    routes.AddEquals(r => r.RouteName.Value, routeName);
    var master = routes.ExecuteFirstOrDefault();
    if (master == null) { Logger.Error("経路マスタに『" + routeName + "』がありません"); return null; }

    var steps = new ModuleSearcher<ApprovalRouteStep>();
    steps.AddEquals(s => s.Route.Value, master.Id.Value);
    steps.OrderBy(s => s.StepNo.Value);

    var route = new ApprovalRouteData();
    foreach (var s in steps.Execute())
    {
        var step = route.AddStep(s.StepName.Value);
        if (s.CompletionPolicy.Value != null && s.CompletionPolicy.Value != "") step.CompletionPolicy = s.CompletionPolicy.Value;
        var members = new ModuleSearcher<ApprovalRouteStepMember>();
        members.AddEquals(m => m.Step.Value, s.Id.Value);
        foreach (var m in members.Execute())
        {
            if (m.ApproverUser.Value == null) continue;
            if (m.ApproverUser.Value == CurrentUser.Id.Value) { Logger.Error("申請者自身が承認者に含まれる経路では申請できません"); return null; }
            step.AddMember(m.ApproverUser.Value, m.IsRequired.Value ?? true);
        }
    }
    return route;
}

// コメント (組み込みコメント欄と同じ値。外付けボタンから使う場合に設定/参照)
承認.Comment = "至急お願いします";

// アクション (標準ボタンは組み込み UI にもある)
承認.Approve("コメント");
承認.Reject("却下理由");
承認.ReturnToApplicant("修正してください");
承認.ReturnToStep(1, "課長からやり直し");        // ステップ設定の ReturnScope=AnyPreviousStep が必要
承認.Withdraw("");                                // 取り下げ (承認が始まる前のみ)
承認.Confirm("");

// 状態参照
var status = 承認.FlowStatus;                     // "InProgress" 等 (未申請は空文字)
var submitted = 承認.IsSubmitted;

// 外付けボタンの出し分け (資格プロパティ。表示制御用で、強制は常にサーバー)
申請ボタン.IsVisible = 承認.CanSubmit;
承認ボタン.IsVisible = 承認.CanApprove;
確認ボタン.IsVisible = 承認.CanConfirm;
取り下げボタン.IsVisible = 承認.CanWithdraw;
再申請ボタン.IsVisible = 承認.CanResubmit;

// 外付けボタンの OnClick は、アクション後の表示更新まで自分のスクリプトでやる。
// 編集ロックのクライアント評価はページ読込時のデータ基準のため、その場では変わらない
var r = 承認.Withdraw("");
if (r.IsSuccess) IsViewOnly = false;              // サーバー側は DataWriteCondition が正
```

- ステップのプロパティ: `StepType` (Approval/Confirmation) / `CompletionPolicy` (RequiredMembers/All/Any) /
  `IsCommentRequiredOnReject` (既定 true) / `ReturnScope` (ApplicantOnly/AnyPreviousStep)
- `AddStep()` は名前なし (ステップ名は任意。表示はステップ番号で代替)。`AddStep(name)` で名前付き
- `AddMember(userId, isRequired)` は必須指定付き。省略時は必須
- `route.AddMembers(new[]{ 課長.Value, 部長.Value })` は**承認者ごとに 1 ステップ**を追加する直列承認の最短形
  (各ステップは名前なし・承認者 1 人・必須)。同じステップに複数人を置く合議は `AddStep().AddMember(a).AddMember(b)`

## CSS

`data-system="approval-flow"` のブロック。主なクラス:

- `.approval-status-badge` / `.approval-status-inprogress` 等 — 状態バッジ
- `.approval-steps` / `.approval-step` / `.approval-step.current` — ステッパー
- `.approval-member` / `.status-approved` 等 — メンバー行
- `.approval-comment` / `.approval-actions` / `.approval-history` — コメント・ボタン・履歴
