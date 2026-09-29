using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryRecorder はテンプレートで常に登録されるので、編集履歴を使わないモジュール・使わないアプリの保存に影響しないこと。
    /// - 設計に編集履歴が無ければ素通し
    /// - 履歴の無いモジュールの保存は送信を書き換えない (一括 INSERT の申告 NoTemporaryIdResolution もそのまま)
    /// - 明細で互いに繋がったモジュールが多い設計でも、従属の宣言の列挙が経路の数だけ膨らまない
    /// </summary>
    public class EditHistoryRecorderOverheadTest
    {
        //100 モジュールが編集できる明細 3 つずつで互いに繋がった設計 (経路を全部列挙すると終わらない)
        static DesignData DenseDesign(bool withHistory)
        {
            var d = new DesignData();
            for (var m = 0; m < 100; m++)
            {
                var mod = new ModuleDesign { Name = "M" + m, DataSourceName = "Main", DbTable = "t" + m };
                mod.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
                mod.Fields.Add(new TextFieldDesign { Name = "F0", DbColumn = "f0" });
                for (var l = 0; l < 3; l++)
                {
                    mod.Fields.Add(new ListFieldDesign
                    {
                        Name = "L" + l, CanCreate = true, CanUpdate = true, CanDelete = true,
                        SearchCondition = new SearchCondition("M" + ((m + l + 1) % 100))
                        {
                            Condition = new FieldVariableMatchCondition { SearchTargetVariable = "F0.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                        },
                    });
                }
                d.AddModule(mod);
            }
            if (!withHistory) return d;

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = "Main", DbTable = "edit_histories", CanCreate = false, CanUpdate = false };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            foreach (var name in new[] { "ModuleName", "DataId", "ChangeType", "Snapshot" })
                history.Fields.Add(new TextFieldDesign { Name = name, DbColumn = name.ToLowerInvariant() });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract", UserId = string.Empty, DateTime = string.Empty });
            d.AddModule(history);
            //履歴を持つのは M1 だけ。M0 は持たない
            d.Modules.Find("M1")!.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            return d;
        }

        static List<ModuleSubmitData> BulkAdd(string moduleName, int count)
            => Enumerable.Range(0, count).Select(i =>
            {
                var row = new ModuleData { Name = moduleName };
                row.Fields["F0"] = new TextFieldData { Value = "x" + i };
                var submit = new ModuleSubmitData { ModuleName = moduleName, NoTemporaryIdResolution = true };
                submit.Add.Add(row);
                return submit;
            }).ToList();

        //時間内に終わること (終わらない処理でテスト全体を止めない)
        static T WithinSeconds<T>(int seconds, Func<T> action)
        {
            var task = Task.Run(action);
            Assert.That(task.Wait(TimeSpan.FromSeconds(seconds)), Is.True, $"{seconds} 秒以内に終わらない");
            return task.Result;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 履歴の無いモジュールの保存は送信を書き換えず本体の保存の結果をそのまま返す(bool withHistory)
        {
            var recorder = new EditHistoryRecorder(DenseDesign(withHistory));
            var transaction = BulkAdd("M0", 10000);
            var expected = transaction.Select(e => new ModuleSubmitResult()).ToList();
            var nextCalls = 0;

            //履歴の無いモジュールの保存では内部操作 (io) を使わない
            var results = WithinSeconds(20, () => recorder.SubmitAsync(null!, transaction, () =>
            {
                nextCalls++;
                return Task.FromResult(expected);
            }).GetAwaiter().GetResult());

            Assert.That(results, Is.SameAs(expected));
            Assert.That(nextCalls, Is.EqualTo(1));
            Assert.That(transaction.All(e => e.NoTemporaryIdResolution), Is.True, "一括 INSERT の申告を外さない");
            Assert.That(transaction.All(e => string.IsNullOrEmpty(e.Id)), Is.True, "仮 Id を付けない");
            Assert.That(transaction.All(e => e.Add.Single().Fields.Keys.SequenceEqual(new[] { "F0" })), Is.True);
        }

        [Test]
        public void 明細で互いに繋がった設計でも従属の宣言の列挙が膨らまない()
        {
            var design = DenseDesign(withHistory: true);
            var module = design.Modules.Find("M1")!;
            var field = module.Fields.OfType<EditHistoryFieldDesign>().Single();
            //除外の指定が途中にあっても同じ
            field.ExcludedOwnedRecords.Add("L0.L1");

            var all = WithinSeconds(20, () => EditHistoryPolicy.Walk(design, module, field, descendIntoNotIncluded: true).ToList());
            var included = WithinSeconds(20, () => EditHistoryPolicy.Walk(design, module, field).ToList());

            //指定したパスは宣言として見つかり、繋がったモジュールの宣言は全部出る
            Assert.That(all.Any(e => e.Path == "L0.L1"), Is.True);
            Assert.That(all.Select(e => (e.Module.Name, e.Field.Name)).Distinct().Count(), Is.EqualTo(300));
            //除外した宣言の先は辿らないが、他の経路で届くモジュールの宣言は出る
            Assert.That(included.Select(e => (e.Module.Name, e.Field.Name)).Distinct().Count(), Is.EqualTo(300));
            Assert.That(WithinSeconds(20, () => field.CheckDesign(new Codeer.LowCode.Blazor.DesignLogic.Check.DesignCheckContext("M1", design, Utilities.CreateDataSource())).Count), Is.GreaterThanOrEqualTo(0));
        }
    }
}
