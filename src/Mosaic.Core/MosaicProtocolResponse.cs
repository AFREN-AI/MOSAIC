using System.Text.Json;

namespace Mosaic.Core;

/// <summary>A protocol response with either workflow output or a stable error code.</summary>
public sealed record MosaicProtocolResponse(
    string ProtocolVersion,
    string? RequestId,
    bool Success,
    JsonElement? Output,
    int? CompletedCalls,
    string? ErrorCode,
    string? ErrorMessage);
