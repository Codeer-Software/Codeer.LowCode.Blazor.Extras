# ApprovalRouteContractField (承認経路契約)

経路マスタ (経路) モジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを
宣言するフィールド。UI もデータも持たない (DB列不要)。エンジンは経路マスタに関与せず、
スクリプトの LoadRoute がこの契約で読む。

## 経路マスタの全体像

経路マスタは「ただのユーザー定義モジュール」3つ + 契約フィールド。管理画面も通常のローコードで作る
(専用フィールドは無い。経路の詳細に Steps の一覧を置き、ステップ行の中に Members の一覧を置く
ListInList 構成が定番。順序は StepNo 列で管理し、一覧のソート条件に StepNo を指定する):

- **経路** (例 ApprovalRoute): `RouteName` (経路名) / `Steps` (List→ステップ) / ApprovalRouteContractField
- **ステップ** (例 ApprovalRouteStep): `Route` (Link→経路) / `StepNo` / `StepName` / `StepType` /
  `CompletionPolicy` / `IsCommentRequiredOnReject` / `ReturnScope` / `Members` (List→承認者) / ApprovalRouteStepContractField
- **ステップ承認者** (例 ApprovalRouteStepMember): `Step` (Link→ステップ) / `ApproverUser` (Link→ユーザー) /
  `IsRequired` / ApprovalRouteStepMemberContractField

StepType / CompletionPolicy / ReturnScope はコード定義 enum の SelectField を推奨
(EnumName "ApprovalStepType" / "ApprovalCompletionPolicy" / "ApprovalReturnScope"。値候補と日本語表示が自動で出る)。

**シンプル構成 (1ステップ1人)**: ステップ契約の `Members` を空にして `ApproverUser`
(ステップ行に直付けのユーザーLink) を設定すると、承認者モジュールなしの2モジュール構成にできる
(経路 + ステップのみ。ステップ一覧は普通の列だけで管理できる)。役割を空にすると「使わない」宣言で、
LoadRoute はスクリプト組み立てと同じ既定値に倒す。詳細は ApprovalRouteStepContractField のドキュメント参照。

申請側は ApprovalFlowField の「経路マスタモジュール」に経路モジュールを指定し、
スクリプトの `LoadRoute(経路名)` で読む:

```csharp
ApprovalRouteData OnBuildRoute() => Approval.LoadRoute("経費ルート");
```

- 読んだ経路は AddStep / AddMember で加工してから申請してもよい
- 空値の既定はスクリプト組み立てと同じ (StepType=承認 / CompletionPolicy=必須メンバー /
  ReturnScope=申請者のみ / コメント必須=true / 必須=true)。承認者未選択の行は読み飛ばす
- マスタを後から変えても進行中のフローは影響を受けない (申請時スナップショットの既存仕様)

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする
- ステップモジュールは `Steps` 一覧の参照先として決まる (参照先に ApprovalRouteStepContractField が必要)

役割: `RouteName` (経路名 = LoadRoute の引数と照合するキー) / `Steps` (List→ステップ)。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalRouteContractFieldDesign" }
```
