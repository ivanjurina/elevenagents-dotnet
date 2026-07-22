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

Not affiliated with ElevenLabs. MIT licensed.
