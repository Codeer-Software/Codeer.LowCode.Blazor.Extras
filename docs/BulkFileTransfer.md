# 一括ファイル入出力 (CSV / 固定長 / 列マッピング / 値の変換)

一覧ページの一括ダウンロード / 一括更新 (ページリンクの「一括ダウンロード」「一括更新」) は、本体だけでは内部名ヘッダの Excel (xlsx) です。
Extras の設定用フィールドをモジュールに定義すると、同じボタンのまま次のように切り替えられます。

- ファイル形式を CSV (UTF-8 / Shift_JIS、カンマ / タブ / セミコロン) や固定長にする
- 列の並び・列名を相手仕様 (WebEDI・他システム連携など) に合わせる
- リンクの参照を Id ではなく名前で出し入れする、取引先コードを自社コードに引き当てる

同じ定義は、詳細画面に置く一括ファイル転送ボタンと、スクリプト (`BulkFileReader` / `BulkFileTransferService`) からの入出力にも効きます。
いずれもサーバー側の対応 (ホストの `ModuleDataController` から Extras.Server の `BulkFileTransfer` への移譲) が必要です。新しいアプリテンプレートには入っています ([ホスト側の結線](#ホスト側の結線))。

## フィールド

| フィールド | 役割 | 置き方 |
|---|---|---|
| CsvFileFormatField (CSVファイル形式) | **ファイル形式**。CSV にする。区切り文字なしで固定長 | モジュールの Fields に定義するだけ (レイアウト配置は不要。置いても何も描画しない) |
| FileColumnMappingField (ファイル列マッピング) | **列構成**。相手仕様の列の並び・列名・固定値・固定長の列幅 | 同上 |
| FileValueConversionField (ファイル値変換) | **値の表し方**。1 つのフィールドの値をファイル上の値と DB の値の間で引き当てる | 同上。変換したいフィールド 1 つにつき 1 つ |
| BulkFileTransferButtonField (一括ファイル転送ボタン) | 詳細画面に置く一括ダウンロード / 一括更新ボタン | 詳細レイアウトに配置 |

形式・列構成・値の表し方は互いに独立していて、組み合わせて使います。

## 組み合わせで決まる形式

| 定義するフィールド | 一括ダウンロード / 一括更新のファイル |
|---|---|
| なし | xlsx (内部名ヘッダ) — 本体のまま |
| CsvFileFormatField だけ | CSV (内部名ヘッダ) |
| FileColumnMappingField だけ | xlsx (相手仕様の列) |
| 両方 | CSV (相手仕様の列) — WebEDI 向け |
| 両方 + CsvFileFormatField の区切り文字を「なし (固定長)」 | 固定長 (相手仕様の列) |

- 内部名ヘッダ = 1 行目が `フィールド名.データメンバ名` (例 `Title.Value`) の見出し。見出しで列を照合するので、列の並び替え・省略ができます
- 相手仕様の列 = 列の位置で対応付けます (ヘッダ行は取込時に読み飛ばすだけ)
- FileValueConversionField はどの形式でも同じように効きます
- ダウンロードのファイル名は `{モジュール名}.{拡張子}` です。拡張子は CsvFileFormatField の「ファイル拡張子」、無ければ `xlsx`
- 一括更新で受け付けるファイルの拡張子は、CsvFileFormatField があればその「ファイル拡張子」だけ、無ければ `xlsx` / `xlsm` です。CSV・固定長の取込は中身も見て判定するので、拡張子がその設定のままで中身が xlsx のファイルも Excel として読み込めます

## CsvFileFormatField (CSVファイル形式)

| プロパティ | デザイナ表示名 | 型 | 説明 |
|---|---|---|---|
| Encoding | エンコーディング | enum | `Utf8Bom` (UTF-8 (BOM付き)。既定。Excel でダブルクリックしても文字化けしない) / `Utf8` (UTF-8) / `ShiftJis` (Shift_JIS)。取込時は BOM があればそちらを優先 |
| Delimiter | 区切り文字 | enum | `Comma` (カンマ。既定) / `Tab` (タブ) / `Semicolon` (セミコロン) / `None` (なし (固定長)。FileColumnMappingField 併用必須) |
| FileExtension | ファイル拡張子 | string | ダウンロードの拡張子 (既定 `csv`。`txt` / `dat` 等)。一括更新もこの拡張子のファイルだけを受け付ける |
| FixedLengthWidthUnit | 固定長の幅の単位 | enum | 固定長での列幅の単位。`Byte` (バイト。既定。Shift_JIS の全角は 2 バイト) / `Char` (文字) |

CSV の仕様:

- RFC 4180 準拠 (区切り文字・引用符・改行を含むセルは引用符で囲み、引用符は二重にする)。改行は CRLF
- 取込時、全セルが空の行は無視する

## FileColumnMappingField (ファイル列マッピング)

| プロパティ | デザイナ表示名 | 型 | 説明 |
|---|---|---|---|
| HasHeader | ヘッダ行あり | bool | 既定 true。出力時は外部列名を 1 行目に出し、取込時は 1 行目を読み飛ばす |
| Columns | 列マッピング | MappingColumns | 列の定義。並び順 = ファイルの列位置。専用エディタで編集する |

列 (Columns の各項目):

| 項目 | 説明 |
|---|---|
| ExternalName | ファイルでの列名 (ヘッダ行に出力。取込は列位置で対応付ける) |
| Field | 対応するフィールド (`フィールド名.データメンバ名`。例 `Customer.Value`)。空なら取込時は無視し、出力時は FixedValue を出す |
| FixedValue | 出力時の固定値 (Field が空の列で使う。取引先コードなど)。Field も FixedValue も空ならブランク列 (出力は空、取込は無視。相手仕様の未使用列の位置合わせ) |
| FixedLengthWidth | 固定長での列幅 (単位は CsvFileFormatField の「固定長の幅の単位」)。固定長では全列に必要 |
| FixedLengthAlignment | 固定長での値の寄せ。`Left` (左寄せ。既定) / `Right` (右寄せ) |
| FixedLengthPaddingChar | 固定長のパディング文字。`Space` (空白。既定) / `Zero` (ゼロ。右寄せの列だけ) |

- 日付・数値の書式は列ではなくフィールドの `Format` に従います (例 DateField の Format を `yyyyMMdd`)。出力時は書式化し、取込時は書式どおりに解釈できない値を行番号付きのエラーにします。画面と別の書式が必要なら連携用のモジュールを分けます
- 和暦などの特殊な書式は、フィールドのデザインクラスに `IExternalTextFormatFieldDesign` を実装した独自フィールドで対応できます

### 固定長

CsvFileFormatField の区切り文字を `None` にすると固定長になります。形式 (幅の単位・エンコーディング・拡張子) は CsvFileFormatField、列幅は FileColumnMappingField の列ごとに設定します。

- 列は桁位置だけで決まるので、ブランク列・固定値列も含めた全列に FixedLengthWidth (1 以上) が必要です
- 出力時は各列を幅までパディングします。**幅に収まらない値は切り詰めず、行番号付きのエラーで失敗**します
- 取込時は各行を幅で切り出し、パディング側 (寄せの逆側) のパディング文字を取り除きます。行が短いときは足りない列を空として扱います
- 数値のゼロ埋め (例 `00120`) は `Zero` + `Right` で表します (左寄せのゼロ埋めは値の末尾の 0 と区別できないため使えません)
- 固定長ファイルは通常ヘッダ行を持たないので `HasHeader: false` にします (true にするとヘッダ行も列幅でパディングされ、収まらない列名はエラー)

## FileValueConversionField (ファイル値変換)

1 つのフィールドの値を、ファイル上の値 (外部値) と DB の値 (内部値) の間で引き当てます。変換表はただの業務モジュールです。

- LinkField の参照を Id ではなく名前で出し入れする (例: オーナーID 列を「オーナー名」で)
- EDI の取引先コード ⇔ 自社コードのようなテキスト列のコード変換

| プロパティ | デザイナ表示名 | 型 | 説明 |
|---|---|---|---|
| TargetField | 変換対象フィールド | string | 変換するフィールド (同じモジュール)。値を持ち、一括更新の対象になる型 |
| ConversionModule | 変換表モジュール | string | 変換表のモジュール。**対象が LinkField なら省略時はリンク先モジュール** |
| ExternalField | 外部値フィールド | string | 変換表でファイルに出す値のフィールド (例 `名前`) |
| InternalField | 内部値フィールド | string | 変換表で DB に入る値のフィールド (例 `Id`)。**対象が LinkField なら省略時は Id** |

- 出力: 内部値で変換表の InternalField を引き、ExternalField の値を出します。引き当てられない値はそのまま出し、空は空のままです
- 取込: 外部値で変換表の ExternalField を引き、InternalField の値を入れます。引き当てられない値は行番号付きのエラー (`Row N, 列名: code 'X' was not found in 'モジュール'`) で、1 件でもあればファイル全体を取り込みません。外部値が複数行に一致したら先頭の行を使います
- 空セル (空白だけを含む) は未設定 (null) として取り込みます。Id 付きの更新では参照を外せます
- 内部名ヘッダの形式では見出しは `オーナーID.Value` のまま、セルの値だけが変わります。列マッピング併用時は、列の Field が変換対象を指している列の値が変わります
- 変換表は実行するユーザーの読み取り権限で読みます (読めない行は引き当てられません)。転送のたびに全件を読むので、数千行程度までを想定しています

```json
{
  "TargetField": "オーナーID",
  "ConversionModule": "",
  "ExternalField": "名前",
  "InternalField": "",
  "Name": "オーナーID_Conversion",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.FileValueConversionFieldDesign"
}
```

0.13.0 より前の FileColumnMappingField の列ごとのコード変換 (`ConversionModule` / `ConversionExternalField` / `ConversionInternalField`) は廃止されました。
デザイナでプロジェクトを開いたときのマイグレーション「ファイル列マッピングのコード変換をファイル値変換フィールドへ移行」で、このフィールドに移せます。

## BulkFileTransferButtonField (一括ファイル転送ボタン)

一覧ページの一括ダウンロード / 一括更新ボタンと同じ処理を、詳細画面から実行するボタンです。例えば受注の詳細画面で、明細の一覧を CSV で出し入れできます。
対象は画面に見えている検索結果か一覧で、次の 2 つの**どちらか一方だけ**を設定します。

| プロパティ | デザイナ表示名 | 型 | 説明 |
|---|---|---|---|
| SearchFieldName | 検索フィールド名 (対象の指定) | string | 同じモジュールの検索フィールド。ユーザーが入力している今の検索条件でダウンロードする |
| ListFieldName | リストフィールド名 (対象の指定) | string | 同じモジュールの一覧 (List / DetailList / TileList)。表示中の検索条件でダウンロードする。一覧の列・ページサイズには縛られず、全列・全件が出る |
| CanBulkDataDownload | 一括ダウンロード | bool | ダウンロードボタンを表示するか。既定 true |
| CanBulkDataUpdate | 一括更新 | bool | アップロードボタンを表示するか。既定 true |
| OnUploaded | アップロード完了イベント | string | 一括更新が成功した後に呼ぶスクリプト |

- 一括更新は、条件の元になった検索結果・一覧の対象モジュールに取り込みます (条件そのものは取込に影響しません)。成功すると対象の一覧を再読み込みしてから OnUploaded を呼びます
- 誰が取り込めるかは、対象モジュールの書き込み権限 (通常の保存と同じ) で決まります。ボタンの表示は権限で切り替わらないので、必要なら表示条件で隠してください

## 取込の検証

一括更新 (一覧ページ・一括ファイル転送ボタン) は、取り込む前にファイル全体を検証します。

- 対応しない列、型変換できないセル、書式どおりでない値、引き当てられない外部値を行番号付きで報告し、1 件でもあれば 1 行も取り込みません (表示は先頭 20 件まで)
- 取込は 1 トランザクションで、Id の一致で追加 / 更新を判定します
- 固定長の出力で幅に収まらない値があるときは、ダウンロードがエラーになります

## デザインチェック

| コード | 内容 |
|---|---|
| `BulkFileTransferButtonFieldDesign:1` | 検索フィールド名とリストフィールド名が両方設定されている、または両方空 |
| `CsvFileFormatFieldDesign:1` | 区切り文字が「なし (固定長)」なのに、同じモジュールに FileColumnMappingField が無い |
| `FileColumnMappingFieldDesign:1` | 固定長で、列幅 (1 以上) が無い列がある |
| `FileColumnMappingFieldDesign:2` | 固定長で、ゼロ埋めの列が右寄せになっていない |
| `FileColumnMappingFieldDesign:3` | 廃止された列ごとのコード変換が残っている (マイグレーションで移せる) |
| `FileValueConversionFieldDesign:1` | 変換対象フィールドが空 |
| `FileValueConversionFieldDesign:2` | 変換対象にできない型 (値を持たない、一括更新の対象外) |
| `FileValueConversionFieldDesign:3` | 変換表モジュールが空 (省略できるのは対象が LinkField のときだけ) |
| `FileValueConversionFieldDesign:4` | 外部値フィールドが空 |
| `FileValueConversionFieldDesign:5` | 内部値フィールドが空 (省略できるのは対象が LinkField のときだけ) |
| `FileValueConversionFieldDesign:6` | 対象が LinkField で、変換表モジュールがリンク先と違う (別モジュール経由の変換が意図どおりならこの指摘を抑止して使う) |
| `FileValueConversionFieldDesign:7` | 同じフィールドを対象にするファイル値変換フィールドが 2 つ以上ある |

このほか、指定したフィールド・モジュールが存在するかも検査されます。フィールド名・モジュール名の変更には追従します。

## スクリプト

一覧ページのボタンでは書けない処理 (行の読み飛ばし、計算、重複チェック、マスタの引き当てなど) は、スクリプトで同じ形式定義を使って入出力します。
同じフィールドに FileValueConversionField とスクリプトの変換を両方かけないでください。

### BulkFileReader (取込)

`new BulkFileReader<モジュール名>()` で作ります。`Read()` がファイル選択 → サーバーでの解析 (形式・列構成・値の変換はそのモジュールの定義どおり) を行います。**DB には書き込みません**。

| メンバー | 説明 |
|---|---|
| `Read()` | ファイルを選んで解析する。選んで解析したら true (キャンセル・エンドポイント未設定は false) |
| `Items` | 解析したモジュールデータの一覧 (`List<ModuleData>`。ファイルの行順)。解釈できなかったセルは値が未設定 |
| `HasError` / `ErrorCount` | 解釈できなかったセルがあるか / その件数 |
| `ErrorText` | 解釈できなかったセルの一覧 (行番号・列・内容) |
| `DownloadErrorText()` | `ErrorText` をテキストファイルでダウンロードする (エラーが無ければ何もしない) |
| `ToModules()` | `Items` をまとめてモジュールにする (モジュールの機能が必要な加工用。値の参照・書き換えだけなら `Items` のままのほうが軽い) |
| `ModuleName` | 取込先のモジュール名 |

一括更新と違い、解釈できないセルがあっても行は捨てません (トレーラ行などはスクリプトで除きます)。

### BulkFileTransferService (出力・一括保存)

| メソッド | 説明 |
|---|---|
| `Download(ModuleSearcher)` | その検索条件で一括ダウンロードする |
| `Download(SearchField)` | 検索フィールドの今の検索条件で一括ダウンロードする |
| `Download(ListField)` | 一覧の表示中の検索条件で一括ダウンロードする (全列・全件) |
| `Download(List<Module>)` / `Download(List<ModuleData>)` | 加工済みの行をそのままファイルにする (検索しない) |
| `Submit(List<Module>)` / `Submit(List<ModuleData>)` | 加工済みの行を 1 トランザクションで一括保存する。Id の一致で追加 / 更新。成功なら true。新規行に採番された Id は返らないので、保存後もモジュールを使い続けるなら `Module.Submit` を使う |

`List<ModuleData>` を渡す形はモジュールを作らないので、大量の行でも軽く動きます (`ModuleSearcher.ExecuteRaw` や `BulkFileReader.Items` の結果をそのまま渡せます)。

```csharp
// ButtonField の OnClick
void ImportButton_OnClick()
{
    var reader = new BulkFileReader<Order>();
    if (!reader.Read()) return;
    if (reader.HasError)
    {
        reader.DownloadErrorText();
        return;
    }
    // ここで reader.Items の行を加工する
    if (BulkFileTransferService.Submit(reader.Items)) Toaster.Success("取り込みました");
}
```

## ホスト側の結線

新しいアプリテンプレートには入っています。既存のアプリに足す場合の要点です。

### サーバー (ModuleDataController)

一覧ページの一括ダウンロード / 一括更新 (`list_file` / `submit_by_file`) の処理本体を `BulkFileTransfer` (`Codeer.LowCode.Blazor.Extras.Server.BulkFile` 名前空間) に移譲し、スクリプト用の 3 つの API を足します。
移譲していないサーバーでは、CsvFileFormatField を定義しても「拡張子が .csv の xlsx」がダウンロードされます。

```csharp
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;

[HttpPost("list_file")]
public async Task<IActionResult> GetListFileAsync(SearchCondition? condition)
    => Ok(await BulkFileTransfer.GetListFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, condition!));

[HttpPost("submit_by_file")]
public async Task<List<ModuleSubmitResult>> SubmitByFileAsync(string? moduleName)
    => await BulkFileTransfer.SubmitByFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body);

//BulkFileTransferService.Download(List<Module> / List<ModuleData>)
[HttpPost("list_file_by_data")]
public async Task<IActionResult> GetListFileByDataAsync(string? moduleName)
    => Ok(await BulkFileTransfer.GetListFileByDataAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body));

//BulkFileTransferService.Submit
[HttpPost("bulk_submit")]
public async Task<IActionResult> BulkSubmitAsync(string? moduleName)
    => Content(await BulkFileTransfer.BulkSubmitAsync(_dataService.ModuleDataIO, moduleName, Request.Body), "application/json");

//BulkFileReader.Read。ModuleData はポリモーフィックなので JsonConverterEx で直列化して返す
[HttpPost("parse_file")]
public async Task<IActionResult> ParseFileAsync(string? moduleName)
    => Content(Codeer.LowCode.Blazor.Json.JsonConverterEx.SerializeObject(
        await BulkFileTransfer.ParseFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body)),
        "application/json");
```

- どれもログインユーザーの `ModuleDataIO` を渡すので、読み書きには通常の権限が効きます
- `SubmitByFileAsync` の `dryRun: true` は検証だけを行い、取り込みません
- 形式を足す・外部システムと連携するなどの独自処理は、`BulkFileTransfer` を呼ばずにこの Controller で差し替えます (CSV の生成 / 解析単体は `Codeer.LowCode.Blazor.Extras.Server.Csv.CsvUtils`)

### クライアント (ServiceInitializer)

スクリプト用の 3 つのエンドポイントを起動時に設定します (一覧ページ・一括ファイル転送ボタンは本体の `list_file` / `submit_by_file` を使うので設定不要)。未設定だと `BulkFileReader.Read()` は false を返し、`BulkFileTransferService` の `Download(List<…>)` / `Submit` は何もしません。

```csharp
using Codeer.LowCode.Blazor.Extras.ScriptObjects;

BulkFileReader.ParseFileEndPoint = "/api/module_data/parse_file";
BulkFileTransferService.ListFileByDataEndPoint = "/api/module_data/list_file_by_data";
BulkFileTransferService.BulkSubmitEndPoint = "/api/module_data/bulk_submit";
```

## 関連

- AI 向けの詳細 (JSON 例を含む): [CsvFileFormatField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/CsvFileFormatFieldDesign.md) / [FileColumnMappingField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/FileColumnMappingFieldDesign.md) / [FileValueConversionField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/FileValueConversionFieldDesign.md) / [BulkFileTransferButtonField](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/BulkFileTransferButtonFieldDesign.md)
