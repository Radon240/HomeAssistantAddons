using HomeAiAddon.Api.HomeAssistant;
using HomeAiAddon.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.BehaviorAnalysis;

public sealed class AnalysisEntityFilter(IOptionsMonitor<AddonOptions> options)
{
    public bool ShouldInclude(string entityId)
    {
        var opts = options.CurrentValue;
        return !EntityPatternMatcher.IsExcludedFromAnalysis(
            entityId,
            opts.AnalysisExcludeEntities,
            opts.AnalysisExcludeDomains);
    }

    public AnalysisFilterSettings GetSettings() =>
        new(
            options.CurrentValue.AnalysisExcludeEntities,
            options.CurrentValue.AnalysisExcludeDomains);
}

public sealed record AnalysisFilterSettings(
    IReadOnlyList<string> ExcludeEntities,
    IReadOnlyList<string> ExcludeDomains)
{
    public bool HasExclusions => ExcludeEntities.Count > 0 || ExcludeDomains.Count > 0;
}
