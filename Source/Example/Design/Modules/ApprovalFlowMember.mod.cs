// 承認データは保護条件 (誰も書けない) でモジュールが閲覧専用になるため、
// 遷移するだけの「開く」ボタンは表示制御を明示的に解除する (書き込みはサーバーが拒否したまま)
void OnAfterInitialization()
{
    OpenRequestButton.IsViewOnly = false;
}

// 承認待ち一覧の「開く」: フロー行から申請書 (TargetModuleName/TargetId) へ遷移する。
// 一覧経由の行モジュールはリスト列にないフィールドが遅延ロードで空のことがあるため、
// 自分の Id で再取得して値を確実に取る (現行テンプレートと同じパターン)
void OpenRequest_OnClick()
{
    var ms = new ModuleSearcher<ApprovalFlowMember>();
    ms.AddEquals(m => m.Id.Value, Id.Value);
    var members = ms.Execute();
    if (members.Count == 0) return;

    var fs = new ModuleSearcher<ApprovalFlow>();
    fs.AddEquals(f => f.Id.Value, members[0].Flow.Value);
    var flows = fs.Execute();
    if (flows.Count == 0) return;

    var target = flows[0];
    NavigationService.NavigateTo(NavigationService.GetModuleDataUrl(target.TargetModuleName.Value, target.TargetId.Value));
}
