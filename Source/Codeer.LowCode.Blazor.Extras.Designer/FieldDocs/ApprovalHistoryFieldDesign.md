# ApprovalHistoryField (承認履歴)

同一モジュール上の ApprovalFlowField を指定し、その承認履歴だけを表示するフィールド。
ApprovalFlowField の標準 UI から履歴の位置を切り離して、レイアウト上の好きな場所に置くために使う。

## Design

### 考え方

- 状態・データは ApprovalFlowField が単一保持する。このフィールドは表示部品で、DB列もデータ送信も持たない。
- 使うときは ApprovalFlowField 側の「履歴を表示」を OFF にする (両方 ON だと二重表示になるだけで害はない)。
- ボタンやコメント入力も自由配置したい場合は、ApprovalFlowField の「アクションボタンを表示」を OFF にして
  ButtonField ＋ スクリプト API で組む (履歴 = このフィールド、操作 = ButtonField という分担)。

### プロパティ

| プロパティ | 説明 |
|---|---|
| 承認フィールド | 表示元の ApprovalFlowField のフィールド名 (既定 "Approval")。同一モジュール上にあること |

### JSON 例

```json
{
  "Name": "ApprovalHistory",
  "ApprovalFieldName": "Approval",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalHistoryFieldDesign"
}
```

## 表示

- 履歴が1件以上あるときだけ表示される (未申請時は何も出ない)。
- 表示内容は ApprovalFlowField 組み込みの履歴表示と同じ (日時・アクション・実行者・コメント)。
- 承認・取り下げ等のアクション成功時は自動で追従して再描画される。
