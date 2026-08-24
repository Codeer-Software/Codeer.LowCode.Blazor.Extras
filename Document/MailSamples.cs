//メール送信のスクリプトサンプル (デザインスクリプト)。
//単発送信は MailField (値と変数のペア・値優先・値はスクリプトから設定可)、一斉送信は BulkMailField。

//① デザイン宣言どおりに送る (MailField "ReceiptMail" を配置し、ToVariable/Subject/Body を設定しておく)
void SendReceiptMail()
{
    var result = ReceiptMail.Send();
    if (result.IsSuccess) Toaster.Success("メールを送信しました");
    else Toaster.Error("メール送信に失敗しました: " + result.Failures[0].Error);
}

//② 完全に動的な送信 (値プロパティをスクリプトで設定 = デザインの変数より優先される)
void SendNotification_OnClick()
{
    //メールインフラ名はデザイン固定 (スクリプトからは変更不可)。デザイン側でも省略可で、
    //省略時は既定のインフラ (appsettings の Mail.DefaultInfraName → 先頭) が使われる
    ReceiptMail.To = CustomerEmail.Value;  //カンマ/セミコロン区切りで複数可 (Cc/Bcc も同様)
    ReceiptMail.Subject = "注文確認";       //値もテンプレートとして {変数} が自レコードで解決される
    ReceiptMail.Body = "注文番号: {OrderId.Value}";

    var result = ReceiptMail.Send();       //履歴の SourceModule/SourceId は自レコード
    if (result.IsSuccess) Toaster.Success("メールを送信しました");
    else Toaster.Error("メール送信に失敗しました: " + result.Failures[0].Error);
}

//③ Excel帳票を添付して送る (添付は送信後にクリアされる)
void SendReport_OnClick()
{
    var searchFile = new ModuleSearcher<TestFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Template");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        excel.OverWrite(this);

        ReceiptMail.To = "sato@example.com;suzuki@example.com";
        ReceiptMail.Cc = "manager@example.com";
        ReceiptMail.Subject = "月次レポート";
        ReceiptMail.Body = "今月のレポートを添付します。";
        ReceiptMail.AddAttachment("report.xlsx", excel);
        ReceiptMail.Send();
    }
}

//④ 一斉送信 (BulkMailField "BulkMail1" を配置しておく。確認ダイアログなしで送るスクリプトAPI)
void SendCampaign_OnClick()
{
    var result = BulkMail1.Send();
    if (result.IsSuccess) Toaster.Success(result.SuccessCount + " 件送信しました");
    else Toaster.Error("失敗 " + result.Failures.Count + " 件");
}
