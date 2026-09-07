void Chat_OnReplyReceived(string replyHtml)
{
    LastReplyLabel.Text = "最後の返事 (HTML " + replyHtml.Length + " 文字) を受け取りました";
}
