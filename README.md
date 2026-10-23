# MOSAIC

**Model Orchestrated Sandboxed API Interaction Core** is a .NET library for running short, structured workflows over host-registered tools. A model can compose tool calls and pass an earlier result to a later call, while the host retains control of which capabilities are granted.

MOSAIC is an early reference implementation. It is not a general-purpose code sandbox, an MCP server, or a replacement for operating-system isolation. It never evaluates generated C#, JavaScript, Python, shell, or other source code. A plan is data, and the only executable operations are handlers explicitly registered by the host.

## What it does

- Accepts a bounded JSON plan with an ordered list of tool calls and one selected output.
- Allows a later step to consume the complete JSON result of an earlier step using `{"$ref":"step-id"}`.
- Checks each tool's required capability against the host-supplied grants immediately before invocation.
- Rejects unknown tools, unknown plan fields, repeated step IDs, missing grants, and forward references.
- Applies host configured limits to plan size, call count, JSON nesting, and selected result size.
- Observes cancellation between calls and forwards the cancellation token to handlers.

## Quick start

Requirements: .NET 10 SDK. No SDK is installed by this project. CI uses GitHub hosted runners.

```csharp
var readCapability = new Capability("catalog.read");
var registry = new ToolRegistry([
    new ToolDefinition("lookup", readCapability, (arguments, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = arguments.GetProperty("key").GetString();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { key, found = true }));
    })
]);

var executor = new WorkflowExecutor(registry, new WorkflowLimits(MaximumSteps: 5));
var grants = new HashSet<Capability> { readCapability };
var result = await executor.ExecuteAsync(Encoding.UTF8.GetBytes(planJson), grants, cancellationToken);
```

Run the complete example with `dotnet run --project samples/Mosaic.Demo`.

Plan shape:

```json
{
  "steps": [
    { "id": "record", "tool": "lookup", "arguments": { "key": "item-42" } },
    { "id": "summary", "tool": "summarize", "arguments": { "record": { "$ref": "record" } } }
  ],
  "output": "summary"
}
```

The reference object replaces its argument value with the full result of the named earlier step. It does not support expressions, property paths, loops, or dynamic tool creation.

## Security model

The model that proposes a plan is untrusted. Tool handlers and capability grants belong to the integrating host. MOSAIC checks registered tool identity and its required capability; a plan cannot grant itself permissions. Unknown tools are denied. Give the executor only the tools and capabilities appropriate for a single task.

MOSAIC executes handlers in-process. A malicious or unsafe handler can access anything the host process can access, block indefinitely, allocate excessive resources, or ignore cancellation. The current limits bound plan input, step count, and returned output; they do not enforce per-handler CPU, memory, network, file-system, or wall clock limits. Run untrusted handlers in an independently isolated process or container with operating-system restrictions, and add host timeouts and quotas appropriate to your environment. Do not treat this library alone as a security boundary.

The executor does not log plan arguments or tool results. Hosts should avoid recording sensitive payloads, credentials, or personal data in their own telemetry.

## Build and verify

```sh
dotnet restore MOSAIC.slnx
dotnet test MOSAIC.slnx --configuration Release --no-restore
dotnet run --project samples/Mosaic.Demo
```

## Roadmap

- Stabilize the .NET API and threat model.
- Add an optional TypeScript client/wrapper that targets the stable .NET host interface.
- Explore other language clients only when the host protocol and compatibility requirements are concrete.
- Add handler timeout and resource-budget integration hooks.

MOSAIC is a workflow orchestrator, not a drop-in implementation of arbitrary-code Code Mode. It uses a constrained, registered-tool plan to make composition and host-side authorization explicit.

## License

Apache-2.0. See [LICENSE](LICENSE).
