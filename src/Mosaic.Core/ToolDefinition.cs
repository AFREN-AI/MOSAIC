using System.Text.Json;

namespace Mosaic.Core;

/// <summary>A registered operation and its host-side authorization requirement.</summary>
public sealed class ToolDefinition
{
    private readonly Func<JsonElement, CancellationToken, ValueTask<JsonElement>> _handler;
    private readonly Func<JsonElement, string, CancellationToken, ValueTask<JsonElement>>? _idempotentHandler;
    private readonly JsonElement? _inputSchema;

    public string Name { get; }
    public Capability RequiredCapability { get; }
    public string Description { get; }
    public JsonElement? InputSchema => _inputSchema?.Clone();
    public bool SupportsIdempotency => _idempotentHandler is not null;

    public ToolDefinition(
        string name,
        Capability requiredCapability,
        Func<JsonElement, CancellationToken, ValueTask<JsonElement>> handler,
        string description = "",
        JsonElement? inputSchema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(requiredCapability);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(description);
        if (inputSchema is { ValueKind: not JsonValueKind.Object })
        {
            throw new ArgumentException("Tool input schema must be a JSON object.", nameof(inputSchema));
        }
        Name = name;
        RequiredCapability = requiredCapability;
        _handler = handler;
        Description = description;
        _inputSchema = inputSchema?.Clone();
    }

    /// <summary>Creates a tool handler that receives a stable idempotency key for durable replay.</summary>
    public ToolDefinition(string name, Capability requiredCapability,
        Func<JsonElement, string, CancellationToken, ValueTask<JsonElement>> idempotentHandler,
        string description = "", JsonElement? inputSchema = null)
        : this(name, requiredCapability, (arguments, cancellationToken) =>
            idempotentHandler(arguments, Guid.NewGuid().ToString("N"), cancellationToken), description, inputSchema)
    {
        ArgumentNullException.ThrowIfNull(idempotentHandler);
        _idempotentHandler = idempotentHandler;
    }

    internal ValueTask<JsonElement> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken) =>
        _handler(arguments, cancellationToken);

    internal ValueTask<JsonElement> InvokeIdempotentlyAsync(JsonElement arguments, string idempotencyKey, CancellationToken cancellationToken) =>
        _idempotentHandler is null
            ? throw new WorkflowRejectedException($"Tool '{Name}' does not support durable idempotency.")
            : _idempotentHandler(arguments, idempotencyKey, cancellationToken);
}
