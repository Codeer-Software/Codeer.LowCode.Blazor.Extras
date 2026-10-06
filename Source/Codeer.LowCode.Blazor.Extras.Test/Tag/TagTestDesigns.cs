using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>
    /// TagField のテスト用のデザイン: タグのマスタ (Tag)・タグ付け (ContactTags)・タグを付けるモジュール (Contact)・
    /// 保存しない入力欄だけのモジュール (Picker)。タグのセットアップが作る形と同じ。
    /// </summary>
    static class TagTestDesigns
    {
        internal const string Ds = "Main";

        internal static DesignData Create(Action<TagFieldDesign>? customize = null, Action<TagFieldDesign>? customizePicker = null)
        {
            var d = new DesignData();

            var tag = new ModuleDesign { Name = "Tag", DataSourceName = Ds, DbTable = "tags", CanCreate = true, CanUpdate = true, CanDelete = true };
            tag.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            tag.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "タグ名", DbColumn = "name", IsRequired = true });
            tag.Fields.Add(new TagContractFieldDesign { Name = "TagContract" });
            tag.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name" }]] };
            d.AddModule(tag);

            var link = new ModuleDesign { Name = "ContactTags", DataSourceName = Ds, DbTable = "contact_tags", CanCreate = true, CanUpdate = true, CanDelete = true };
            link.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            link.Fields.Add(new IdFieldDesign { Name = "OwnerId", DbColumn = "contact_id", IsManualInput = false });
            link.Fields.Add(new LinkFieldDesign
            {
                Name = "Tag", DbColumn = "tag_id", SearchCondition = new SearchCondition { ModuleName = "Tag" },
                ValueVariable = "Id.Value", DisplayTextVariable = "Name.Value",
            });
            link.Fields.Add(new TagLinkContractFieldDesign { Name = "TagLinkContract" });
            link.ListLayouts[""] = new ListLayoutDesign { DataOnlyFields = { "OwnerId", "Tag" } };
            d.AddModule(link);

            var contact = new ModuleDesign { Name = "Contact", DataSourceName = Ds, DbTable = "contacts", CanCreate = true, CanUpdate = true, CanDelete = true };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "名前", DbColumn = "name" });
            var tags = new TagFieldDesign { Name = "Tags", DisplayName = "タグ", SearchCondition = LinkCondition() };
            customize?.Invoke(tags);
            contact.Fields.Add(tags);
            contact.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Name", "Tags" } };
            contact.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name" }, new ListElement { FieldName = "Tags" }]] };
            d.AddModule(contact);

            var picker = new ModuleDesign { Name = "Picker" };
            var pick = new TagFieldDesign { Name = "Pick", DisplayName = "付けるタグ", TagModuleName = "Tag" };
            customizePicker?.Invoke(pick);
            picker.Fields.Add(pick);
            picker.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Pick" } };
            d.AddModule(picker);
            return d;
        }

        //タグのセットアップが TagField に入れる検索条件: このレコードのタグ付け行を、付けた順に
        internal static SearchCondition LinkCondition() => new()
        {
            ModuleName = "ContactTags",
            Condition = MultiMatchCondition.And(new FieldVariableMatchCondition { SearchTargetVariable = "OwnerId.Value", Variable = "Id.Value", Comparison = MatchComparison.Equal }),
            SortConditions = new List<SortCondition> { new() { Variable = "Id.Value" } },
        };

        /// <summary>SQLite のテーブル (タグのセットアップの DDL と同じ形) と、最初のデータ。</summary>
        internal static async Task CreateTablesAsync(DbAccessor db)
        {
            await db.ExecuteAsync(Ds, "CREATE TABLE tags (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL COLLATE NOCASE UNIQUE)", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE contact_tags (id INTEGER PRIMARY KEY AUTOINCREMENT, contact_id INTEGER NOT NULL REFERENCES contacts(id), tag_id INTEGER NOT NULL REFERENCES tags(id))", new());
            //タグ: 1 展示会 / 2 DXPO / 3 セミナー / 4 100%達成
            await db.ExecuteAsync(Ds, "INSERT INTO tags (name) VALUES ('展示会'), ('DXPO'), ('セミナー'), ('100%達成')", new());
            //A = 展示会, DXPO / B = 展示会 / C = セミナー / D = なし
            await db.ExecuteAsync(Ds, "INSERT INTO contacts (name) VALUES ('A'), ('B'), ('C'), ('D')", new());
            await db.ExecuteAsync(Ds, "INSERT INTO contact_tags (contact_id, tag_id) VALUES (1, 1), (1, 2), (2, 1), (3, 3)", new());
        }
    }
}
