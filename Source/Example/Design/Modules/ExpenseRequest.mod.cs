// 承認フロー実機確認サンプル (シンプル版)。
// 申請・再申請ボタンはフィールドの標準 UI (経路の組み立てだけがアプリの責務)

// 経路を組み立てる (フィールドの「経路組み立て」に設定。null を返すと申請中止)
ApprovalRouteData OnBuildRoute()
{
    if (Approver1.Value == null)
    {
        Logger.Error("一次承認者を選択してください");
        return null;
    }
    var route = Approval.NewRoute("経費ルート");
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
