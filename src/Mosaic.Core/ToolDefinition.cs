using System.Text.Json;

namespace Mosaic.Core;

/// <summary>A registered operation and its host-side authorization requirement.</summary>
public sealed class ToolDefinition
{
    private readonly Func<JsonElement, CancellationToken, ValueTask<JsonElement>> _handler;

    public string Name { get; }
    public Capability RequiredCapability { get; }

    public ToolDefinition(
        string name,
        Capability requiredCapability,
        Func<JsonElement, CancellationToken, ValueTask<JsonElement>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(requiredCapability);
        ArgumentNullException.ThrowIfNull(handler);
        Name = name;
        RequiredCapability = requiredCapability;
        _handler = handler;
    }

    internal ValueTask<JsonElement> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken) =>
        _handler(arguments, cancellationToken);
}
