// A text conversation with an ElevenLabs agent from a .NET console, including a
// client tool the agent can call. Voice works the same way: send audio chunks with
// SendAudioChunkAsync and play AudioEvent chunks as they arrive.
//
// Requires: ELEVENLABS_AGENT_ID (public agent), or additionally ELEVENLABS_API_KEY
// for a private agent (the sample then fetches a signed URL first).
using ElevenAgents.Net;
using ElevenAgents.Net.Realtime;

var agentId = Environment.GetEnvironmentVariable("ELEVENLABS_AGENT_ID")
    ?? throw new InvalidOperationException("Set the ELEVENLABS_AGENT_ID environment variable.");
var apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");

var options = new ConversationOptions();
if (apiKey is not null)
{
    // private agent: exchange the api key for a signed url server-side
    using var client = new ElevenAgentsClient(apiKey);
    options.SignedUrl = await client.GetSignedUrlAsync(agentId);
}
else
{
    options.AgentId = agentId;
}

await using var conversation = await AgentConversation.ConnectAsync(options);

// a client tool the agent can call (configure a client tool named "get_local_time" on your agent)
conversation.RegisterTool("get_local_time", (_, _) =>
    Task.FromResult(DateTimeOffset.Now.ToString("HH:mm zzz")));

Console.WriteLine("Connected. Type to chat, empty line to quit.\n");

// receive loop
var receiving = Task.Run(async () =>
{
    await foreach (var evt in conversation.ReceiveEventsAsync())
    {
        using (evt)
        {
            switch (evt)
            {
                case ConversationStartedEvent started:
                    Console.WriteLine($"[conversation {started.ConversationId}]");
                    break;
                case AgentResponseEvent response:
                    Console.WriteLine($"agent: {response.Text}");
                    break;
                case ClientToolCallEvent call:
                    Console.WriteLine($"[agent called tool: {call.ToolName}]");
                    break;
                case ErrorEvent error:
                    Console.WriteLine($"[error: {error.Message}]");
                    break;
            }
        }
    }
});

while (Console.ReadLine() is { Length: > 0 } line)
    await conversation.SendUserMessageAsync(line);

await conversation.DisposeAsync();
await receiving;
