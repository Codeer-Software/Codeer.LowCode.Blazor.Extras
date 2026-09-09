namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// Agent に渡す補足の文書 (業務用語の定義、集計の決まり、データの見方など)。
    /// 出所はホストが決める。標準はデザインプロジェクトの <c>Resources/AIChat/*.md</c> (App.zip に入ってデプロイで反映) で、
    /// アプリ固有の説明をデザインと同じ場所・同じ版で管理できる。
    /// </summary>
    /// <param name="Name">文書名 (モデルが read_document で指定する。ファイル名から拡張子を除いたもの等)</param>
    /// <param name="Text">本文 (Markdown かプレーンテキスト)</param>
    public sealed record AIChatDocument(string Name, string Text);
}
