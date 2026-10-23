namespace Mosaic.Core;

/// <summary>Bounds and launches one isolated host process per workflow request.</summary>
public sealed record MosaicWorkerOptions
{
    public string Executable { get; }
    public IReadOnlyList<string> Arguments { get; }
    public TimeSpan Timeout { get; }
    public int MaximumRequestBytes { get; }
    public int MaximumResponseBytes { get; }
    public long MaximumManagedHeapBytes { get; }
    public int MaximumConcurrentWorkers { get; }

    public MosaicWorkerOptions(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        int maximumRequestBytes = 262_144,
        int maximumResponseBytes = 1_048_576,
        long maximumManagedHeapBytes = 268_435_456,
        int maximumConcurrentWorkers = 4)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        var executionTimeout = timeout ?? TimeSpan.FromSeconds(15);
        if (executionTimeout <= TimeSpan.Zero || executionTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Worker timeout must be between zero and five minutes.");
        }
        if (maximumRequestBytes is < 1 or > MosaicProtocolServer.MaximumRequestBytes || maximumResponseBytes is < 1 or > 16_777_216)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes), "Worker message limits are outside the supported range.");
        }
        if (maximumManagedHeapBytes is < 16_777_216 or > 2_147_483_648)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumManagedHeapBytes), "Managed heap limit must be between 16 MiB and 2 GiB.");
        }
        if (maximumConcurrentWorkers is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentWorkers), "Concurrent worker limit must be between one and sixty-four.");
        }
        Executable = executable;
        Arguments = Array.AsReadOnly(arguments.ToArray());
        Timeout = executionTimeout;
        MaximumRequestBytes = maximumRequestBytes;
        MaximumResponseBytes = maximumResponseBytes;
        MaximumManagedHeapBytes = maximumManagedHeapBytes;
        MaximumConcurrentWorkers = maximumConcurrentWorkers;
    }
}
