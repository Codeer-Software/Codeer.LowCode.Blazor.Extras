スクリプトからの一括ファイル取込。`new BulkFileReader<モジュール名>()` で取込先モジュールを指定して使う
(生成したインスタンスは必ず一度変数に受けてからメソッドを呼ぶ。`new BulkFileReader<注文>().Read()` のような直接チェーンは不可)。

- `Read()` … ファイル選択ダイアログを開き、選択されたファイルをサーバーで解析する。
  戻り値は「ファイルを選択して解析したか」(キャンセルは `false`)。DB には書き込まない
- `Items` … 解析済みのモジュールデータ列 (ファイルの行順)。Module 実体化を行わないため大量行でも軽い。
  フィールドの値の参照・書換 (`m.コード.Value`) は型付きでそのまま書ける (補完・リネーム追従も効く)。
  書き込みは行を加工したうえで `BulkFileTransferService.Submit(Items)` で行う
- `ToModules()` … Items をまとめて Module 化して返す (モジュールの機能が必要な大幅加工用。ModuleSearcher の Execute/ExecuteRaw と同じ使い分け)。
  実体化コストがかかる (0.3ms/行 程度) ため、参照や値の書換だけなら Items をそのまま使う
- `HasError` / `ErrorCount` … 解釈できなかったセル (変換表に無いコード・書式不一致・型変換不能) があったか。
  スクリプトで見るのは基本ここだけ (セル単位の細かいハンドリングは書かない)
- `ErrorText` … エラー詳細 (行番号・列・内容の一覧テキスト)。コンソールに出すなら `Logger.Error(reader.ErrorText)`
- `DownloadErrorText()` … エラー詳細を `{モジュール名}_errors.txt` としてダウンロードしてユーザーに渡す
- 解析はモジュールの `CsvFileFormatField` / `FileColumnMappingField` の定義に従う
  (CSV/固定長/xlsx 自動判定、外部列名の対応付け、コード変換、日付・数値の書式)。どちらも未定義なら内部名ヘッダの xlsx/CSV
- ファイル形式・列対応の定義はスクリプト変換と常に併用する。排他なのは「同じ列」への二重のコード変換だけ
  (宣言的な `ConversionModule` とスクリプト変換は列ごとにどちらか一方)
- 解釈できなかったセルは値未設定になる (詳細は `ErrorText`)。
  行自体は捨てられないので、不要行はスクリプト側で除外する

このオブジェクトを使うアプリはサーバー側の対応実装 (`BulkFileTransfer.ParseFileAsync` への移譲) が必要。エンドポイント URL はアプリの初期化 (ServiceInitializer の `BulkFileReader.ParseFileEndPoint`) で設定する (テンプレートは設定済み)。

## 取込ページの作り方

取込ボタンは **DB に結びつかない UI 専用モジュール (DbTable なし・Id フィールドなし) に置く**。
書き込みは `BulkFileTransferService.Submit(取込リスト)` で行う。リストだけが1トランザクションで書き込まれ
(this は書き込まれない)、新規行がまとまっていれば multi-row INSERT で高速に挿入される。
Id の一致で追加/更新を判定する (Id なし・テンポラリ Id は新規)。保存した新規行の採番 Id は返らない (投げ切り)。

