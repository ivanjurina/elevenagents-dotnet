# ElevenAgents.Net: realtime ElevenLabs agents from .NET

unofficial .net client for the [ElevenLabs Agents platform](https://elevenlabs.io/docs/eleven-agents/overview). official sdks exist for python, typescript, kotlin and swift. nothing for .net. this fills that gap.

not affiliated with ElevenLabs. complements the excellent community [ElevenLabs-DotNet](https://github.com/RageAgainstThePixel/ElevenLabs-DotNet) library, which covers tts, voices, dubbing and more; this library covers what it doesn't: realtime agent conversations.

## what you get

- realtime conversations over websocket: text chat, audio streaming, transcripts, interruptions
- client tools: register a c# function, the agent calls it mid-conversation
- signed-url auth flow for private agents
- automatic ping/pong handling
- typed events for the common protocol messages, raw `JsonElement` access for everything else
- net8.0, zero dependencies, async-first with `CancellationToken` everywhere, testable transport abstraction

## quickstart

```
dotnet add package ElevenAgents.Net
```

talk to a public agent:

```csharp
using ElevenAgents.Net.Realtime;

await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { AgentId = "your_agent_id" });

// give the agent a tool it can call
conversation.RegisterTool("get_local_time", (parameters, ct) =>
    Task.FromResult(DateTimeOffset.Now.ToString("HH:mm zzz")));

_ = Task.Run(async () =>
{
    await foreach (var evt in conversation.ReceiveEventsAsync())
        using (evt)
            if (evt is AgentResponseEvent r) Console.WriteLine($"agent: {r.Text}");
});

await conversation.SendUserMessageAsync("hi, what time is it?");
```

private agent? get a signed url server-side first:

```csharp
using var client = new ElevenAgentsClient(apiKey);          // xi-api-key stays on the server
var signedUrl = await client.GetSignedUrlAsync("agent_id"); // valid 15 minutes
await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { SignedUrl = signedUrl });
```

voice: send microphone chunks with `SendAudioChunkAsync` (format from `conversation.AgentAudioFormat`, typically pcm_16000) and play `AudioEvent.GetAudioBytes()` as they arrive.

## project layout

| path | what |
|---|---|
| `src/ElevenAgents.Net` | the library (realtime client + rest client) |
| `tests/ElevenAgents.Net.SmokeTests` | offline protocol tests against a fake transport, no network needed |
| `samples/ChatAgent` | console chat with an agent, including a client tool |

## run the tests

```
dotnet run --project tests/ElevenAgents.Net.SmokeTests
```

## give your agent access to your .NET code (Semantic Kernel bridge)

The companion package [`ElevenAgents.Net.SemanticKernel`](src/ElevenAgents.Net.SemanticKernel)
turns every function in a Semantic Kernel `Kernel` into an ElevenLabs client tool. The agent
handles speech, the LLM, and voice; your existing C# runs when the agent needs it:

```csharp
using ElevenAgents.Net.SemanticKernel;

var kernel = Kernel.CreateBuilder().Build();
kernel.Plugins.AddFromType<OrdersPlugin>("orders");

await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { AgentId = "your_agent_id" });

KernelToolBridge.Register(conversation, kernel); // one line, every function is now a tool
```

See `samples/VoiceAgentWithTools` for a full runnable example.

## roadmap

- webrtc transport for browser-parity voice latency
- typed agent configuration models (create/update agents from c#)
- Microsoft Agent Framework bridge (same idea as the Semantic Kernel one)
- multi-context websocket support

## license

MIT
