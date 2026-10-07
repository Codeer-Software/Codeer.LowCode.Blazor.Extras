using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>
    /// TagField のテスト用のデザイン: タグ付け (ContactTags = OwnerId + Name) と、タグを付けるモジュール (Contact)。タグのセットアップが作る形と同じ。
    /// 会社 → 社員 → タグ の 2 段の検索を見るときは <see cref="AddCompany"/> で会社 (Company。社員の一覧 People) を足す。
    /// </summary>
    static class TagTestDesigns
    {
        internal const string Ds = "Main";

        internal static DesignData Create(Action<TagFieldDesign>? customize = null)
        {
            var d = new DesignData();

            var link = new ModuleDesign { Name = "ContactTags", DataSourceName = Ds, DbTable = "contact_tags", CanCreate = true, CanUpdate = true, CanDelete = true };
            link.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            link.Fields.Add(new IdFieldDesign { Name = "OwnerId", DbColumn = "owner_id", IsManualInput = false });
            link.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "タグ名", DbColumn = "name", IsRequired = true, MaxLength = TagField.MaxTagLength });
            link.Fields.Add(new TagLinkContractFieldDesign { Name = "TagLinkContract" });
            link.ListLayouts[""] = new ListLayoutDesign { DataOnlyFields = { "OwnerId", "Name" } };
            d.AddModule(link);

            var contact = new ModuleDesign { Name = "Contact", DataSourceName = Ds, DbTable = "contacts", CanCreate = true, CanUpdate = true, CanDelete = true };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "名前", DbColumn = "name" });
            contact.Fields.Add(new IdFieldDesign { Name = "CompanyId", DbColumn = "company_id", IsManualInput = true });
            var tags = new TagFieldDesign { Name = "Tags", DisplayName = "タグ", SearchCondition = LinkCondition() };
            customize?.Invoke(tags);
            contact.Fields.Add(tags);
            contact.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Name", "Tags" } };
            contact.ListLayouts[""] = new ListLayoutDesign { Elements = [[new ListElement { FieldName = "Name" }, new ListElement { FieldName = "Tags" }]] };
            d.AddModule(contact);
            return d;
        }

        /// <summary>会社 (Company) と、その社員の一覧 (People → Contact)。会社の検索に 社員.タグ (People.Tags) を置く形。</summary>
        internal static void AddCompany(DesignData d)
        {
            var company = new ModuleDesign { Name = "Company", DataSourceName = Ds, DbTable = "companies" };
            company.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            company.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "会社名", DbColumn = "name" });
            company.Fields.Add(new ListFieldDesign
            {
                Name = "People",
                SearchCondition = new SearchCondition
                {
                    ModuleName = "Contact",
                    Condition = MultiMatchCondition.And(new FieldVariableMatchCondition { SearchTargetVariable = "CompanyId.Value", Variable = "Id.Value", Comparison = MatchComparison.Equal }),
                },
            });
            //顧客企業 の検索の 社員.タグ と同じ: 子のフィールドを LinkFieldNames で出し、検索レイアウトに置く
            company.LinkFieldNames.Add("People.Tags");
            var search = new SearchGridLayoutDesign();
            search.Rows.Add(new GridRow { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = "People.Tags" } } } });
            company.SearchLayouts[""] = new SearchLayoutDesign { Layout = search };
            d.AddModule(company);
        }

        /// <summary>
        /// デザインをファイルに書いて読み直す (サーバー・デザイナがデザインを読むのと同じ経路)。
        /// 読み込みのときに本体が LinkFieldNames のフィールド (People.Tags など) を作るので、親の検索に子のフィールドを置く形を見るときに使う。
        /// </summary>
        internal static DesignData Reload(DesignData d)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"tag_design_{Guid.NewGuid():N}");
            var zipDir = dir + "_zip";
            Directory.CreateDirectory(Path.Combine(dir, "Modules"));
            Directory.CreateDirectory(zipDir);
            try
            {
                foreach (var name in d.Modules.GetModuleNames())
                    File.WriteAllText(Path.Combine(dir, "Modules", $"{name}.mod.json"), Codeer.LowCode.Blazor.Json.JsonConverterEx.SerializeObject(d.Modules.Find(name)!));
                System.IO.Compression.ZipFile.CreateFromDirectory(dir, Path.Combine(zipDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(zipDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
                try { Directory.Delete(zipDir, true); } catch { }
            }
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
            await db.ExecuteAsync(Ds, "CREATE TABLE companies (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, company_id INTEGER)", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE contact_tags (id INTEGER PRIMARY KEY AUTOINCREMENT, owner_id INTEGER NOT NULL REFERENCES contacts (id) ON DELETE CASCADE, name TEXT NOT NULL)", new());
            await db.ExecuteAsync(Ds, "CREATE UNIQUE INDEX ux_contact_tags_owner_id_name ON contact_tags (owner_id, name)", new());
            await db.ExecuteAsync(Ds, "CREATE INDEX ix_contact_tags_name ON contact_tags (name)", new());
            //A = 展示会, DXPO / B = 展示会 / C = セミナー / D = なし (よく使われている順: 展示会 2、DXPO 1、セミナー 1)
            await db.ExecuteAsync(Ds, "INSERT INTO contacts (name) VALUES ('A'), ('B'), ('C'), ('D')", new());
            await db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, '展示会'), (1, 'DXPO'), (2, '展示会'), (3, 'セミナー')", new());
        }
    }
}
