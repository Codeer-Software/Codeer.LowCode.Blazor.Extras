# ApprovalFlowField (承認フロー)

申請書モジュールに1つ置くと、申請・承認・却下・差し戻し・取り下げ・再申請・回覧確認と、
ステッパー形式の進捗表示・コメント・履歴表示を提供するフィールド。

## Design

### 考え方

- 状態遷移はすべてサーバーの command API (`/api/approval/*`) が検証・実行する。
  クライアントの表示制御はスクリプトで外せるが、サーバーが拒否する (権限と同じ大原則)。
- 承認データは通常のモジュール (フロー / メンバー / 履歴の3つ) に保存される。
  フィールド名は既定名で固定 (下記の必須フィールド)。モジュール名はプロパティで変更できる。
- 複数の申請書モジュールが同じ承認モジュールを共有するのが標準。
  承認待ち一覧はメンバーモジュールの ListPage (`ApproverUser = ログインユーザー AND Status = Waiting`) で
  全申請種別を横断して作れる。
- 経路 (誰が承認するか) は申請時にスクリプトで組み立てて渡す (`AllowScriptRoute` が必要)。
  メンバーは解決済みのユーザー Id を渡す。
- FK (承認フロー行への参照) はサーバーだけが書く。クライアントからは送信されない。

### 動作

- 申請 = 申請書の保存 + フロー生成 + FK 設定 + 履歴を同一トランザクションで実行
- ステップは直列。ステップ内は複数承認者 (完了条件 = RequiredMembers / All / Any)
- `Confirmation` (回覧) ステップはフローの進行をブロックしない
- 再申請は経路を再解決し、試行番号 (AttemptNo) で世代を分けて旧メンバーを温存する
- 楽観ロック: フローモジュールの `OptimisticLocking` フィールドで二重承認・同時操作を防止

### デザイナー設定プロパティ

| プロパティ | 説明 |
|---|---|
| DB列 | 承認フロー行への FK 列 |
| 状態コピー列 | フロー状態のコピーを保存する自テーブルの列 (任意)。設定するとエンジンが遷移のたびに書き戻し、条件式・検索・一覧列で変数 `Approval.State` が JOIN なしで使える (null = 未申請) |
| 申請者コピー列 | 申請者ユーザー Id のコピー列 (任意)。変数 `Approval.Applicant` が使える (申請者本人の判定用) |
| 承認メンバー一覧フィールド | 承認メンバーを表示する同一モジュール上の ListField 名 (既定 ApprovalMembers)。条件エディタの「現在の承認待ち」「最終承認の番」がこの一覧への存在条件を組み立てる |
| 承認フローモジュール | フロー本体のモジュール名 (既定 ApprovalFlow) |
| 承認メンバーモジュール | 承認者スナップショットのモジュール名 (既定 ApprovalFlowMember) |
| 承認履歴モジュール | 監査履歴のモジュール名 (既定 ApprovalHistory)。エンジンは INSERT のみ |
| スクリプト経路を許可 | スクリプトで組み立てた経路の受け入れ (既定 false)。有効化 = 申請側スクリプトが承認者を指定できる |
| 取り下げ許可範囲 | BeforeFirstApproval (既定・承認が始まる前のみ) / Anytime (進行中ならいつでも)。業務ポリシー |
| 進捗を表示 / 履歴を表示 / コメント欄を表示 / アクションボタンを表示 | 標準 UI の表示切り替え。アクションボタンを OFF にすると ButtonField ＋ スクリプト API でアプリ独自の承認 UI に置き換えられる (サーバーの検証はどの UI からでも同じ) |
| 経路組み立て | ApprovalRouteData を返すスクリプト (null で申請中止)。設定すると組み込みの申請・再申請ボタンが出て、スクリプト API の Submit() / Resubmit() も使える |
| 状態変化時 | フロー状態が変わった後に呼ぶスクリプト (承認・取り下げ等の成功後)。編集可否の表示更新に使う |
| 非表示アクション | 組み込みボタンから隠すアクション (カンマ区切り。例 "Withdraw,Return")。一部だけ外付けにする用 |
| コメント入力フィールド | コメントの入力元フィールド。指定すると組み込みコメント欄は出ず、そのフィールド値がコメントになる (RichTextField 等に差し替え可) |

