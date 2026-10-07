# TagField - タグ入力

タグを入力・表示・検索するフィールドです。画面ではチップ (× で外せる) で表示します。タグは **タグ付けのテーブルに持ちます**。タグのマスタはありません:

```
tag_tests (タグを付けるモジュール)      tag_test_tags (タグ付け。タグを付けるモジュールごとに 1 つ)
├── id ◄──────────────────────────── owner_id   FK (レコードを消せば一緒に消える)
└── ...                               name       タグ名をそのまま持つ
                                      一意: (owner_id, name) 大文字小文字を区別しない   インデックス: name
```

- **タグ付け** (`TagLinkContractField` を置いたモジュール): 1 行 = あるレコードに付いたタグ 1 つ (タグを付けたレコードの Id + タグ名)
- **TagField**: タグ付けを子の一覧として持つ一覧フィールド。読み込み・保存 (レコードの保存に一緒に載る)・削除の連鎖・権限・編集履歴は本体の一覧と同じ仕組みで動く
- **TagInputField**: 保存しない入力欄 (取り込み画面などで「付けるタグ」を選ぶ欄)。入力部品と候補の設定は TagField と同じ

モジュールとテーブルは **タグのセットアップ** で作ります (下記)。

## 機能

- **入力**: 文字を打って Enter・「,」・「、」で確定 (設定でスペースでも)。× でタグを外す。入力欄の外へフォーカスが移ると、打ちかけの文字もタグになる (ウィンドウの切り替えでは確定しない)
- **日本語入力**: IME の変換中のキーは無視する。変換を確定する Enter でタグは作られず、確定後に「、」があれば分ける
- **保存**: タグの足し外しは何も書かない。レコードを保存したときに一緒に書く (1 つのトランザクション・レコードの書き込み権限)。保存せずにやめれば何も残らない
- **候補**: 打った文字を含むタグを、**よく使われている順**に最大 10 件表示する。サーバーで「タグ名でグループ化・件数の多い順・上位 20 件・部分一致」を 1 回問い合わせる (全件は読まない)。行の読み取り条件 (DataRead) はそのまま効く。↑↓ と Enter、またはクリックで選ぶ。付いているタグは出ない。打った `%` `_` は文字のまま
- **候補の出どころ** (`CandidateSource`): `TagRows` = このフィールドのタグ付けに付いているタグ (既定) / `Module` = 指定したモジュールの列 (列が TagField なら、そのタグ付けのタグ) / `Values` = 設定に書いた決まったタグ (問い合わせない)
- **決まったタグだけ**: `AllowNewTags = false` なら候補にあるタグだけ入力できる (足すときに確かめる。無いタグはエラー表示)
- **表記**: 同一判定は大文字小文字を区別しない (`DXPO` と `dxpo` は同じタグ)。同じレコードの中では先の表記を残す。新しく足すタグは、既に使われている表記があればいちばん使われている表記に寄せる
- **検索**: 検索レイアウトに置くと、選んだタグを「すべて含む / いずれかを含む」で組み合わせる。どちらも SQL で判定する (すべて含む = 本体の `ContainsAll`)。条件にはタグ名が入るので、URL に入れて共有しても後から付いたタグが反映される。タグは丸ごと一致 (「展示会」で「展示会2026」は当たらない)。付いていないタグはどのレコードにも合わない。タグを選んでいなければ絞らない
- **一覧の列**: 一覧レイアウトにも置ける。ページの行の分のタグをまとめて 1 回で読む (行ごとに問い合わせない)
- **閲覧 (読取専用)**: チップだけを表示
- **検証**: 必須 (`IsRequired`) = 1 つ以上のタグ
- **意味検索**: SemanticSearchField の `SourceFields` に入れると、タグ名を並べた行が文章に入る (保存時・再索引とも)

## タグのセットアップ

- デザイナ: メニュー Tools > タグのセットアップ
- CLI (headless): `<designer.exe> tag-setup "<projectDir>" --target <Module> [--field Tags | --no-field] [--link-name <Module>Tags]
  [--link-table <table>] [--owner-column owner_id] [--data-source <name>] [--ddl-out <path.sql>]`

生成内容:

1. タグ付けモジュール (既定 `<Module>Tags`: `Id` / `OwnerId` / `Name` + `TagLinkContractField`)
2. タグを付けるモジュールに TagField (既定 `Tags`)。同名の TagField が結び付きなしで、または同名の TagInputField が既にあれば、それを結び付けた TagField にする (設定は引き継ぐ)
3. DDL: テーブル (タグ名は NOT NULL・インデックスを張れる長さ)、(owner_id, name) の一意インデックス (大文字小文字を区別しない)、name のインデックス、レコードへの外部キー (削除は連鎖)

