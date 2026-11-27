using Codeer.LowCode.Blazor.OperatingModel;
using Excel.Report.PDF;
using ClosedXML.Excel;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class ExcelCellIndex
    {
        public int RowIndex { get; set; }
        public int ColumnIndex { get; set; }
        public ExcelCellIndex GetNext(int rowOffset, int columnOffset)
            => new ExcelCellIndex { RowIndex = RowIndex + rowOffset, ColumnIndex = ColumnIndex + columnOffset };
    }

    public class Excel : IDisposable
    {
        private class DataGetter : IExcelSymbolConverter
        {
            Module? _module;
            string _name = string.Empty;

            internal DataGetter(Module module)
                => _module = module;

            DataGetter(Module? module, string name)
            {
                _module = module;
                _name = name;
            }

            public IExcelSymbolConverter CreateChildExcelSymbolConverter(object? obj, string name)
                => new DataGetter(obj as Module, name);

            public async Task<ExcelOverWriteCell?> GetData(string text)
            {
                if (_module == null)
                    return null;
                if (!string.IsNullOrEmpty(_name))
                    return await GetData(_module, _name, text);
                var value = new ObjectWrapper<object>();
                return await _module.TryGetValueByPropertyTextAsync(text, value) ? new ExcelOverWriteCell { Value = Adjust(value.Value) } : null;
            }

            public async Task<ExcelOverWriteCell?> GetData(object? x, string elementName, string text)
            {
                if (_module == null)
                    return null;
                var value = new ObjectWrapper<object>();
                return await _module.TryGetValueByPropertyTextAsync(x, text, elementName, value) ? new ExcelOverWriteCell { Value = Adjust(value.Value) } : null;
            }

            static object? Adjust(object? value)
                => value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : value;
        }

        //OverWrite(ModuleData) の記号の解決。Module 版 (DataGetter) と同じ記法を、ModuleData の項目と FieldData のプロパティで辿る。
        //解決できない記号は null を返す (セルは書き換わらず、記号がそのまま残る = Module 版と同じ)
        private class ModuleDataGetter : IExcelSymbolConverter
        {
            readonly ModuleData? _data;
            readonly string _name = string.Empty;
            readonly DesignData? _designData;

            internal ModuleDataGetter(ModuleData data, DesignData? designData)
            {
                _data = data;
                _designData = designData;
            }

            ModuleDataGetter(ModuleData? data, string name, DesignData? designData)
            {
                _data = data;
                _name = name;
                _designData = designData;
            }

            //Module 版と同じく、繰り返しの中では要素 (行) が記号の解決先になる
            public IExcelSymbolConverter CreateChildExcelSymbolConverter(object? obj, string name)
                => new ModuleDataGetter(obj as ModuleData, name, _designData);

            public Task<ExcelOverWriteCell?> GetData(string text)
            {
                if (_data == null) return Task.FromResult<ExcelOverWriteCell?>(null);
                var path = !string.IsNullOrEmpty(_name) && text.StartsWith(_name + ".") ? text.Substring(_name.Length + 1) : text;
                return Task.FromResult(TryResolve(_data, path.Split('.'), out var value) ? new ExcelOverWriteCell { Value = Adjust(value) } : null);
            }

            bool TryResolve(ModuleData data, string[] names, out object? value)
            {
                value = null;
                object? obj = data;
                for (var i = 0; i < names.Length;)
                {
                    if (obj == null) return false;
                    if (obj is ModuleData moduleData)
                    {
                        //項目名はドットを含むことがある (リンク越しの項目) ので長い名前から当てる
                        var next = FindField(moduleData, names, i, out var field);
                        if (next != i)
                        {
                            obj = field;
                            i = next;
                            continue;
                        }
                    }
                    if (!TryGetMember(obj, names[i], out obj)) return false;
                    i++;
                }
                value = obj;
                return true;
            }

            //見つかれば次の位置。データに無い項目は、デザインにあれば空の値 (Module 版の未入力の項目と同じ) にする
            int FindField(ModuleData data, string[] names, int start, out object? field)
            {
                for (var end = names.Length; start < end; end--)
                {
                    if (data.Fields.TryGetValue(string.Join(".", names[start..end]), out var found))
                    {
                        field = found;
                        return end;
                    }
                }
                field = _designData?.Modules.Find(data.Name)?.Fields.FirstOrDefault(e => e.Name == names[start])?.CreateData();
                return field == null ? start : start + 1;
            }

            //Module 版でフィールドのメンバーとして書く名前のうち、ModuleData では別の名前のもの (一覧の行・子モジュール)
            static bool TryGetMember(object obj, string name, out object? value)
            {
                value = null;
                if (obj is ListFieldData list && name == "Rows")
                {
                    value = list.Children;
                    return true;
                }
                if (obj is ModuleFieldData child && name == "ChildModule")
                {
                    value = child.Data;
                    return true;
                }
                var property = obj.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (property == null || property.GetIndexParameters().Length != 0 || System.Reflection.CustomAttributeExtensions.GetCustomAttribute<ScriptHideAttribute>(property) != null) return false;
                value = property.GetValue(obj);
                return true;
            }

            static object? Adjust(object? value)
                => value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : value;
        }

        XLWorkbook _book;
        string _fileName;

        [ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        [ScriptInject]
        public IHttpService? Http { get; set; }

        [ScriptHide]
        public static Func<MemoryStream, MemoryStream>? ConvertPdf { get; set; }

        /// <summary>
        /// PDF 変換専用のエンドポイント (xlsx の本文を POST し PDF の本文を受ける)。空 (既定) なら共通の口 (<see cref="Api.ExtrasTransport"/> = ホストの 1 本の API の ExcelApi) を使う。
        /// 変換専用の Controller を持つホストが起動時に設定する。<see cref="ConvertPdf"/> が設定されていればどちらも使わない。
        /// </summary>
        [ScriptHide]
        public static string ConvertPdfEndPoint { get; set; } = string.Empty;

        public Excel(MemoryStream? stream, string fileName)
        {
            _fileName = fileName;
            _book = new XLWorkbook(stream);
        }

        [ScriptHide]
        public string FileName => _fileName;

        public void Dispose() => _book.Dispose();

        public async Task OverWrite(Module data)
            => await _book.OverWrite(new DataGetter(data));

        /// <summary>
        /// テンプレートを検索で読んだデータ (ModuleSearcher の ExecuteRaw 等) で埋める。記法は Module 版と同じ
        /// ($項目名.Value・#LoopRow($一覧.Rows, row) と $row.項目名.Value・$子.ChildModule.項目名.Value)。
        /// 解決するのはデータに載っている項目と FieldData のプロパティだけ (モジュールのスクリプトの変数・関数、サービス、画面のフィールドだけが持つメンバーは解決しない = 記号がそのまま残る)。
        /// </summary>
        public async Task OverWrite(ModuleData data)
            => await _book.OverWrite(new ModuleDataGetter(data, Services?.AppInfoService.GetDesignData()));

        /// <summary>xlsx のメールの添付にする。</summary>
        public MailAttachment ToAttachment(string fileName)
            => new() { FileName = fileName, ContentBase64 = Convert.ToBase64String(GetBytes()) };

        /// <summary>PDF に変換してメールの添付にする。変換は <see cref="ConvertPdf"/> (サーバーのホストが設定する)。設定されていなければ例外。</summary>
        public MailAttachment ToPdfAttachment(string fileName)
        {
            using var pdf = ToPdfMemoryStream();
            return new() { FileName = fileName, ContentBase64 = Convert.ToBase64String(pdf.ToArray()) };
        }

        /// <summary>xlsx の内容 (Job スクリプトの FileService.Save でレコードの添付ファイルにする等)。</summary>
        public MemoryStream ToMemoryStream() => new(GetBytes());

        /// <summary>PDF に変換した内容。変換は <see cref="ConvertPdf"/> (サーバーのホストが設定する)。設定されていなければ例外。</summary>
        public MemoryStream ToPdfMemoryStream()
        {
            var convert = ConvertPdf ?? throw new InvalidOperationException("このアプリでは PDF に変換できません (Excel.ConvertPdf が設定されていません)");
            //変換は渡したストリームを閉じることがあるので、本の保存先ではなく写しを渡す
            using var stream = new MemoryStream(GetBytes());
            //変換が返すストリームの位置や状態によらず、先頭から読める写しを返す
            using var pdf = convert(stream);
            return new MemoryStream(pdf.ToArray());
        }

        public ExcelCellIndex? FindCellByText(string text)
        {
            var texts = _book.Worksheet(1).ReadAllTexts();
            for (int i = 0; i < texts.Count; i++)
            {
                for (int j = 0; j < texts[i].Count; j++)
                {
                    if (texts[i][j].Trim() == text)
                    {
                        return new ExcelCellIndex { RowIndex = i + 1, ColumnIndex = j + 1 };
                    }
                }
            }
            return null;
        }

        public void SetCellValue(ExcelCellIndex cell, object value)
        {
            var sheet = _book.Worksheets.First();
            sheet.Cell(cell.RowIndex, cell.ColumnIndex).SetValue(XLCellValue.FromObject(value));
        }

        public void CopyCells(ExcelCellIndex source, ExcelCellIndex destination, int rowCount, int colCount)
        {
            var sheet = _book.Worksheets.First();
            var rangeToCopy = sheet.Range(source.RowIndex, source.ColumnIndex, source.RowIndex + rowCount, source.ColumnIndex + colCount);
            rangeToCopy.CopyTo(sheet.Cell(destination.RowIndex, destination.ColumnIndex));
        }

        public void AddImage(ExcelCellIndex cellIndex, Stream stream)
        {
            var sheet = _book.Worksheets.First();
            var image = sheet.AddPicture(stream);
            image.MoveTo(sheet.Cell(cellIndex.RowIndex, cellIndex.ColumnIndex), 2, 2);
        }

        public async Task<bool> Download()
        {
            var stream = GetStream();
            if (stream == null) return false;
            return await Services!.UIService.DownloadFile(stream, Path.GetFileNameWithoutExtension(_fileName) + ".xlsx");
        }

        public async Task<bool> DownloadPdf()
        {
            var stream = GetStream();
            if (stream == null) return false;

            //差し替え (ConvertPdf) があればそれ、変換専用のルート (ConvertPdfEndPoint) があればそこへ POST、どちらも無ければ共通の口 (ExtrasTransport) の ConvertPdf
            MemoryStream pdfStream;
            if (ConvertPdf != null)
            {
                pdfStream = ConvertPdf(stream);
            }
            else if (!string.IsNullOrEmpty(ConvertPdfEndPoint))
            {
                if (Http == null) return false;
                var result = await Http.PostContent(ConvertPdfEndPoint, new StreamContent(stream));
                if (result == null) return false;
                pdfStream = (MemoryStream)await result.Content.ReadAsStreamAsync();
            }
            else
            {
                if (!Api.ExtrasTransport.CanSend(Http)) return false;
                var (response, _) = await Api.ExtrasTransport.CallAsync<ExcelConvertPdfResponse>(Http, ExcelApiNames.Feature, ExcelApiNames.ConvertPdf, new ExcelConvertPdfRequest { Content = stream.ToArray() });
                if (response == null) return false;
                pdfStream = new MemoryStream(response.Content);
            }
            return await Services!.UIService.DownloadFile(pdfStream, Path.GetFileNameWithoutExtension(_fileName) + ".pdf");
        }

        [ScriptHide]
        public byte[] GetBytes()
        {
            //保存先のストリームは閉じない (ClosedXML は保存した後の本をそのストリームから読むので、閉じると 2 回目の保存が失敗する)
            return GetStream()?.ToArray() ?? Array.Empty<byte>();
        }

        MemoryStream? GetStream()
        {
            var newStream = new MemoryStream();
            _book.SaveAs(newStream);
            newStream.Seek(0, SeekOrigin.Begin);
            return newStream;
        }
    }
}
