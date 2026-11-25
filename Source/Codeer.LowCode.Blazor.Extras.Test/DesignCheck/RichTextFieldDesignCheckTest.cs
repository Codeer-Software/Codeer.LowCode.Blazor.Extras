using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    /// <summary>
    /// RichTextField のメンションの設計チェック (候補 = 同じモジュールの一覧フィールドの行)。
    /// 形: mod に一覧 Users (AppUser を読む) と RichTextField Body。AppUser はログインユーザーのモジュール (表 users・Id の列 id)。
    /// </summary>
    public class RichTextFieldDesignCheckTest
    {
        const string UserDs = "datasource";

        static string Code(int number) => DesignCheckCode.Create(typeof(RichTextFieldDesign), number);

        static ModuleDesign CreateUserModule(string name = "AppUser")
        {
            var user = new ModuleDesign { Name = name, DataSourceName = UserDs, DbTable = "users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            user.Fields.Add(new TextFieldDesign { Name = "LoginName", DbColumn = "login_name" });
            return user;
        }

        static ListFieldDesign CreateList(string moduleName = "AppUser")
            => new() { Name = "Users", SearchCondition = new SearchCondition { ModuleName = moduleName, LimitCount = null } };

        static RichTextFieldDesign CreateField() => new()
        {
            Name = "Body", DbColumn = "DbColumn", EnableMention = true,
            MentionListField = "Users", MentionDisplayNameField = "Name", MentionLoginNameField = "LoginName",
        };

        //正しい形を作ってから、テストごとに 1 か所だけ崩す
        static List<DesignCheckInfo> Check(Action<Setup>? customize = null)
        {
            var setup = new Setup();
            customize?.Invoke(setup);
            if (setup.List != null) setup.Module.Fields.Add(setup.List);
            setup.Module.Fields.Add(setup.Field);
            return setup.Field.CheckDesign(new DesignCheckContext("mod", setup.DesignData, Utilities.CreateDataSource()));
        }

        class Setup
        {
            public DesignData DesignData { get; }
            public ModuleDesign Module { get; }
            public ModuleDesign User { get; } = CreateUserModule();
            public ListFieldDesign? List { get; set; } = CreateList();
            public RichTextFieldDesign Field { get; } = CreateField();

            public Setup()
            {
                (DesignData, Module) = Utilities.CreateDesignData();
                DesignData.AddModule(User);
                DesignData.AppSettings.CurrentUserModuleDesignName = User.Name;
            }

            /// <summary>ユーザーの表を見る一般向けのユーザーモジュール (書き込めない)。一覧はこれを読む。</summary>
            public ModuleDesign AddPublicUserModule()
            {
                var module = CreateUserModule("PublicUser");
                module.CanCreate = module.CanUpdate = module.CanDelete = false;
                DesignData.AddModule(module);
                List!.SearchCondition.ModuleName = module.Name;
                return module;
            }
        }

        static void AssertSingle(List<DesignCheckInfo> ret, int number, string member)
        {
            Assert.That(ret.Select(e => e.Code), Is.EqualTo(new[] { Code(number) }));
            ret[0].AssertFieldLocation("mod", "Body", member);
        }

        [Test]
        public void メンションを使わなければ一覧は要らない()
        {
            var ret = Check(e =>
            {
                e.List = null;
                e.Field.EnableMention = false;
                e.Field.MentionListField = string.Empty;
                e.Field.MentionDisplayNameField = string.Empty;
            });
            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void ユーザーのモジュールを読む一覧なら指摘なし()
            => Assert.That(Check(), Is.Empty);

        [Test]
        public void ログインIDの項目は空でもよい()
            => Assert.That(Check(e => e.Field.MentionLoginNameField = string.Empty), Is.Empty);

        #region :1 一覧そのもの

        [Test]
        public void 一覧の指定が要る()
        {
            var ret = Check(e => e.Field.MentionListField = string.Empty);
            AssertSingle(ret, 1, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void 一覧は同じモジュールにあること()
        {
            var ret = Check(e => e.List = null);
            AssertSingle(ret, 1, nameof(RichTextFieldDesign.MentionListField));
            Assert.That(ret[0].Message, Does.Contain("Users"));
        }

        [Test]
        public void 一覧はListFieldだけ()
        {
            var ret = Check(e =>
            {
                e.List = null;
                e.Module.Fields.Add(new DetailListFieldDesign { Name = "Users", SearchCondition = new SearchCondition { ModuleName = "AppUser" } });
            });
            AssertSingle(ret, 1, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void 列モードの一覧は使えない()
        {
            var ret = Check(e => e.List!.DbColumn = "DbColumn");
            AssertSingle(ret, 1, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void 検索フィールドと結び付いた一覧は使えない()
        {
            var ret = Check(e => e.Module.Fields.Add(new SearchFieldDesign { Name = "Search", ResultsViewFieldName = "Users" }));
            AssertSingle(ret, 1, nameof(RichTextFieldDesign.MentionListField));
            Assert.That(ret[0].Message, Does.Contain("Search"));
        }

        #endregion

        #region :2 全部の行 / :3 編集しない

        [Test]
        public void 件数の上限のある一覧は使えない()
        {
            var ret = Check(e => e.List!.SearchCondition.LimitCount = 50);
            AssertSingle(ret, 2, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void メモリ内ページングなら件数の上限があってよい()
        {
            var ret = Check(e =>
            {
                e.List!.SearchCondition.LimitCount = 50;
                e.List.IsInMemoryPaging = true;
            });
            Assert.That(ret, Is.Empty);
        }

        [TestCase(nameof(ListFieldDesignBase.CanCreate))]
        [TestCase(nameof(ListFieldDesignBase.CanUpdate))]
        [TestCase(nameof(ListFieldDesignBase.CanDelete))]
        [TestCase(nameof(ListFieldDesignBase.DeleteTogether))]
        public void 行を編集できる一覧と親と一緒に消す一覧は使えない(string property)
        {
            var ret = Check(e => typeof(ListFieldDesignBase).GetProperty(property)!.SetValue(e.List, true));
            AssertSingle(ret, 3, nameof(RichTextFieldDesign.MentionListField));
        }

        #endregion

        #region :4 一覧が読むモジュール

        [Test]
        public void ユーザーのモジュールが要る()
        {
            var ret = Check(e => e.DesignData.AppSettings.CurrentUserModuleDesignName = string.Empty);
            AssertSingle(ret, 4, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void 一覧が読むのはユーザーの表を見るモジュール()
        {
            //mod はユーザーとは別の表
            var ret = Check(e => e.List!.SearchCondition.ModuleName = "mod");
            AssertSingle(ret, 4, nameof(RichTextFieldDesign.MentionListField));
            Assert.That(ret[0].Message, Does.Contain("AppUser"));
        }

        [Test]
        public void 一般向けのユーザーモジュールを読む一覧なら指摘なし()
            => Assert.That(Check(e => e.AddPublicUserModule()), Is.Empty);

        //データソース / 表 / Id の列 のどれかが違えば、ユーザーの表を見るモジュールではない
        [TestCase("other", "users", "id")]
        [TestCase(UserDs, "members", "id")]
        [TestCase(UserDs, "users", "code")]
        public void 一般向けのユーザーモジュールは同じデータソースと表とIdの列(string dataSource, string table, string idColumn)
        {
            var ret = Check(e =>
            {
                var module = e.AddPublicUserModule();
                module.DataSourceName = dataSource;
                module.DbTable = table;
                module.Fields.OfType<IdFieldDesign>().Single().DbColumn = idColumn;
            });
            AssertSingle(ret, 4, nameof(RichTextFieldDesign.MentionListField));
        }

        #endregion

        #region :5 表示名とログイン ID の項目

        [Test]
        public void 表示名の項目の指定が要る()
        {
            var ret = Check(e => e.Field.MentionDisplayNameField = string.Empty);
            AssertSingle(ret, 5, nameof(RichTextFieldDesign.MentionDisplayNameField));
        }

        [Test]
        public void 表示名の項目は一覧が読むモジュールにあること()
        {
            var ret = Check(e => e.Field.MentionDisplayNameField = "Nickname");
            AssertSingle(ret, 5, nameof(RichTextFieldDesign.MentionDisplayNameField));
            Assert.That(ret[0].Message, Does.Contain("Nickname").And.Contain("AppUser"));
        }

        [Test]
        public void ログインIDの項目は一覧が読むモジュールにあること()
        {
            var ret = Check(e => e.Field.MentionLoginNameField = "Account");
            AssertSingle(ret, 5, nameof(RichTextFieldDesign.MentionLoginNameField));
        }

        [Test]
        public void 項目は一般向けのユーザーモジュールの側で確かめる()
        {
            //ユーザーのモジュールにあっても、一覧が読むモジュールに無ければ指摘
            var ret = Check(e => e.AddPublicUserModule().Fields.RemoveAll(f => f.Name == "LoginName"));
            AssertSingle(ret, 5, nameof(RichTextFieldDesign.MentionLoginNameField));
            Assert.That(ret[0].Message, Does.Contain("PublicUser"));
        }

        #endregion

        #region :6 / :7 一般向けのユーザーモジュール

        [TestCase(nameof(ModuleDesign.CanCreate))]
        [TestCase(nameof(ModuleDesign.CanUpdate))]
        [TestCase(nameof(ModuleDesign.CanDelete))]
        public void 一般向けのユーザーモジュールは書き込めないこと(string property)
        {
            var ret = Check(e => typeof(ModuleDesign).GetProperty(property)!.SetValue(e.AddPublicUserModule(), true));
            AssertSingle(ret, 6, nameof(RichTextFieldDesign.MentionListField));
            Assert.That(ret[0].Message, Does.Contain("PublicUser"));
        }

        [Test]
        public void ユーザーのモジュール自身は書き込めてよい()
        {
            var ret = Check(e => e.User.CanCreate = e.User.CanUpdate = e.User.CanDelete = true);
            Assert.That(ret, Is.Empty);
        }

        static BooleanFieldDesign LogicalDelete(string column) => new() { Name = SystemFieldNames.LogicalDelete, DbColumn = column };

        [Test]
        public void ユーザーのモジュールが論理削除なら一般向けにも同じ設定が要る()
        {
            var ret = Check(e =>
            {
                e.User.Fields.Add(LogicalDelete("is_deleted"));
                e.AddPublicUserModule();
            });
            AssertSingle(ret, 7, nameof(RichTextFieldDesign.MentionListField));
            Assert.That(ret[0].Message, Does.Contain("PublicUser"));
        }

        [Test]
        public void 論理削除の列が違えば同じ設定ではない()
        {
            var ret = Check(e =>
            {
                e.User.Fields.Add(LogicalDelete("is_deleted"));
                e.AddPublicUserModule().Fields.Add(LogicalDelete("deleted"));
            });
            AssertSingle(ret, 7, nameof(RichTextFieldDesign.MentionListField));
        }

        [Test]
        public void 同じ論理削除の設定があれば指摘なし()
        {
            var ret = Check(e =>
            {
                e.User.Fields.Add(LogicalDelete("is_deleted"));
                e.AddPublicUserModule().Fields.Add(LogicalDelete("IS_DELETED"));
            });
            Assert.That(ret, Is.Empty);
        }

        #endregion

        #region 名前の変更

        static (DesignData, ModuleDesign, RichTextFieldDesign) CreateForRename()
        {
            var setup = new Setup();
            //このフィールドのモジュールにも同じ名前の項目がある (こちらの名前の変更には追従しない)
            setup.Module.Fields.Add(new TextFieldDesign { Name = "Name" });
            setup.Module.Fields.Add(setup.List!);
            setup.Module.Fields.Add(setup.Field);
            return (setup.DesignData, setup.Module, setup.Field);
        }

        [Test]
        public void 一覧の名前の変更に追従する()
        {
            var (designData, module, field) = CreateForRename();
            var result = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Field, ModuleName = module.Name, OwnerModule = module.Name, Source = "Users", Destination = "Members",
            });
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.MentionListField, Is.EqualTo("Members"));
        }

        [Test]
        public void 項目の名前の変更は一覧が読むモジュールの項目に追従する()
        {
            var (designData, module, field) = CreateForRename();

            var other = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Field, ModuleName = module.Name, OwnerModule = module.Name, Source = "Name", Destination = "Title",
            });
            Assert.That(other.RenameNeeded, Is.False);

            var result = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Field, ModuleName = "AppUser", OwnerModule = module.Name, Source = "Name", Destination = "FullName",
            });
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.MentionDisplayNameField, Is.EqualTo("FullName"));
            Assert.That(field.MentionLoginNameField, Is.EqualTo("LoginName"));
        }

        #endregion
    }
}
