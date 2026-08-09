一斉送信 (`BulkMail`) の宛先1件。`bulk.AddRecipient(address)` で作る。

| メソッド | 説明 |
|---|---|
| `AddCc(address)` / `AddBcc(address)` | Cc / Bcc を追加 |
| `SetVariable(name, value)` | テンプレートの `{name}` に差し込む値 |

```csharp
var bulk = new BulkMail();
bulk.Subject = "{Name} 様";
bulk.Body = "金額: {Amount} 円";
bulk.AddRecipient("a@example.com").SetVariable("Name", "田中").SetVariable("Amount", "12,345");
bulk.AddRecipient("b@example.com").SetVariable("Name", "鈴木").SetVariable("Amount", "8,000");
bulk.Send();
```
