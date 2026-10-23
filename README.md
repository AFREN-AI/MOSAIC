# MOSAIC

**Model Orchestrated Sandboxed API Interaction Core** is a .NET library for running short, structured workflows over host-registered tools. A model can compose tool calls, branch on an earlier boolean result, and map a bounded list through a registered tool while the host retains control of which capabilities are granted.

MOSAIC is an early reference implementation. Its v0.1 plan contract is shared by the .NET core and language wrappers. It is not a general-purpose code sandbox, an MCP server, or a replacement for operating-system isolation. It never evaluates generated C#, JavaScript, Python, shell, or other source code. A plan is data, and the only executable operations are handlers explicitly registered by the host.

## What it does

- Accepts a bounded JSON plan with an ordered list of tool calls and one selected output.
- Allows a later step to consume the complete JSON result of an earlier step using `{"$ref":"step-id"}`.
- Supports a boolean `when` branch and bounded `forEach` over earlier array results; each executed tool call still passes the same capability check.
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

The reference object replaces its argument value with the full result of the named earlier step. A loop refers to each item in its arguments with `{"$item":true}`. `when` accepts a boolean literal or reference to an earlier boolean result. Loops are capped by `MaximumLoopIterations` and all executed calls share `MaximumSteps`; there are no expressions, property paths, nested loops or dynamic tool creation.

```json
{
  "steps": [
    { "id": "enabled", "tool": "feature.enabled", "arguments": {} },
    { "id": "users", "tool": "directory.list", "arguments": {}, "when": { "$ref": "enabled" } },
    {
      "id": "checks",
      "forEach": {
        "items": { "$ref": "users" },
        "tool": "policy.check",
        "arguments": { "user": { "$item": true } }
      }
    }
  ],
  "output": "checks"
}
```

## Host protocol and process runner

The v1.0 stdio protocol carries a request ID, version, method and workflow plan. Callers cannot supply capabilities. The host applies its own grants. `MosaicProcessClient` starts a fresh host process per request and bounds request/response bytes, runtime, managed .NET heap and concurrent workers. It kills the worker tree on timeout or cancellation and discards worker stderr.

This worker mode is process separation, **not a complete security sandbox**: on its own the worker still inherits the launching user's operating-system permissions and may have network or filesystem access. For hostile tools, configure the executable to launch inside a separately enforced container or operating-system sandbox with read-only filesystems, resource quotas and network denial. The managed heap limit does not constrain native allocations; timeout is wall-clock, not a kernel CPU quota.

## MCP integration

The `AfrenAI.Mosaic.Mcp` package adapts the engine to the official MCP C# SDK. It exposes one `mosaic_execute` tool that accepts a plan and invokes the same host-owned capability policy. Your application chooses and configures its MCP transport; the package does not start an implicit listener.

```csharp
builder.Services
    .AddMosaicMcpServer(protocolServer)
    .WithStdioServerTransport();
```

## GitHub Packages

