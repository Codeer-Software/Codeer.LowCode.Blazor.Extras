`WebApiService` の戻り値。`StatusCode` (HTTP ステータス) と `JsonObject` (レスポンスボディの JSON) を持つ。
通信自体に失敗した場合もこの型で返り、`StatusCode` にエラーコードが入る。

`IsSuccess` (`StatusCode` が 200〜299) も持つ。`WebApiField.Execute()` の戻り値でもあり、そのときは呼び出せなかったら `StatusCode` が 0 で `ErrorMessage` に説明が入る (`WebApiService` では `ErrorMessage` は空のまま)。

`Body` (string) は応答の本文そのまま (無ければ空文字列)。`WebApiField.Execute()` の戻り値で入る (`WebApiService` では空のまま)。
`WebApiField` は成功した応答の本文 (空でなければ) を `ResultField` の JsonField に入れる。JsonField が受け付けない (外側の形が合わない・値の型が合わない・JSON として読めない) ときは、`StatusCode`・`JsonObject`・`Body` は応答のままで `ErrorMessage` にその理由が入る。
