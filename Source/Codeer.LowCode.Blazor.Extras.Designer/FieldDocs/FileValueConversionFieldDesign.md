## Design

一覧ページの一括ダウンロード/一括更新 (と `BulkFileReader` / `BulkFileTransferService`) で、**対象フィールド 1 つの値をファイル上の表し方 (外部値) と DB の値 (内部値) の間で引き当てる**設定用フィールドです。
モジュールの Fields に定義するだけで有効になり、レイアウトへの配置は不要です (配置しても実行時は何も描画されません)。

変換表は**ただの業務モジュール**です。出力時は内部値で `InternalField` を引いて `ExternalField` の値を出し、取込時は外部値で `ExternalField` を引いて `InternalField` の値を入れます。

- 親を持つモジュールで、`LinkField` の参照を **Id の数字ではなく名前で** Excel に出し入れする (例: オーナーID 列を「オーナー名」で)
- EDI の取引先コード ⇔ 自社コードのような**テキスト列のコード変換**

どちらも同じ仕組みで扱います。**変換したいフィールド 1 つにつき、このフィールドを 1 つ**定義します。

### 列構成 (FileColumnMappingField) との分担

- 標準形式 (内部名ヘッダの xlsx/CSV) でも `FileColumnMappingField` 併用時でも**同じように効きます**。列構成はマッピング、値の表し方はこのフィールド、という直交した分担で、優先順位や上書きの概念はありません
- 標準形式: 見出しは従来どおり `オーナーID.Value` のまま、**セルの値だけ**が名前になります (見出し照合・列の並び替え可・列の省略可・不明な見出しはエラー、は標準形式のまま)
- `FileColumnMappingField` 併用時: マッピング列の `Field` が `TargetField` を指していれば、その列の値が変換されます。`FixedValue` 列・`Field` が空の列は対象外

### 取込・出力の挙動

- 出力: 内部値 → 変換表を `InternalField` で引き、`ExternalField` の値を出す。内部値が null なら空セル。引き当てられない内部値はそのまま出す
- 取込: 外部値 → 変換表を `ExternalField` で引き、`InternalField` の値を入れる
- **空セル (空白のみを含む) は null (未設定)** として取り込む。Id 付き更新では null で上書き (参照を外せる)
- 引き当てられない外部値は行番号付きエラー `Row N, 列名: code 'X' was not found in 'モジュール'`。他の列のエラーと同じく全行分を列挙し、1 件でもあればファイル全体を取り込まない
- 外部値が変換表の複数行に一致する場合は先頭の行を採る
- 変換表の検索は現在ユーザーの読み取り権限で行う (読めない行は引き当てられない)。転送時に全件読み込んで辞書にするため、変換表は数千行程度までを想定する

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名。対象が読める命名を推奨 (例 `オーナーID_Conversion`) |
| TargetField | string | ○ | 変換対象のフィールド名 (同じモジュール内)。値を持ち一括入出力できるフィールド (`ListField` や設定用フィールドは不可) |
| ConversionModule | string | △ | 変換表となる業務モジュール名。**対象が `LinkField` なら省略時はリンク先モジュール** |
| ExternalField | string | ○ | ファイルに出す値を持つフィールド名 (例 `名前`) |
| InternalField | string | △ | DB に入る値を持つフィールド名 (例 `Id`)。**対象が `LinkField` なら省略時は `Id`** |

### デザインチェック

- `TargetField` が同じモジュールに無い / 変換対象にできない型
- `ConversionModule` が無い、`ExternalField` / `InternalField` がそのモジュールに無い (LinkField 対象の省略時は既定値で解決して検査)
- LinkField 対象で `ConversionModule` を明示し、リンク先と食い違う (別モジュール経由の変換が意図的なら指摘コードで抑止する)
- 同じ `TargetField` を指すファイル値変換フィールドが 2 つ以上ある
- rename-field / rename-module で `TargetField` / `ConversionModule` / `ExternalField` / `InternalField` は追従する

### モジュール JSON 例 (LinkField の参照を名前で入出力)

`オーナーID` が `オーナー` モジュールへの `LinkField` のとき、次の 1 フィールドで一括ダウンロードの `オーナーID.Value` 列に名前が出て、取込時は名前から Id に戻ります (変換表 = リンク先、内部値 = Id は省略時の既定値):

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

### モジュール JSON 例 (テキスト列のコード変換)

変換表 `EdiCustomerMap` (`EdiCode` = 外部コード、`CustomerCode` = 内部値) で `Customer` を引き当てる (EDI 型。`FileColumnMappingField` / `CsvFileFormatField` と併用しても同じ定義):

```json
{
  "TargetField": "Customer",
  "ConversionModule": "EdiCustomerMap",
  "ExternalField": "EdiCode",
  "InternalField": "CustomerCode",
  "Name": "Customer_Conversion",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.FileValueConversionFieldDesign"
}
```

### 0.13.0 より前からの移行

`FileColumnMappingField` の列ごとの `ConversionModule` / `ConversionExternalField` / `ConversionInternalField` は廃止されました。
残っているデザインはデザインチェックがエラーにします。デザイナでプロジェクトを開いたときのマイグレーション
「ファイル列マッピングのコード変換をファイル値変換フィールドへ移行」を実行すると、変換付きの列ごとに
`<フィールド名>_Conversion` という名前のこのフィールドが作られ、列側の設定が消えます
(同じ対象に設定の違う変換が付いている列は自動移行できないので残り、デザインチェックが指摘します)。
名前解決のためだけにマッピングを使っていた場合は、マッピングを削除して標準形式に戻し、このフィールドだけを残せます。

### サーバー側の対応 (必須)

`CsvFileFormatField` / `FileColumnMappingField` と同じく、サーバーテンプレートの `ModuleDataController` が
`BulkFileTransfer` (Codeer.LowCode.Blazor.Extras.Server) に移譲済みである必要があります。

## Script

### スクリプト API

このフィールドは値を持たず、スクリプトから利用できる API を公開していません。
スクリプトの一括入出力 (`BulkFileReader` / `BulkFileTransferService`) にも同じ変換が適用されます
(同じフィールドに宣言的な値変換とスクリプト変換を両方かけないこと)。

## CSS

### CSS カスタマイズ

実行時は何も描画しないため、CSS カスタマイズの対象はありません。
