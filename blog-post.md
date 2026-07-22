# giving a .net app a voice: building on the elevenlabs agents platform

*draft for ivanjurina.com, ivan jurina, july 2026*

[ElevenLabs](https://elevenlabs.io) is best known for text to speech, but the thing i find most interesting is [ElevenAgents](https://elevenlabs.io/docs/eleven-agents/overview): you configure an agent with a prompt, a voice and some tools, and it handles the whole voice loop. speech to text, the llm, turn-taking, interruptions, text to speech. you just open a websocket and talk.

they ship sdks for python, typescript, kotlin and swift. nothing for .net. so if you want to build a voice agent from c#, you're hand-rolling the websocket protocol. i didn't want to do that every time, so i built [ElevenAgents.Net](https://github.com/ivanjurina/elevenagents-dotnet). here's what the protocol actually looks like and the two design decisions that mattered.

## the protocol is a typed event stream

after the handshake, the agent sends a `conversation_initiation_metadata` event with a conversation id and the negotiated audio formats. then it's a stream of json events, each with a `type`: `user_transcript`, `agent_response`, `audio`, `interruption`, `vad_score`, `ping`, `client_tool_call`. you send events back: `user_message`, `user_audio_chunk`, `client_tool_result`, `pong`.

the naive way to model this is a big enum and a switch. the problem is the event list is long and still growing, and you don't want your library to break the day elevenlabs adds an event type. so: typed classes for the events people actually handle, and a raw `JsonElement` on the base class for everything else.

```csharp
await foreach (var evt in conversation.ReceiveEventsAsync())
    using (evt)
        switch (evt)
        {
            case AgentResponseEvent r: Console.WriteLine($"agent: {r.Text}"); break;
            case UserTranscriptEvent t: Console.WriteLine($"you: {t.Transcript}"); break;
            case UnknownEvent u: Log(u.Raw); break; // future event types still usable
        }
```

`UnknownEvent.Raw` means a new server event is never a breaking change. you can read it today and i can add a typed wrapper later without anyone's code changing.

## two protocol chores the library should just do

two things in the protocol are pure mechanics that no caller should have to think about.

first, ping/pong. the server sends `ping` events with an id and expects a matching `pong` for latency measurement. forget it and the connection looks dead. so the library answers pings itself, before the event is even handed to you.

second, client tools. this is the good part. an agent can be configured with "client tools", and mid-conversation it emits a `client_tool_call` event: run this function with these parameters and give me the result. that's how a voice agent does something real instead of just talking. the library lets you register a handler and wires up the response frame:

```csharp
conversation.RegisterTool("get_order_status", async (parameters, ct) =>
{
    var id = parameters.GetProperty("orderId").GetString();
    var order = await orders.GetAsync(id, ct);
    return $"Order {id} is {order.Status}.";
});
```

when the agent calls the tool, your code runs and the result is spoken back. if your handler throws, the library reports it as a tool error instead of dropping the turn. this is stock async-with-cancellation c#, which is the whole point: it should feel like the rest of your codebase, not like a protocol you're fighting.

## the payoff: your existing .net code, now with a voice

here's the part i actually built this for. if you already use [Semantic Kernel](https://github.com/microsoft/semantic-kernel), you have plugins: c# methods decorated as kernel functions. those are exactly what a voice agent's tools want to be. so there's a companion package that maps every kernel function to an elevenlabs client tool in one line:

```csharp
var kernel = Kernel.CreateBuilder().Build();
kernel.Plugins.AddFromType<OrdersPlugin>("orders");

await using var conversation = await AgentConversation.ConnectAsync(
    new ConversationOptions { AgentId = agentId });

KernelToolBridge.Register(conversation, kernel); // every function is now callable by voice
```

the agent handles speech, the model and the voice. your business logic runs when the model decides it needs it. the same `OrdersPlugin` you'd expose to a text chat agent now works over a phone call, and you wrote it once.

## what's real and what's next

the realtime client, the event model, ping/pong, client tools and the semantic kernel bridge are all live-verified against a real agent: i asked a voice agent "what's the status of order 1234?" and watched it call my c# method and speak the result back. audio streaming and webrtc are modeled from the docs and next on my list to exercise end to end. the library is net8.0, zero dependencies in the core, async-first with cancellation everywhere, and has an offline test suite that replays captured protocol frames so ci doesn't need network or credits.

one gotcha worth documenting, because it cost me a debugging session: the websocket serves the *published* version of your agent, not your draft. add a client tool, and until you hit publish, the live conversation still runs the old config with no tools. the agent will even narrate "let me check that" and then do nothing, because it was never told the tool exists. publish, and it works.

it's on nuget as `ElevenAgents.Net` and `ElevenAgents.Net.SemanticKernel`, and the source is on my github. .net is a big audience for voice agents, enterprise contact-center teams especially, and right now that audience has no official path onto this platform. if you're at elevenlabs and reading this: happy to help close that gap properly.
