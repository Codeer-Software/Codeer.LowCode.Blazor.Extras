# TagLinkContractField (タグ付け契約)

タグ付けモジュール (タグを付けるモジュール 1 つにつき 1 つ。1 行 = あるレコードに付いたタグ 1 つ) に 1 つ置き、「役割 → 自モジュールのフィールド名」を宣言するフィールド。
UI もデータも持たない (DB 列不要)。行はタグを付けたレコードの Id とタグ名だけを持つ (タグのマスタは無い)。TagField はこのモジュールを子の一覧として持つ。

モジュールは手で作らず **タグのセットアップ** (Tools > タグのセットアップ / CLI `tag-setup`) で生成する。詳しくは TagFieldDesign を参照。

## Design

| 役割 (表示名) | 型 | 内容 | 必須 |
|---|---|---|---|
| OwnerId (タグを付けたレコードの Id) | Id | タグを付けたレコードの Id (`IsManualInput: false`。レコードの保存で CLB が入れる) | ○ |
| TagName (タグ名) | Text | タグ名。既定のフィールド名は `Name` (役割名を Name にするとフィールド名と重なるので TagName) | ○ |

- 一覧レイアウト (`""`) の DataOnlyFields に OwnerId とタグ名を入れる (TagField が行を読むため)
- テーブルには (OwnerId, タグ名) の一意インデックス・タグ名のインデックス・レコードへの外部キー (削除は連鎖) を張る (セットアップの DDL が張る)
- タグ名の TextField には MaxLength 200 (TagField.MaxTagLength) を入れる (入力欄・DDL の列の長さと同じ)

## 設計チェック

| コード | 内容 |
|---|---|
| `TagLinkContractFieldDesign:1` | 役割のフィールドの型が違う (OwnerId は Id、TagName は Text) |

役割のフィールドが自モジュールに無い・必須の役割が空・同じモジュールに複数置いた、もエラーになる。
