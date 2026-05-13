using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PingController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() =>
        Ok(new { ok = true, utc = DateTimeOffset.UtcNow });
}
