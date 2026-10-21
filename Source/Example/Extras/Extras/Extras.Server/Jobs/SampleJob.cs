using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Extras.Server.Services;

namespace Extras.Server.Jobs
{
    /// <summary>
    /// 例: dotnet Extras.Server.dll job Sample &lt;モジュール名&gt;
    /// 指定したモジュールの一覧を 1 ページ読んで、件数と先頭の Id を出す。
    /// </summary>
    public class SampleJob : IJob
    {
        public string Name => "Sample";

        public async Task<string?> RunAsync(DataService data, string[] args)
        {
            if (args.Length < 1) throw new ArgumentException("モジュール名を指定してください。例: job Sample Customer");

            //JobTriggerField (ボタン) から起動されたときは、画面の内容の JSON ({"data":...,"submit":[...]} / {}) が 1 つ渡る。
            //戻り値が画面の成功の通知になる
            if (args[0].StartsWith('[') || args[0].StartsWith('{'))
            {
                return $"JSON を受け取りました ({args[0].Length} 文字)";
            }

            var moduleName = args[0];
            var page = await data.ModuleDataIO.GetListAsync(new SearchCondition(moduleName), 0);

            foreach (var row in page.Items.Take(5))
            {
                Console.WriteLine($"  Id = {GetId(row)}");
            }
            return $"{moduleName}: {page.TotalCount} 件";
        }

        static string GetId(ModuleData row)
            => row.Fields.TryGetValue(SystemFieldNames.Id, out var id) && id is IdFieldData idData ? idData.Value ?? string.Empty : string.Empty;
    }
}
