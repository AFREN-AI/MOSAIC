using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mosaic.Core;

/// <summary>Persists approval and result state for durable tool invocations.</summary>
public interface IMosaicExecutionStore
{
    ValueTask<MosaicExecutionRecord?> LoadAsync(string executionId, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(MosaicExecutionRecord record, CancellationToken cancellationToken = default);
}

/// <summary>Durable state for a single host-authorized tool invocation.</summary>
public sealed record MosaicExecutionRecord(
    string ExecutionId,
    string ToolName,
    JsonElement Arguments,
    MosaicExecutionState State,
    JsonElement? Output = null,
    string? RejectionReason = null);

/// <summary>Approval lifecycle for a durable invocation.</summary>
public enum MosaicExecutionState
{
    PendingApproval,
    Approved,
    Rejected,
    Completed
}

/// <summary>Stores execution snapshots as atomically replaced JSON files.</summary>
public sealed class FileMosaicExecutionStore : IMosaicExecutionStore
{
    private readonly string _directory;
    private readonly JsonSerializerOptions _options = CreateJsonOptions();

    public FileMosaicExecutionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public async ValueTask<MosaicExecutionRecord?> LoadAsync(string executionId, CancellationToken cancellationToken = default)
    {
        var path = GetPath(executionId);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MosaicExecutionRecord>(stream, _options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveAsync(MosaicExecutionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var target = GetPath(record.ExecutionId);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, _options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetPath(string executionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executionId)));
        return Path.Combine(_directory, $"{digest}.json");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