### 必須フィールド (既定名・綴り固定)

- フローモジュール: `Status` / `TargetModuleName` / `TargetId` / `RouteName` / `AttemptNo` / `CurrentStepNo` と
  `OptimisticLocking` (OptimisticLockingField、IncrementVersion 推奨)
- メンバーモジュール: `Flow` (Link→フロー) / `AttemptNo` / `StepNo` / `StepName` / `StepType` / `CompletionPolicy` /
  `IsCommentRequiredOnReject` / `ReturnScope` / `ApproverUser` (Link→ユーザー) / `IsRequired` / `IsFinalStep` / `Status` / `ActedAt`
- 履歴モジュール: `Flow` / `AttemptNo` / `StepNo` / `Action` / `ActorUser` (Link→ユーザー) / `FromStatus` / `ToStatus` / `Comment` / `ActedAt`

### 権限設定 (必須)

- 承認モジュール3つの「書き込み可能ユーザー条件」は誰も満たさない条件にする
  (承認データはサーバーの内部経路だけが書く。未設定だと正規の保存 API で改ざんできてしまう)
- 「状態コピー列」を設定し、申請書モジュールの「データによる認可 書き込み」に編集ロックを宣言する:
  `Approval.State が null (未申請・null 検索) / Returned / Withdrawn / Rejected` のときだけ書き込み可。
  自テーブルの列なので dotted リンク宣言は不要 (dotted 列 `Approval.Status` は一覧の状態列表示にだけ使う)
- アプリの Current User Module 設定が必須 (承認者・申請者の判定に使う)

### 条件エディタでの書き方 (専用検索コントロール)

条件エディタで対象にこのフィールドを選ぶと、専用の検索コントロールが出る。種類を選ぶだけで
内部値 (Waiting 等) やリストのパスを書かずに条件を組み立てられる:

- **状態**: 未申請 / 承認中 / 承認済み / 却下 / 差し戻し / 取り下げ の複数選択 (未申請 = null 検索)。
  「状態コピー列」の設定が必要
- **申請者**: 申請者と変数の比較 (既定 = 現在ユーザーの Id)。「申請者コピー列」の設定が必要
- **現在の承認待ち**: 指定ユーザーが今まさに承認待ち (承認中 + メンバー一覧への同一行存在条件)
- **最終承認の番**: 最終承認ステップの番が指定ユーザーに回っている (査定額のような
  「最終承認者だけが記入できるフィールド」の PermissionField に使う)

「現在の承認待ち」「最終承認の番」は「承認メンバー一覧フィールド」の ListField が必要:

- ListField `ApprovalMembers`: 検索条件 = メンバーモジュール、バインド `Flow.Value == Approval.Id`。
  詳細レイアウトの DataOnlyFields に登録する
- メンバーモジュールのリストレイアウトの DataOnlyFields に `StepType` / `IsFinalStep` を登録する
  (埋め込みリストのロード列はリンク先レイアウトで決まるため。
  未登録だとクライアント評価が false に倒れ、承認者の番でも見た目が読み取り専用になる)

### 応用: 状態・役割による細かい編集制御 (購買申請サンプルの構成)

- 行条件 (データによる認可 書き込み) = [状態: 未申請/差し戻し/取り下げ/却下] OR [現在の承認待ち]
- 申請内容の PermissionField = [状態: 未申請] OR [申請者 = 現在ユーザー]
- 査定額の PermissionField = [最終承認の番]
- 申請時のデータ正当性チェックは OnBuildRoute で行い、null を返して申請を中止する
  (フィールド必須は IsRequired、複合条件・業務ルールは OnBuildRoute、の分担)

デザインファイル (JSON) を直接書く場合は正準形にする (条件エディタが1行として表示・編集できる形):

