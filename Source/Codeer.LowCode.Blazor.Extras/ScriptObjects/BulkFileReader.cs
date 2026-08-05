using Codeer.LowCode.Blazor.Extras.BulkFile;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// スクリプトからの一括ファイル取込。new BulkFileReader&lt;XXXModule&gt;() のイディオム
    /// (モジュール名が ctor に渡る。コアの AddModuleGenericType 登録) で使う。
    /// Read() = ファイル選択 → アップロード → サーバー解析 (CSV/固定長/列マッピング/コード変換の既存パイプライン)。
    /// 結果は Items (解析済みモジュール列)・HasError/ErrorCount/ErrorText (解釈できなかったセルの情報) に載る。
    /// スクリプトはエラーの有無だけを見て、詳細は DownloadErrorText() でユーザーにテキストで渡すのが基本形。
    /// DB には書き込まない (書き込みは行を加工したうえで this.Submit(Items) で行う)。
    /// このオブジェクトを使うアプリはサーバー側の対応実装 (parse_file = BulkFileTransfer.ParseFileAsync への移譲) と、
    /// アプリ初期化での ParseFileEndPoint 設定が必要。
    /// </summary>
    public class BulkFileReader
    {
        /// <summary>
        /// サーバーの解析エンドポイント。URL はアプリ (Controller を持つ側) の持ち物なので
        /// アプリの初期化 (テンプレートの ServiceInitializer) で設定する。未設定なら Read() は false を返す。
        /// </summary>
        [ScriptHide]
        public static string ParseFileEndPoint { get; set; } = string.Empty;

        readonly string _moduleName;

        [ScriptHide, ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        public BulkFileReader(string moduleDesignName) => _moduleName = moduleDesignName;

        /// <summary>取込先モジュール名 (ジェネリック引数で指定したモジュール)。</summary>
        public string ModuleName => _moduleName;

        /// <summary>Read() で解析したモジュール列 (ファイルの行順)。解釈できなかったセルは値未設定+フィールドエラー。</summary>
        public List<Module> Items { get; private set; } = [];

        /// <summary>Read() で解釈できなかったセルがあったか。</summary>
        public bool HasError => ErrorCount > 0;

        /// <summary>解釈できなかったセルの件数。</summary>
        public int ErrorCount { get; private set; }

        /// <summary>解釈できなかったセルの詳細 (行番号・列・内容の一覧テキスト)。</summary>
        public string ErrorText { get; private set; } = string.Empty;

        /// <summary>
        /// ファイルを選択して解析する。戻り値は「ファイルを選択して解析したか」(キャンセル/未設定環境は false)。
        /// 結果は Items / HasError / ErrorText に載る。
        /// </summary>
        public async Task<bool> Read()
        {
            Items = [];
            ErrorCount = 0;
            ErrorText = string.Empty;

            if (Services == null || string.IsNullOrEmpty(_moduleName)) return false;
            if (string.IsNullOrEmpty(ParseFileEndPoint)) return false;
            if (Services.AppInfoService.IsDesignMode) return false;
            var js = Services.Provider.GetService<IJSRuntime>();
            var http = Services.Provider.GetService<Codeer.LowCode.Blazor.Extras.Services.IHttpService>();
            if (js == null || http == null) return false;

            //ファイル選択 (キャンセルは false)
            await using var interop = await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Codeer.LowCode.Blazor.Extras/bulkfilereader-interop.js");
            var picked = await interop.InvokeAsync<PickedFile?>("pickFile", "");
            if (picked == null) return false;

            //サーバー解析 (CSV/固定長/xlsx 自動判定 + 列マッピング/コード変換)
            using var content = new ByteArrayContent(Convert.FromBase64String(picked.ContentBase64));
            var result = await http.PostContentAsJsonAsync<BulkFileParseResult>(
                $"{ParseFileEndPoint}?moduleName={_moduleName}", content);
            if (result == null) return false;

            //モジュール化 + 解釈できなかったセルをフィールドエラーに載せる
            var modules = new List<Module>();
            foreach (var data in result.Items)
            {
                modules.Add(await ModuleCreationService.CreateModuleAsync(Services, data));
            }
            foreach (var e in result.Errors)
            {
                if (e.ItemIndex < 0 || modules.Count <= e.ItemIndex) continue;
                modules[e.ItemIndex].GetField(e.FieldName)?.SetError(e.Message);
            }

            Items = modules;
            ErrorCount = result.Errors.Count;
            ErrorText = string.Join(Environment.NewLine,
                result.Errors.Select(e => $"Row {e.FileRow}, {e.ColumnLabel}: {e.Message}"));
            return true;
        }

        /// <summary>直前の Read() のエラー詳細をテキストファイルとしてダウンロードする (エラーが無ければ何もしない)。</summary>
        public async Task DownloadErrorText()
        {
            if (Services == null || !HasError) return;
            var stream = new MemoryStream(new UTF8Encoding(true).GetBytes(ErrorText));
            await Services.UIService.DownloadFile(stream, $"{_moduleName}_errors.txt");
        }

        class PickedFile
        {
            public string Name { get; set; } = string.Empty;
            public string ContentBase64 { get; set; } = string.Empty;
        }
    }
}