**冪等**: 既存のモジュールは作らず、結び付き済みの TagField は触らない。DDL は自動実行されない (結果画面の実行ボタン、または `--ddl-out` のファイルを実行する)。
画面への配置はデザイナで行う (TagField を詳細・一覧・検索のレイアウトに置く)。

大文字小文字を区別しない一意インデックスの書き方は DB ごとに違います: SQL Server・MySQL は既定の照合順序のまま、SQLite は列に `COLLATE NOCASE`、PostgreSQL はタグ名の列を `citext` (拡張。比較・LIKE も大文字小文字を区別しない)、Oracle は `UPPER(name)` の式インデックス。

## デザイナー設定プロパティ (TagField)

| プロパティ | デザイナ表示名 | 型 | 説明 |
|---|---|---|---|
| SearchCondition | 検索条件 | | タグ付けモジュールと、このレコードへの結び付き (`OwnerId.Value = Id.Value`)、並び (`Id` 昇順 = 付けた順)。セットアップが入れる |
| Placeholder | プレースホルダ | string | タグが無いときの入力欄のプレースホルダ |
| ConfirmOnSpace | スペースでも確定 | bool | スペースでもタグを確定する。既定 false (日本語のタグにはスペースが入ることがあるため) |
| AllowNewTags | 候補に無いタグも入れられる | bool | 既定 true。false なら候補にあるタグだけ |
| CandidateSource | 候補の出どころ | enum | `TagRows` (既定) / `Module` / `Values` |
| CandidateModuleName / CandidateFieldName | 候補のモジュール / フィールド | string | `Module` のとき。フィールドは TextField か TagField |
| CandidateValues | 決まったタグ (1 行 1 つ) | string | `Values` のとき。書いた順に出す |
| IsRequired | 必須 | bool | 1 つ以上のタグ |
| IsSimpleSearchParameter | 簡易検索パラメータ | bool | 検索欄に一致の選択を出さない |
| SearchMatchDefaultValue | 検索の一致の既定 | enum | `All` = すべて含む (既定) / `Any` = いずれかを含む。画面で切り替えられる |

ほかに `DisplayName` / `OnDataChanged` / `OnSearchDataChanged` が使えます。一覧フィールドの表示用の設定 (ページング・行の追加削除ボタン等) はタグには関係ないので出ません。候補の件数は設定にしません (表示 10 件・問い合わせ 20 件)。

TagInputField の設定は `DisplayName` / `Placeholder` / `ConfirmOnSpace` / `AllowNewTags` / 候補の設定 / `IsRequired` / `OnDataChanged` です (`CandidateSource` の既定は `Module`。自分のタグ付けを持たないので `TagRows` は使えない)。

## スクリプト API

TagField:

| メンバ | 説明 |
|---|---|
| `Tags` | 付いているタグ (`List<string>`、付けた順、読み取り専用) |
| `AddTag(string tag)` | タグを足す。同じタグは重ねない。区切りを含めば分けて足す |
| `RemoveTag(string tag)` | タグを外す |
| `SetTags(List<string> tags)` | タグを置き換える (外したタグを外し、無いタグを足す) |
| `HasTag(string tag)` | そのタグが付いているか |
| `LoadTags()` | このレコードのタグを読む (まだなら)。スクリプトの ModuleSearcher で読んだレコードは子の一覧を読まないので、`Tags` を見る前に呼ぶ。`AddTag` / `RemoveTag` / `SetTags` は自分で読む |
| `SearchTags` | 検索条件で選んでいるタグ (get / set) |
| `SearchMatch` | 検索の一致 (`TagSearchMatch.All` / `TagSearchMatch.Any`、get / set) |

TagInputField: `Tags` / `AddTag` / `RemoveTag` / `SetTags` / `HasTag` / `Clear()`。

タグの変更は、ほかのフィールドと同じくレコードを保存したときに書かれます。

```csharp
// 登録する人全員に、入力欄 (TagInputField) で選んだタグを足す
void Register_OnClick()
{
    foreach (var person in People.Rows)
    {
        foreach (var tag in NewTags.Tags) person.Tags.AddTag(tag);
    }
}
```

## 配置の注意

- 一括ダウンロード / 一括更新 (BulkFileTransfer) はタグを扱いません (タグはレコードの列ではないため)
- タグに色・説明・並び順を持たせたい、複数のモジュールでタグの一覧を共有したい場合は、タグ名で引く別のモジュールを足してください (外部キーでは縛りません)

## CSS カスタマイズ

チップは `.tag-chip`、入力欄全体は `.tag-editor`、閲覧表示は `.tag-view` です。色や角丸を変えたいときは app.css で上書きしてください。

```css
.tag-chip { background: #eef3ff; border-color: #c7d7ff; }
```
