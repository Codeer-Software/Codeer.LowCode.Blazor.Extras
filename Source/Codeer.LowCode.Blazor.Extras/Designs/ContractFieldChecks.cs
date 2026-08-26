using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 契約フィールドに関するデザインチェック。契約フィールド自身 (ContractFieldDesignBase) と、
    /// 契約を要求する側のフィールド (BulkMailField 等) の両方から使う。
    /// </summary>
    static class ContractFieldChecks
    {
        /// <summary>
        /// デザインの「現在のユーザーのモジュール」が契約 TContract を実装していること
        /// (「自分を差出人にする」や GmailTokenField が使う差出人契約の検証)。
        /// </summary>
        internal static void CheckCurrentUserModuleImplementsContract<TContract>(DesignCheckContext context,
            List<DesignCheckInfo> result, string ownerFieldName, string memberName) where TContract : ContractFieldDesignBase
        {
            var moduleName = context.DesignData.AppSettings.CurrentUserModuleDesignName;
            if (string.IsNullOrEmpty(moduleName)) return; //CurrentUser モジュール未設定はアプリ設定側のチェック対象
            var module = context.DesignData.Modules.Find(moduleName);
            if (module == null) return;
            if (module.Fields.OfType<TContract>().Any()) return;

            result.Add(new FieldDesignCheckInfo
            {
                Location = new FieldDesignDataLocation
                { Module = context.OwnerModule, Field = ownerFieldName, Member = memberName },
                Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                    module.Name, typeof(TContract).Name),
            });
        }

        /// <summary>
        /// 指定フィールドが一覧フィールドであること + その一覧の先のモジュールが契約 TContract を実装していること。
        /// </summary>
        internal static void CheckListImplementsContract<TContract>(DesignCheckContext context, List<DesignCheckInfo> result,
            string ownerFieldName, string memberName, string listFieldName) where TContract : ContractFieldDesignBase
        {
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            var field = ownModule?.Fields.FirstOrDefault(e => e.Name == listFieldName);
            if (field == null) return; //不在は呼び出し側のフィールド存在チェックが指摘済み

            if (field is not IListFieldDesign list)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = ownerFieldName, Member = memberName },
                    Message = string.Format(Properties.Resources.ApprovalCheck_RoleMustBeListFormat, listFieldName),
                });
                return;
            }

            var targetModule = context.DesignData.Modules.Find(list.SearchCondition.ModuleName);
            if (targetModule == null) return; //一覧側のモジュール不在チェックが指摘する
            if (targetModule.Fields.OfType<TContract>().Any()) return;

            result.Add(new FieldDesignCheckInfo
            {
                Location = new FieldDesignDataLocation
                { Module = context.OwnerModule, Field = ownerFieldName, Member = memberName },
                Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                    targetModule.Name, typeof(TContract).Name),
            });
        }
    }
}
