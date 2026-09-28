using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>編集履歴テスト共通のデザイン (受注 Order + 明細 OrderItem + 履歴 EditHistory)。</summary>
    static class EditHistoryTestDesigns
    {
        public const string Ds = "Main";

        /// <param name="logicalDelete">Order / OrderItem を論理削除 (LogicalDelete フィールド) にする。</param>
        public static DesignData Create(bool withHistoryField = true, string historyModuleName = "EditHistory", bool logicalDelete = false)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            user.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(user);

            var order = new ModuleDesign { Name = "Order", DataSourceName = Ds, DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            order.Fields.Add(new TextFieldDesign { Name = "Title", DisplayName = "件名", DbColumn = "title" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DisplayName = "金額", DbColumn = "amount" });
            order.Fields.Add(new TextFieldDesign { Name = "Secret", DisplayName = "機密", DbColumn = "secret" });
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Items",
                DisplayName = "明細",
                DeleteTogether = true,
                SearchCondition = new SearchCondition("OrderItem")
                {
                    Condition = new FieldVariableMatchCondition
                    {
                        SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value",
                    },
                },
            });
            //従属でない (参照するだけの) 一覧はスナップショットの対象外
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Related",
                DeleteTogether = false,
                SearchCondition = new SearchCondition("OrderItem"),
            });
            if (withHistoryField)
                order.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = historyModuleName });
            if (logicalDelete)
                order.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "is_deleted" });
            order.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(order);

            var item = new ModuleDesign { Name = "OrderItem", DataSourceName = Ds, DbTable = "order_items" };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            item.Fields.Add(new LinkFieldDesign { Name = "Order", SearchCondition = new SearchCondition("Order"), DbColumn = "order_id" });
            item.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "品名", DbColumn = "name" });
            item.Fields.Add(new NumberFieldDesign { Name = "Qty", DisplayName = "数量", DbColumn = "qty" });
            if (logicalDelete)
                item.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "is_deleted" });
            item.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(item);

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories" };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            history.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            history.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            history.Fields.Add(new TextFieldDesign { Name = "UserId", DbColumn = "user_id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "DateTime", DbColumn = "date_time" });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract" });
            history.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(history);
            return d;
        }
    }
}
