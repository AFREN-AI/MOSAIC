using System.Text;
using System.Text.Json;
using Mosaic.Core;
using Xunit;

namespace Mosaic.Core.Tests;

public sealed class WorkflowExecutorTests
{
    private static readonly Capability ReadCapability = new("data.read");
    private static readonly ToolRegistry Registry = new(
    [
        new ToolDefinition("echo", ReadCapability, (arguments, _) => ValueTask.FromResult(arguments.Clone())),
        new ToolDefinition("wrap", ReadCapability, (arguments, _) => ValueTask.FromResult(JsonSerializer.SerializeToElement(new { nested = arguments.GetProperty("value") })))
    ]);

    [Fact]
    public async Task ExecuteAsync_AllowsRegisteredAndAuthorizedTools()
    {
        const string plan = "{\"steps\":[{\"id\":\"first\",\"tool\":\"echo\",\"arguments\":{\"value\":7}}],\"output\":\"first\"}";
        var result = await Executor().ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability> { ReadCapability });
        Assert.Equal(7, result.Output.GetProperty("value").GetInt32());
        Assert.Equal(1, result.CompletedCalls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingCapabilityBeforeCallingTool()
    {
        const string plan = "{\"steps\":[{\"id\":\"first\",\"tool\":\"echo\",\"arguments\":{}}],\"output\":\"first\"}";
        await Assert.ThrowsAsync<WorkflowRejectedException>(async () => await Executor().ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability>()));
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesOnlyEarlierStepReferences()
    {
        const string plan = "{\"steps\":[{\"id\":\"first\",\"tool\":\"echo\",\"arguments\":{\"answer\":42}},{\"id\":\"second\",\"tool\":\"wrap\",\"arguments\":{\"value\":{\"$ref\":\"first\"}}}],\"output\":\"second\"}";
        var result = await Executor().ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability> { ReadCapability });
        Assert.Equal(42, result.Output.GetProperty("nested").GetProperty("answer").GetInt32());
    }

    [Fact]
    public async Task ExecuteAsync_RejectsForwardReferences()
    {
        const string plan = "{\"steps\":[{\"id\":\"first\",\"tool\":\"wrap\",\"arguments\":{\"value\":{\"$ref\":\"later\"}}},{\"id\":\"later\",\"tool\":\"echo\",\"arguments\":{}}],\"output\":\"first\"}";
        await Assert.ThrowsAsync<WorkflowRejectedException>(async () => await Executor().ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability> { ReadCapability }));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnknownPlanProperties()
    {
        const string plan = "{\"steps\":[],\"output\":\"x\",\"permissions\":[\"admin\"]}";
        await Assert.ThrowsAsync<WorkflowRejectedException>(async () => await Executor().ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability> { ReadCapability }));
    }

    private static WorkflowExecutor Executor() => new(Registry);
}

public sealed class MosaicProtocolServerTests
{
    private static readonly Capability Capability = new("math.read");
    private static readonly WorkflowExecutor Executor = new(new ToolRegistry(
    [
        new ToolDefinition("echo", Capability, (arguments, _) => ValueTask.FromResult(arguments.Clone()))
    ]));

    [Fact]
    public async Task HandleAsync_UsesHostOwnedCapabilitiesAndReturnsCanonicalResponse()
    {
        const string request = "{\"protocolVersion\":\"1.0\",\"requestId\":\"r-1\",\"method\":\"execute\",\"plan\":{\"steps\":[{\"id\":\"one\",\"tool\":\"echo\",\"arguments\":{\"ok\":true}}],\"output\":\"one\"}}";
        var server = new MosaicProtocolServer(Executor, [Capability]);
        var response = await server.HandleAsync(Encoding.UTF8.GetBytes(request));
        using var json = JsonDocument.Parse(MosaicProtocolServer.SerializeResponse(response));
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("r-1", json.RootElement.GetProperty("requestId").GetString());
        Assert.True(json.RootElement.GetProperty("output").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_RejectsClientSuppliedPermissions()
    {
        const string request = "{\"protocolVersion\":\"1.0\",\"requestId\":\"r-2\",\"method\":\"execute\",\"capabilities\":[\"admin\"],\"plan\":{}}";
        var response = await new MosaicProtocolServer(Executor, []).HandleAsync(Encoding.UTF8.GetBytes(request));
        Assert.False(response.Success);
        Assert.Equal("invalid_request", response.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_RejectsUnsupportedProtocolVersion()
    {
        const string request = "{\"protocolVersion\":\"9.0\",\"requestId\":\"r-3\",\"method\":\"execute\",\"plan\":{}}";
        var response = await new MosaicProtocolServer(Executor, []).HandleAsync(Encoding.UTF8.GetBytes(request));
        Assert.Equal("unsupported_version", response.ErrorCode);
    }
}
