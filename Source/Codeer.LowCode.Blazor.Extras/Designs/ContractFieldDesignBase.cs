using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 契約フィールドの基底。契約を実装するモジュール上に置き、「役割 → 自モジュールのフィールド名」の
    /// マッピングを宣言する (役割プロパティ名 = 既定フィールド名 = 初期値。nameof で自己参照)。
    /// 機能側 (エンジン・チェック・生成) はこのマッピング経由で
    /// フィールドを解決するため、モジュールのフィールド名は自由に変えられる
    /// (役割プロパティは自モジュールのフィールド参照としてリネームにも追従する)。
    /// 契約フィールドを置いたモジュールに役割のフィールドが無ければデザインチェックがエラーにする。
    /// UI もデータも持たない設定運搬フィールド (PermissionField と同じ流儀)。
    /// </summary>
    public abstract class ContractFieldDesignBase : FieldDesignBase
    {
        protected ContractFieldDesignBase(string typeFullName) : base(typeFullName) { }

        public override string GetWebComponentTypeFullName() => string.Empty;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new ContractField(this);

        /// <summary>役割プロパティ (プロパティ名 = 役割名) の一覧。チェックとリネーム追従で使う。</summary>
        internal IEnumerable<System.Reflection.PropertyInfo> GetRoleProperties()
            => GetType().GetProperties()
                .Where(e => e.DeclaringType != typeof(FieldDesignBase) && e.PropertyType == typeof(string) &&
                            e.GetCustomAttributes(typeof(DesignerAttribute), true).Length > 0);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //同じ契約フィールドが複数あると解決が曖昧になる
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && ownModule.Fields.Count(e => e.GetType() == GetType()) > 1)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldDuplicatedFormat,
                        GetType().Name),
                });
            }

            //役割のフィールドが自モジュールに存在すること (=このモジュールが契約を実装していること)
            foreach (var role in GetRoleProperties())
            {
                var fieldName = role.GetValue(this)?.ToString() ?? string.Empty;
                context.CheckFieldFieldExistence(Name, role.Name, fieldName).AddTo(result);
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context));
            foreach (var role in GetRoleProperties())
            {
                var current = role.GetValue(this)?.ToString() ?? string.Empty;
                builder.AddField(current, x => role.SetValue(this, x));
            }
            return builder.Build();
        }

        //役割が一覧フィールドであること + 一覧の先のモジュールが指定の契約を実装していることのチェック
        private protected void CheckListRole<TContract>(DesignCheckContext context, List<DesignCheckInfo> result,
            string roleName, string fieldName) where TContract : ContractFieldDesignBase
        {
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            var field = ownModule?.Fields.FirstOrDefault(e => e.Name == fieldName);
            if (field == null) return; //不在は役割チェックが指摘済み

            if (field is not IListFieldDesign list)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = roleName },
                    Message = string.Format(Properties.Resources.ApprovalCheck_RoleMustBeListFormat, fieldName),
                });
                return;
            }

            var targetModule = context.DesignData.Modules.Find(list.SearchCondition.ModuleName);
            if (targetModule == null) return; //一覧側のモジュール不在チェックが指摘する
            if (!targetModule.Fields.OfType<TContract>().Any())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = roleName },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                        targetModule.Name, typeof(TContract).Name),
                });
            }
        }
    }
}
