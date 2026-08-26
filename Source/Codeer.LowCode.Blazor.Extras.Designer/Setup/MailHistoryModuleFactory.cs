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
        /// <param name="detailModuleName">送信明細モジュール名。空 = 明細を持たない (履歴契約の Details も空)。</param>
        internal static ModuleDesign Create(string moduleName, string dataSourceName, string userModuleName, string detailModuleName = "")
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
            var hasDetails = !string.IsNullOrEmpty(detailModuleName);
            if (hasDetails)
            {
                //明細一覧: 明細モジュールを History (Link) = 自分の Id で絞る
                module.Fields.Add(new ListFieldDesign
                {
                    Name = "Details",
                    SearchCondition = new SearchCondition(detailModuleName)
                    {
                        Condition = new FieldVariableMatchCondition
                        {
                            SearchTargetVariable = "History.Value",
                            Comparison = MatchComparison.Equal,
                            Variable = "Id.Value",
                        },
                    },
                });
            }
            module.Fields.Add(new MailHistoryContractFieldDesign { Name = "Contract", Details = hasDetails ? "Details" : string.Empty });

            //詳細: [ラベル(140px) | フィールド] の行を縦に並べる。明細一覧は末尾に全幅で置く
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
            if (hasDetails)
            {
                grid.Rows.Add(new GridRow
                {
                    Margin = new ThicknessDesign { Top = 8 },
                    Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "Details" } } }
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

        /// <summary>
        /// 送信明細モジュール (1 宛先 1 行)。履歴モジュールの Details 一覧の参照先。
        /// フィールド構成は MailHistoryDetailContractFieldDesign の既定役割に一致させる。
        /// </summary>
        internal static ModuleDesign CreateDetail(string moduleName, string historyModuleName, string dataSourceName, string userModuleName)
        {
            var module = new ModuleDesign
            {
                Name = moduleName,
                DataSourceName = dataSourceName,
                DbTable = Pluralize(ToSnakeCase(moduleName)),
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
                ("宛先", new TextFieldDesign { Name = "To", DbColumn = "to_address" }),
                ("件名", new TextFieldDesign { Name = "Subject", DbColumn = "subject" }),
                ("本文", new TextFieldDesign { Name = "Body", DbColumn = "body", IsMultiline = true, IsAutoFitRows = true }),
                ("送信成否", new BooleanFieldDesign { Name = "IsSuccess", DbColumn = "is_success" }),
                ("失敗理由", new TextFieldDesign { Name = "Error", DbColumn = "error" }),
            };

            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            module.Fields.Add(new LinkFieldDesign
            {
                Name = "History",
                DbColumn = "history_id",
                SearchCondition = new SearchCondition(historyModuleName),
            });
            foreach (var (label, field) in rows)
            {
                module.Fields.Add(field);
                module.Fields.Add(new LabelFieldDesign { Name = field.Name + "Label", Text = label });
            }
            module.Fields.Add(new MailHistoryDetailContractFieldDesign { Name = "Contract" });

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

            //一覧 (履歴の詳細に埋め込まれる): 宛先・件名・成否。本文は行を開いて見る
            module.ListLayouts[string.Empty] = new ListLayoutDesign
            {
                Elements =
                [
                    [
                        new ListElement { FieldName = "To", Label = "宛先" },
                        new ListElement { FieldName = "Subject", Label = "件名" },
                        new ListElement { FieldName = "IsSuccess", Label = "成否" },
                        new ListElement { FieldName = "Error", Label = "失敗理由" },
                    ]
                ],
            };
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
