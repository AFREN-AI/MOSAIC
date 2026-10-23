using System.Text.RegularExpressions;

namespace Mosaic.Core;

/// <summary>Resource and image settings for the Linux container code executor.</summary>
public sealed record MosaicCodeSandboxOptions
{
    public string ImageDigest { get; }
    public string ContainerRuntime { get; }
    public TimeSpan Timeout { get; }
    public int MaximumCodeBytes { get; }
    public int MaximumMessageBytes { get; }
    public int MaximumToolCalls { get; }

    public MosaicCodeSandboxOptions(string imageDigest, string containerRuntime = "docker", TimeSpan? timeout = null,
        int maximumCodeBytes = 65_536, int maximumMessageBytes = 1_048_576, int maximumToolCalls = 32)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDigest);
        if (!Regex.IsMatch(imageDigest, "^[A-Za-z0-9][A-Za-z0-9./:_-]*@sha256:[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Sandbox images must be pinned by a sha256 digest.", nameof(imageDigest));
        ArgumentException.ThrowIfNullOrWhiteSpace(containerRuntime);
        var executionTimeout = timeout ?? TimeSpan.FromSeconds(20);
        if (executionTimeout <= TimeSpan.Zero || executionTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Code execution timeout must be between zero and two minutes.");
        if (maximumCodeBytes is < 1 or > 262_144 || maximumMessageBytes is < 1 or > 4_194_304 || maximumToolCalls is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maximumCodeBytes), "Sandbox limits exceed supported bounds.");
        ImageDigest = imageDigest;
        ContainerRuntime = containerRuntime;
        Timeout = executionTimeout;
        MaximumCodeBytes = maximumCodeBytes;
        MaximumMessageBytes = maximumMessageBytes;
        MaximumToolCalls = maximumToolCalls;
    }
}
