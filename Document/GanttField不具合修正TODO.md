# GanttField 不具合修正 TODO

調査日: 2026-11-16 / 最終更新: 2026-11-18 / 対象ブランチ: `feature/gantt` / 本体 `Codeer.LowCode.Blazor` 1.3.67

GanttField と、Example のタスク詳細ダイアログ (`GanttTask` の「Gantt用」レイアウト) で見つかった不具合の原因と修正案。
1・2・4・11 は対応済み、6・8 は一部対応済み (各項目の「状態」を参照)。残りは未着手。

- 確認方法: ソースを読み、Example (`Extras.Server`、`/Main/Gantt`) を起動して実画面で再現した
- 本体の動きは本体ソースの最新 (1.3.68、2026-11-17 時点の origin/main) で確認した。Extras が参照するのは 1.3.67
- 対象外: 「タスク詳細のカレンダーの表示が崩れる」は再現できず原因不明のため、この資料には含めない

## 一覧

| # | 内容 | 直す場所 | 判断が要るか | 状態 |
|---|---|---|---|---|
| 1 | 週のタイトルの先頭に「年」だけが出る | GanttFieldComponent | 不要 | 対応済み |
| 2 | 開始 < 終了 のチェックが無い | GanttField | 不要 | 対応済み |
| 3 | 進捗率 0 - 100 のチェックが無い | GanttField (または Example のデザイン) | 案を選ぶ | 未着手 |
| 4 | ラベルが「Title, Start, End, Progress」と英語で出る | Example のデザイン | 不要 | 対応済み |
| 5 | 色の #000000 が既定値のように見える | ColorPickerField + Example のデザイン | 案を選ぶ | 未着手 |
| 6 | 入力エラーで編集内容が消える | GanttField (Calendar / TaskBoard / MarkerList も同じ作り) | 残り 3 フィールドを直すか決める | Gantt は対応済み |
| 7 | 「文字色」が何にも効かない | Example のデザイン | 不要 | 未着手 |
| 8 | 追加時の初期値が現在時刻 | GanttField / GanttFieldComponent | 不要 | 一部対応済み (分までに切り捨て) |
| 9 | 期間指定ポップアップが無反応になる | GanttFieldComponent | 不要 | 未着手 |
| 10 | スクリプトのメソッド名の不統一 | GanttField + Example のスクリプト | 変えるか決める | 未着手 |
| 11 | バーをダブルクリックしても編集ダイアログが開かない | GanttFieldComponent | 不要 | 対応済み |

1 - 5 が報告された不具合、6 - 10 は調査中に見つけたもの、11 は 2 の確認中に見つかったもの。

---

## 報告された不具合

### 1. 週のタイトルの先頭に「年」だけが出る

**状態**: 対応済み (`Fields/GanttTitle`、テスト `Test/Fields/GanttTitleTest`)。日・週・月のどの表示にも年を出す。月またぎ・年またぎの週は CalendarField と同じ形。月をまたぐ月表示 (幅に合わせない 42 日表示) は「2026年11月 - 2026年12月」。

日表示は曜日も出す (「2026年11月15日(日)」/「Sunday, November 15, 2026」)。日表示のヘッダーは時刻だけで、曜日がどこにも出ないため。書式はガント専用のリソース `DateFormatDayTitleWithWeekday` (CalendarField の日表示はヘッダーに曜日があるので変えていない)。

以下は対応前の調査内容。

**現象**: ツールバー左上が「年11月 15日 - 21日」になる (実画面で再現)。

**原因**: `GanttFieldComponent.razor` の `GetTitle()` が、リソース `WeekTitleSameMonth` の `{3}` (年) に空文字を渡している。

```csharp
_ when s.Month == e.Month => string.Format(Resources.WeekTitleSameMonth,
  s.ToString(Resources.DateFormatMonthNameFull, _culture), s.Day, e.Day, ""),
```

日本語リソースは `{3}年{0} {1}日 - {2}日` なので、年が空のまま「年」だけ残る。
英語リソース (`{0} {1} - {2}, {3}`) でも末尾に「, 」が残る。

**修正案**: CalendarField と同じく `s.Year` を渡す → 「2026年11月 15日 - 21日」。

**あわせて直したい点**: 同じ `GetTitle()` のほかの表示にも年が出ない。年をまたいで前へ / 次へ動かすと何年か分からない。

