using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Mosaic.Core;
using ModelContextProtocol.Server;

namespace Mosaic.Mcp;

/// <summary>Exposes MOSAIC plans through the Model Context Protocol.</summary>
[McpServerToolType]
public sealed class MosaicMcpTools(MosaicProtocolServer protocolServer)
{
    /// <summary>Executes a bounded plan using capabilities configured by the trusted host.</summary>
    [McpServerTool(Name = "mosaic_execute"), Description("Execute a bounded MOSAIC workflow plan using only tools and capabilities configured by the host.")]
    public async ValueTask<string> ExecuteAsync(
        [Description("A MOSAIC workflow plan with steps and an output step identifier.")] JsonElement plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(protocolServer);
        var request = new MosaicProtocolRequest(
            MosaicProtocolServer.CurrentVersion,
            Guid.NewGuid().ToString("N"),
            "execute",
            plan.Clone());
        var response = await protocolServer.HandleAsync(
            MosaicProtocolServer.SerializeRequest(request),
            cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(MosaicProtocolServer.SerializeResponse(response));
    }
}
