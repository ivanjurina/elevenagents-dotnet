// Offline tests for the Semantic Kernel bridge.
// A fake transport replays a client_tool_call frame; we assert the matching kernel
// function ran and its result was sent back to the agent. No network, no LLM.
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ElevenAgents.Net.Realtime;
using ElevenAgents.Net.SemanticKernel;
using Microsoft.SemanticKernel;

var failures = 0;
var passed = 0;
void Ok(string name) { passed++; Console.WriteLine($"  ok  {name}"); }
void Fail(string name, string detail) { failures++; Console.WriteLine($"FAIL  {name}: {detail}"); }
void Assert(bool cond, string name, string detail = "") { if (cond) Ok(name); else Fail(name, detail); }

var kernel = Kernel.CreateBuilder().Build();
kernel.Plugins.AddFromType<OrdersPlugin>("orders");

// GetToolNames lists every kernel function
{
    var names = KernelToolBridge.GetToolNames(kernel);
    Assert(names.Contains("get_order_status") && names.Contains("echo"), "tool names enumerated",
        string.Join(",", names));
}

// A tool call routes to the kernel function and the result goes back to the agent
{
    var call = """
        {"type":"client_tool_call","client_tool_call":{"tool_name":"get_order_status","tool_call_id":"tc_1","expects_response":true,"parameters":{"orderId":"1234"}}}
        """;
    var transport = new FakeTransport(call);
    var conversation = await AgentConversation.ConnectAsync(new ConversationOptions { AgentId = "a" }, transport);
    var registered = KernelToolBridge.Register(conversation, kernel);
    Assert(registered.Contains("get_order_status"), "bridge registered the tool");

    await foreach (var evt in conversation.ReceiveEventsAsync()) evt.Dispose();

    var result = transport.Sent.Select(s => JsonDocument.Parse(s))
        .FirstOrDefault(d => d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "client_tool_result");
    Assert(result is not null, "tool result sent back");
    Assert(result?.RootElement.GetProperty("tool_call_id").GetString() == "tc_1", "result carries call id");
    Assert(result?.RootElement.GetProperty("result").GetString()?.Contains("1234") == true,
        "kernel function ran with the agent's parameters");
    await conversation.DisposeAsync();
}

// Numeric + string parameters are coerced from JSON into kernel arguments
{
    var call = """
        {"type":"client_tool_call","client_tool_call":{"tool_name":"echo","tool_call_id":"tc_2","expects_response":true,"parameters":{"text":"hi","times":3}}}
        """;
    var transport = new FakeTransport(call);
    var conversation = await AgentConversation.ConnectAsync(new ConversationOptions { AgentId = "a" }, transport);
    KernelToolBridge.Register(conversation, kernel);
    await foreach (var evt in conversation.ReceiveEventsAsync()) evt.Dispose();

    var result = transport.Sent.Select(s => JsonDocument.Parse(s))
        .First(d => d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "client_tool_result");
    Assert(result.RootElement.GetProperty("result").GetString() == "hihihi", "typed parameters coerced from json");
    await conversation.DisposeAsync();
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

sealed class OrdersPlugin
{
    [KernelFunction("get_order_status")]
    [Description("Gets order status by id.")]
    public string GetOrderStatus(string orderId) => $"Order {orderId} shipped.";

    [KernelFunction("echo")]
    [Description("Repeats text a number of times.")]
    public string Echo(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}

sealed class FakeTransport(params string[] serverFrames) : IWebSocketTransport
{
    public List<string> Sent { get; } = [];
    public Task ConnectAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
    public Task SendAsync(string json, CancellationToken ct) { Sent.Add(json); return Task.CompletedTask; }
    public async IAsyncEnumerable<string> ReceiveMessagesAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var f in serverFrames) { await Task.Yield(); yield return f; }
    }
    public Task CloseAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