The GitHub Actions workflow publishes `AfrenAI.Mosaic` and `AfrenAI.Mosaic.Mcp` to the AFREN-AI GitHub NuGet registry on version tags. CI builds and tests the full `.slnx`, then packs both libraries. Install using the feed URL `https://nuget.pkg.github.com/AFREN-AI/index.json`; GitHub Packages requires a classic personal access token even for public NuGet packages. See [GitHub's NuGet registry guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry) for credential setup.

## NuGet.org distribution

GitHub Packages remains the source of tagged releases. A separate manually dispatched workflow can publish the same libraries to NuGet.org, which permits normal public NuGet restore without GitHub credentials. Before enabling it, configure the repository secret `NUGET_API_KEY`. Then run **Publish NuGet.org packages**, supply an unused semantic version, and explicitly check the public publication confirmation. NuGet.org package IDs and published versions are permanent; the workflow does not run on tags.

## Progressive tool discovery

Add descriptions and JSON Schema input definitions to each `ToolDefinition`. Hosts can call `ToolRegistry.Search(query)` and `ToolRegistry.Describe(name)`, or use the MCP tools `mosaic_search` and `mosaic_describe`. Discovery returns public metadata only; it never includes host capability names or grants. The host still checks capabilities immediately before invocation.

## Durable approvals and replay

`MosaicDurableToolRunner` records a tool request before approval, persists an approval or rejection, and saves completed output for replay. `FileMosaicExecutionStore` stores JSON snapshots in a host-selected directory and is intended for a single host process. Protect that directory as application data: arguments and outputs may contain sensitive information. For multi-process or multi-region hosts, implement `IMosaicExecutionStore` with a transactional shared store. Completed executions replay their stored output. A crash after a side effect but before its result is journaled can cause the handler to run again; durable handlers must honor the execution ID as their idempotency key.

## Generated JavaScript execution

`MosaicCodeExecutor` runs async JavaScript functions in a Linux container pinned by image digest. Build the runner from [`sandbox/Dockerfile`](sandbox/Dockerfile), publish it to a trusted registry, and pass its immutable `repository@sha256:...` reference to `MosaicCodeSandboxOptions`. The host disables network access, uses a read-only filesystem and non-root UID, drops Linux capabilities, and applies process, memory, CPU, message, call-count, and wall-clock limits. Only explicitly selected, host-authorized tools are exposed through the RPC bridge. Every invocation waits for the host approval callback and is journaled with a stable idempotency key, so these tools must use idempotency-aware handlers. The Node `vm` module is not itself the security boundary; container isolation is required. Use a trusted rootless container runtime with its default seccomp/AppArmor or SELinux policy, and do not mount host paths or pass secrets into the image.

```csharp
var runner = new MosaicDurableToolRunner(registry, new FileMosaicExecutionStore("mosaic-state"));
var sandbox = new MosaicCodeSandboxOptions("ghcr.io/afren-ai/mosaic-sandbox@sha256:<64-hex-digest>");
var codeExecutor = new MosaicCodeExecutor(registry, sandbox, runner);
var output = await codeExecutor.ExecuteAsync(
    executionId: "request-2026-0001",
    code: "async (tools) => await tools['catalog.lookup']({ key: 'item-42' })",
    allowedTools: ["catalog.lookup"],
    grantedCapabilities: grants,
    approvalCallback: async (id, toolName, arguments, token) =>
        await approvalUi.RequestAsync(id, toolName, arguments, token));
```

## Language wrappers

The plan contract is stable enough for client libraries that construct and serialize plans. These wrappers share the JSON format and validate basic construction rules such as non-empty identifiers, duplicate steps, selected output, and references to earlier steps:

- TypeScript and JavaScript: [`wrappers/typescript`](wrappers/typescript). The TypeScript package compiles to JavaScript for both module consumers.
- Python: [`wrappers/python`](wrappers/python).
- Go: [`wrappers/go`](wrappers/go).
- C: [`wrappers/c`](wrappers/c), a small C11 plan serializer.
- C++: [`wrappers/cpp`](wrappers/cpp), a C++17 builder over the C serializer.
- Ruby: [`wrappers/ruby`](wrappers/ruby).
- Haskell: [`wrappers/haskell`](wrappers/haskell).
- Rust: [`wrappers/rust`](wrappers/rust).

These wrappers build plan JSON; they do not invoke the .NET engine or execute tools. An application passes the serialized plan to its trusted .NET host, which owns tool registration, authorization, and execution. The shared plan schema is in [`conformance/plan.schema.json`](conformance/plan.schema.json). Plan contract compatibility is intended to follow semantic versioning from v1.0; until then, wrappers are versioned `0.1.x` alongside this early API.

## Versioned host protocol

Protocol v1.0 accepts one JSON request per line over standard input and returns one JSON response per line on standard output. See [`conformance/host-request.schema.json`](conformance/host-request.schema.json) and [`conformance/host-response.schema.json`](conformance/host-response.schema.json). The request carries a request ID, protocol version, method, and workflow plan. It cannot supply or expand capabilities; the host grants permissions from its own trusted configuration.

Run the reference host with `dotnet run --project src/Mosaic.Host`. The reference host exposes only a demonstration `math.add` tool under its `math.read` capability. For example:

```json
{"protocolVersion":"1.0","requestId":"demo-1","method":"execute","plan":{"steps":[{"id":"sum","tool":"math.add","arguments":{"left":20,"right":22}}],"output":"sum"}}
```

## Security model

The model that proposes a plan is untrusted. Tool handlers and capability grants belong to the integrating host. MOSAIC checks registered tool identity and its required capability; a plan cannot grant itself permissions. Unknown tools are denied. Give the executor only the tools and capabilities appropriate for a single task.

In-process handlers run with host process privileges. Use `MosaicProcessClient` to isolate the host's worker lifetime and bound protocol I/O, or use a container/OS sandbox for hostile handlers. Do not treat process separation alone as a security boundary.

The executor does not log plan arguments or tool results. Hosts should avoid recording sensitive payloads, credentials, or personal data in their own telemetry.

## Build and verify

```sh
dotnet restore MOSAIC.slnx
dotnet test MOSAIC.slnx --configuration Release --no-restore
dotnet run --project samples/Mosaic.Demo
```

## Packages

The core and MCP adapter are published to GitHub Packages as `AfrenAI.Mosaic` and `AfrenAI.Mosaic.Mcp`. Package feeds and install instructions are in the GitHub Packages section below.

MOSAIC is a workflow orchestrator, not a drop-in implementation of arbitrary-code Code Mode. It uses a constrained, registered-tool plan to make composition and host-side authorization explicit.

## License

Apache-2.0. See [LICENSE](LICENSE).
