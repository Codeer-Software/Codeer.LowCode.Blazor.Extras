namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement.TextExtractors
{
    static class ExtractorStream
    {
        //Open XML / ClosedXML / PdfPig は行き来できる Stream が要るので、メモリに読んでから渡す
        internal static async Task<MemoryStream> ToMemoryAsync(Stream content, CancellationToken cancellationToken)
        {
            var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            return memory;
        }
    }
}
