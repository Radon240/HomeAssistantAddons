using HomeAiAddon.Api.HomeAssistant;

namespace HomeAiAddon.Api.BehaviorAnalysis;

public sealed class AnalysisEntityFilter(IAnalysisExclusionStore exclusionStore)
{
    public bool ShouldInclude(string entityId)
    {
        var snapshot = exclusionStore.GetSnapshot();
        return !EntityPatternMatcher.IsExcludedFromAnalysis(
            entityId,
            snapshot.EffectiveExcludeEntities,
            snapshot.EffectiveExcludeDomains);
    }

    public AnalysisExclusionsSnapshot GetSnapshot() => exclusionStore.GetSnapshot();
}
