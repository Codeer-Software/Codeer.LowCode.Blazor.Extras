namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// WebAPI (コントローラのアクション) の監査ログの分類を宣言する。<see cref="AuditLogMiddleware"/> が読む。
    /// 付けないアクションは <see cref="AuditCategory.Other"/> として記録される。クラスに付けると全アクションの既定になり、アクション側が優先。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AuditAttribute : Attribute
    {
        public AuditCategory Category { get; }

        public AuditAttribute(AuditCategory category) => Category = category;
    }
}
