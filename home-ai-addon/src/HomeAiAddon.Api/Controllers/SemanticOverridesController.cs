using HomeAiAddon.Api.Semantic;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/semantic-overrides")]
public sealed class SemanticOverridesController(ISemanticOverrideStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SemanticOverridesResponse>> Get(CancellationToken cancellationToken)
    {
        var overrides = await store.GetAllAsync(cancellationToken);
        return Ok(new SemanticOverridesResponse(overrides));
    }

    [HttpPut("{entityId}")]
    public async Task<ActionResult<SemanticOverridesResponse>> Put(
        string entityId,
        [FromBody] UpsertSemanticOverrideRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = request with { EntityId = entityId };
        var overrides = await store.UpsertAsync(normalized, cancellationToken);
        return Ok(new SemanticOverridesResponse(overrides));
    }

    [HttpDelete("{entityId}")]
    public async Task<ActionResult<SemanticOverridesResponse>> Delete(
        string entityId,
        CancellationToken cancellationToken)
    {
        var overrides = await store.DeleteAsync(entityId, cancellationToken);
        return Ok(new SemanticOverridesResponse(overrides));
    }
}
