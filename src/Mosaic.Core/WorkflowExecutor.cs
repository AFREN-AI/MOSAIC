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
        var completedCalls = 0;
        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (step.When is { } condition)
            {
                var resolvedCondition = ResolveReferences(condition, results, null);
                if (resolvedCondition.ValueKind != JsonValueKind.True && resolvedCondition.ValueKind != JsonValueKind.False)
                {
                    throw new WorkflowRejectedException("when must resolve to a boolean value.");
                }
                if (resolvedCondition.ValueKind == JsonValueKind.False)
                {
                    results.Add(step.Id, JsonSerializer.SerializeToElement<object?>(null));
                    continue;
                }
            }

            if (step.ForEach is { } loop)
            {
                var items = ResolveReferences(loop.Items, results, null);
                if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > _limits.MaximumLoopIterations)
                {
                    throw new WorkflowRejectedException("forEach items must be an array within the configured iteration limit.");
                }
                var outputs = new List<JsonElement>(items.GetArrayLength());
                foreach (var item in items.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (completedCalls >= _limits.MaximumSteps)
                    {
                        throw new WorkflowRejectedException("Workflow exceeds the configured tool-call limit.");
                    }
                    outputs.Add(await InvokeToolAsync(loop.Tool, loop.Arguments, item, results, grantedCapabilities, cancellationToken).ConfigureAwait(false));
                    completedCalls++;
                }
                results.Add(step.Id, JsonSerializer.SerializeToElement(outputs));
                continue;
            }

            if (completedCalls >= _limits.MaximumSteps)
            {
                throw new WorkflowRejectedException("Workflow exceeds the configured tool-call limit.");
            }
            var toolOutput = await InvokeToolAsync(step.Tool!, step.Arguments!, null, results, grantedCapabilities, cancellationToken).ConfigureAwait(false);
            completedCalls++;
            results.Add(step.Id, toolOutput.Clone());
        }

        var output = results[plan.OutputStep];
        if (JsonSerializer.SerializeToUtf8Bytes(output).Length > _limits.MaximumOutputBytes)
        {
            throw new WorkflowRejectedException("Workflow output exceeds the configured byte limit.");
        }
        return new WorkflowResult(output.Clone(), completedCalls);
    }

    private async ValueTask<JsonElement> InvokeToolAsync(
        string toolName,
        JsonElement argumentsTemplate,
        JsonElement? item,
        IReadOnlyDictionary<string, JsonElement> results,
        IReadOnlySet<Capability> grantedCapabilities,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGet(toolName, out var tool) || tool is null)
        {
            throw new WorkflowRejectedException($"Tool is not registered: {toolName}");
        }
        if (!grantedCapabilities.Contains(tool.RequiredCapability))
        {
            throw new WorkflowRejectedException($"Required capability was not granted for tool: {toolName}");
        }
        var arguments = ResolveReferences(argumentsTemplate, results, item);
        return (await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false)).Clone();
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
            if (item.ValueKind != JsonValueKind.Object || !HasNoUnknownProperties(item, "id", "tool", "arguments", "forEach", "when"))
            {
                throw new WorkflowRejectedException("Step contains an unknown property.");
            }
            var id = Required(item, "id").GetString();
            if (string.IsNullOrWhiteSpace(id) || !identifiers.Add(id))
            {
                throw new WorkflowRejectedException("Step identifiers must be non-empty and unique.");
            }
            var hasLoop = item.TryGetProperty("forEach", out var loopElement);
            var hasToolName = item.TryGetProperty("tool", out var toolElement);
            var hasArguments = item.TryGetProperty("arguments", out var argumentsElement);
            var hasTool = hasToolName || hasArguments;
            var when = item.TryGetProperty("when", out var whenElement) ? whenElement.Clone() : (JsonElement?)null;
            if (hasLoop == hasTool || (hasLoop && when is not null))
            {
                throw new WorkflowRejectedException("A step must be either a tool call or forEach loop; loops cannot have when.");
            }
            if (hasLoop)
            {
                if (loopElement.ValueKind != JsonValueKind.Object || !HasOnlyProperties(loopElement, "items", "tool", "arguments"))
                {
                    throw new WorkflowRejectedException("forEach must contain only items, tool and arguments.");
                }
                var loopItems = Required(loopElement, "items");
                var loopTool = Required(loopElement, "tool").GetString();
                var loopArguments = Required(loopElement, "arguments");
                if (loopItems.ValueKind != JsonValueKind.Object || !HasOnlyProperties(loopItems, "$ref") || loopTool is null || string.IsNullOrWhiteSpace(loopTool) || loopArguments.ValueKind != JsonValueKind.Object)
                {
                    throw new WorkflowRejectedException("forEach items must reference an earlier result; tool and arguments must be valid.");
                }
                steps.Add(new Step(id, null, null, new ForEach(loopItems.Clone(), loopTool, loopArguments.Clone()), null));
                continue;
            }
            var tool = hasToolName ? toolElement.GetString() : null;
            var arguments = argumentsElement;
            if (string.IsNullOrWhiteSpace(tool) || arguments.ValueKind != JsonValueKind.Object || (when is not null && when.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Object)))
            {
                throw new WorkflowRejectedException("Tool names must be non-empty; arguments must be an object; when must be boolean or a result reference.");
            }
            if (when.HasValue && when.Value.ValueKind == JsonValueKind.Object &&
                (when.Value.EnumerateObject().Count() != 1 || !when.Value.TryGetProperty("$ref", out var reference) || reference.ValueKind != JsonValueKind.String))
            {
                throw new WorkflowRejectedException("A when object must contain only a string $ref.");
            }
            steps.Add(new Step(id, tool, arguments.Clone(), null, when));
        }
        if (!identifiers.Contains(output))
        {
            throw new WorkflowRejectedException("Output must refer to a declared step.");
        }
        return new Plan(steps, output);
    }

    private static JsonElement ResolveReferences(JsonElement value, IReadOnlyDictionary<string, JsonElement> results, JsonElement? item)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteResolved(writer, value, results, item);
        }
        using var resolved = JsonDocument.Parse(buffer.ToArray(), StrictJsonOptions);
        return resolved.RootElement.Clone();
    }

    private static void WriteResolved(Utf8JsonWriter writer, JsonElement value, IReadOnlyDictionary<string, JsonElement> results, JsonElement? loopItem)
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
            if (properties.Length == 1 && properties[0].NameEquals("$item"))
            {
                if (loopItem is null || properties[0].Value.ValueKind != JsonValueKind.True)
                {
                    throw new WorkflowRejectedException("$item is only valid as true inside a forEach argument template.");
                }
                loopItem.Value.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                writer.WritePropertyName(property.Name);
                WriteResolved(writer, property.Value, results, loopItem);
            }
            writer.WriteEndObject();
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var child in value.EnumerateArray())
            {
                WriteResolved(writer, child, results, loopItem);
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

    private static bool HasNoUnknownProperties(JsonElement element, params string[] allowed) =>
        element.EnumerateObject().All(property => allowed.Contains(property.Name, StringComparer.Ordinal));

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new WorkflowRejectedException($"Missing required property: {name}");

    private sealed record ForEach(JsonElement Items, string Tool, JsonElement Arguments);
    private sealed record Step(string Id, string? Tool, JsonElement? Arguments, ForEach? ForEach, JsonElement? When);
    private sealed record Plan(IReadOnlyList<Step> Steps, string OutputStep);
}

/// <summary>A workflow was invalid, exceeded a host limit or lacked authorization.</summary>
public sealed class WorkflowRejectedException(string message) : Exception(message);
