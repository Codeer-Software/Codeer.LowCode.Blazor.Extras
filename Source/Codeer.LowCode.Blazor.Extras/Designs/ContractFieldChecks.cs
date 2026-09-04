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
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        public static class Codes
        {
            public const int RoleMustBeList = 1;
            public const int ContractFieldMissing = 2;
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
                    Code = DesignCheckCode.Create(typeof(ContractFieldChecks), Codes.RoleMustBeList),
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
                Code = DesignCheckCode.Create(typeof(ContractFieldChecks), Codes.ContractFieldMissing),
                Location = new FieldDesignDataLocation
                { Module = context.OwnerModule, Field = ownerFieldName, Member = memberName },
                Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                    targetModule.Name, typeof(TContract).Name),
            });
        }
    }
}
