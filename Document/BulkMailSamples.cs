//=====================================================================
// BulkMail サンプル (CLBスクリプト)
// メール送信機能仕様.md の補足。宛先ソースは Rows / Searcher / AddRecipient の
// どれか1つだけ設定する(複数設定・未設定は失敗が返る)。
//=====================================================================

// ─────────────────────────────────────────────
// ① 画面の一覧の行に一斉送信(SFAの基本形)
//    宛先=行のEmailフィールド、OptOut=trueの行はスキップ(配信停止)
//    差し込み {FieldName} は行の値の表示文字列(Selectは表示名、数値/日付はデザインの書式)
// ─────────────────────────────────────────────
void SendCampaign_OnClick()
{
    var bulk = new BulkMail();
    bulk.Sender = "Campaign";                       // appsettings の Mail.Senders の名前(省略時は先頭)
    bulk.Subject = "{Name} 様へ 8月のご案内";
    bulk.Body = "{Name} 様\n\nいつもありがとうございます。\n担当: {SalesName}\n";
    bulk.ToField = "Email";
    bulk.ExcludeField = "OptOut";
    bulk.Rows = CustomerList.Rows;                  // 画面に出ている一覧の行
    bulk.Source = this;                             // 送信履歴にこのレコード(キャンペーン)を紐づけ

    var result = bulk.Send();
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

// ─────────────────────────────────────────────
// ② 検索条件で一斉送信(サーバー解決・大量向け)
//    宛先一覧はクライアントに載らない。{RecordUrl}(レコード詳細へのリンク。
//    appsettings の Mail.AppBaseUrl が必要)が使えるのはこの経路だけ
// ─────────────────────────────────────────────
void SendToGoldMembers_OnClick()
{
    var searcher = new ModuleSearcher<Customer>();
    searcher.AddEquals(e => e.Rank.Value, "Gold");

    var bulk = new BulkMail();
    bulk.Sender = "Campaign";
    bulk.Subject = "ゴールド会員限定のご案内";
    bulk.Body = "{Name} 様\n\n会員ページはこちら:\n{RecordUrl}\n";
    bulk.ToField = "Email";
    bulk.ExcludeField = "OptOut";
    bulk.Searcher = searcher;

    var result = bulk.Send();
    Toaster.Success(result.SuccessCount + "/" + result.TotalCount + " 件送信しました");
}

// ─────────────────────────────────────────────
// ③ 宛先を自分で組み立てる(差し込み値も自由に)
//    この経路の差し込み値は自分で文字列化する(①②のような自動整形はない)
// ─────────────────────────────────────────────
void SendReminders_OnClick()
{
    var bulk = new BulkMail();
    bulk.Sender = "Notify";
    bulk.Subject = "【リマインド】{TaskName} の期限が近づいています";
    bulk.IsBodyHtml = true;                         // HTML本文では差し込み値が自動エスケープされる
    bulk.Body = "<p>{Name} さん</p><p>期限: <b>{DueDate}</b></p>";

    foreach (var row in TaskList.Rows)
    {
        if (row.DueDate.Value > DateTime.Today.AddDays(3)) continue;
        bulk.AddRecipient(row.AssigneeEmail.Value)
            .SetVariable("Name", row.AssigneeName.Value)
            .SetVariable("TaskName", row.Title.Value)
            .SetVariable("DueDate", row.DueDate.Value.ToString("yyyy/MM/dd"));
    }

    var result = bulk.Send();
    Toaster.Success(result.SuccessCount + " 件送信しました");
}

// ─────────────────────────────────────────────
// ④ 送信前に差し込み結果を確認(プレビュー)
// ─────────────────────────────────────────────
void Preview_OnClick()
{
    var bulk = new BulkMail();
    bulk.Subject = "{Name} 様へ 8月のご案内";
    bulk.Body = "{Name} 様\n担当: {SalesName}";

    var first = CustomerList.Rows[0];
    MessageBox.Show(bulk.PreviewSubject(first) + "\n\n" + bulk.Preview(first));
}

// ─────────────────────────────────────────────
// (参考) 単発送信は Mail を使う
// ─────────────────────────────────────────────
void SendNotification_OnClick()
{
    var mail = new Mail();
    mail.Sender = "Notify";
    mail.AddTo(CustomerEmail.Value);                // ";"区切りで複数可。AddCc/AddBccも同様
    mail.Subject = "注文確認";
    mail.Body = "注文番号: " + OrderId.Value;
    mail.Source = this;

    var result = mail.Send();
    if (!result.IsSuccess) Toaster.Error("送信に失敗しました");
}
