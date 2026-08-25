// 承認フロー実機確認サンプル (経路マスタ版)。
// 経路マスタ (経路 / ステップ / ステップ承認者) はただのユーザー定義モジュールで、承認フロー側は
// このスクリプトが返す ApprovalRouteData しか見ない。マスタの形や承認者の決め方はプロジェクトの自由
// (名指し / 役職や部署から解決 / 決め打ちロジック、どれも OnBuildRoute の書き方の違い)。
// 申請・再申請ボタンはフィールドの標準 UI

// 経路を組み立てる (フィールドの「経路組み立て」に設定。null を返すと申請中止)
ApprovalRouteData OnBuildRoute()
{
    var routes = new ModuleSearcher<ApprovalRoute>();
    routes.AddEquals(r => r.RouteName.Value, "経費ルート");
    var master = routes.ExecuteFirstOrDefault();
    if (master == null)
    {
        Logger.Error("経路マスタに『経費ルート』がありません");
        return null;
    }

    var steps = new ModuleSearcher<ApprovalRouteStep>();
    steps.AddEquals(s => s.Route.Value, master.Id.Value);
    steps.OrderBy(s => s.StepNo.Value);

    var route = Approval.NewRoute(master.RouteName.Value);
    foreach (var s in steps.Execute())
    {
        var step = route.AddStep(s.StepName.Value);
        if (s.StepType.Value != null && s.StepType.Value != "") step.StepType = s.StepType.Value;
        if (s.CompletionPolicy.Value != null && s.CompletionPolicy.Value != "") step.CompletionPolicy = s.CompletionPolicy.Value;
        if (s.ReturnScope.Value != null && s.ReturnScope.Value != "") step.ReturnScope = s.ReturnScope.Value;
        step.IsCommentRequiredOnReject = s.IsCommentRequiredOnReject.Value ?? true;

        var members = new ModuleSearcher<ApprovalRouteStepMember>();
        members.AddEquals(m => m.Step.Value, s.Id.Value);
        foreach (var m in members.Execute())
        {
            if (m.ApproverUser.Value != null) step.AddMember(m.ApproverUser.Value, m.IsRequired.Value ?? true);
        }
    }
    return route;
}

// 受付メールを送る (MailField のデモ。実送信には appsettings の Mail 設定が必要)
void SendReceiptMail()
{
    var result = ReceiptMail.Send();
    if (!result.IsSuccess)
    {
        Logger.Error("メール送信に失敗しました");
    }
}
