using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// 自分のデータを意味検索の文章にするフィールド (子レコードなど、本体のデータ型の switch では文章にできない形を持つフィールド) が実装する。
    /// 「どう文章にするか」はそのフィールドが決める。
    /// </summary>
    public interface ISemanticSearchTextSource
    {
        /// <summary>フィールドのデータを人が読む文字列に。空なら null (文章に入れない)。</summary>
        string? GetSemanticText(DesignData? design, FieldDataBase data);
    }
}