- 状態の複数選択 = `FieldMatchCondition` (`FieldName` = 承認フィールド名) に
  `Approval.State` への `FieldValueMatchCondition` (未申請は `NullValue`) を Or で並べる
- 現在の承認待ち / 最終承認の番 = `FieldMatchCondition` の And で
  `[Approval.State == "InProgress", (StepType == "Approval" または IsFinalStep == true),
  ApprovalMembers.Status.Value == "Waiting", ApprovalMembers.ApproverUser.Value == CurrentUser.Id.Value]`。
  And で同じ一覧を指す条件は「同一行が全条件を満たす」= SQL の exists と同じ意味で評価される
- 申請者 = `FieldVariableMatchCondition` (`Approval.Applicant` と `CurrentUser.Id.Value`)

### 状態値

- フロー: InProgress / Completed / Rejected / Returned / Withdrawn (未申請 = フロー行なし)
- 「取消」状態は無い。完全にやめる場合は取り下げてからレコードを削除する
- 取り下げは既定で承認が始まる前のみ (承認後は承認者に差し戻してもらう。「取り下げ許可範囲」で変更可)
- メンバー: Pending / Waiting / Approved / Rejected / Confirmed / Skipped。
  **Waiting は「本当に今待っている人」だけ** (未到達ステップは Pending。到達時にエンジンが昇格させる)。
  承認待ち一覧や「自分の番」判定は `Status == Waiting` だけで書ける
- 完了条件: RequiredMembers (必須全員。必須ゼロなら任意1人) / All / Any

## Script

```csharp
// 経路の組み立て (プロパティ「経路組み立て」に設定。組み込みの申請/再申請ボタンが呼ぶ)
ApprovalRouteData OnBuildRoute()
{
    var route = 承認.NewRoute("経費ルート");
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

// 経路を自前で渡す形も可 (AllowScriptRoute が必要なのは全経路共通)
承認.SubmitWithRoute(route);
承認.ResubmitWithRoute(route);

// コメント (組み込みコメント欄と同じ値。外付けボタンから使う場合に設定/参照)
承認.Comment = "至急お願いします";

// アクション (標準ボタンは組み込み UI にもある)
承認.Approve("コメント");
承認.Reject("却下理由");
承認.ReturnToApplicant("修正してください");
承認.ReturnToStep(1, "課長からやり直し");        // ステップ設定の ReturnScope=AnyPreviousStep が必要
承認.Withdraw("");                                // 取り下げ (承認が始まる前のみ)
承認.Confirm("");
承認.Reload();                                    // 表示データの再読込

// 状態参照
var status = 承認.FlowStatus;                     // "InProgress" 等 (未申請は空文字)
var submitted = 承認.IsSubmitted;

// 状態変化時スクリプト (プロパティ「状態変化時」に設定) の定番:
// その場アクション後は編集ロックのクライアント評価が古いままなので、ここで表示を切り替える
void OnApprovalStateChanged()
{
    var s = 承認.FlowStatus;
    申請ボタン.IsVisible = !承認.IsSubmitted;
    再申請ボタン.IsVisible = s == "Rejected" || s == "Returned" || s == "Withdrawn";
    IsViewOnly = s == "InProgress" || s == "Completed";  // サーバー側は DataWriteCondition が正
}
```

- ステップのプロパティ: `StepType` (Approval/Confirmation) / `CompletionPolicy` (RequiredMembers/All/Any) /
  `IsCommentRequiredOnReject` (既定 true) / `ReturnScope` (ApplicantOnly/AnyPreviousStep)
- `AddMember(userId, isRequired)` は必須指定付き。省略時は必須

## CSS

`data-system="approval-flow"` のブロック。主なクラス:

- `.approval-status-badge` / `.approval-status-inprogress` 等 — 状態バッジ
- `.approval-steps` / `.approval-step` / `.approval-step.current` — ステッパー
- `.approval-member` / `.status-approved` 等 — メンバー行
- `.approval-comment` / `.approval-actions` / `.approval-history` — コメント・ボタン・履歴
