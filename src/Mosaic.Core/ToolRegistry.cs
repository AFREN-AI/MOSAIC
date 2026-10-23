namespace Mosaic.Core;

/// <summary>Immutable registry of tools available to workflows.</summary>
public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, ToolDefinition> _tools;

    public ToolRegistry(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var map = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            ArgumentNullException.ThrowIfNull(tool);
            if (!map.TryAdd(tool.Name, tool))
            {
                throw new ArgumentException($"Duplicate tool name: {tool.Name}", nameof(tools));
            }
        }
        _tools = map;
    }

    internal bool TryGet(string name, out ToolDefinition? tool) => _tools.TryGetValue(name, out tool);

    /// <summary>Searches registered tool names and descriptions without exposing host capabilities.</summary>
    public IReadOnlyList<ToolDescriptor> Search(string query, int maximumResults = 20)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (maximumResults is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults), "Search result limit must be between one and one hundred.");
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return _tools.Values
            .Select(tool => new { Tool = tool, Score = Score(tool, terms) })
            .Where(candidate => terms.Length == 0 || candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Tool.Name, StringComparer.Ordinal)
            .Take(maximumResults)
            .Select(candidate => Describe(candidate.Tool))
            .ToArray();
    }

    /// <summary>Returns the public descriptor for a registered tool, if present.</summary>
    public ToolDescriptor? Describe(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _tools.TryGetValue(name, out var tool) ? Describe(tool) : null;
    }

    private static int Score(ToolDefinition tool, IReadOnlyList<string> terms)
    {
        var searchableText = $"{tool.Name} {tool.Description}";
        return terms.Sum(term => searchableText.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
    }

    private static ToolDescriptor Describe(ToolDefinition tool) =>
        new(tool.Name, tool.Description, tool.InputSchema);
}

/// <summary>Model-facing metadata for a registered tool; authorization remains host-owned.</summary>
public sealed record ToolDescriptor(string Name, string Description, System.Text.Json.JsonElement? InputSchema);
