using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField のインターセプタ (ホストの ModuleDataIO に登録する)。
    /// <list type="bullet">
    /// <item>保存: 対象フィールド (SourceFields) が変わった保存だけ、保存が終わった後に行をサーバーで読み直し (権限を通さない内部読み = 保存した人がこのフィールドや
    /// 対象フィールドを読めなくても索引は最新になる)、文章と埋め込みベクトルを書き込み専用列だけに書く (同じトランザクション。レコードの更新ではないので
    /// 更新日時・楽観ロックの版・ExecuteSqlField は動かない)。対象 = 追加した行・対象フィールドを送ってきた更新の行・対象フィールドの従属レコード (タグ付けの行など) を
    /// 付け外しした送信の主レコード。送信データにこのフィールドのデータが付いていても (旧結線の ApplyAsync など) 保存した行から作り直す (索引はいつもサーバーが作る)。
    /// 埋め込みの失敗は文章だけ保存して保存を止めないが、索引列の書き込みの失敗は保存の失敗 (DB の失敗は同じトランザクションなので握れない)</item>
    /// <item>条件の前処理: 文章だけの意味検索の条件 (<see cref="SemanticMatchCondition.Text"/> あり・Vector 空。画面の検索欄が作る) に、索引付けと同じ埋め込みプロバイダでベクトルを入れる。
    /// 検索の中身 (SQL・権限) は本体と SemanticSearchFieldDesign のまま変えない</item>
    /// </list>
    /// 読み出しの後処理には関わらない (素通し)。
    /// </summary>
    internal sealed class SemanticSearchInterceptor(SemanticSearchService service) : IModuleDataIOInterceptor
    {
        //索引を作る行: 送信 (結果と突き合わせる番号)・行のデータ (主レコードが送信に無ければ null)・モジュール・Id フィールド・作り直すフィールド・送信時の Id
        sealed record Target(int SubmitIndex, ModuleSubmitData Submit, ModuleData? Data, ModuleDesign Module, IdFieldDesign IdField, List<SemanticSearchFieldDesign> Fields, string Id);

        public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIOInternalAccess io, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            var design = service.GetDesign();
            var targets = new List<Target>();
            var assigned = new Dictionary<int, string>();
            for (var i = 0; i < transactionData.Count; i++)
            {
                var submit = transactionData[i];
                if (submit.Add.Count == 0 && submit.Update.Count == 0 && submit.Delete.Count == 0 && submit.SearchDelete.Count == 0) continue;
                if (AssignTemporaryIdToRootAdd(design, submit) is { } tempId) assigned[i] = tempId;
                //主レコードの対象フィールドの従属レコード (タグ付けの行など) を付け外しする送信か
                var childModules = OwnedChildModules(design, submit.ModuleName);
                var childTouched = childModules.Count > 0 && submit.Add.Concat(submit.Update).Select(e => e.Name).Concat(submit.Delete.Select(e => e.ModuleName)).Concat(submit.SearchDelete.Select(e => e.ModuleName)).Any(childModules.Contains);

                var hasRoot = false;
                foreach (var data in submit.Add)
                {
                    var target = Create(design, i, submit, data, changedOnly: false, childTouched);
                    if (target == null) continue;
                    targets.Add(target);
                    if (IsRoot(target)) hasRoot = true;
                }
                foreach (var data in submit.Update)
                {
                    var target = Create(design, i, submit, data, changedOnly: true, childTouched);
                    if (target == null) continue;
                    targets.Add(target);
                    if (IsRoot(target)) hasRoot = true;
                }
                //主レコード自身は Update に載らない保存 (タグ付け行だけを足した・外した等) でも、主レコードの文章は変わる
                if (!hasRoot && childTouched && !string.IsNullOrEmpty(submit.Id) && Create(design, i, submit, null, false, true, submit.ModuleName, submit.Id) is { } root) targets.Add(root);
            }

            var results = await next();
            //自分が付けた仮 Id は応答に出さない (一括 INSERT は仮 Id を解決せずそのまま返す)
            foreach (var (i, tempId) in assigned)
            {
                if (i >= results.Count) break;
                if (results[i].SourceId == tempId) results[i].SourceId = string.Empty;
                if (results[i].DestinationId == tempId) results[i].DestinationId = string.Empty;
            }
            if (targets.Count == 0 || results.Count != transactionData.Count || results.Any(r => !string.IsNullOrEmpty(r.ExceptionMessage))) return results;
            await IndexAsync(io, design, targets, results);
            return results;
        }

        //行の索引の対象。changedOnly (更新の行) は対象フィールドを送ってきたフィールドだけ (主レコードで従属を付け外しした送信なら全部)
        static Target? Create(DesignData design, int submitIndex, ModuleSubmitData submit, ModuleData? data, bool changedOnly, bool childTouched, string? moduleName = null, string? id = null)
        {
            var module = design.Modules.Find(moduleName ?? data!.Name);
            if (module == null) return null;
            var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).ToList();
            if (fields.Count == 0) return null;
            var idField = IdField(module);
            if (idField == null) return null;
            if (data != null)
            {
                if (!data.Fields.TryGetValue(idField.Name, out var idData) || idData is not IdFieldData { Value: { Length: > 0 } value }) return null;
                id = value;
            }
            var target = new Target(submitIndex, submit, data, module, idField, fields, id!);
            if (changedOnly && data != null && !(IsRoot(target) && childTouched))
            {
                target = target with { Fields = fields.Where(f => SemanticSearchText.SourceFields(module, f).Any(data.Fields.ContainsKey)).ToList() };
                if (target.Fields.Count == 0) return null;
            }
            return target;
        }

        static IdFieldDesign? IdField(ModuleDesign module)
            => module.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id);

        //主レコードの対象フィールドのうち、従属レコードを持つもの (TagField など) の子モジュール名
        static HashSet<string> OwnedChildModules(DesignData design, string moduleName)
        {
            var result = new HashSet<string>();
            var module = design.Modules.Find(moduleName);
            if (module == null) return result;
            foreach (var field in module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns))
            {
                foreach (var name in SemanticSearchText.SourceFields(module, field))
                {
                    if (module.Fields.FirstOrDefault(f => f.Name == name) is not IOwnedRecordsFieldDesign owned) continue;
                    foreach (var declaration in owned.GetOwnedRecords(design.Modules))
                        if (!string.IsNullOrEmpty(declaration.Condition.ModuleName)) result.Add(declaration.Condition.ModuleName);
                }
            }
            return result;
        }

        //主レコード (送信のモジュールと Id。Id 手打ち変更 "@replace:旧->新" の主レコードも)
        static bool IsRoot(Target target)
            => target.Module.Name == target.Submit.ModuleName
                && (target.Id == target.Submit.Id || (target.Data != null && target.Id.StartsWith("@replace:") && ReplaceSource(target.Id) == target.Submit.Id));

        static string ReplaceSource(string id)
        {
            var arrow = id.LastIndexOf("->", StringComparison.Ordinal);
            return arrow < 0 ? string.Empty : id["@replace:".Length..arrow];
        }

        //新規の主レコードに Id が無い (ファイル取込・スクリプトの一括保存) なら仮 Id を付けて、保存後に採番された Id を結果から引けるようにする (編集履歴と同じ)。
        //主モジュールの Id 無しの行が複数あれば全部に付け、最初の行を送信の主レコードにする。送信の Id に入れた仮 Id を返す
        static string? AssignTemporaryIdToRootAdd(DesignData design, ModuleSubmitData submit)
        {
            if (!string.IsNullOrEmpty(submit.Id)) return null;
            var module = design.Modules.Find(submit.ModuleName);
            if (module == null || !module.Fields.OfType<SemanticSearchFieldDesign>().Any(f => f.HasColumns)) return null;
            var idField = IdField(module);
            if (idField == null) return null;
            string? first = null;
            foreach (var root in submit.Add.Where(e => e.Name == submit.ModuleName && (e.Fields.GetValueOrDefault(idField.Name) as IdFieldData)?.Value is null or ""))
            {
                var tempId = IdFieldData.NewId();
                root.Fields[idField.Name] = tempId;
                first ??= tempId.Value;
            }
            if (first == null) return null;
            submit.Id = first;
            return first;
        }

        //保存した行を読み直して索引を書く
        async Task IndexAsync(ModuleDataIOInternalAccess io, DesignData design, List<Target> targets, List<ModuleSubmitResult> results)
        {
            var rows = new List<(Target Target, ModuleData Row, ModuleData Update, List<SemanticSearchFieldDesign> Fields)>();
            var done = new HashSet<(string Module, string Id)>();
            var warned = new HashSet<string>();
            foreach (var target in targets)
            {
                //送信データにこのフィールドのデータが付いていても (旧結線の ApplyAsync など) 保存した行から作り直す = 索引はいつもサーバーが作る
                var fields = target.Fields;
                var id = ResolveId(target, results[target.SubmitIndex]);
                if (id == null)
                {
                    //主レコードの Id が引けない = 一括 INSERT (採番 Id を返さない)。ApplyAsync の文章も無ければホストの BulkAddAsync の結線漏れ
                    if (IsRoot(target) && target.Data != null && fields.Any(f => !target.Data.Fields.ContainsKey(f.Name)) && warned.Add(target.Module.Name)) service.WarnBulkAddNotIndexed(target.Module.Name);
                    continue;
                }
                if (!done.Add((target.Module.Name, id))) continue;
                //保存時と再索引で同じ読み方 (権限を通さない。従属レコードは文章に使う宣言だけ)。論理削除した行は読めない = 索引しない
                var row = await SemanticSearchService.ReadForIndexAsync(io, target.Module, id, fields);
                if (row == null || !row.Fields.ContainsKey(target.IdField.Name)) continue;
                var update = new ModuleData { Name = row.Name };
                update.Fields[target.IdField.Name] = row.Fields[target.IdField.Name];
                rows.Add((target, row, update, fields));
            }
            if (rows.Count == 0) return;

            //フィールドごとに、行の文章をまとめて 1 回で埋め込む (失敗は警告ログを出して文章だけ書く = 再索引で埋められる)
            foreach (var group in rows.SelectMany(r => r.Fields.Select(f => (r.Row, r.Update, r.Target.Module, Field: f))).GroupBy(e => e.Field))
            {
                var items = group.ToList();
                var texts = items.Select(e => SemanticSearchText.Build(design, e.Module, e.Row, e.Field)).ToList();
                var vectors = await service.EmbedForSaveAsync(texts, CancellationToken.None);
                for (var i = 0; i < items.Count; i++)
                    items[i].Update.Fields[group.Key.Name] = new SemanticSearchFieldData { Text = texts[i], Vector = vectors[i] == null ? null : SemanticSearchVector.Encode(vectors[i]!) };
            }
            //書き込み専用列だけを書く (レコードの更新ではない)。失敗 (列の型が埋め込みの次元と合わない等) は保存の失敗にする
            //(同じトランザクションの DB の失敗は握れない: PostgreSQL は以後の文が全部失敗し、握ると保存が黙って消える)
            foreach (var (_, _, update, _) in rows) await io.UpdateWriteOnlyColumnsAsync(update);
        }

        //保存後の Id: 主レコードは結果の Id (仮 Id の採番・手打ちの Id の変更も反映済み)、他の行は仮 Id なら対応表で、Id 手打ち変更なら新しい Id。引けない (一括 INSERT など) なら null
        static string? ResolveId(Target target, ModuleSubmitResult result)
        {
            if (IsRoot(target))
                return string.IsNullOrEmpty(result.DestinationId) || IdFieldData.IsTemporaryId(result.DestinationId) ? null : result.DestinationId;
            if (IdFieldData.IsTemporaryId(target.Id)) return result.TemporaryIdMap.TryGetValue(target.Id, out var id) ? id : null;
            if (target.Id.StartsWith("@replace:"))
            {
                var arrow = target.Id.LastIndexOf("->", StringComparison.Ordinal);
                return arrow < 0 ? null : target.Id[(arrow + 2)..];
            }
            return target.Id;
        }

        public Task GetListAsync(ModuleDataIOInternalAccess io, SearchCondition condition, Paging<ModuleData> result)
            => Task.CompletedTask;

        public async Task PrepareConditionAsync(ModuleDataIOInternalAccess io, ModuleMatchCondition condition)
        {
            var module = service.GetDesign().Modules.Find(condition.ModuleName);
            var targets = Collect(condition.Condition)
                .Where(e => e.Vector.Length == 0 && !string.IsNullOrWhiteSpace(e.Text)
                    && module?.Fields.FirstOrDefault(f => f.Name == e.FieldName) is SemanticSearchFieldDesign { HasColumns: true })
                .ToList();
            if (targets.Count == 0) return;

            var provider = service.GetProvider() ?? throw LowCodeException.Create(Resources.SemanticSearch_ProviderNotConfigured);
            var texts = targets.Select(e => e.Text.Trim()).Distinct().ToList();
            var vectors = await provider.EmbedAsync(texts);
            var byText = texts.Select((text, i) => (text, vector: vectors[i])).ToDictionary(e => e.text, e => e.vector);
            foreach (var target in targets) target.Vector = byText[target.Text.Trim()];
        }

        static IEnumerable<SemanticMatchCondition> Collect(MatchConditionBase? condition)
        {
            switch (condition)
            {
                case SemanticMatchCondition semantic:
                    yield return semantic;
                    break;
                case MultiMatchCondition multi:
                    foreach (var child in multi.Children)
                        foreach (var e in Collect(child)) yield return e;
                    break;
            }
        }
    }
}
