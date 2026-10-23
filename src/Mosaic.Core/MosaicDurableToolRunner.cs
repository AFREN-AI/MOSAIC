using System.Text.Json;

namespace Mosaic.Core;

/// <summary>Persists approval decisions and completed results for resumable tool calls.</summary>
public sealed class MosaicDurableToolRunner(ToolRegistry registry, IMosaicExecutionStore store)
{
    /// <summary>Creates an approval request without invoking the tool.</summary>
    public async ValueTask<MosaicExecutionRecord> BeginAsync(
        string executionId,
        string toolName,
        JsonElement arguments,
        IReadOnlySet<Capability> grantedCapabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(grantedCapabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        if (executionId.Length > 256) throw new ArgumentException("Execution identifiers cannot exceed 256 characters.", nameof(executionId));
        if (!registry.TryGet(toolName, out var tool) || tool is null || !grantedCapabilities.Contains(tool.RequiredCapability))
            throw new WorkflowRejectedException("Tool is unavailable or its required capability was not granted.");
        if (!tool.SupportsIdempotency)
            throw new WorkflowRejectedException("Durable execution requires a tool handler that honors idempotency keys.");
        var existing = await store.LoadAsync(executionId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            EnsureSameRequest(existing, toolName, arguments);
            return existing;
        }
        var request = new MosaicExecutionRecord(executionId, toolName, arguments.Clone(), MosaicExecutionState.PendingApproval);
        await store.SaveAsync(request, cancellationToken).ConfigureAwait(false);
        return request;
    }

    /// <summary>Records an approval or rejection and resumes an approved invocation.</summary>
    public async ValueTask<MosaicExecutionRecord> ResolveAsync(
        string executionId,
        bool approved,
        IReadOnlySet<Capability> grantedCapabilities,
        CancellationToken cancellationToken = default)
    {
        var record = await store.LoadAsync(executionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("No pending MOSAIC execution has this identifier.");
        if (record.State == MosaicExecutionState.Completed || record.State == MosaicExecutionState.Rejected) return record;
        if (!registry.TryGet(record.ToolName, out var tool) || tool is null || !grantedCapabilities.Contains(tool.RequiredCapability))
            throw new WorkflowRejectedException("Tool is unavailable or its required capability was not granted.");
        if (!tool.SupportsIdempotency)
            throw new WorkflowRejectedException("Durable execution requires a tool handler that honors idempotency keys.");
        if (!approved)
        {
            var rejected = record with { State = MosaicExecutionState.Rejected, RejectionReason = "Approval denied." };
            await store.SaveAsync(rejected, cancellationToken).ConfigureAwait(false);
            return rejected;
        }
        var approvedRecord = record with { State = MosaicExecutionState.Approved };
        await store.SaveAsync(approvedRecord, cancellationToken).ConfigureAwait(false);
        var output = await tool.InvokeIdempotentlyAsync(record.Arguments, record.ExecutionId, cancellationToken).ConfigureAwait(false);
        var completed = approvedRecord with { State = MosaicExecutionState.Completed, Output = output.Clone() };
        await store.SaveAsync(completed, cancellationToken).ConfigureAwait(false);
        return completed;
    }

    private static void EnsureSameRequest(MosaicExecutionRecord existing, string toolName, JsonElement arguments)
    {
        if (!string.Equals(existing.ToolName, toolName, StringComparison.Ordinal) ||
            !JsonElement.DeepEquals(existing.Arguments, arguments))
            throw new WorkflowRejectedException("Execution identifier is already bound to a different tool request.");
    }
}
