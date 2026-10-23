using Microsoft.Extensions.DependencyInjection;
using Mosaic.Core;
using ModelContextProtocol.Server;

namespace Mosaic.Mcp;

/// <summary>Registers the MOSAIC MCP tool adapter with a host service collection.</summary>
public static class MosaicMcpServiceCollectionExtensions
{
    /// <summary>Adds the MCP SDK server services and the MOSAIC execution tool.</summary>
    public static IMcpServerBuilder AddMosaicMcpServer(
        this IServiceCollection services,
        MosaicProtocolServer protocolServer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(protocolServer);
        services.AddSingleton(protocolServer);
        return services.AddMcpServer().WithTools<MosaicMcpTools>();
    }
}
