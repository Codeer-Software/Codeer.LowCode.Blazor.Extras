using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField のインターセプタ (ホストの ModuleDataIO に登録する)。
    /// <list type="bullet">
    /// <item>保存: 索引を作り直す行は、送信の形ではなく本体が記録した「この保存で変更したレコード」(<see cref="SubmitContext.ChangedRecords"/>) で決める。
    /// 規則は 2 つ: (1) 追加した行・削除を取り消した行と、対象フィールド (SourceFields) を書いた更新の行 (一括 INSERT の行は除く = 取込の索引は取込後の ReindexMissing)。
    /// (2) 対象フィールドの従属レコード (タグ付けの行など) を追加・更新・削除・取り消しした、その親の行 (親は従属の宣言の束縛 (子の項目 = 親の項目) から引く。
    /// 消す子・更新する子は保存の前にも読んで、消える前 / 付け替える前の親を控える)。取込・内部書き込み・他の拡張が作り直した行も、本体を通って書かれていれば同じ規則で拾う。
    /// 保存が終わった後に行をサーバーで読み直し (権限を通さない内部読み = 保存した人がこのフィールドや対象フィールドを読めなくても索引は最新になる)、
    /// 文章と埋め込みベクトルを書き込み専用列だけに書く (同じトランザクション。レコードの更新ではないので更新日時・楽観ロックの版・ExecuteSqlField は動かない)。
    /// 索引の列はサーバーだけが書く: 送信にこのフィールドのデータが載っていても保存の前に捨てる。
    /// 埋め込みの失敗は文章だけ保存して保存を止めないが、索引列の書き込みの失敗は保存の失敗 (DB の失敗は同じトランザクションなので握れない)。
    /// 失敗は本体のインターセプタの約束どおり結果の ExceptionMessage で返す (例外のまま投げると外側のインターセプタ (監査ログ) には成功に見える)</item>
    /// <item>条件の前処理: 文章だけの意味検索の条件 (<see cref="SemanticMatchCondition.Text"/> あり・Vector 空。画面の検索欄が作る) に、索引付けと同じ埋め込みプロバイダでベクトルを入れる。
    /// 検索の中身 (SQL・権限) は本体と SemanticSearchFieldDesign のまま変えない</item>
    /// </list>
    /// 読み出しの後処理には関わらない (素通し)。
    /// </summary>
    internal sealed class SemanticSearchInterceptor(SemanticSearchService service) : IModuleDataIOInterceptor
    {
        //従属レコードの宣言 1 つ: 子モジュールの行から親 (索引を作り直す行) を引くための束縛 (ChildVariable (子の項目) = ParentVariable (親の項目)。複数ならすべて AND)
        sealed record OwnedParent(ModuleDesign Parent, SemanticSearchFieldDesign Field, IReadOnlyList<(string ChildVariable, string ParentVariable)> Bindings);

        //索引を作り直す行 (モジュール・Id → 作り直すフィールド)
        sealed class Targets
        {
            readonly Dictionary<(string Module, string Id), (ModuleDesign Module, List<SemanticSearchFieldDesign> Fields)> _items = new();
            public IEnumerable<KeyValuePair<(string Module, string Id), (ModuleDesign Module, List<SemanticSearchFieldDesign> Fields)>> Items => _items;
            public int Count => _items.Count;
            public void Add(ModuleDesign module, string id, IEnumerable<SemanticSearchFieldDesign> fields)
            {
                if (string.IsNullOrEmpty(id)) return;
                if (!_items.TryGetValue((module.Name, id), out var target)) _items[(module.Name, id)] = target = (module, new List<SemanticSearchFieldDesign>());
                foreach (var field in fields) if (!target.Fields.Contains(field)) target.Fields.Add(field);
            }
        }

        public async Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIO io, SubmitContext context, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
        {
            var design = service.GetDesign();
            var access = io.CreateSystemAccess();
            //索引の列はサーバーだけが書く (クライアントが送ってきた文章・ベクトルは使わない)
            foreach (var data in transactionData.SelectMany(e => e.Add.Concat(e.Update)))
            {
                var module = design.Modules.Find(data.Name);
                if (module == null) continue;
                foreach (var field in module.Fields.OfType<SemanticSearchFieldDesign>()) data.Fields.Remove(field.Name);
            }
            //消える / 付け替わる従属レコードの親は、保存の前に読んで控える (消えた後は読めない・付け替えた後は元の親が分からない)
            var ownedParents = OwnedParents(design);
            var targets = new Targets();
            try
            {
                if (ownedParents.Count > 0) await AddParentsBeforeSaveAsync(access, ownedParents, transactionData, targets);
            }
            catch (Exception ex) when (ex is not LowCodeAccessDeniedException)
            {
                return Fail(transactionData, ex);
            }

            var results = await next();
            if (results.Count != transactionData.Count || results.Any(r => !string.IsNullOrEmpty(r.ExceptionMessage))) return results;

            try
            {
                var changedRecords = context.ChangedRecords;
                await CollectTargetsAsync(access, design, changedRecords, ownedParents, targets);
                if (targets.Count > 0) await IndexAsync(access, design, targets);
            }
            catch (Exception ex) when (ex is not LowCodeAccessDeniedException)
            {
                return Fail(transactionData, ex);
            }
            return results;
        }

        //索引付けの失敗 (索引列の書き込み失敗など) は結果の ExceptionMessage で返す (本体がロールバックする)。
        //例外のまま投げると本体が最外で ExceptionMessage にするので、外側のインターセプタ (監査ログ) からは成功に見える。権限の拒否だけは型のまま外側へ上げる
        List<ModuleSubmitResult> Fail(List<ModuleSubmitData> transactionData, Exception ex)
        {
            service.LogIndexFailed(ex);
            return transactionData.Select(e => new ModuleSubmitResult { SourceId = e.Id, DestinationId = e.Id, ExceptionMessage = ex.Message }).ToList();
        }

        //本体が記録した変更から、作り直す行を決める
        static async Task CollectTargetsAsync(ModuleDataIOSystemAccess io, DesignData design, IReadOnlyList<ChangedRecord> changedRecords, Dictionary<string, List<OwnedParent>> ownedParents, Targets targets)
        {
            foreach (var changed in changedRecords)
            {
                var module = design.Modules.Find(changed.ModuleName);
                if (module == null) continue;

                //(1) 追加した行・削除を取り消した行は全部、更新した行は対象フィールドを書いたフィールドだけ。
                //一括 INSERT の行 (手入力 Id) は除く = 一括取込は Id の付け方によらず索引を付けない (取込後の ReindexMissing。大量取込の埋め込みを保存の中で待たない)
                if (changed.Kind != ChangedRecordKind.Delete && !changed.IsBulkInsert)
                {
                    var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns)
                        .Where(f => changed.Kind != ChangedRecordKind.Update || SemanticSearchText.SourceFields(module, f).Any(changed.FieldNames.Contains)).ToList();
                    if (fields.Count > 0) targets.Add(module, changed.Id, fields);
                }

                //(2) 対象フィールドの従属レコードを変えたら、その親 (消した子の親は保存の前に控えてある)。
                //一括 INSERT の行は (1) と同じく除く = 取込の親の索引は取込後の Reindex (大量取込の分の親の埋め込みを保存の中で待たない。DB 採番の行は Id も無い)
                if (changed.Kind != ChangedRecordKind.Delete && !changed.IsBulkInsert && !string.IsNullOrEmpty(changed.Id) && ownedParents.TryGetValue(changed.ModuleName, out var parents))
                {
                    var row = await io.GetWithOwnedRecordsAsync(changed.ModuleName, changed.Id, _ => false);
                    if (row != null) await AddParentsAsync(io, parents, row, targets);
                }
            }
        }

        //送信で消す従属レコード (Delete / SearchDelete) と更新する従属レコードの親を、保存の前に読んで控える (消える前 / 付け替える前の親)
        static async Task AddParentsBeforeSaveAsync(ModuleDataIOSystemAccess io, Dictionary<string, List<OwnedParent>> ownedParents, List<ModuleSubmitData> transactionData, Targets targets)
        {
            foreach (var submit in transactionData)
            {
                foreach (var (moduleName, id) in submit.Delete.Select(e => (e.ModuleName, e.Id))
                             .Concat(submit.Update.Select(e => (e.Name, (e.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty))))
                {
                    if (!ownedParents.TryGetValue(moduleName, out var parents) || string.IsNullOrEmpty(id) || IdFieldData.IsTemporaryId(id)) continue;
                    var row = await io.GetWithOwnedRecordsAsync(moduleName, id, _ => false);
                    if (row != null) await AddParentsAsync(io, parents, row, targets);
                }
                foreach (var searchDelete in submit.SearchDelete)
                {
                    if (!ownedParents.TryGetValue(searchDelete.ModuleName, out var parents)) continue;
                    var condition = searchDelete.JsonClone();
                    condition.SelectFields.Clear();
                    for (var pageIndex = 0; ; pageIndex++)
                    {
                        var page = await io.GetListAsync(condition, pageIndex);
                        foreach (var row in page.Items) await AddParentsAsync(io, parents, row, targets);
                        if (pageIndex + 1 >= page.PageCount || page.Items.Count == 0) break;
                    }
                }
            }
        }

        //子の行から親の行を引いて対象に足す (子の項目 = 親の項目。束縛のどれかの親の項目が Id ならその値そのもの、そうでなければ束縛すべてで親を検索)
        static async Task AddParentsAsync(ModuleDataIOSystemAccess io, List<OwnedParent> parents, ModuleData childRow, Targets targets)
        {
            foreach (var parent in parents)
            {
                var values = parent.Bindings
                    .Select(b => (b.ParentVariable, Value: ValueOf(childRow, new VariableName(b.ChildVariable).FieldName.FullName)))
                    .Where(e => e.Value != null).ToList();
                if (values.Count != parent.Bindings.Count) continue;
                if (values.FirstOrDefault(e => new VariableName(e.ParentVariable).FieldName.FullName == SystemFieldNames.Id) is { Value: { } parentId })
                {
                    targets.Add(parent.Parent, parentId.ToString() ?? string.Empty, [parent.Field]);
                    continue;
                }
                var condition = new SearchCondition(parent.Parent.Name) { SelectFields = [SystemFieldNames.Id] };
                var conditions = values.Select(e => (MatchConditionBase)new FieldValueMatchCondition { SearchTargetVariable = e.ParentVariable, Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(e.Value!) }).ToArray();
                condition.Condition = conditions.Length == 1 ? conditions[0] : MultiMatchCondition.And(conditions);
                for (var pageIndex = 0; ; pageIndex++)
                {
                    var page = await io.GetListAsync(condition, pageIndex);
                    foreach (var row in page.Items)
                        if (row.Fields.GetValueOrDefault(SystemFieldNames.Id) is IdFieldData { Value: { Length: > 0 } id }) targets.Add(parent.Parent, id, [parent.Field]);
                    if (pageIndex + 1 >= page.PageCount || page.Items.Count == 0) break;
                }
            }
        }

        //束縛の項目の値 (親を指すリンク・Id・数値など)。無ければ null
        static object? ValueOf(ModuleData row, string fieldName)
            => row.Fields.GetValueOrDefault(fieldName) switch
            {
                ValueFieldDataBase<string> s => string.IsNullOrEmpty(s.Value) ? null : s.Value,
                ValueFieldDataBase<decimal?> d => d.Value,
                ValueFieldDataBase<int?> i => i.Value,
                ValueFieldDataBase<long?> l => l.Value,
                ModuleFieldData m => string.IsNullOrEmpty(m.Id) ? null : m.Id,
                _ => null,
            };

        //対象フィールドのうち従属レコードを持つもの (TagField など) の宣言を、子モジュール名 → 親の引き方 に並べる
        static Dictionary<string, List<OwnedParent>> OwnedParents(DesignData design)
        {
            var result = new Dictionary<string, List<OwnedParent>>();
            foreach (var module in design.Modules.ToList())
            {
                foreach (var field in module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns))
                {
                    foreach (var name in SemanticSearchText.SourceFields(module, field))
                    {
                        if (module.Fields.FirstOrDefault(f => f.Name == name) is not IOwnedRecordsFieldDesign owned) continue;
                        foreach (var declaration in owned.GetOwnedRecords(design.Modules))
                        {
                            var childModule = declaration.Condition.ModuleName;
                            var bindings = declaration.Condition.GetFieldVariableConditions().Select(b => (b.SearchTargetVariable, b.Variable)).ToList();
                            if (string.IsNullOrEmpty(childModule) || bindings.Count == 0) continue;
                            if (!result.TryGetValue(childModule, out var list)) result[childModule] = list = new List<OwnedParent>();
                            list.Add(new OwnedParent(module, field, bindings));
                        }
                    }
                }
            }
            return result;
        }

        //保存した行を読み直して索引を書く
        async Task IndexAsync(ModuleDataIOSystemAccess io, DesignData design, Targets targets)
        {
            var rows = new List<(ModuleDesign Module, ModuleData Row, ModuleData Update, List<SemanticSearchFieldDesign> Fields)>();
            foreach (var ((_, id), (module, fields)) in targets.Items)
            {
                //保存時と再索引で同じ読み方 (権限を通さない。従属レコードは文章に使う宣言だけ)。論理削除した行・消えた行は読めない = 索引しない
                var row = await SemanticSearchService.ReadForIndexAsync(io, module, id, fields);
                var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id);
                if (row == null || idField == null || !row.Fields.ContainsKey(idField.Name)) continue;
                var update = new ModuleData { Name = row.Name };
                update.Fields[idField.Name] = row.Fields[idField.Name];
                rows.Add((module, row, update, fields));
            }
            if (rows.Count == 0) return;

            //フィールドごとに、行の文章をまとめて 1 回で埋め込む (失敗は警告ログを出して文章だけ書く = 再索引で埋められる)
            foreach (var group in rows.SelectMany(r => r.Fields.Select(f => (r.Row, r.Update, r.Module, Field: f))).GroupBy(e => e.Field))
            {
                var items = group.ToList();
                var texts = new List<string>();
                foreach (var e in items) texts.Add(await service.BuildTextAsync(design, e.Module, e.Row, e.Field, CancellationToken.None));
                var vectors = await service.EmbedForSaveAsync(texts.Select(t => SemanticSearchText.ForEmbedding(group.Key, t)).ToList(), CancellationToken.None);
                for (var i = 0; i < items.Count; i++)
                    items[i].Update.Fields[group.Key.Name] = new SemanticSearchFieldData { Text = texts[i], Vector = vectors[i] == null ? null : SemanticSearchVector.Encode(vectors[i]!) };
            }
            //書き込み専用列だけを書く (レコードの更新ではない)。失敗 (列の型が埋め込みの次元と合わない等) は保存の失敗にする
            //(同じトランザクションの DB の失敗は握れない: PostgreSQL は以後の文が全部失敗し、握ると保存が黙って消える)
            foreach (var (_, _, update, _) in rows) await io.WriteWithoutSideEffectsAsync(update);
        }

        public Task GetListAsync(ModuleDataIO io, SearchCondition condition, Paging<ModuleData> result)
            => Task.CompletedTask;

        public async Task PrepareConditionAsync(ModuleDataIO io, ModuleMatchCondition condition)
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
