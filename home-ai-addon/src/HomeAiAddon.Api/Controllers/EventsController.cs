using HomeAiAddon.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class EventsController(IStateChangeEventStore store) : ControllerBase
{
    /// <param name="entity">entity_id, шаблон (light.*) или domain (light).</param>
    [HttpGet]
    public async Task<ActionResult<EventsListResponse>> Get(
        [FromQuery] int limit = 100,
        [FromQuery] string? entity = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var items = await store.QueryAsync(limit, entity, cancellationToken);
            return Ok(new EventsListResponse(items.Count, items));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("stats/hourly")]
    public async Task<ActionResult<HourlyStatsResponse>> GetHourlyStats(
        [FromQuery] int hours = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var buckets = await store.GetHourlyStatsAsync(hours, cancellationToken);
            return Ok(new HourlyStatsResponse(hours, buckets));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed record EventsListResponse(int Count, IReadOnlyList<StateChangeEventDto> Items);

public sealed record HourlyStatsResponse(int Hours, IReadOnlyList<HourlyEventBucket> Buckets);
