using System.Text.Json;

namespace Mosaic.Core;

/// <summary>The selected workflow result and number of tool calls completed.</summary>
public sealed record WorkflowResult(JsonElement Output, int CompletedCalls);
