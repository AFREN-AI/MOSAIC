using System.Text;
using System.Text.Json;
using Mosaic.Core;

var mathCapability = new Capability("math.read");
var tools = new ToolRegistry(
[
    new ToolDefinition("add", mathCapability, (arguments, _) =>
    {
        var sum = arguments.GetProperty("left").GetInt32() + arguments.GetProperty("right").GetInt32();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { sum }));
    }),
    new ToolDefinition("format", mathCapability, (arguments, _) =>
    {
        var sum = arguments.GetProperty("value").GetProperty("sum").GetInt32();
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new { message = $"The result is {sum}." }));
    })
]);
var plan = """
    {"steps":[{"id":"total","tool":"add","arguments":{"left":20,"right":22}},{"id":"summary","tool":"format","arguments":{"value":{"$ref":"total"}}}],"output":"summary"}
    """;
var result = await new WorkflowExecutor(tools).ExecuteAsync(Encoding.UTF8.GetBytes(plan), new HashSet<Capability> { mathCapability });
Console.WriteLine(JsonSerializer.Serialize(result.Output, new JsonSerializerOptions { WriteIndented = true }));
