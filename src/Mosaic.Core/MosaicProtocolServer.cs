using System.Text;
using System.Text.Json;

namespace Mosaic.Core;

/// <summary>Handles versioned host requests while retaining capabilities in the trusted host.</summary>
public sealed class MosaicProtocolServer
{
    public const string CurrentVersion = "1.0";
    public const int MaximumRequestBytes = 1_048_576;
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 32 };
    private static readonly JsonSerializerOptions WireJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly WorkflowExecutor _executor;
    private readonly HashSet<Capability> _hostGrantedCapabilities;

    /// <summary>Creates a protocol server with host-owned capabilities.</summary>
    public MosaicProtocolServer(WorkflowExecutor executor, IEnumerable<Capability> hostGrantedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(hostGrantedCapabilities);
        _executor = executor;
        _hostGrantedCapabilities = new HashSet<Capability>(hostGrantedCapabilities);
    }

    /// <summary>Searches host-registered tools without returning capability names.</summary>
    public IReadOnlyList<ToolDescriptor> SearchTools(string query, int maximumResults = 20) =>
        _executor.Registry.Search(query, maximumResults);

    /// <summary>Describes one host-registered tool without returning its capability name.</summary>
    public ToolDescriptor? DescribeTool(string name) => _executor.Registry.Describe(name);

    /// <summary>Validates and handles a single JSON request.</summary>
    public async ValueTask<MosaicProtocolResponse> HandleAsync(
        ReadOnlyMemory<byte> requestJson,
        CancellationToken cancellationToken = default)
    {
        string? requestId = null;
        if (requestJson.Length > MaximumRequestBytes)
        {
            return Failure(null, "request_too_large", "Request exceeds the configured byte limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(requestJson, JsonOptions);
            var request = ParseRequest(document.RootElement);
            requestId = request.RequestId;
            if (!string.Equals(request.ProtocolVersion, CurrentVersion, StringComparison.Ordinal))
            {
                return Failure(request.RequestId, "unsupported_version", "The requested protocol version is not supported.");
            }
            if (!string.Equals(request.Method, "execute", StringComparison.Ordinal))
            {
                return Failure(request.RequestId, "unsupported_method", "The requested method is not supported.");
            }

            var result = await _executor.ExecuteAsync(
                Encoding.UTF8.GetBytes(request.Plan.GetRawText()),
                _hostGrantedCapabilities,
                cancellationToken).ConfigureAwait(false);
            return new MosaicProtocolResponse(CurrentVersion, request.RequestId, true, result.Output, result.CompletedCalls, null, null);
        }
        catch (JsonException)
        {
            return Failure(null, "invalid_json", "Request must be valid JSON.");
        }
        catch (MosaicProtocolException exception)
        {
            return Failure(exception.RequestId, exception.ErrorCode, exception.Message);
        }
        catch (WorkflowRejectedException exception)
        {
            return Failure(requestId, "workflow_rejected", exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(requestId, "execution_failed", "Workflow execution failed.");
        }
    }

    /// <summary>Serializes a response using the protocol's canonical camel-case field names.</summary>
    public static byte[] SerializeResponse(MosaicProtocolResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonSerializer.SerializeToUtf8Bytes(response, WireJsonOptions);
    }

    /// <summary>Serializes a request with the protocol's canonical camel-case field names.</summary>
    public static byte[] SerializeRequest(MosaicProtocolRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.SerializeToUtf8Bytes(request, WireJsonOptions);
    }

    private static MosaicProtocolRequest ParseRequest(JsonElement root)
    {
        RejectDuplicateProperties(root);
        if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root, "protocolVersion", "requestId", "method", "plan"))
        {
            throw new MosaicProtocolException(null, "invalid_request", "Request must contain protocolVersion, requestId, method and plan only.");
        }

        var version = ReadString(root, "protocolVersion");
        var requestId = ReadString(root, "requestId");
        var method = ReadString(root, "method");
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
        {
            throw new MosaicProtocolException(null, "invalid_request", "requestId must contain 1 to 128 characters.");
        }
        var plan = root.GetProperty("plan");
        if (plan.ValueKind != JsonValueKind.Object)
        {
            throw new MosaicProtocolException(requestId, "invalid_request", "plan must be a JSON object.");
        }
        return new MosaicProtocolRequest(version, requestId, method, plan.Clone());
    }

    private static string ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new MosaicProtocolException(null, "invalid_request", $"{propertyName} must be a string.");
        }
        return value.GetString() ?? string.Empty;
    }

    private static bool HasExactProperties(JsonElement value, params string[] allowed) =>
        value.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal)) &&
        allowed.All(name => value.TryGetProperty(name, out _));

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("Duplicate JSON property.");
                }
                RejectDuplicateProperties(property.Value);
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item);
            }
        }
    }

    private static MosaicProtocolResponse Failure(string? requestId, string code, string message) =>
        new(CurrentVersion, requestId, false, null, null, code, message);
}

internal sealed class MosaicProtocolException(string? requestId, string errorCode, string message) : Exception(message)
{
    public string? RequestId { get; } = requestId;
    public string ErrorCode { get; } = errorCode;
}
