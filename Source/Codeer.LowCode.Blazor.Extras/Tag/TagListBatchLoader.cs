using System.Runtime.CompilerServices;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>
    /// 一覧の行の TagField のタグ付け行を、ページの行の分まとめて 1 回で読む。
    /// 本体は一覧の行では子の一覧を読まない (行ごとに問い合わせが出るため)。行の TagField は初期化で登録だけして返り、
    /// 同じ流れで作られた行が揃ったところで「OwnerId In (行の Id)」を 1 回問い合わせて各行に配る。
    /// 待つのは次の実行の機会まで (Task.Yield)。行の作成の合間に本当に非同期の待ちがあれば何回かに分かれるが、結果は同じ。
    /// 本体側の口 B (一覧のページの行の子の一覧をまとめて 1 回で読む) ができるまでの回避策。口ができたらこのクラスごと置き換える。
    /// </summary>
    internal static class TagListBatchLoader
    {
        sealed class Batch
        {
            public List<TagField> Fields { get; } = new();
            public required Task Done { get; init; }
        }

        //アプリ (Services) ごと・タグ付けモジュールと TagField ごとに、集め中のまとめ
        static readonly ConditionalWeakTable<Codeer.LowCode.Blazor.RequestInterfaces.Services, Dictionary<string, Batch>> _pending = new();
        static readonly object _lock = new();

        internal static Task Register(TagField field)
        {
            var key = $"{field.Binding!.LinkModule}/{field.Module.Design.Name}/{field.Design.Name}";
            //ロックの中で登録まで済ませる (まとめの実行はロックを取ってから行を集めるので、最初の行より先には走らない)
            lock (_lock)
            {
                var pending = _pending.GetOrCreateValue(field.Services);
                if (!pending.TryGetValue(key, out var batch))
                {
                    batch = new Batch { Done = RunAsync(field, key) };
                    pending[key] = batch;
                }
                batch.Fields.Add(field);
                return batch.Done;
            }
        }

        static async Task RunAsync(TagField first, string key)
        {
            await Task.Yield();
            List<TagField> fields;
            lock (_lock)
            {
                var pending = _pending.GetOrCreateValue(first.Services);
                fields = pending.TryGetValue(key, out var batch) ? batch.Fields.ToList() : new() { first };
                pending.Remove(key);
            }

            var binding = first.Binding!;
            var owners = fields.Select(e => e.OwnerKey).Where(e => e.Length > 0).Distinct().ToList();
            var rows = new List<ModuleData>();
            if (owners.Count > 0)
            {
                try
                {
                    rows = await TagContracts.ReadAllAsync((condition, page) => ReadPageAsync(first.Services, condition, page),
                        TagContracts.LinkRowsCondition(binding, first.Design, owners));
                }
                catch (Exception e)
                {
                    await first.Services.Logger.Error(e.Message);
                    return;
                }
            }
            var byOwner = rows.GroupBy(e => TagContracts.OwnerId(e, binding)).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var field in fields)
            {
                await field.ApplyListRowsAsync(byOwner.TryGetValue(field.OwnerKey, out var mine) ? mine : new());
            }
        }

        /// <summary>クライアントのモジュールデータ API で 1 ページ読む。</summary>
        internal static async Task<Paging<ModuleData>?> ReadPageAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services services, SearchCondition condition, int pageIndex)
            => (await services.ModuleDataService.GetListAsync(new List<GetListRequest> { new() { Condition = condition, PageIndex = pageIndex } })).FirstOrDefault();
    }
}
