using HomeAiAddon.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class EventsController(IStateChangeEventStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EventsListResponse>> Get(
        [FromQuery] int limit = 100,
        [FromQuery] string? entity = null,
        CancellationToken cancellationToken = default)
    {
        var items = await store.QueryAsync(limit, entity, cancellationToken);
        return Ok(new EventsListResponse(items.Count, items));
    }

    [HttpGet("stats/hourly")]
    public async Task<ActionResult<HourlyStatsResponse>> GetHourlyStats(
        [FromQuery] int hours = 1,
        CancellationToken cancellationToken = default)
    {
        var buckets = await store.GetHourlyStatsAsync(hours, cancellationToken);
        return Ok(new HourlyStatsResponse(hours, buckets));
    }
}

public sealed record EventsListResponse(int Count, IReadOnlyList<StateChangeEventDto> Items);

public sealed record HourlyStatsResponse(int Hours, IReadOnlyList<HourlyEventBucket> Buckets);
