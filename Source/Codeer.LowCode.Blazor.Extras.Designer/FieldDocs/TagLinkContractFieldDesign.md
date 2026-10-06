# TagLinkContractField (タグ付け契約)

タグ付けモジュール (タグを付けるモジュール 1 つにつき 1 つ。1 行 = あるレコードにあるタグが付いていること) に 1 つ置き、「役割 → 自モジュールのフィールド名」を宣言するフィールド。
UI もデータも持たない (DB 列不要)。CLB の多対多の形 (本体 ← IdField / LinkField → マスタ) そのもので、TagField はこのモジュールを子の一覧として持つ。

モジュールは手で作らず **タグのセットアップ** (Tools > タグのセットアップ / CLI `tag-setup`) で生成する。詳しくは TagFieldDesign を参照。

## Design

| 役割 (表示名) | 型 | 内容 | 必須 |
|---|---|---|---|
| OwnerId (タグを付けたレコードの Id) | Id | タグを付けたレコードの Id (`IsManualInput: false`。レコードの保存で CLB が入れる) | ○ |
| Tag (タグのマスタへのリンク) | Link | 付けたタグ。リンクの先は TagContractField を置いたマスタ、表示文字列はマスタのタグ名 (`DisplayTextVariable = "Name.Value"`) | ○ |

- 一覧レイアウト (`""`) の DataOnlyFields に OwnerId と Tag を入れる (TagField が行を読むため)
- テーブルには (OwnerId, Tag) の一意インデックスと Tag のインデックス、両方の外部キーを張る (セットアップの DDL が張る)

## 設計チェック

| コード | 内容 |
|---|---|
| `TagLinkContractFieldDesign:1` | 役割のフィールドの型が違う (OwnerId は Id、Tag は Link) |
| `TagLinkContractFieldDesign:2` | Tag のリンクの先に TagContractField が無い |
| `TagLinkContractFieldDesign:3` | Tag のリンクの表示文字列がマスタのタグ名でない |

役割のフィールドが自モジュールに無い・必須の役割が空・同じモジュールに複数置いた、もエラーになる。
