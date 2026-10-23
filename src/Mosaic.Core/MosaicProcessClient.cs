using System.Diagnostics;
using System.Text;

namespace Mosaic.Core;

/// <summary>Invokes a protocol host in a fresh worker process with bounded I/O and runtime.</summary>
public sealed class MosaicProcessClient
{
    private const int ReadBufferSize = 4096;
    private static readonly string[] InheritedEnvironmentNames =
    ["PATH", "SystemRoot", "DOTNET_ROOT", "DOTNET_ROOT_X64", "TEMP", "TMP", "TMPDIR", "LD_LIBRARY_PATH"];
    private readonly MosaicWorkerOptions _options;
    private readonly SemaphoreSlim _workerSlots;

    /// <summary>Creates a bounded process client.</summary>
    public MosaicProcessClient(MosaicWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _workerSlots = new SemaphoreSlim(options.MaximumConcurrentWorkers, options.MaximumConcurrentWorkers);
    }

    /// <summary>Runs one versioned protocol request in a new worker process.</summary>
    public async ValueTask<byte[]> ExecuteAsync(
        MosaicProtocolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestJson = MosaicProtocolServer.SerializeRequest(request);
        if (requestJson.Length > _options.MaximumRequestBytes)
        {
            throw new WorkflowRejectedException("Worker request exceeds the configured byte limit.");
        }

        await _workerSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteInWorkerAsync(requestJson, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _workerSlots.Release();
        }
    }

    private async ValueTask<byte[]> ExecuteInWorkerAsync(byte[] requestJson, CancellationToken cancellationToken)
    {
        using var worker = StartWorker();
        using var timeoutSource = new CancellationTokenSource(_options.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var errorDrain = DrainStandardErrorAsync(worker, linkedSource.Token);
        try
        {
            await worker.StandardInput.WriteLineAsync(Encoding.UTF8.GetString(requestJson).AsMemory(), linkedSource.Token).ConfigureAwait(false);
            worker.StandardInput.Close();
            var responseText = await ReadBoundedOutputAsync(worker, linkedSource.Token).ConfigureAwait(false);
            await worker.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
            await errorDrain.ConfigureAwait(false);
            if (worker.ExitCode != 0)
            {
                throw new InvalidOperationException("MOSAIC worker exited unsuccessfully.");
            }
            return Encoding.UTF8.GetBytes(responseText);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Terminate(worker);
            await IgnoreDrainFailureAsync(errorDrain).ConfigureAwait(false);
            throw new TimeoutException("MOSAIC worker exceeded its configured execution time.");
        }
        catch
        {
            Terminate(worker);
            await IgnoreDrainFailureAsync(errorDrain).ConfigureAwait(false);
            throw;
        }
    }

    private Process StartWorker()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.Executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in _options.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment.Clear();
        CopyRuntimeEnvironment(startInfo);
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = _options.MaximumManagedHeapBytes.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
        startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        var worker = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!worker.Start())
        {
            worker.Dispose();
            throw new InvalidOperationException("MOSAIC worker process could not be started.");
        }
        return worker;
    }

    private static async Task DrainStandardErrorAsync(Process worker, CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBufferSize];
        while (await worker.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
        {
            // Diagnostics are discarded to prevent secret leakage and pipe backpressure.
        }
    }

    private static async Task IgnoreDrainFailureAsync(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The linked timeout or caller cancellation stopped the diagnostic drain.
        }
        catch (ObjectDisposedException)
        {
            // The worker's redirected stream closed while it was being terminated.
        }
        catch (IOException)
        {
            // Diagnostics are intentionally discarded; a broken pipe must not mask the worker result.
        }
    }

    private static void CopyRuntimeEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in InheritedEnvironmentNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                startInfo.Environment[name] = value;
            }
        }
        startInfo.Environment["HOME"] = Path.GetTempPath();
    }

    private async Task<string> ReadBoundedOutputAsync(Process worker, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[ReadBufferSize];
        var encodedBytes = 0;
        while (true)
        {
            var read = await worker.StandardOutput.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            encodedBytes += Encoding.UTF8.GetByteCount(buffer, 0, read);
            if (encodedBytes > _options.MaximumResponseBytes)
            {
                throw new WorkflowRejectedException("Worker response exceeds the configured byte limit.");
            }
            output.Append(buffer, 0, read);
        }
        var text = output.ToString();
        var line = text.EndsWith('\n') ? text[..^1] : text;
        if (line.Contains('\n') || line.Contains('\r') || string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("MOSAIC worker must return exactly one protocol response.");
        }
        return line;
    }

    private static void Terminate(Process worker)
    {
        try
        {
            if (!worker.HasExited)
            {
                worker.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The worker exited between the state check and termination.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process tree exited between the state check and termination.
        }
    }
}
