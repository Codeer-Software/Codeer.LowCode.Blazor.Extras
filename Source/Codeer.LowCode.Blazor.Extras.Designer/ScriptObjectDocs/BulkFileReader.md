スクリプトからの一括ファイル取込。`new BulkFileReader<モジュール名>()` で取込先モジュールを指定して使う
(生成したインスタンスは必ず一度変数に受けてからメソッドを呼ぶ。`new BulkFileReader<注文>().Read()` のような直接チェーンは不可)。

- `Read()` … ファイル選択ダイアログを開き、選択されたファイルをサーバーで解析する。
  戻り値は「ファイルを選択して解析したか」(キャンセルは `false`)。DB には書き込まない
- `Items` … 解析済みのモジュール列 (ファイルの行順)。書き込みは行を加工したうえで `this.Submit(Items)` で行う
- `HasError` / `ErrorCount` … 解釈できなかったセル (変換表に無いコード・書式不一致・型変換不能) があったか。
  スクリプトで見るのは基本ここだけ (セル単位の細かいハンドリングは書かない)
- `ErrorText` … エラー詳細 (行番号・列・内容の一覧テキスト)。コンソールに出すなら `Logger.Error(reader.ErrorText)`
- `DownloadErrorText()` … エラー詳細を `{モジュール名}_errors.txt` としてダウンロードしてユーザーに渡す
- 解析はモジュールの `CsvFileFormatField` / `FileColumnMappingField` の定義に従う
  (CSV/固定長/xlsx 自動判定、外部列名の対応付け、コード変換、日付・数値の書式)。どちらも未定義なら内部名ヘッダの xlsx/CSV
- ファイル形式・列対応の定義はスクリプト変換と常に併用する。排他なのは「同じ列」への二重のコード変換だけ
  (宣言的な `ConversionModule` とスクリプト変換は列ごとにどちらか一方)
- 解釈できなかったセルは値未設定のまま該当フィールドにもエラーが載る。
  行自体は捨てられないので、不要行はスクリプト側で除外する

このオブジェクトを使うアプリはサーバー側の対応実装 (`BulkFileTransfer.ParseFileAsync` への移譲) が必要。エンドポイント URL はアプリの初期化 (ServiceInitializer の `BulkFileReader.ParseFileEndPoint`) で設定する (テンプレートは設定済み)。

## 取込ページの作り方

取込ボタンは **DB に結びつかない UI 専用モジュール (DbTable なし・Id フィールドなし) に置く**。
書き込みは `this.Submit(取込リスト)` で行う。呼び出し元 (this) が Id フィールドを持たなければ
渡したリストだけがトランザクション一括書込され、Id フィールドを持つモジュールから呼ぶと
this 自身の新規行 (空行) も一緒に書き込まれてしまう。

```csharp
// 取込 → スクリプトで変換・検証 → 一括書き込み (取込パターンの基本形。UI専用モジュールに置く)
void Import_OnClick()
{
    var reader = new BulkFileReader<注文>();
    if (!reader.Read()) { return; }   // キャンセル

    // 解釈できないセルがあればユーザーに詳細テキストを渡して中止 (取り込むかどうかは方針次第)
    if (reader.HasError)
    {
        reader.DownloadErrorText();
        return;
    }
    var list = reader.Items;

    // 変換表は行ループの外で一度だけ辞書化する (行ループ内で ModuleSearcher を使わない)
    var mapSearcher = new ModuleSearcher<コード変換表>();
    var maps = mapSearcher.Execute();
    var dic = new Dictionary<string, string>();
    foreach (var mp in maps) { dic[mp.外部コード.Value] = mp.内部コード.Value; }

    var ok = new List<注文>();
    foreach (var m in list)
    {
        var code = m.コード.Value;
        if (code == null || code == "") { continue; }          // 不要行 (トレーラ等) はスキップ
        if (dic.ContainsKey(code)) { m.コード.Value = dic[code]; }
        else { m.コード.SetError("コードが変換表にありません"); }
        ok.Add(m);
    }

    // トランザクション一括書き込み。this (UI専用モジュール) は書き込まれず、リストだけが書き込まれる
    if (ok.Count == 0) { return; }
    this.Submit(ok);
}
```

## 取込の行ロジックのパターン集

いずれも「`Read()` の後、`this.Submit()` の前」のループに書く (`list` = `reader.Items`)。

### 必須チェック・検証エラー

エラーを見つけたら `SetError` でフィールドに載せる。取り込むかどうかはスクリプトの方針次第
(エラー行を除外して残りを取り込む / 1件でもエラーなら全体を中止してメッセージ表示、など)。

```csharp
int errorCount = 0;
foreach (var m in list)
{
    if (m.品名.Value == null || m.品名.Value == "") { m.品名.SetError("品名は必須です"); errorCount = errorCount + 1; }
    if (m.数量.Value == null || m.数量.Value <= 0) { m.数量.SetError("数量は1以上"); errorCount = errorCount + 1; }
}
if (errorCount > 0)
{
    Logger.Error(errorCount + " 件のエラーがあるため取り込みません");
    return;
}
```

### 演算 (計算列・二列結合)

```csharp
foreach (var m in list)
{
    m.金額.Value = m.単価.Value * m.数量.Value;
    m.表示名.Value = m.コード.Value + ":" + m.品名.Value;
}
```

### ファイル内の重複チェック

```csharp
var seen = new HashSet<string>();
foreach (var m in list)
{
    var key = m.コード.Value;
    if (seen.Contains(key)) { m.コード.SetError("ファイル内で重複しています"); }
    seen.Add(key);
}
```

### 取込先 (DB) との重複チェック・既存行の更新

既存データも行ループの外で一度だけ取得して辞書化する。既存行を更新したい場合は、
ファイルの値を既存モジュールへ写して既存モジュールの方を Submit する
(Read() が返す行は Id を持たない新規なので、そのまま Submit すると常に INSERT になる)。

```csharp
var existingSearcher = new ModuleSearcher<注文>();
var existing = existingSearcher.Execute();
var byCode = new Dictionary<string, 注文>();
foreach (var e in existing) { byCode[e.コード.Value] = e; }

var writes = new List<注文>();
foreach (var m in list)
{
    if (byCode.ContainsKey(m.コード.Value))
    {
        var current = byCode[m.コード.Value];   // 既存行を更新
        current.数量.Value = m.数量.Value;
        writes.Add(current);
    }
    else
    {
        writes.Add(m);                          // 新規行
    }
}
this.Submit(writes);
```

### マスタ引き当て (1つのコードから複数列を埋める)

変換表の行モジュールごと辞書化すれば、1コードから複数の値を引ける。

```csharp
var masterSearcher = new ModuleSearcher<商品マスタ>();
var masters = masterSearcher.Execute();
var byCode = new Dictionary<string, 商品マスタ>();
foreach (var e in masters) { byCode[e.商品コード.Value] = e; }

foreach (var m in list)
{
    if (byCode.ContainsKey(m.商品コード.Value))
    {
        var master = byCode[m.商品コード.Value];
        m.品名.Value = master.品名.Value;
        m.単価.Value = master.単価.Value;
    }
    else { m.商品コード.SetError("マスタにありません"); }
}
```

## 性能の目安

1000行規模なら実用域 (解析+モジュール化が1〜2秒、行ロジックのループは1秒未満)。
支配項は書き込み (`Submit`) の DB INSERT で、1000行で数秒〜10秒程度。
1万行を超える規模を扱う場合は、処理中の表示 (ボタンの二度押し防止や件数表示) や分割取込を検討する。
