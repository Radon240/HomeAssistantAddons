using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class InfoController(
    IOptionsSnapshot<AddonOptions> addonOptions,
    ApplicationDbContext db,
    IHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var opts = addonOptions.Value;
        var dbOk = false;
        try
        {
            dbOk = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            dbOk = false;
        }

        var response = new
        {
            application = typeof(InfoController).Assembly.GetName().Name,
            environment = env.EnvironmentName,
            displayName = opts.DisplayName,
            enableVerboseApi = opts.EnableVerboseApi,
            databaseConnected = dbOk,
            utc = DateTimeOffset.UtcNow
        };

        return Ok(response);
    }
}
