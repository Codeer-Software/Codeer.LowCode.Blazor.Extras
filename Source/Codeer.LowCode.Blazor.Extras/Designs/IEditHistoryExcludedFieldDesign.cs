namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 編集履歴の差分と復元の対象外にするフィールドの宣言 (目印)。
    /// ユーザーの編集ではなくサーバーが別のタイミングで書く値 (承認フローの参照など) を持つフィールドが実装する。
    /// 版には記録されるが、差分には出ず、「この版に戻す」でも触らない。
    /// </summary>
    public interface IEditHistoryExcludedFieldDesign
    {
    }
}
