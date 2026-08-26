# 承認フロー

申請書モジュールに **ApprovalFlowField** を 1 つ置くと、申請・承認・却下・差し戻し・取り下げ・再申請・回覧と、
ステッパー形式の進捗表示・コメント・履歴が動きます。承認データは通常のモジュールに保存されるため、
一覧・検索・画面カスタマイズはいつものローコードのやり方でできます。

- [概略](#概略) — 何ができるか、導入の流れ
- [詳細](#詳細) — 経路の組み立て、状態、権限、契約、通知、API

---

## 概略

### できること

- **直列ステップの承認**。ステップ内に複数の承認者 (全員 / 誰か 1 人 / 必須メンバー全員)
- **回覧** (Confirmation) ステップ — 承認を止めずに確認だけ求める
- **却下 / 差し戻し (申請者へ・前のステップへ) / 取り下げ / 再申請**。再申請は世代 (試行番号) で履歴を温存
- **承認待ち一覧・承認状況一覧** (全申請種別を横断)
- **順番が回ってきた人への通知メール** (任意)
- **編集ロック** — 申請中は申請書を書けない、最終承認者だけが記入できる欄、などを条件で表現
- すべての状態遷移は**サーバーが検証**します。画面のボタンを消しても、権限のない操作はサーバーが拒否します

### 導入の流れ

1. **デザイナ Tools > 承認フローのセットアップ** を実行
   承認モジュール群 (フロー / メンバー / 履歴 + 承認待ち・承認状況の一覧 + 任意で経路マスタ) と
   ページリンク、テーブル作成 DDL が生成されます。DDL でテーブルを作成してください
   (コマンドライン: `<designer.exe> approval-setup "<projectDir>"`)
2. 申請書モジュールに **ApprovalFlowField** を置き、FK 列 (例 `approval_id`) を DB に追加
3. 申請書のスクリプトに**経路を組み立てる関数**を書き、フィールドの「経路組み立て」に設定

   ```csharp
   ApprovalRouteData OnBuildRoute()
   {
       // 課長 → 部長 の直列承認 (承認者はユーザー Id)
       return new ApprovalRouteData().AddMembers(new[]{ 課長.Value, 部長.Value });
   }
   ```

4. 申請書の「データによる認可 (書き込み)」に編集ロック条件を設定 (未申請 / 差し戻し / 取り下げ / 却下のときだけ書ける)
5. 承認対象モジュール enum `ApprovalTargetModule` に申請書モジュールを追加 (名前 = モジュール名 / 表示 = 申請書の名前。一覧の「申請種別」列でモジュール名を読み替えるためのもので、無くても承認は動く)

これで詳細画面に「申請」ボタンが現れ、申請後はステッパー・承認ボタン・コメント・履歴が表示されます。

---

## 詳細

### 全体像

```
申請書モジュール (複数可)
  └ ApprovalFlowField … 画面 (ステッパー・ボタン・コメント・履歴) + フロー行への FK
        │  申請 / 承認 などの操作はすべてサーバー API へ
        ▼
承認モジュール群 (全申請書で共有。セットアップが生成)
  ApprovalFlow        … 1 申請 = 1 行 (状態・申請者・現在ステップ)
  ApprovalFlowMember  … 承認者ごとに 1 行 (ステップ番号・種別・状態・操作日時)
  ApprovalHistory     … 操作の記録 (追記のみ)
  MyApprovalList / ApprovalStatusList … 承認待ち / 承認状況の一覧 (検索用)
  ApprovalRoute / ApprovalRouteStep / ApprovalRouteStepMember … 経路マスタ (任意)
```

- 承認モジュールは**サーバーだけが書きます**。3 モジュールの「書き込み可能ユーザー条件」は誰も満たさない条件にしてください (セットアップの既定)
- 申請時に経路 (誰が承認するか) を**メンバー行にスナップショット**するので、進行中の申請は経路変更や人事異動の影響を受けません

### 経路の組み立て (スクリプト)

`ApprovalRouteData` を返す関数を書き、フィールドの「経路組み立て」に設定します。`null` を返すと申請を中止します
(入力チェックや業務ルールによる申請不可はここで判定します)。

```csharp
ApprovalRouteData OnBuildRoute()
{
    var route = new ApprovalRouteData();

    // 最短形: 承認者ごとに 1 ステップの直列承認
    route.AddMembers(new[]{ 課長.Value, 部長.Value });

    // ステップを細かく指定する形
    var step = route.AddStep("合議");                       // AddStep() で名前なしも可 (表示はステップ番号)
    step.AddMember(営業部長.Value).AddMember(経理部長.Value); // 同じステップに複数人 (既定: 全員必須)
    step.CompletionPolicy = "Any";                          // RequiredMembers (既定) / All / Any
    step.ReturnScope = "AnyPreviousStep";                   // 前のステップへの差し戻しを許可
    step.IsCommentRequiredOnReject = false;                 // 却下時のコメントを任意に

    var confirm = route.AddStep("経理回覧");
    confirm.StepType = "Confirmation";                      // 回覧 (フローを止めない)
    confirm.AddMember(経理担当.Value);

    if (Amount.Value > 1000000 && 社長.Value == null) { Logger.Error("100 万円超は社長承認が必要です"); return null; }
    return route;
}
```

| ビルダー | 説明 |
|---|---|
| `route.AddMembers(string[] userIds)` | 承認者ごとに 1 ステップ (名前なし・1 人・必須) を追加。直列承認の最短形 |
| `route.AddStep(name)` / `route.AddStep()` | ステップを追加して返す |
| `step.AddMember(userId)` / `AddMember(userId, isRequired)` | 承認者を追加 (既定は必須)。`step` を返すので続けて書ける |
| `step.StepType` | `Approval` (既定) / `Confirmation` (回覧) |
| `step.CompletionPolicy` | `RequiredMembers` (必須メンバー全員。必須ゼロなら誰か 1 人) / `All` / `Any` |
| `step.ReturnScope` | `ApplicantOnly` (既定) / `AnyPreviousStep` |
| `step.IsCommentRequiredOnReject` | 却下・差し戻し時のコメント必須 (既定 true) |

**経路マスタから読む**: セットアップで経路マスタ (経路 / ステップ / ステップ承認者の 3 段) を生成すると、
マスタを読んで `ApprovalRouteData` を返す共通スクリプト `Load(経路名)` も生成されます。
申請書からは `return new ApprovalRoute().Load("経費ルート");` と呼ぶだけです。
マスタは通常のモジュールなので、形を変えるのも、役職や部署から承認者を解決するロジックを足すのも自由です。

### 状態

| フロー (申請全体) | 意味 |
|---|---|
| (フロー行なし) | 未申請 |
| InProgress | 承認中 |
| Completed | 承認完了 |
| Rejected | 却下 |
| Returned | 差し戻し (申請者へ) |
| Withdrawn | 取り下げ |

Rejected / Returned / Withdrawn からは申請書を編集して**再申請**できます (試行番号 +1、旧世代のメンバー・履歴は残る)。
「取消」状態はありません。完全にやめる場合は取り下げてからレコードを削除します。

| メンバー (承認者ごと) | 意味 |
|---|---|
| Pending | 未到達 (前のステップが終わると Waiting になる) |
| Waiting | **今、承認を待っている人** |
| Approved / Rejected / Confirmed | 承認済 / 却下 / 確認済 (回覧) |
| Skipped | 不要になった (誰か 1 人で完了したときの残り、却下後の残りなど) |

「自分の番か」は `Status == Waiting AND ApproverUser == 現在ユーザー` だけで判定できます。

### 画面の設定 (ApprovalFlowField のプロパティ)

| プロパティ | 説明 |
|---|---|
| DB列 | フロー行への FK 列 |
| 承認フローモジュール | 既定 `ApprovalFlow`。メンバー・履歴モジュールはフロー側の一覧定義から自動で決まる |
| 経路組み立て | `ApprovalRouteData` を返すスクリプト関数 |
| 取り下げ許可範囲 | `BeforeFirstApproval` (既定・承認が始まる前のみ) / `Anytime` |
| 進捗を表示 / 履歴を表示 / コメント欄を表示 / アクションボタンを表示 | 標準 UI の部分的な ON/OFF。全部 OFF にして ButtonField + スクリプトで独自 UI にもできる |

履歴だけを別の場所に置きたい場合は **ApprovalHistoryField** (同一モジュールの ApprovalFlowField を指定) を使います。

### 権限と編集ロック

状態・申請者はフロー行が持つので、申請書側の条件は **リンク越し参照** (`承認.Status.Value` など) で書きます。
条件エディタの対象フィールド候補にそのまま並びます。

| やりたいこと | 条件 |
|---|---|
| 申請中は編集不可 | データによる認可 (書き込み) = `承認.Status` が null (未申請) / Returned / Withdrawn / Rejected の Or |
| 申請者だけが直せる欄 | PermissionField = `承認.Applicant == CurrentUser.Id` |
| 今の承認者だけが直せる欄 | And: `承認.Status == InProgress`、`承認.Members.StepType == Approval`、`承認.Members.Status == Waiting`、`承認.Members.ApproverUser == CurrentUser.Id` |
| 最終承認者だけが記入できる欄 (査定額など) | 上の `StepType == Approval` を `承認.Members.IsFinalStep == true` に替える |

- クライアント側でも正しく見せるには、申請書の詳細レイアウトの DataOnlyFields に `承認.Status` / `承認.Applicant` / `承認.Members` を登録します (未登録でもサーバー側の強制は正しく、表示が読み取り専用側に倒れるだけ)
- アプリ設定の「現在のユーザーのモジュール」が必須です (承認者・申請者の判定に使う)

### 通知メール (任意)

承認メンバーモジュールに **MailField** を置き、承認メンバー契約の「順番到達通知メール」に指定すると、
承認の順番が回ってきた人へサーバーが自動で通知します。文面の `{変数}` はメンバー行で解決されます
(`{StepName.Value}`、`{ApproverUser.Name.Value}`、宛先 `ApproverUser.Email.Value`)。
メール側の準備 (送信インフラ設定・差出人契約・送信履歴) は [メール送信](Mail.md) のセットアップで行います。
通知の失敗は承認操作を失敗させません。

### 契約フィールド (フィールド名の対応表)

承認モジュールはそれぞれ**契約フィールド**を 1 つ持ち、「役割 → 自モジュールのフィールド名」を宣言します。
セットアップが既定名で生成するので通常は触りません。フィールド名を変えたいときは対応を変えます
(リネームには自動追従。不在ならデザインチェックがエラー)。

| 契約 | 必須の役割 | 任意の役割 (空 = 使わない) |
|---|---|---|
| フロー契約 | Status / TargetModuleName / TargetId / Applicant / AttemptNo / CurrentStepNo / Members / Histories | — |
| メンバー契約 | Flow / AttemptNo / StepNo / ApproverUser / Status | StepName / IsFinalStep / ActedAt (表示用。空なら書かない)、StepType / CompletionPolicy / ReturnScope / IsCommentRequiredOnReject / IsRequired (空ならそれぞれ Approval / RequiredMembers / ApplicantOnly / false / true で動く)、TurnNotifyMail |
| 履歴契約 | Flow | AttemptNo / Action / ActorUser / Comment / ActedAt (空なら記録しない) |

任意の役割を空にすると、その列を持たない小さな構成にできます。ポリシー系を空にした場合、経路スクリプトで
指定した値はメンバー行に写らず既定で動くので、その概念を使わないアプリだけで空にしてください。

### スクリプト API (ApprovalFlowField)

```csharp
// 申請 / 再申請 (「経路組み立て」を呼んで、保存と同じトランザクションで申請)
var r = 承認.Submit();          if (!r.IsSuccess) Logger.Error(r.ErrorMessage);
承認.Resubmit();

// 操作
承認.Approve("コメント");
承認.Reject("却下理由");
承認.ReturnToApplicant("修正してください");
承認.ReturnToStep(1, "課長からやり直し");   // ReturnScope = AnyPreviousStep のステップから
承認.Withdraw("");
承認.Confirm("");
承認.Comment = "至急お願いします";           // 組み込みコメント欄と同じ値

// 状態
承認.FlowStatus      // "InProgress" 等 (未申請は空文字)
承認.IsSubmitted

// 外付けボタンの出し分け (表示制御用。強制はサーバー)
申請ボタン.IsVisible   = 承認.CanSubmit;
承認ボタン.IsVisible   = 承認.CanApprove;
確認ボタン.IsVisible   = 承認.CanConfirm;
取り下げボタン.IsVisible = 承認.CanWithdraw;
再申請ボタン.IsVisible = 承認.CanResubmit;
```

操作の戻り値は `ApprovalActionResult` (`IsSuccess` / `ErrorMessage` / `FlowId` / `TargetId`) です。

### サーバー API と結線 (アプリテンプレートに含まれるもの)

- すべての操作は `POST /api/approval` に `ApprovalCommand` (Action + 対象 + 楽観ロック値) を送る 1 本の API です。
  アプリの `ApprovalController` はこれを受けて `ApprovalEngine.ExecuteAsync(command)` を呼ぶだけで、通知メールとログの結線がアプリ側の唯一の責務です
- サーバーは毎回、認証ユーザーの資格 (承認者本人か・申請者本人か)、状態遷移の整合、楽観ロック値、
  差し戻し範囲、コメント必須、経路の妥当性 (承認ステップが 1 つ以上・承認者空でない) を検証します
- クライアント起動時に `ApprovalTransport.EndPointBase = "/api/approval"` を設定します

### セットアップコマンドの内容

```
<designer.exe> approval-setup "<projectDir>" [--data-source <name>] [--route standard|none]
    [--user-module AppUser] [--user-name-field Name] [--user-email-field Email]
    [--no-mail] [--no-pageframe] [--ddl-out <path.sql>]
```

- 生成物: ApprovalFlow / ApprovalFlowMember / ApprovalHistory、MyApprovalList / ApprovalStatusList、
  経路マスタ 3 モジュール (`--route standard` のとき)、承認対象モジュール enum `ApprovalTargetModule`、PageFrame のリンク、DDL
- 冪等: 既にあるモジュールは生成しません (承認モジュール群は 1 セットを全申請書で共有)
- 通知メールを含める場合は、先に **メールのセットアップ** を実行しておきます
