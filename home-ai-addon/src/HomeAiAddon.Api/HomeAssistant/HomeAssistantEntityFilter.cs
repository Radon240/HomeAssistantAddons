using HomeAiAddon.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.HomeAssistant;

public sealed class HomeAssistantEntityFilter(IOptionsMonitor<AddonOptions> options)
{
    public bool ShouldTrack(string entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return false;
        }

        var opts = options.CurrentValue;
        return EntityPatternMatcher.MatchesEntityFilters(
            entityId,
            opts.EntityFilter,
            opts.DomainFilter);
    }
}
