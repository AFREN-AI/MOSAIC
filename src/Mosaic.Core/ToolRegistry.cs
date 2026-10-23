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
}
