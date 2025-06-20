using Excel.Report.PDF;
using Microsoft.AspNetCore.Mvc;

namespace Extras.Server.Controllers
{
    [ApiController]
    [Route("api/excel")]
    public class ExcelController : ControllerBase
    {
        [HttpPost("pdf")]
        public async Task<IActionResult> ConvertToPdfAsync()
        {
            using (var memoryStream = new MemoryStream())
            {
                await Request.Body.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                var pdfStream = ExcelConverter.ConvertToPdf(memoryStream, 1);
                return Ok(pdfStream);
            }
        }
    }
}
