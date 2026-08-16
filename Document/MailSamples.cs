//=====================================================================
// メール送信サンプル (CLBスクリプト)
// メール送信機能仕様.md の補足。
// 一斉送信は BulkMailField (仕様書§8。デザイナで配置・設定) に一本化されている。
// スクリプトで書くのは単発送信 (Mail) と、BulkMailField のスクリプト起動だけ。
//=====================================================================

// ─────────────────────────────────────────────
// ① 単発送信 (通知・承認依頼など)
// ─────────────────────────────────────────────
void SendNotification_OnClick()
{
    var mail = new Mail();
    mail.MailInfraName = "Notify";                     // appsettings の Mail.Infras の設定名(省略時は先頭)
    mail.AddTo(CustomerEmail.Value);                // ";"区切りで複数可。AddCc/AddBccも同様
    mail.Subject = "注文確認";
    mail.Body = "注文番号: " + OrderId.Value;
    mail.Source = this;                             // 送信履歴にこのレコードを紐づけ(省略可)

    var result = mail.Send();
    if (!result.IsSuccess) Toaster.Error("送信に失敗しました");
}

// ─────────────────────────────────────────────
// ② 帳票を添付して単発送信
// ─────────────────────────────────────────────
void SendQuotation_OnClick()
{
    var searchFile = new ModuleSearcher<TemplateFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Quotation");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        excel.OverWrite(this);                      // {{フィールド名}} を自モジュールの値で置換

        var mail = new Mail();
        mail.AddTo(CustomerEmail.Value);
        mail.Subject = "お見積り";
        mail.Body = "見積書を添付します。";
        mail.AddAttachment("見積.xlsx", excel);
        mail.Source = this;
        mail.Send();
    }
}

// ─────────────────────────────────────────────
// ③ BulkMailField をスクリプトから起動する
//    (確認ダイアログ・トーストは出ない。制御は呼び出し側)
//    宛先・テンプレート・配信停止はフィールドのデザイン設定に従う
// ─────────────────────────────────────────────
void SendCampaign_OnClick()
{
    var answer = MessageBox.Show("一斉送信します。よろしいですか？", "送信", "キャンセル");
    if (answer != "送信") return;

    var result = BulkMail1.Send();                  // BulkMail1 = 配置した BulkMailField
    if (result.IsSuccess)
    {
        Toaster.Success(result.SuccessCount + " 件送信しました");
    }
    else
    {
        Toaster.Error(result.SuccessCount + "/" + result.TotalCount + " 件送信。失敗: "
            + result.Failures[0].To + " (" + result.Failures[0].Error + ")");
    }
}
