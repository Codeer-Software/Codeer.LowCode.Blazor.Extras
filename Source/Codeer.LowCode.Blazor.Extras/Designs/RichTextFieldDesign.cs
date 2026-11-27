using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    [Designer(DisplayName = "$RichTextField")]
    public class RichTextFieldDesign() : ValueFieldDesignBase(typeof(RichTextFieldDesign).FullName!)
    {
        /// <summary>
        /// デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。
        /// メンションは未公開のうちに方式を変えたので、番号は前の方式 (サーバーの API) から振り直している。
        /// </summary>
        private const int CodeMentionList = 1;
        private const int CodeMentionListNotAllRecords = 2;
        private const int CodeMentionListEditable = 3;
        private const int CodeMentionListModule = 4;
        private const int CodeMentionListRowField = 5;
        private const int CodeMentionModuleWritable = 6;
        private const int CodeMentionModuleLogicalDelete = 7;

        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$RichTextFieldDbColumn"), DbColumn(nameof(RichTextFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        /// <summary>
        /// メンションの候補の行を持つ一覧フィールド (同じモジュールの全体設定に置いた ListField) の名前。
        /// 空でなければメンションを使う (本文の中で @ を打つと候補を出して、選んだ人を埋め込める)。既定は空 = 使わない。
        /// 誰が候補になるかは、その一覧の検索条件と、一覧が読むモジュールの権限で決まる。
        /// </summary>
        [Designer(Index = 1, CandidateType = CandidateType.Field, DisplayName = "$RichTextFieldMentionListField")]
        [TargetFieldType(Types = [typeof(ListFieldDesign)])]
        public string MentionListField { get; set; } = string.Empty;

        /// <summary>メンションを使うか (候補の一覧 <see cref="MentionListField"/> が指定されているか)。</summary>
        internal bool UsesMention => !string.IsNullOrEmpty(MentionListField);

        /// <summary>一覧の行のうち、候補の表示名として出す項目 (一覧が読むモジュールのフィールド名)。</summary>
        [Designer(Index = 2, DisplayName = "$RichTextFieldMentionDisplayNameField")]
        public string MentionDisplayNameField { get; set; } = string.Empty;

        /// <summary>
        /// 一覧の行のうち、表示名の横に補助で出す項目 (一覧が読むモジュールのフィールド名)。絞り込みにも使い、表示名が空の人はこの値で出す。空なら出さない。
        /// </summary>
        [Designer(Index = 3, DisplayName = "$RichTextFieldMentionDisplayNameSubField")]
        public string MentionDisplayNameSubField { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(RichTextFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new RichTextField(this);

        public override FieldDataBase? CreateData() => new RichTextFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            //一覧が空 = メンションを使わない。そのときは表示名の項目などが入っていても見ない (使われないだけ)
            if (UsesMention) CheckMention(context, result);
            return result;
        }

        //候補は同じモジュールの一覧 (ListField) の行。一覧が候補の置き場として使える形か、一覧が読むモジュールがユーザーの表を見ているかを確かめる
        void CheckMention(DesignCheckContext context, List<DesignCheckInfo> result)
        {
            void Add(int code, string member, string message) => result.Add(new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), code),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = member },
                Message = message,
            });

            //:1 一覧そのもの (一覧が決まらないと、先は見られない)
            var ownerModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownerModule?.Fields.FirstOrDefault(e => e.Name == MentionListField) is not ListFieldDesign list)
            {
                Add(CodeMentionList, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListNotListField, MentionListField));
                return;
            }
            if (!string.IsNullOrEmpty(list.DbColumn))
            {
                Add(CodeMentionList, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListColumnMode, MentionListField));
                return;
            }
            var searchField = ownerModule.Fields.OfType<SearchFieldDesign>().FirstOrDefault(e => e.ResultsViewFieldName == MentionListField);
            if (searchField != null)
            {
                Add(CodeMentionList, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListBoundToSearchField, MentionListField, searchField.Name));
            }

            //:2 全部の行を持つ (本体の従属レコードの宣言の HoldsAllRecords と同じ判定)。上限で切れた先の人が候補に出なくなる
            if (list.SearchCondition.LimitCount is > 0 && !list.IsInMemoryPaging)
            {
                Add(CodeMentionListNotAllRecords, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListNotAllRecords, MentionListField));
            }

            //:3 行を編集しない・親と一緒に消さない (DeleteTogether だと、このモジュールのレコードを消したときに候補のユーザーの行が消える)
            if (list.CanCreate || list.CanUpdate || list.CanDelete || list.DeleteTogether)
            {
                Add(CodeMentionListEditable, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListEditable, MentionListField));
            }

            //:4 一覧が読むのはユーザーのモジュールか、同じ表を見る一般向けのユーザーモジュール (本文に埋める Id がユーザーの Id であるため)
            var userModuleName = context.DesignData.AppSettings.CurrentUserModuleDesignName;
            var userModule = string.IsNullOrEmpty(userModuleName) ? null : context.DesignData.Modules.Find(userModuleName);
            if (userModule == null)
            {
                Add(CodeMentionListModule, nameof(MentionListField), Properties.Resources.RichTextMentionCheck_NoUserModule);
                return;
            }
            var listModuleName = list.SearchCondition.ModuleName;
            var listModule = string.IsNullOrEmpty(listModuleName) ? null : context.DesignData.Modules.Find(listModuleName);
            if (listModule == null || !IsSameTable(listModule, userModule))
            {
                Add(CodeMentionListModule, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ListModule, MentionListField, listModuleName, userModuleName));
                return;
            }

            //:5 表示名・サブ表示名の項目 (行のレイアウトに置かれているかは見ない)
            if (string.IsNullOrEmpty(MentionDisplayNameField))
            {
                Add(CodeMentionListRowField, nameof(MentionDisplayNameField), Properties.Resources.RichTextMentionCheck_DisplayNameRequired);
            }
            foreach (var (member, field) in new[] { (nameof(MentionDisplayNameField), MentionDisplayNameField), (nameof(MentionDisplayNameSubField), MentionDisplayNameSubField) })
            {
                if (string.IsNullOrEmpty(field) || listModule.Fields.Any(e => e.Name == field)) continue;
                Add(CodeMentionListRowField, member, string.Format(Properties.Resources.RichTextMentionCheck_RowFieldNotFound, field, listModule.Name));
            }

            //:6 / :7 一般向けのユーザーモジュール (ユーザーのモジュールとは別の、同じ表を見るモジュール) に守ってもらう条件
            if (listModule.Name == userModule.Name) return;
            if (listModule.CanCreate || listModule.CanUpdate || listModule.CanDelete)
            {
                Add(CodeMentionModuleWritable, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ModuleWritable, listModule.Name, userModule.Name));
            }
            if (userModule.UsesLogicalDelete() && !HasSameLogicalDelete(listModule, userModule))
            {
                Add(CodeMentionModuleLogicalDelete, nameof(MentionListField), string.Format(Properties.Resources.RichTextMentionCheck_ModuleLogicalDelete, listModule.Name, userModule.Name));
            }
        }

        //同じデータソース・同じ表・同じ Id の列 (表と列の名前は DB の識別子なので大文字と小文字を区別しない)
        static bool IsSameTable(ModuleDesign module, ModuleDesign userModule)
        {
            if (module.Name == userModule.Name) return true;
            var id = module.Fields.OfType<IdFieldDesign>().FirstOrDefault();
            var userId = userModule.Fields.OfType<IdFieldDesign>().FirstOrDefault();
            if (id == null || userId == null) return false;
            if (string.IsNullOrEmpty(userModule.DbTable) || string.IsNullOrEmpty(userId.DbColumn)) return false;
            return module.DataSourceName == userModule.DataSourceName
                && string.Equals(module.DbTable, userModule.DbTable, StringComparison.OrdinalIgnoreCase)
                && string.Equals(id.DbColumn, userId.DbColumn, StringComparison.OrdinalIgnoreCase);
        }

        //論理削除は予約名のフィールド (LogicalDelete / DeletedAt / Deleter) で決まる。ユーザーのモジュールにあるものが、同じ列で置かれていること
        static readonly string[] LogicalDeleteFieldNames = [SystemFieldNames.LogicalDelete, SystemFieldNames.DeletedAt, SystemFieldNames.Deleter];

        static bool HasSameLogicalDelete(ModuleDesign module, ModuleDesign userModule)
        {
            if (!module.UsesLogicalDelete()) return false;
            foreach (var name in LogicalDeleteFieldNames)
            {
                var userField = userModule.Fields.FirstOrDefault(e => e.Name == name);
                if (userField == null) continue;
                var field = module.Fields.FirstOrDefault(e => e.Name == name);
                if (field == null || field.GetType() != userField.GetType()) return false;
                if (!DbColumns(field).SequenceEqual(DbColumns(userField), StringComparer.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        //フィールドが書く列 ([DbColumn] の付いたプロパティの値。本体の GetDbColumns は internal)
        static List<string> DbColumns(FieldDesignBase field) => field.GetType().GetProperties()
            .Where(e => e.PropertyType == typeof(string) && e.IsDefined(typeof(DbColumnAttribute), true))
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => (string?)e.GetValue(field) ?? string.Empty)
            .ToList();

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context))
                .AddField(MentionListField, x => MentionListField = x);
            //表示名・サブ表示名の項目は、一覧が読むモジュールの項目
            var list = context.DesignData.Modules.Find(context.OwnerModule)?.Fields.FirstOrDefault(e => e.Name == MentionListField) as ListFieldDesign;
            var listModuleName = list?.SearchCondition.ModuleName;
            if (!string.IsNullOrEmpty(listModuleName))
            {
                builder.AddField(listModuleName, MentionDisplayNameField, x => MentionDisplayNameField = x)
                    .AddField(listModuleName, MentionDisplayNameSubField, x => MentionDisplayNameSubField = x);
            }
            return builder.Build();
        }
    }
}
