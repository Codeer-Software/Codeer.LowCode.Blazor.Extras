using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// メール送信履歴モジュールのデザインを生成する。
    /// フィールド構成は MailHistoryContractFieldDesign の既定役割に一致させる
    /// (契約フィールド同梱なので、後からフィールド名を変えてもリネーム追従で契約が正になる)。
    /// 有効化はサーバー設定 appsettings の Mail.HistoryModuleName にこのモジュール名を設定する。
    /// </summary>
    internal static class MailHistoryModuleFactory
    {
        internal static ModuleDesign Create(string moduleName, string dataSourceName, string userModuleName)
        {
            var module = new ModuleDesign
            {
                Name = moduleName,
                DataSourceName = dataSourceName,
                DbTable = Pluralize(ToSnakeCase(moduleName)),
                //履歴はシステムの記録 (サーバー内部経路で書かれる)。画面からは誰も書けない
                UserWriteCondition = new ModuleMatchCondition
                {
                    ModuleName = userModuleName,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = "Id.Value",
                        Comparison = MatchComparison.Equal,
                        Value = new StringValue { Value = "__nobody__" },
                    },
                },
            };

            var rows = new List<(string Label, FieldDesignBase Field)>
            {
                ("送信日時", new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" }),
                ("送信インフラ", new TextFieldDesign { Name = "MailInfraName", DbColumn = "mail_infra_name" }),
                ("件名", new TextFieldDesign { Name = "Subject", DbColumn = "subject" }),
                ("送信数", new NumberFieldDesign { Name = "TotalCount", DbColumn = "total_count" }),
                ("成功数", new NumberFieldDesign { Name = "SuccessCount", DbColumn = "success_count" }),
                ("失敗明細", new TextFieldDesign { Name = "FailureDetails", DbColumn = "failure_details", IsMultiline = true, IsAutoFitRows = true }),
                ("送信元モジュール", new TextFieldDesign { Name = "SourceModule", DbColumn = "source_module" }),
                ("送信元Id", new TextFieldDesign { Name = "SourceId", DbColumn = "source_id" }),
            };

            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            foreach (var (label, field) in rows)
            {
                module.Fields.Add(field);
                module.Fields.Add(new LabelFieldDesign { Name = field.Name + "Label", Text = label });
            }
            module.Fields.Add(new MailHistoryContractFieldDesign { Name = "Contract" });

            //詳細: [ラベル(140px) | フィールド] の行を縦に並べる
            var grid = new GridLayoutDesign();
            foreach (var (_, field) in rows)
            {
                grid.Rows.Add(new GridRow
                {
                    Columns =
                    {
                        new GridColumn { Width = 140, Layout = new FieldLayoutDesign { FieldName = field.Name + "Label" } },
                        new GridColumn { Layout = new FieldLayoutDesign { FieldName = field.Name } },
                    }
                });
            }
            module.DetailLayouts[string.Empty] = new DetailLayoutDesign { Layout = grid };

            //一覧: 主要列。明細 (FailureDetails) は詳細で見る
            module.ListLayouts[string.Empty] = new ListLayoutDesign
            {
                Elements =
                [
                    [
                        new ListElement { FieldName = "SentAt", Label = "送信日時" },
                        new ListElement { FieldName = "Subject", Label = "件名" },
                        new ListElement { FieldName = "MailInfraName", Label = "送信インフラ" },
                        new ListElement { FieldName = "TotalCount", Label = "送信数" },
                        new ListElement { FieldName = "SuccessCount", Label = "成功数" },
                        new ListElement { FieldName = "SourceModule", Label = "送信元" },
                    ]
                ],
            };

            //一覧ページの新規/編集ボタンは UserWriteCondition (誰も書けない) により自動的に出ない
            return module;
        }

        static string Pluralize(string name)
        {
            if (name.EndsWith("y") && name.Length >= 2 && !"aeiou".Contains(name[^2])) return name[..^1] + "ies";
            if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh")) return name + "es";
            return name + "s";
        }

        internal static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