| 表示 | 今 | 修正後の例 |
|---|---|---|
| 日 | 11月15日 | 2026年11月15日 (`DateFormatDayTitle`) |
| 月 | 11月 | 2026年11月 (`DateFormatMonthTitle`) |
| 月またぎの週 | 11月29日 - 12月5日 | 2026年11月29日 - 12月5日 |
| 月 (FitToWidth でない 42 日表示) | 11月 - 12月 | 2026年11月 - 12月 |

CalendarField には年またぎ用の `WeekTitleDiffMonth` 系のリソースがあるので、同じものを使う。

**テスト**: タイトルの組み立てを Field 側の関数に寄せれば `Test/Fields/GanttViewStartTest` と同じ形で単体テストできる。

---

### 2. 開始 < 終了 のチェックが無い

**状態**: 対応済み (`Fields/GanttDateRange`、`GanttField.ValidateTaskAsync`、テスト `Test/Fields/GanttDateRangeTest`)。

- 判定: 日時は 開始 < 終了 (同じ日時も不可)、日付だけは 開始 <= 終了、どちらかが空なら見ない
- 比較は分まで。画面で入力する単位が分で、追加時の初期値に入っていた秒・ミリ秒のせいで「同じに見える日時」の判定が割れていた
- エラーは終了の項目の下に出し、通知もする。文言は項目の表示名で組み立てる (「終了は開始より後にしてください。」)
- 入れた場所は追加・更新の確定時だけ。下の修正案にある保存時 (`GanttField.ValidateInput()`) の検査は入れていない。DB に前後が逆の行が既にあると、原因が画面に見えないまま親の保存が毎回止まるため

以下は対応前の調査内容。

**現象**: 開始 11/18 10:00・終了 11/17 10:00 で OK を押すとそのまま通る。左のラベル行だけ出て、バーが描かれないタスクになる (実画面で再現)。

**原因**: チェックがどこにも無い。`GanttField.AddAsync` / `EditAsync` はダイアログのモジュールの `ValidateInput()` (各フィールド単体の検証) を呼ぶだけで、開始と終了の前後関係は誰も見ていない。

**修正案**: 開始・終了の項目を知っているのは GanttField なので、GanttField で比較する。

- 判定
  - DateTimeField: 開始 < 終了
  - DateField (日付のみ): 開始 <= 終了 (同日 = 1 日のタスクを許す)
  - どちらかが空のときは、この検査では何もしない (必須かどうかは各フィールドの `IsRequired` に任せる)
- 入れる場所
  - 追加・更新の確定時 (`AddAsync` / `EditAsync`)
  - 保存時の `GanttField.ValidateInput()` (ドラッグ・スクリプトで作られた行も拾う)
- エラーの見せ方: 終了フィールドに `SetError` でメッセージを出す (文言はリソースに追加。例「終了は開始より後にしてください」)

**補足**: バーのドラッグ (リサイズ) は `OnMouseMove` で既に逆転を防いでいる。

---

### 3. 進捗率 0 - 100 のチェックが無い

**現象**: 150 がそのまま入る。ラベルは「100%」と表示されるが、値は 150 のまま保存対象になる (実画面で再現)。

**原因**: Example の `GanttTask.Progress` (NumberField) に最小・最大が設定されていない。描画された `<input type="number">` にも min / max が無い。GanttField は表示のときに丸めるだけ (`ProgressScale.ToFillPercent`)。

**修正案** (どちらか、または両方):

| 案 | 内容 | 長所 | 短所 |
|---|---|---|---|
| A | GanttField が `ProgressScale` に応じた範囲を検査する (Percent: 0 - 100、Ratio: 0 - 1) | デザインの設定漏れに依存しない。Ratio にも正しく効く | フィールドの責務が少し増える |
| B | Example のデザインで Progress に最小 0・最大 100 を設定する | コード変更なし | 利用者のデザインでは同じ漏れが起きる |

**推奨**: A (入れる場所とエラーの見せ方は 2 と同じ = `GanttField.ValidateTaskAsync` に足し、進捗の項目の下にエラーを出す)。Example は B も入れておくと、入力欄の時点で範囲が効く。

**本体の確認結果**: `NumberFieldDesign` に `Min` / `Max` (`decimal?`) があり、`NumberField.ValidateInput()` が範囲外をエラーにする (メッセージは本体のリソース)。案 B は `"Min": 0, "Max": 100` を足すだけで効く。

---

### 4. ラベルが「Title, Start, End, Progress」と英語で出る

