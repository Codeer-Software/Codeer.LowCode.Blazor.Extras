// 承認データは保護条件 (誰も書けない) でモジュールが閲覧専用になるため、
// 遷移するだけの「開く」ボタンは表示制御を明示的に解除する (書き込みはサーバーが拒否したまま)
void OnAfterInitialization()
{
    OpenRequestButton.IsViewOnly = false;
}

// 承認フロー管理の「開く」: 申請書 (TargetModuleName/TargetId) へ遷移する。
// 一覧の DataOnlyFields に TargetId を登録してあるので行の値をそのまま使える
void OpenRequest_OnClick()
{
    if (TargetModuleName.Value == null || TargetId.Value == null) return;
    NavigationService.NavigateTo(NavigationService.GetModuleDataUrl(TargetModuleName.Value, TargetId.Value));
}
