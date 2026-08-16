// 承認フロー実機確認サンプル (シンプル版)。
// 経路は経路マスタ (承認経路マスタ画面で管理) の「経費ルート」を使う。
// 申請・再申請ボタンはフィールドの標準 UI (経路の取得だけがアプリの責務)

// 経路を組み立てる (フィールドの「経路組み立て」に設定。null を返すと申請中止)
ApprovalRouteData OnBuildRoute()
{
    var route = Approval.LoadRoute("経費ルート");
    if (route == null)
    {
        Logger.Error("経路マスタに『経費ルート』がありません");
        return null;
    }
    return route;
}

// 受付メールを送る (MailField のデモ。実送信には appsettings の Mail.Infras 設定が必要)
void SendReceiptMail()
{
    var result = ReceiptMail.Send();
    if (!result.IsSuccess)
    {
        Logger.Error("メール送信に失敗しました");
    }
}
