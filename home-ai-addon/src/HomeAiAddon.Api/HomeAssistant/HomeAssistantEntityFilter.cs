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
        var entityPatterns = opts.EntityFilter;
        var domainPatterns = opts.DomainFilter;

        if (entityPatterns.Count == 0 && domainPatterns.Count == 0)
        {
            return true;
        }

        if (entityPatterns.Count > 0 && entityPatterns.Any(p => MatchesPattern(entityId, p)))
        {
            return true;
        }

        if (domainPatterns.Count == 0)
        {
            return false;
        }

        var domain = ExtractDomain(entityId);
        return domainPatterns.Any(p => MatchesPattern(domain, p));
    }

    private static string ExtractDomain(string entityId)
    {
        var dot = entityId.IndexOf('.');
        return dot > 0 ? entityId[..dot] : entityId;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        pattern = pattern.Trim();
        if (pattern == "*")
        {
            return true;
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
