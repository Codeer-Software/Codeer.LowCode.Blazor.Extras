`MailService.CreateMessage()` で生成するメールメッセージ。各メソッドは自身を返す。
アドレス (`AddTo` / `AddCc` / `AddBcc`) は `;` 区切りで複数指定できる。
`SetBody` はテキスト本文、`SetHtmlBody` は HTML 本文 (後に呼んだ方が有効)。
組み立てたら `MailService.Send(message)` で送信する。
