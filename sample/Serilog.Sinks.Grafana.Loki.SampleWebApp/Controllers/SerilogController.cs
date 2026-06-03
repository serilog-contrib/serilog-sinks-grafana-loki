using System.Net;
using Microsoft.AspNetCore.Mvc;

namespace Serilog.Sinks.Grafana.Loki.SampleWebApp.Controllers;

[ApiController]
[Route("serilog")]
public class SerilogController(ILogger<SerilogController> logger) : ControllerBase
{
    [HttpGet("info")]
    public IActionResult GetInfo()
    {
        var odin = new { Id = 1, Name = "Odin" };
        logger.LogInformation("God of the day {@God}", odin);
        return Ok(odin);
    }

    [HttpGet("error")]
    public IActionResult GetError(int id = 3)
    {
        try
        {
            _ = id / 0;
            return Ok();
        }
        catch (DivideByZeroException ex)
        {
            logger.LogError(ex, "An error occurred");
            return StatusCode((int)HttpStatusCode.InternalServerError, ex.Message);
        }
    }
}
