using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Mosaic.Core;

/// <summary>Host callback that decides whether one generated-code tool call may proceed.</summary>
public delegate ValueTask<bool> MosaicApprovalCallback(string executionId, string toolName, JsonElement arguments, CancellationToken cancellationToken);

/// <summary>Runs model-written JavaScript in a restricted Linux container and mediates every tool call.</summary>
public sealed class MosaicCodeExecutor(ToolRegistry registry, MosaicCodeSandboxOptions options, MosaicDurableToolRunner durableRunner)
{
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>Executes one async JavaScript function with only the selected host tools.</summary>
    public async ValueTask<JsonElement> ExecuteAsync(string executionId, string code, IEnumerable<string> allowedTools,
        IReadOnlySet<Capability> grantedCapabilities, MosaicApprovalCallback approvalCallback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(durableRunner);
        ArgumentNullException.ThrowIfNull(allowedTools);
        ArgumentNullException.ThrowIfNull(grantedCapabilities);
        ArgumentNullException.ThrowIfNull(approvalCallback);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var codeBytes = Encoding.UTF8.GetBytes(code);
        if (codeBytes.Length > options.MaximumCodeBytes) throw new WorkflowRejectedException("Generated code exceeds the configured byte limit.");
        var toolSet = ResolveTools(allowedTools, grantedCapabilities);
        return await RunContainerAsync(executionId, code, toolSet, grantedCapabilities, approvalCallback, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyDictionary<string, ToolDefinition> ResolveTools(IEnumerable<string> allowedTools, IReadOnlySet<Capability> grants)
    {
        var selected = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
        foreach (var name in allowedTools.Distinct(StringComparer.Ordinal))
        {
            if (!registry.TryGet(name, out var tool) || tool is null || !grants.Contains(tool.RequiredCapability))
                throw new WorkflowRejectedException("A selected code tool is unavailable or unauthorized.");
            selected.Add(name, tool);
        }
        if (selected.Count == 0) throw new WorkflowRejectedException("At least one host-authorized tool must be selected.");
        return selected;
    }

    private async ValueTask<JsonElement> RunContainerAsync(string executionId, string code, IReadOnlyDictionary<string, ToolDefinition> tools,
        IReadOnlySet<Capability> grants, MosaicApprovalCallback approvalCallback, CancellationToken cancellationToken)
    {
        var containerName = $"mosaic-{Guid.NewGuid():N}";
        using var process = StartContainer(containerName);
        using var timeout = new CancellationTokenSource(options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var calls = 0;
        try
        {
            await WriteMessageAsync(process, new SandboxRequest("start", code, tools.Keys.ToArray()), linked.Token).ConfigureAwait(false);
            while (await ReadMessageAsync(process, linked.Token).ConfigureAwait(false) is { } message)
            {
                var response = await HandleMessageAsync(message, executionId, tools, grants, approvalCallback, ++calls, linked.Token).ConfigureAwait(false);
                if (response.Output is { } output) return output;
                await WriteMessageAsync(process, response.Message!, linked.Token).ConfigureAwait(false);
                if (calls > options.MaximumToolCalls) throw new WorkflowRejectedException("Generated code exceeded the host tool-call limit.");
            }
            throw new InvalidOperationException("Sandbox exited before returning a result.");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Generated code exceeded its configured execution time.");
        }
        finally
        {
            await StopContainerAsync(containerName).ConfigureAwait(false);
            await TerminateContainerClientAsync(process).ConfigureAwait(false);
        }
    }

    private Process StartContainer(string name)
    {
        var start = new ProcessStartInfo(options.ContainerRuntime)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "run", "--rm", "--init", "-i", "--name", name, "--network=none", "--read-only", "--cap-drop=ALL", "--security-opt=no-new-privileges:true", "--pids-limit=64", "--memory=256m", "--cpus=1", "--user=65532:65532", "--tmpfs", "/tmp:rw,noexec,nosuid,size=16m", options.ImageDigest, "node", "/opt/mosaic/runner.mjs" }) start.ArgumentList.Add(argument);
        var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("Container runtime could not start the code sandbox.");
        _ = DrainStandardErrorAsync(process.StandardError);
        return process;
    }

    private static async Task DrainStandardErrorAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) > 0) { }
    }

    private async ValueTask<SandboxReply> HandleMessageAsync(string message, string executionId,
        IReadOnlyDictionary<string, ToolDefinition> tools, IReadOnlySet<Capability> grants,
        MosaicApprovalCallback approvalCallback, int calls, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message);
        RejectDuplicateProperties(document.RootElement);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new WorkflowRejectedException("Sandbox message must be a JSON object.");
        var type = root.GetProperty("type").GetString();
        if (type == "result") return SandboxReply.Completed(root.GetProperty("output").Clone());
        if (type == "error") throw new WorkflowRejectedException("Sandboxed code execution failed.");
        if (type != "tool_call" || calls > options.MaximumToolCalls) throw new WorkflowRejectedException("Sandbox returned an invalid or excessive tool request.");
        var identifier = root.GetProperty("id").GetString() ?? throw new WorkflowRejectedException("Sandbox tool request is missing its identifier.");
        var name = root.GetProperty("name").GetString() ?? string.Empty;
        if (!tools.TryGetValue(name, out var tool) || !grants.Contains(tool.RequiredCapability))
            throw new WorkflowRejectedException("Sandbox requested a tool that was not selected or authorized.");
        var arguments = root.GetProperty("arguments");
        if (arguments.ValueKind != JsonValueKind.Object) throw new WorkflowRejectedException("Sandbox tool arguments must be a JSON object.");
        var toolArguments = arguments.Clone();
        var durableId = $"{executionId}:{identifier}";
        var record = await durableRunner.BeginAsync(durableId, name, toolArguments, grants, cancellationToken).ConfigureAwait(false);
        if (record.State == MosaicExecutionState.PendingApproval)
        {
            var approved = await approvalCallback(durableId, name, toolArguments, cancellationToken).ConfigureAwait(false);
            record = await durableRunner.ResolveAsync(durableId, approved, grants, cancellationToken).ConfigureAwait(false);
        }
        else if (record.State == MosaicExecutionState.Approved)
        {
            record = await durableRunner.ResolveAsync(durableId, true, grants, cancellationToken).ConfigureAwait(false);
        }
        if (record.State != MosaicExecutionState.Completed || record.Output is not { } output)
            throw new WorkflowRejectedException("Generated-code tool call was not approved or did not complete.");
        return SandboxReply.ToolResult(identifier, output);
    }

    private async ValueTask<string?> ReadMessageAsync(Process process, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[1];
        while (await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
        {
            if (buffer[0] == (byte)'\n') return Encoding.UTF8.GetString(message.ToArray()).TrimEnd('\r');
            if (message.Length >= options.MaximumMessageBytes) throw new WorkflowRejectedException("Sandbox message exceeds the configured byte limit.");
            message.WriteByte(buffer[0]);
        }
        if (message.Length == 0) return null;
        throw new InvalidOperationException("Sandbox protocol ended without a newline-terminated response.");
    }

    private async ValueTask WriteMessageAsync(Process process, object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, WireOptions);
        if (Encoding.UTF8.GetByteCount(json) > options.MaximumMessageBytes) throw new WorkflowRejectedException("Sandbox message exceeds the configured byte limit.");
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask StopContainerAsync(string name)
    {
        Process? stop = null;
        try
        {
            var start = new ProcessStartInfo(options.ContainerRuntime)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("kill");
            start.ArgumentList.Add(name);
            stop = Process.Start(start);
            if (stop is null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stop.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { stop?.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        catch (System.ComponentModel.Win32Exception) { }
        finally { stop?.Dispose(); }
    }

    private static async ValueTask TerminateContainerClientAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (OperationCanceledException) { }
    }

    private sealed record SandboxRequest(string Type, string Code, IReadOnlyCollection<string> Tools);
    private sealed record ToolResponse(string Type, string Id, JsonElement Output, string? Error = null);
    private sealed record SandboxReply(JsonElement? Output, ToolResponse? Message)
    {
        public static SandboxReply Completed(JsonElement output) => new(output, null);
        public static SandboxReply ToolResult(string id, JsonElement output) => new(null, new ToolResponse("tool_result", id, output));
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new WorkflowRejectedException("Sandbox message contains duplicate JSON properties.");
                RejectDuplicateProperties(property.Value);
            }
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) RejectDuplicateProperties(value);
        }
    }
}
