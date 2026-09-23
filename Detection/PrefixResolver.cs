using rt_tester.Configuration;

namespace rt_tester.Detection;

public sealed record ResolvedId(string AgencyId, string OriginalId);

public sealed class PrefixResolver
{
    private readonly List<string> _prefixesLongestFirst;
    private readonly Dictionary<string, string> _prefixToAgency;

    public PrefixResolver(AggregatorConfig config)
    {
        _prefixToAgency = config.Agencies.ToDictionary(
            a => a.AgencyId + config.PrefixSeparator,
            a => a.AgencyId);

        _prefixesLongestFirst = _prefixToAgency.Keys
            .OrderByDescending(p => p.Length)
            .ToList();
    }

    /// <summary>Strips the longest matching agency prefix. Returns null if no configured agency prefix matches.</summary>
    public ResolvedId? Resolve(string? mergedId)
    {
        if (string.IsNullOrEmpty(mergedId)) return null;

        foreach (var prefix in _prefixesLongestFirst)
        {
            if (mergedId.StartsWith(prefix, StringComparison.Ordinal))
            {
                var agencyId = _prefixToAgency[prefix];
                var original = mergedId[prefix.Length..];
                return new ResolvedId(agencyId, original);
            }
        }

        return null;
    }

    public bool HasPrefixFor(string? id, string agencyId, string separator)
        => !string.IsNullOrEmpty(id) && id.StartsWith(agencyId + separator, StringComparison.Ordinal);
}
