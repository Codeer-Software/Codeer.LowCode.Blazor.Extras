## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.RichTextFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

書式付きテキスト（太字・箇条書き・リンク等）をリッチテキストエディタで入力し、HTML 文字列として DB カラムに保存する値フィールド。`ValueFieldDesignBase` を継承し、値の型は `string`（HTML マークアップ）。

> 表示専用で HTML を出すだけなら [MarkupStringField](MarkupStringField.md)（core）。ユーザーに書式付きで**入力**させたいときに RichTextField を使う。

### C# クラス定義 (真実の源)

このフィールドは外部ライブラリ `Codeer.LowCode.Blazor.Extras` で定義されている。`ValueFieldDesignBase` を継承する。

### プロパティ

> 共通プロパティ（Name, IgnoreModification, OnValidateInput）は [_FieldCommon.md](_FieldCommon.md) を参照。
> 値フィールド共通プロパティ（DisplayName, IsRequired, OnDataChanged）は [_FieldCommon.md](_FieldCommon.md) の「ValueFieldDesignBase」を参照。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DbColumn` | string | `""` | HTML 文字列を保存する DB カラム名。 |
| `MentionListField` | string | `""` | メンションの候補の行を持つ一覧フィールド (同じモジュールの ListField) の名前。**空でなければメンションを使う** (空 = 使わない。有効・無効の設定は別に無い)。下の「メンション」。 |
| `MentionDisplayNameField` | string | `""` | 一覧が読むモジュールのフィールド名。候補の表示名として出す (メンションを使うなら必須)。 |
| `MentionDisplayNameSubField` | string | `""` | 一覧が読むモジュールのフィールド名。表示名の横に補助で小さく出す (メールアドレス・所属など)。絞り込みにも使い、表示名が空の人はこの値で出す。空なら出さない。 |

### メンション

本文の中で `@` を打つと候補が出て、選んだ人を本文に埋め込める (「全員」も選べる)。本文に埋め込むだけで、通知やメンションの記録は無い。メンション専用のサーバーの処理もホストの結線も無い。

候補は、同じモジュールに置いた ListField の検索条件で読める行。誰が候補になるかは、その一覧の検索条件と、一覧が読むモジュールの権限 (`@` を打っている人の権限) で決まる。

#### 一覧の置き方

1. RichTextField と同じモジュールの**全体設定の `Fields` に ListField を置く。レイアウトには置かない** (画面に一覧は出ない)。
2. 一覧の `SearchCondition.ModuleName` に、ユーザーのモジュール (`AppSettings.CurrentUserModuleDesignName`) か、下の「一般向けのユーザーモジュール」を指定する。
3. 一覧の検索条件に、候補を絞る条件を書く (同じ部署の人だけ・有効な人だけ、など。編集中のレコードの値も `CurrentUser.〜` も書ける)。停止中の人などを自動で外す処理は無いので、外すなら条件に書く。並び順が候補の並び順になる。
4. **一覧の行のレイアウト (`LayoutName` の ListLayout) に、表示名の項目 (とサブ表示名の項目) を置く。** 一覧は行のレイアウトに置いた項目だけを読む (一覧自体をレイアウトに置かなくても、行のレイアウトは要る)。
5. 一覧は全部の行を持つ設定にする (`SearchCondition.LimitCount` を付けないか、`IsInMemoryPaging`)。
6. 一覧は行を編集できない設定にする (`CanCreate` / `CanUpdate` / `CanDelete` / `DeleteTogether` をすべて false)。列モード (`DbColumn`) にしない。SearchField と結び付けない。
7. RichTextField の `MentionListField` に一覧の名前、`MentionDisplayNameField` に表示名の項目の名前を入れる。

> **手順 1 と 4 は設計チェックでは分からない。** 守らなくても指摘は出ず、「候補が出ない」だけになる。候補が出ないときは、まずこの 2 つを確かめる。

- 一覧は ListField (`ListFieldDesign`) だけ。DetailList と TileList は指定できない。
- 候補は 500 人程度までを目安にする (最初の `@` で全員分を読む)。それより多ければ一覧の検索条件で絞る。
- 編集中のレコードの Id を使う条件の一覧は、未保存 (新規作成) の画面では候補が出ない。`CurrentUser.〜` だけの条件なら出る。

#### 一覧が読むモジュール

本文に埋める Id はユーザーの Id なので、一覧が読むのは次のどちらか。

- ユーザーのモジュール (`AppSettings.CurrentUserModuleDesignName`)。`@` を打つ人がユーザーのモジュールを読める設計のとき。
- **一般向けのユーザーモジュール** (ユーザーのモジュールを一般の利用者が読めない設計のとき、別に作る)。次の 3 つを守る (守らないと設計チェックが指摘する)。
  - ユーザーのモジュールと同じデータソース・同じ表・同じ Id の列。
  - 書き込めない (`CanCreate` / `CanUpdate` / `CanDelete` が false)。
  - ユーザーのモジュールが論理削除なら、同じ論理削除のフィールド (同じ名前・型・列) を置く。

一般向けのユーザーモジュールに置く項目は、利用者に見せてよいものだけにする (表示名・メール・部署など)。役割のような項目は置かない。これは設計チェックでは見ないので、設計者が決める。

#### 設計チェック

どれも `MentionListField` が空でないときだけ見る (空なら、ほかの 2 つに値が入っていても指摘しない)。

| コード | 内容 |
|---|---|
| `RichTextFieldDesign:1` | `MentionListField` の一覧が、同じモジュールの ListField でないか、列モード (`DbColumn` あり) か、SearchField と結び付いている |
| `RichTextFieldDesign:2` | 一覧が全部の行を持たない (件数の上限があり、メモリ内ページングでない) |
| `RichTextFieldDesign:3` | 一覧が行を編集できるか、親と一緒に消す設定 (`CanCreate` / `CanUpdate` / `CanDelete` / `DeleteTogether` のどれか) |
| `RichTextFieldDesign:4` | 一覧が読むモジュールが、ユーザーのモジュールでも、同じデータソース・表・Id の列のモジュールでもない (ユーザーのモジュールが無いときを含む) |
| `RichTextFieldDesign:5` | `MentionDisplayNameField` が空か、表示名・サブ表示名の項目が一覧の読むモジュールに無い (行のレイアウトに置かれているかは見ない) |
| `RichTextFieldDesign:6` | 一般向けのユーザーモジュールが書き込める |
| `RichTextFieldDesign:7` | ユーザーのモジュールが論理削除なのに、一般向けのユーザーモジュールに同じ論理削除の設定が無い |

#### JSON例 (メンションあり)

同じモジュールの `Fields` に置く一覧と RichTextField (抜粋)。一覧 `MentionUsers` はどのレイアウトにも置かない。`AppUser` の ListLayout `""` に `Name` と `Email` を置いておく。

```json
{
  "SearchCondition": { "ModuleName": "AppUser", "LimitCount": null },
  "LayoutName": "",
  "CanCreate": false,
  "CanUpdate": false,
  "CanDelete": false,
  "DeleteTogether": false,
  "Name": "MentionUsers",
  "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.ListFieldDesign"
},
{
  "DbColumn": "content",
  "MentionListField": "MentionUsers",
  "MentionDisplayNameField": "Name",
  "MentionDisplayNameSubField": "Email",
  "Name": "Content",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.RichTextFieldDesign"
}
```

### 必要な DB 構成

HTML 文字列はそれなりに長くなりうるため、長さ無制限のテキスト型カラムを用意する。

```sql
content TEXT NULL
```

### JSON例

```json
{
  "DbColumn": "content",
  "MentionListField": "",
  "MentionDisplayNameField": "",
  "MentionDisplayNameSubField": "",
  "DisplayName": "本文",
  "IsRequired": false,
  "OnDataChanged": "",
  "Name": "Content",
  "IgnoreModification": false,
  "OnValidateInput": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.RichTextFieldDesign"
}
```

> 既定状態は [../../Defaults/RichTextFieldDesign.json](../../Defaults/RichTextFieldDesign.json) を参照。

### ランタイム動作

- 編集モードではリッチテキストエディタを表示し、入力結果を HTML 文字列として保持する。
- 表示するとき・エディタに入れるとき・貼り付けるときに HTML を消毒する。スクリプトが動くもの (script・イベント属性・javascript: の URL)・フォームの部品・class・位置や重なりを変える style (position・z-index 等) を落とす。文字の書式・色・揃え・表・画像・リンクは残る。
- `IsRequired: true` のとき値が空（または空白のみ）だとバリデーションエラーになる。

## Script

### プロパティ

| プロパティ | 型 | 読み書き | 説明 |
|---|---|---|---|
| `Value` | string | 読み書き | 入力された HTML 文字列。 |
| `MentionedIds` | string[] | 読み取り | 今の値に入っているメンションのユーザー Id (重複を除き、本文の順)。 |
| `MentionsAll` | bool | 読み取り | 今の値に「全員」のメンションが入っているか。 |

どちらも値 (`Value`) の HTML から取り出すので、保存済みかどうかに関係なく今の内容を返す。保存のときのスクリプトから自前の通知を作る、といった使い方ができる (Id はブラウザから送られた HTML の中身なので、通知に使うなら実在するユーザーか確かめる)。

> 共通スクリプトプロパティ（Color, BackgroundColor, IsEnabled, IsVisible, IsViewOnly 等）は [_FieldCommon.md](_FieldCommon.md) / [_ScriptApi.md](_ScriptApi.md) を参照。
