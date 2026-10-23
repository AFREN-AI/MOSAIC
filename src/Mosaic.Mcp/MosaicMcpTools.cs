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
    private static readonly JsonSerializerOptions WireOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Searches registered tool names and descriptions for progressive discovery.</summary>
    [McpServerTool(Name = "mosaic_search"), Description("Search host-registered MOSAIC tools. Returns metadata only; execution still requires a separate authorized plan.")]
    public string SearchTools(
        [Description("Terms to match against tool names and descriptions.")] string query,
        [Description("Maximum number of matches, from 1 to 100.")] int maximumResults = 20)
    {
        ArgumentNullException.ThrowIfNull(protocolServer);
        return JsonSerializer.Serialize(protocolServer.SearchTools(query, maximumResults), WireOptions);
    }

    /// <summary>Returns the name, description, and input schema of a registered tool.</summary>
    [McpServerTool(Name = "mosaic_describe"), Description("Describe one host-registered MOSAIC tool, including its JSON input schema when provided.")]
    public string DescribeTool(
        [Description("Exact registered tool name returned by mosaic_search.")] string name)
    {
        ArgumentNullException.ThrowIfNull(protocolServer);
        var descriptor = protocolServer.DescribeTool(name);
        return JsonSerializer.Serialize(descriptor, WireOptions);
    }

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