取込行は UI に表示されないため、検証エラーは `SetError` でなくメッセージを集めてユーザーに返す
(件数と内容を `Logger.Error` やトーストで示す / 独自のエラーテキストを組み立てる)。

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

    var ok = new List<注文.Data>();
    var errorCount = 0;
    foreach (var m in list)
    {
        var code = m.コード.Value;
        if (code == null || code == "") { continue; }          // 不要行 (トレーラ等) はスキップ
        if (dic.ContainsKey(code)) { m.コード.Value = dic[code]; }
        else { errorCount = errorCount + 1; continue; }        // 変換できない行は除外して件数を数える
        ok.Add(m);
    }
    if (errorCount > 0) { Logger.Error(errorCount + " 件のコードが変換表にありません"); return; }

    // トランザクション一括書き込み (新規行は multi-row INSERT でまとめて挿入される)
    if (ok.Count == 0) { return; }
    BulkFileTransferService.Submit(ok);
}
```

## 取込の行ロジックのパターン集

いずれも「`Read()` の後、`BulkFileTransferService.Submit()` の前」のループに書く (`list` = `reader.Items`)。

### 必須チェック・検証エラー

取込行は UI に表示されないため、エラーはメッセージとして集めてユーザーに返す。
取り込むかどうかはスクリプトの方針次第 (エラー行を除外して残りを取り込む / 1件でもエラーなら全体を中止、など)。

```csharp
var errors = new List<string>();
var row = 1;
foreach (var m in list)
{
    row = row + 1;
    if (m.品名.Value == null || m.品名.Value == "") { errors.Add("Row " + row + ": 品名は必須です"); }
    if (m.数量.Value == null || m.数量.Value <= 0) { errors.Add("Row " + row + ": 数量は1以上"); }
}
if (errors.Count > 0)
{
    Logger.Error(errors.Count + " 件のエラーがあるため取り込みません\n" + string.Join("\n", errors));
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
var duplicated = new List<string>();
foreach (var m in list)
{
    var key = m.コード.Value;
    if (seen.Contains(key)) { duplicated.Add(key); }
    seen.Add(key);
}
if (duplicated.Count > 0) { Logger.Error("ファイル内で重複: " + string.Join(", ", duplicated)); return; }
```

### 取込先 (DB) との重複チェック・既存行の更新

既存データも行ループの外で一度だけ取得して辞書化する。既存行を更新したい場合は、
ファイルの値を既存モジュールへ写して既存モジュールの方を Submit する
(Read() が返す行は Id を持たない新規なので INSERT、既存モジュールは Id を持つので UPDATE になる)。
既存行 (Module) と混ぜて1つのリストにするため、ファイル行は `ToModules()` で Module 化して型を揃える。

```csharp
var existingSearcher = new ModuleSearcher<注文>();
var existing = existingSearcher.Execute();
var byCode = new Dictionary<string, 注文>();
foreach (var e in existing) { byCode[e.コード.Value] = e; }

var writes = new List<注文>();
foreach (var m in reader.ToModules())
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
BulkFileTransferService.Submit(writes);
```

### マスタ引き当て (1つのコードから複数列を埋める)

変換表の行モジュールごと辞書化すれば、1コードから複数の値を引ける。

```csharp
var masterSearcher = new ModuleSearcher<商品マスタ>();
var masters = masterSearcher.Execute();
var byCode = new Dictionary<string, 商品マスタ>();
foreach (var e in masters) { byCode[e.商品コード.Value] = e; }

var notFound = new List<string>();
foreach (var m in list)
{
    if (byCode.ContainsKey(m.商品コード.Value))
    {
        var master = byCode[m.商品コード.Value];
        m.品名.Value = master.品名.Value;
        m.単価.Value = master.単価.Value;
    }
    else { notFound.Add(m.商品コード.Value); }
}
if (notFound.Count > 0) { Logger.Error("マスタにありません: " + string.Join(", ", notFound)); return; }
```

## 性能の目安

`Items` は ModuleData 列 (Module 実体化なし) のため、1万行規模でも解析〜行ロジックまで数秒に収まる。
書き込みは `BulkFileTransferService.Submit` なら新規行が multi-row INSERT でまとまるため1万行でも数秒程度
(手入力Idモジュールは行ごとの存在チェックが残るため1000行で数秒)。
`ToModules()` での Module 化は 0.3ms/行 程度 (1万行で約3秒) なので、必要なとき (モジュールの機能を使う大幅加工) だけにする。
1万行を大きく超える規模を扱う場合は、処理中の表示 (ボタンの二度押し防止や件数表示) を検討する。
