// A voice agent that can call your .NET code.
//
// The ElevenLabs agent handles speech-to-text, the LLM, and text-to-speech.
// Your Semantic Kernel plugin functions are exposed as client tools, so when the
// caller asks "what's the status of order 1234?", the agent calls your C# method.
//
// Setup:
//   1. Create an agent at https://elevenlabs.io/app/agents
//   2. Add client tools named "get_order_status" and "get_local_time" to the agent
//   3. Run: ELEVENLABS_AGENT_ID=agent_xxx dotnet run --project samples/VoiceAgentWithTools
using System.ComponentModel;
using ElevenAgents.Net.Realtime;
using ElevenAgents.Net.SemanticKernel;
using Microsoft.SemanticKernel;

var agentId = Environment.GetEnvironmentVariable("ELEVENLABS_AGENT_ID")
    ?? throw new InvalidOperationException("Set the ELEVENLABS_AGENT_ID environment variable.");

// Build a kernel with your existing business logic as plugins.
var kernel = Kernel.CreateBuilder().Build();
kernel.Plugins.AddFromType<OrdersPlugin>("orders");

Console.WriteLine("Tools the agent can call: " + string.Join(", ", KernelToolBridge.GetToolNames(kernel)));

await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { AgentId = agentId });

// One line: every kernel function becomes a client tool the agent can call.
KernelToolBridge.Register(conversation, kernel);

Console.WriteLine("Connected. Type to chat, empty line to quit.\n");

var receiving = Task.Run(async () =>
{
    await foreach (var evt in conversation.ReceiveEventsAsync())
        using (evt)
            switch (evt)
            {
                case AgentResponseEvent r: Console.WriteLine($"agent: {r.Text}"); break;
                case ClientToolCallEvent c: Console.WriteLine($"[agent called {c.ToolName}]"); break;
                case ErrorEvent e: Console.WriteLine($"[error: {e.Message}]"); break;
            }
});

while (Console.ReadLine() is { Length: > 0 } line)
    await conversation.SendUserMessageAsync(line);

await conversation.DisposeAsync();
await receiving;

/// <summary>Example business logic exposed to the voice agent.</summary>
sealed class OrdersPlugin
{
    [KernelFunction("get_order_status")]
    [Description("Gets the current status of a customer order by its id.")]
    public string GetOrderStatus([Description("The order id, e.g. 1234.")] string orderId) =>
        $"Order {orderId} shipped yesterday and arrives tomorrow.";

    [KernelFunction("get_local_time")]
    [Description("Gets the current local time.")]
    public string GetLocalTime() => DateTimeOffset.Now.ToString("HH:mm zzz");
}
