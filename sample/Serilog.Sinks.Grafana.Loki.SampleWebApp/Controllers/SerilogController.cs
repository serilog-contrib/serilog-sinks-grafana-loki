using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Serilog.Sinks.Grafana.Loki.SampleWebApp.Controllers
{
    [ApiController]
    [Route("serilog")]
    public class SerilogController : ControllerBase
    {
        private readonly ILogger<SerilogController> _logger;

        public SerilogController(ILogger<SerilogController> logger)
        {
            _logger = logger;
        }

        [HttpGet("info")]
        public IActionResult GetInfo()
        {
            var odin = new {Id = 1, Name = "Odin"};
            _logger.LogInformation("God of the day {@God}", odin);
            return Ok(odin);
        }

        [HttpGet("error")]
        public IActionResult GetError(int id = 3)
        {
            try
            {
                // ReSharper disable once IntDivisionByZero
                var result = id/0;
                return Ok(result);
            }
            catch (DivideByZeroException ex)
            {
                _logger.LogError(ex, "An error occured");
                return StatusCode((int) HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
}