namespace Mosaic.Core;

/// <summary>A host-defined permission required to invoke a tool.</summary>
public sealed record Capability
{
    public string Name { get; }

    public Capability(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
