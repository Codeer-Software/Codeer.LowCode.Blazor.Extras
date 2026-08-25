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

        /// <summary>
        /// 値を「変数」として扱う役割名 (リンクパス可。既定 = 自モジュールのフィールド名として扱う)。
        /// 宛先の人が別モジュールにいる名簿 (中間テーブル) では "Contact.Email.Value" のように書けるようにする。
        /// </summary>
        private protected virtual HashSet<string> VariableRoleNames => new();

        /// <summary>
        /// 必須の役割名。空にするとデザインチェックエラー。
        /// ここに無い役割は**空 = その項目は使わない**という宣言 (エラーにしない)。
        /// 表示名にも「(必須)」を入れて、プロパティを見れば分かるようにしている。
        /// </summary>
        private protected virtual HashSet<string> RequiredRoleNames => new();

        /// <summary>その役割が必須か (空にできないか)。機能側 (エンジン) の実行時検証でも使う。</summary>
        internal bool IsRoleRequired(string roleName) => RequiredRoleNames.Contains(roleName);

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

            //役割のフィールド(または変数)が自モジュールから解決できること (=このモジュールが契約を実装していること)。
            //必須でない役割は空にできる (= その項目は使わない)
            foreach (var role in GetRoleProperties())
            {
                var fieldName = role.GetValue(this)?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(fieldName))
                {
                    if (!RequiredRoleNames.Contains(role.Name)) continue;
                    result.Add(new FieldDesignCheckInfo
                    {
                        Location = new FieldDesignDataLocation
                        { Module = context.OwnerModule, Field = Name, Member = role.Name },
                        Message = string.Format(Properties.Resources.ContractCheck_RoleRequiredFormat, role.Name),
                    });
                    continue;
                }
                if (VariableRoleNames.Contains(role.Name))
                    context.CheckFieldVariableExistence(Name, role.Name, fieldName).AddTo(result);
                else
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
                if (VariableRoleNames.Contains(role.Name))
                    builder.AddVariable(current, x => role.SetValue(this, x));
                else
                    builder.AddField(current, x => role.SetValue(this, x));
            }
            return builder.Build();
        }

        //役割が一覧フィールドであること + 一覧の先のモジュールが指定の契約を実装していることのチェック
        private protected void CheckListRole<TContract>(DesignCheckContext context, List<DesignCheckInfo> result,
            string roleName, string fieldName) where TContract : ContractFieldDesignBase
            => ContractFieldChecks.CheckListImplementsContract<TContract>(context, result, Name, roleName, fieldName);
    }
}
