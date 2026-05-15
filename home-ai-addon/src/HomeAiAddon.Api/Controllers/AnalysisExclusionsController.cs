using HomeAiAddon.Api.BehaviorAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/recommendations/exclusions")]
public sealed class AnalysisExclusionsController(IAnalysisExclusionStore exclusionStore) : ControllerBase
{
    [HttpGet]
    public ActionResult<AnalysisExclusionsResponse> Get()
    {
        var snapshot = exclusionStore.GetSnapshot();
        return Ok(Map(snapshot));
    }

    [HttpPut]
    public async Task<ActionResult<AnalysisExclusionsResponse>> Put(
        [FromBody] UpdateAnalysisExclusionsRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await exclusionStore.SaveUiExclusionsAsync(
            request.ExcludeEntities ?? [],
            request.ExcludeDomains ?? [],
            cancellationToken);
        return Ok(Map(snapshot));
    }

    private static AnalysisExclusionsResponse Map(AnalysisExclusionsSnapshot snapshot) =>
        new(
            snapshot.UiExcludeEntities,
            snapshot.UiExcludeDomains,
            snapshot.ConfigExcludeEntities,
            snapshot.ConfigExcludeDomains,
            snapshot.EffectiveExcludeEntities,
            snapshot.EffectiveExcludeDomains,
            snapshot.HasExclusions);
}

public sealed record UpdateAnalysisExclusionsRequest(
    IReadOnlyList<string>? ExcludeEntities,
    IReadOnlyList<string>? ExcludeDomains);

public sealed record AnalysisExclusionsResponse(
    IReadOnlyList<string> UiExcludeEntities,
    IReadOnlyList<string> UiExcludeDomains,
    IReadOnlyList<string> ConfigExcludeEntities,
    IReadOnlyList<string> ConfigExcludeDomains,
    IReadOnlyList<string> EffectiveExcludeEntities,
    IReadOnlyList<string> EffectiveExcludeDomains,
    bool HasExclusions);
