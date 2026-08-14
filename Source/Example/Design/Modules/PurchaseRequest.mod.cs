// 承認フロー実機確認サンプル (高機能版)。
// 申請時のデータ正当性チェック + 最終承認者だけが編集できる査定額フィールドのデモ
// 申請・再申請ボタンはフィールドの標準 UI (経路の組み立てだけがアプリの責務)

// 経路を組み立てる (フィールドの「経路組み立て」に設定。null を返すと申請中止)
ApprovalRouteData OnBuildRoute()
{
    //申請時のデータ正当性チェック (null を返すと申請は中止され、保存もされない)
    if (Amount.Value == null || Amount.Value <= 0)
    {
        Logger.Error("金額は1円以上を入力してください");
        return null;
    }
    if (Approver1.Value == null)
    {
        Logger.Error("一次承認者を選択してください");
        return null;
    }
    if (Amount.Value >= 100000 && Approver2.Value == null)
    {
        Logger.Error("10万円以上の申請は二次承認者が必要です");
        return null;
    }
    if (Approver1.Value == CurrentUser.Id.Value || Approver2.Value == CurrentUser.Id.Value)
    {
        Logger.Error("自分を承認者には指定できません");
        return null;
    }
    var route = Approval.NewRoute("購買ルート");
    var step1 = route.AddStep("一次承認");
    step1.AddMember(Approver1.Value, true);
    if (Approver2.Value != null)
    {
        var step2 = route.AddStep("二次承認");
        step2.AddMember(Approver2.Value, true);
    }
    return route;
}

// フロー状態が変わったとき (承認・取り下げ等の後) にフィールドから呼ばれる。
// その場アクションでは編集ロックのクライアント評価 (ページ読込時のデータ基準) が
// 更新されないため、編集可否の表示をここで切り替える (サーバー側は DataWriteCondition が正)
void OnApprovalStateChanged()
{
    var status = Approval.FlowStatus;
    IsViewOnly = status == "InProgress" || status == "Completed";
}
