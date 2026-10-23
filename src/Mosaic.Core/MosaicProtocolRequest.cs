using System.Text.Json;

namespace Mosaic.Core;

/// <summary>A versioned request sent to a MOSAIC host over a trusted transport.</summary>
public sealed record MosaicProtocolRequest(
    string ProtocolVersion,
    string RequestId,
    string Method,
    JsonElement Plan);
