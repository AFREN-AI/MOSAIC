using System.Text.Json;
using Mosaic.Core;

var mathCapability = new Capability("math.read");
var registry = new ToolRegistry(
[
    new ToolDefinition("math.add", mathCapability, (arguments, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!arguments.TryGetProperty("left", out var left) || !left.TryGetInt64(out var leftValue) ||
            !arguments.TryGetProperty("right", out var right) || !right.TryGetInt64(out var rightValue))
        {
            throw new WorkflowRejectedException("math.add requires integer left and right arguments.");
        }
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { sum = checked(leftValue + rightValue) }));
    })
]);
var server = new MosaicProtocolServer(new WorkflowExecutor(registry), [mathCapability]);
using var input = new BufferedStream(Console.OpenStandardInput());
var output = Console.OpenStandardOutput();
while (ReadBoundedLine(input) is { } requestLine)
{
    var response = await server.HandleAsync(requestLine);
    await output.WriteAsync(MosaicProtocolServer.SerializeResponse(response));
    await output.WriteAsync("\n"u8.ToArray());
    await output.FlushAsync();
}

static byte[]? ReadBoundedLine(Stream input)
{
    using var line = new MemoryStream();
    var oversized = false;
    while (true)
    {
        var value = input.ReadByte();
        if (value < 0 || value == '\n')
        {
            if (value < 0 && line.Length == 0 && !oversized)
            {
                return null;
            }
            if (!oversized && line.Length > 0 && line.GetBuffer()[(int)line.Length - 1] == '\r')
            {
                line.SetLength(line.Length - 1);
            }
            if (oversized)
            {
                return new byte[MosaicProtocolServer.MaximumRequestBytes + 1];
            }
            return line.ToArray();
        }
        if (line.Length < MosaicProtocolServer.MaximumRequestBytes)
        {
            line.WriteByte((byte)value);
        }
        else
        {
            oversized = true;
        }
    }
}
