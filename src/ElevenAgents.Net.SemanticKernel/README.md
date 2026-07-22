# ElevenAgents.Net.SemanticKernel

Bridges [Microsoft Semantic Kernel](https://github.com/microsoft/semantic-kernel) to the
[ElevenLabs Agents platform](https://elevenlabs.io/docs/eleven-agents/overview). Every
`KernelFunction` in your kernel becomes an ElevenLabs client tool, so a voice agent can
call your existing .NET code by name.

```csharp
using ElevenAgents.Net.Realtime;
using ElevenAgents.Net.SemanticKernel;

// your kernel, with whatever plugins you already have
var kernel = Kernel.CreateBuilder().Build();
kernel.Plugins.AddFromType<OrdersPlugin>();

await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { AgentId = "your_agent_id" });

// one line: every kernel function is now a tool the agent can call
KernelToolBridge.Register(conversation, kernel);
```

Configure client tools on the ElevenLabs agent with names matching your kernel functions
(`KernelToolBridge.GetToolNames(kernel)` lists them). When the agent decides to call one,
the matching kernel function runs with the tool's parameters and its result is spoken back.

## configuring the agent

Two halves have to line up: the client registers a handler (this library), and the **agent**
must declare a matching client tool. In the ElevenLabs dashboard, open your agent → Tools →
add a **Client Tool** whose name matches the kernel function (e.g. `get_order_status`), with
matching parameters. Then **publish the agent** — the WebSocket serves the published version,
so an unpublished tool is invisible to a live conversation (the agent will act like the tool
doesn't exist). This tripped me up; publishing fixed it.

Not affiliated with ElevenLabs. MIT licensed.
