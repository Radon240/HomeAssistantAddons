namespace HomeAiAddon.Api.HomeAssistant;

public static class EntityPatternMatcher
{
    public static bool Matches(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern))
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

    /// <summary>
    /// Фильтр из UI/API: light.*, sensor.temp, или domain light (без точки).
    /// </summary>
    public static bool MatchesEntityFilter(string entityId, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        filter = filter.Trim();
        if (filter.Contains('*', StringComparison.Ordinal) || filter.Contains('.', StringComparison.Ordinal))
        {
            return Matches(entityId, filter);
        }

        return Matches(entityId, $"{filter}.*") || Matches(ExtractDomain(entityId), filter);
    }

    public static bool MatchesEntityFilters(string entityId, IReadOnlyList<string> entityPatterns, IReadOnlyList<string> domainPatterns)
    {
        if (entityPatterns.Count == 0 && domainPatterns.Count == 0)
        {
            return true;
        }

        if (entityPatterns.Count > 0 && entityPatterns.Any(p => Matches(entityId, p)))
        {
            return true;
        }

        if (domainPatterns.Count == 0)
        {
            return false;
        }

        var domain = ExtractDomain(entityId);
        return domainPatterns.Any(p => Matches(domain, p));
    }

    public static string ExtractDomain(string entityId)
    {
        var dot = entityId.IndexOf('.');
        return dot > 0 ? entityId[..dot] : entityId;
    }

    /// <summary>Исключение из анализа: совпадение с любым шаблоном entity или domain.</summary>
    public static bool IsExcludedFromAnalysis(
        string entityId,
        IReadOnlyList<string> excludeEntityPatterns,
        IReadOnlyList<string> excludeDomainPatterns)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return true;
        }

        if (excludeEntityPatterns.Count > 0
            && excludeEntityPatterns.Any(p => MatchesEntityFilter(entityId, p)))
        {
            return true;
        }

        if (excludeDomainPatterns.Count == 0)
        {
            return false;
        }

        var domain = ExtractDomain(entityId);
        return excludeDomainPatterns.Any(p =>
            MatchesEntityFilter(entityId, p) || Matches(domain, p) || MatchesEntityFilter(domain, p));
    }
}