**状態**: 対応済み。`GanttTask.mod.json` の 4 フィールドに `DisplayName` を入れた (タイトル / 開始 / 終了 / 進捗率)。「背景色」「文字色」を同じ形に揃えるのと、保存ボタンの「Submit」は触っていない。

以下は対応前の調査内容。

**現象**: タスク詳細の見出しが英語のフィールド名になる。「背景色」「文字色」だけ日本語。

**原因**: 言語設定ではなくデザイン。見出しは `Text` が空の LabelField (`RelativeField` 指定) で、参照先フィールドの `DisplayName` も空なので、フィールド名がそのまま出ている。「背景色」「文字色」は LabelField の `Text` に直接書いてあるので日本語になる。

**修正案**: `Source/Example/Design/Modules/GanttTask.mod.json` の 4 フィールドに `DisplayName` を入れる。

| フィールド | DisplayName |
|---|---|
| Title | タイトル |
| Start | 開始 |
| End | 終了 |
| Progress | 進捗 |

一覧レイアウトの列見出しも同時に日本語になる。「背景色」「文字色」も同じ形 (`BackgroundColor` / `Color` の `DisplayName`) に揃えると統一できる。

---

### 5. 色の #000000 が既定値のように見える

**現象**: 色を触らずにタスクを追加すると、ピッカーは黒 (#000000) を表示しているのに、バーは既定の青 (#1a73e8) で描かれる (実画面で再現)。

**原因**: ColorPickerField は値が空のとき `Design.Default` (既定 `#000000`) を**表示するだけ**で、値には入れない。

```razor
private string DisplayValue => string.IsNullOrEmpty(Field.Value) ? Field.Design.Default : Field.Value!;
```

値は空のままなので、GanttField は「色の指定なし」と見てデザインの `BarColor` → CSS の既定色を使う。

**副作用**: ピッカーが最初から #000000 を指しているので、黒を選んでも変更イベントが起きず、黒を設定できない。一度別の色を選んでから黒に戻す必要がある。

**修正案** (ColorPickerField の仕様変更になるので、どちらかを選ぶ):

| 案 | 内容 | 長所 | 短所 |
|---|---|---|---|
| A | 「未設定」を状態として見せる。値が空なら「未設定」と表示し、設定済みならクリアボタンを出す | ガントの「空 = フィールドの既定色」と合う。黒も選べる | UI の追加が要る。`Default` の意味 (未設定時の見本色) を docs に書き直す |
| B | 新規レコードで `Default` を実際の値として入れる | 見た目と値が一致する | 既定のままだと全タスクが黒で始まる。既存の空の行は直らない |

**推奨**: A。Example は加えて `BackgroundColor` の `Default` を空かガントの既定色 `#1a73e8` にする。

**影響範囲**: ColorPickerField を使うほかの画面 (ProgressField の `ColorField` など) も同じ動きになる。docs/ColorPickerField.md の更新が要る。

---

## 調査中に見つけたもの

### 6. 入力エラーで編集内容が消える

**状態**: GanttField は対応済み (`AddAsync` / `EditAsync`)。エラーのときは同じモジュールでダイアログを開き直すので、入力した内容と各項目のエラーが残る。いったん閉じてから開き直す作りなので、一瞬ちらつくことがある。CalendarField / TaskBoardField / MarkerListField は未着手。

以下は対応前の調査内容。

**現象**: ダイアログで入力エラーになると、「入力エラー」の通知だけ出て編集内容が破棄される。どの項目が悪いかも見えない。

**原因**: ダイアログを閉じた**後**に検証している。

```csharp
if (await mod.ShowDialogAsync(OK, Cancel) != OK) return;
if (!await mod.ValidateInput())
{
    await Services.UIService.NotifyError(Properties.Resources.InputError);
    return;
}
```

**修正案**: エラーのときは同じモジュールでダイアログを開き直す (各フィールドのエラー表示が残った状態で直せる)。2・3 の検証を入れるとエラーになる場面が増えるので、同時に直したい。

**範囲**: CalendarField / TaskBoardField / MarkerListField も同じ作り。共通の部品 (`OwnedRecordModules` あたり) に寄せて 4 フィールドまとめて直すか、今回は Gantt だけにするかを決める。

**本体の確認結果**: `Module.ShowDialogAsync` は押されたボタンの文字列を返すだけで、`DialogButton` にも「閉じる前に検証する」口は無い。Extras 側で開き直す形にする。

---

### 7. 「文字色」が何にも効かない

**現象**: 「Gantt用」レイアウトに「文字色」(`Color`) があるが、変えてもガントに反映されない。

**原因**: GanttFieldDesign に文字色の項目は無い。バーの文字色は背景色から自動で決まる (YIQ コントラスト)。

**修正案**: 「Gantt用」レイアウトから「文字色」を外す。

---

### 8. 追加時の初期値が現在時刻

**状態**: 一部対応済み。初期値を分までに切り捨てた (`GanttDateRange.TruncateToMinute`。2 の「同じ日時の判定が割れる」の原因だったため)。初期値が今日の現在時刻であること・表示中の期間と関係ないこと・DateField で 2 日のタスクになることは未着手。

以下は対応前の調査内容。

**現象**: 「+ 追加」の初期値が、開始 = 現在時刻 (例 14:27)、終了 = その 1 日後になる。

**問題**:
- 半端な時刻が入る
- 表示中の期間と関係なく今日に入るので、別の週や月を見ているときに追加すると画面に現れない

**原因**: `GanttFieldComponent.OnAddClicked` が `Field.AddAsync(DateTime.Now)` を呼んでいる。

**修正案**: 表示範囲に今日が含まれていれば今日の 0:00、含まれていなければ表示範囲の開始日を初期値にする。DateField のときは 開始 = 終了 = その日 (今は終了が翌日になり 2 日のタスクになる)。

---

### 9. 期間指定ポップアップが無反応になる

**現象**: 期間指定 (CustomRange) のポップアップで 開始 >= 終了 のまま OK を押すと、何も起きない。

**原因**: `ApplyRangePopup` が黙って return している。

```csharp
if (_rangeEditStart >= _rangeEditEnd) return;
```

**修正案**: ポップアップ内にエラーメッセージを出す (文言は 2 と共用できる)。

---

### 10. Example のスクリプトのメソッド名 (名前の不統一)

`Source/Example/Design/Modules/Gantt.mod.cs`:

```csharp
Gantt.SetCustomRangeAsync(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));
```

GanttField のほかの公開メソッドは `[ScriptName("Reload")]` のように Async を外した名前でスクリプトに出している。`SetCustomRangeAsync` は `[ScriptMethod]` だけで `ScriptName` が無い。

**本体の確認結果**: スクリプト上の名前は `ScriptName` があればその名前、無ければメソッド名そのまま (`ScriptObjectMemberAccessor.GetMethodName`)。今のスクリプトは `SetCustomRangeAsync` で呼べるので不具合ではなく、名前の不統一だけ (ボタンを押しての動作確認はしていない)。

**修正案**: `[ScriptName("SetCustomRange")]` を付けてスクリプトと docs/GanttField.md を揃える。既存のスクリプトが `SetCustomRangeAsync` で呼んでいると動かなくなるので、変えるかどうかは要判断。

---

### 11. バーをダブルクリックしても編集ダイアログが開かない

**状態**: 対応済み (`GanttFieldComponent.razor`)。

**現象**: docs/GanttField.md の操作方法には「バーをダブルクリック → 編集ダイアログを表示」とあるが、何も起きない。開けるのは左のタスク名のクリックだけだった。

**原因**: バーにはドラッグ用の `onmousedown` しか無く、ダブルクリックの処理が元から実装されていなかった。

**対応**: バーと両端のリサイズのつまみに `ondblclick` を付け、タスク名のクリックと同じ `OnTaskClicked` を呼ぶ。編集できないときは閲覧専用のダイアログが開く。

---

## 残りの進め方

1. **すぐ直せるもの**: 7 (Example のデザイン)、9 (ポップアップ)
2. **検証まわり**: 3 (進捗率)、8 の残り (初期値の日付)
3. **仕様を決めてから**: 5 (ColorPickerField)
4. **変えるか決めてから**: 6 の残り (Calendar / TaskBoard / MarkerList)、10

決めてほしいこと:

- 3: 案 A (GanttField で検査) でよいか
- 5: 案 A (未設定を見せる) / 案 B (既定値を値として入れる) のどちらか
- 6: Calendar / TaskBoard / MarkerList も開き直す形にするか
- 10: スクリプト上の名前を変えるか

決まったこと:

- 1: 日・月の表示にも年を入れる。日表示は曜日も出す
- 2: 比較は分まで。保存時の検査は入れない
- コミットは 1 件ずつ、画面で確認してから手動で行う
