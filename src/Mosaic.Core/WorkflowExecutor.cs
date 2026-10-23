using System.Text.Json;
using System.IO;

namespace Mosaic.Core;

/// <summary>Executes a bounded JSON workflow using only host-registered tools.</summary>
public sealed class WorkflowExecutor(ToolRegistry registry, WorkflowLimits? limits = null)
{
    private readonly WorkflowLimits _limits = limits ?? new WorkflowLimits();
    private static readonly JsonDocumentOptions StrictJsonOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 };

    /// <summary>Validates, authorizes and executes one workflow plan.</summary>
    /// <param name="planJson">JSON object with steps and an output step identifier.</param>
    /// <param name="grantedCapabilities">Permissions supplied by the trusted host.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    public async ValueTask<WorkflowResult> ExecuteAsync(
        ReadOnlyMemory<byte> planJson,
        IReadOnlySet<Capability> grantedCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(grantedCapabilities);
        _limits.Validate();
        if (planJson.Length > _limits.MaximumInputBytes)
        {
            throw new WorkflowRejectedException("Workflow input exceeds the configured byte limit.");
        }

        using var document = JsonDocument.Parse(planJson, StrictJsonOptions);
        RejectDuplicateProperties(document.RootElement);
        var plan = ParsePlan(document.RootElement);
        var results = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registry.TryGet(step.Tool, out var tool) || tool is null)
            {
                throw new WorkflowRejectedException($"Tool is not registered: {step.Tool}");
            }
            if (!grantedCapabilities.Contains(tool.RequiredCapability))
            {
                throw new WorkflowRejectedException($"Required capability was not granted for tool: {step.Tool}");
            }
            var arguments = ResolveReferences(step.Arguments, results);
            var toolOutput = await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
            results.Add(step.Id, toolOutput.Clone());
        }

        var output = results[plan.OutputStep];
        if (JsonSerializer.SerializeToUtf8Bytes(output).Length > _limits.MaximumOutputBytes)
        {
            throw new WorkflowRejectedException("Workflow output exceeds the configured byte limit.");
        }
        return new WorkflowResult(output.Clone(), plan.Steps.Count);
    }

    private Plan ParsePlan(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !HasOnlyProperties(root, "steps", "output"))
        {
            throw new WorkflowRejectedException("Plan must contain only 'steps' and 'output'.");
        }
        var stepsElement = Required(root, "steps");
        var output = Required(root, "output").GetString();
        if (stepsElement.ValueKind != JsonValueKind.Array || stepsElement.GetArrayLength() is < 1 || stepsElement.GetArrayLength() > _limits.MaximumSteps)
        {
            throw new WorkflowRejectedException("Plan steps are empty or exceed the configured limit.");
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new WorkflowRejectedException("Plan output must name a step.");
        }
        var steps = new List<Step>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in stepsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !HasOnlyProperties(item, "id", "tool", "arguments"))
            {
                throw new WorkflowRejectedException("Each step must contain only 'id', 'tool' and 'arguments'.");
            }
            var id = Required(item, "id").GetString();
            var tool = Required(item, "tool").GetString();
            var arguments = Required(item, "arguments");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(tool) || arguments.ValueKind != JsonValueKind.Object || !identifiers.Add(id))
            {
                throw new WorkflowRejectedException("Step identifiers and tool names must be non-empty and unique; arguments must be an object.");
            }
            steps.Add(new Step(id, tool, arguments.Clone()));
        }
        if (!identifiers.Contains(output))
        {
            throw new WorkflowRejectedException("Output must refer to a declared step.");
        }
        return new Plan(steps, output);
    }

    private static JsonElement ResolveReferences(JsonElement value, IReadOnlyDictionary<string, JsonElement> results)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolved(writer, value, results);
        }
        using var resolved = JsonDocument.Parse(buffer.ToArray(), StrictJsonOptions);
        return resolved.RootElement.Clone();
    }

    private static void WriteResolved(Utf8JsonWriter writer, JsonElement value, IReadOnlyDictionary<string, JsonElement> results)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Length == 1 && properties[0].NameEquals("$ref"))
            {
                var identifier = properties[0].Value.GetString();
                if (identifier is null || !results.TryGetValue(identifier, out var result))
                {
                    throw new WorkflowRejectedException("References must point to results from earlier steps.");
                }
                result.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteResolved(writer, property.Value, results);
            }
            writer.WriteEndObject();
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                WriteResolved(writer, item, results);
            }
            writer.WriteEndArray();
            return;
        }
        value.WriteTo(writer);
    }

    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new WorkflowRejectedException($"Duplicate JSON property: {property.Name}");
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

    private static bool HasOnlyProperties(JsonElement element, params string[] allowed) =>
        element.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal)) &&
        allowed.All(name => element.TryGetProperty(name, out _));

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new WorkflowRejectedException($"Missing required property: {name}");

    private sealed record Step(string Id, string Tool, JsonElement Arguments);
    private sealed record Plan(IReadOnlyList<Step> Steps, string OutputStep);
}

/// <summary>A workflow was invalid, exceeded a host limit or lacked authorization.</summary>
public sealed class WorkflowRejectedException(string message) : Exception(message);
