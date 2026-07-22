// Framework-free smoke tests for ElevenAgents.Net.
// Runs fully offline: a fake transport replays canned server frames captured from
// the documented protocol and records everything the client sends.
using System.Text.Json;
using ElevenAgents.Net.Realtime;

var failures = 0;
var passed = 0;

void Ok(string name) { passed++; Console.WriteLine($"  ok  {name}"); }
void Fail(string name, string detail) { failures++; Console.WriteLine($"FAIL  {name}: {detail}"); }
void Assert(bool condition, string name, string detail = "")
{
    if (condition) Ok(name); else Fail(name, detail);
}

// ---------- helpers ----------

static FakeTransport Transport(params string[] serverFrames) => new(serverFrames);

static async Task<(FakeTransport transport, List<ConversationEvent> events)> RunConversation(
    ConversationOptions options, Action<AgentConversation>? configure = null, params string[] serverFrames)
{
    var transport = Transport(serverFrames);
    var conversation = await AgentConversation.ConnectAsync(options, transport);
    configure?.Invoke(conversation);
    var events = new List<ConversationEvent>();
    await foreach (var evt in conversation.ReceiveEventsAsync())
        events.Add(evt);
    await conversation.DisposeAsync();
    return (transport, events);
}

// ---------- initiation ----------
{
    var options = new ConversationOptions
    {
        AgentId = "agent_123",
        SystemPromptOverride = "you are a test agent",
        FirstMessageOverride = "hi",
        LanguageOverride = "en",
        VoiceIdOverride = "voice_1",
    };
    options.DynamicVariables["user_name"] = "Ivan";

    var (transport, _) = await RunConversation(options);

    Assert(transport.ConnectedUri!.ToString().Contains("agent_id=agent_123"), "connect url contains agent id");

    using var init = JsonDocument.Parse(transport.Sent[0]);
    var root = init.RootElement;
    Assert(root.GetProperty("type").GetString() == "conversation_initiation_client_data", "initiation frame sent first");
    Assert(root.GetProperty("conversation_config_override").GetProperty("agent").GetProperty("prompt").GetProperty("prompt").GetString() == "you are a test agent",
        "prompt override in initiation");
    Assert(root.GetProperty("conversation_config_override").GetProperty("tts").GetProperty("voice_id").GetString() == "voice_1",
        "voice override in initiation");
    Assert(root.GetProperty("dynamic_variables").GetProperty("user_name").GetString() == "Ivan",
        "dynamic variables in initiation");
}

// ---------- signed url takes precedence ----------
{
    var transport = Transport();
    var conversation = await AgentConversation.ConnectAsync(
        new ConversationOptions { SignedUrl = "wss://api.elevenlabs.io/v1/convai/conversation?token=abc" }, transport);
    Assert(transport.ConnectedUri!.Query.Contains("token=abc"), "signed url used verbatim");
    await conversation.DisposeAsync();
}

// ---------- metadata event ----------
{
    var meta = """
        {"type":"conversation_initiation_metadata","conversation_initiation_metadata_event":{"conversation_id":"conv_1","agent_output_audio_format":"pcm_16000","user_input_audio_format":"pcm_16000"}}
        """;
    var transport = Transport(meta);
    var conversation = await AgentConversation.ConnectAsync(new ConversationOptions { AgentId = "a" }, transport);
    await foreach (var evt in conversation.ReceiveEventsAsync()) evt.Dispose();
    Assert(conversation.ConversationId == "conv_1", "conversation id captured");
    Assert(conversation.AgentAudioFormat == "pcm_16000", "audio format captured");
    await conversation.DisposeAsync();
}

// ---------- ping -> automatic pong ----------
{
    var ping = """{"type":"ping","ping_event":{"event_id":777,"ping_ms":50}}""";
    var (transport, events) = await RunConversation(new ConversationOptions { AgentId = "a" }, null, ping);
    Assert(events.Count == 1 && events[0] is PingEvent, "ping event surfaced");
    var pong = transport.Sent.Select(s => JsonDocument.Parse(s))
        .FirstOrDefault(d => d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "pong");
    Assert(pong is not null && pong.RootElement.GetProperty("event_id").GetInt32() == 777, "pong sent with matching event_id");
}

// ---------- audio event ----------
{
    var payload = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
    var audio = $$"""{"type":"audio","audio_event":{"audio_base_64":"{{payload}}","event_id":5}}""";
    var (_, events) = await RunConversation(new ConversationOptions { AgentId = "a" }, null, audio);
    var evt = events.OfType<AudioEvent>().FirstOrDefault();
    Assert(evt is not null && evt.EventId == 5, "audio event parsed");
    Assert(evt is not null && evt.GetAudioBytes().SequenceEqual(new byte[] { 1, 2, 3, 4 }), "audio bytes decode");
}

// ---------- transcript + agent response ----------
{
    var frames = new[]
    {
        """{"type":"user_transcript","user_transcription_event":{"event_id":1,"user_transcript":"hello there"}}""",
        """{"type":"agent_response","agent_response_event":{"event_id":2,"agent_response":"hi, how can i help?"}}""",
    };
    var (_, events) = await RunConversation(new ConversationOptions { AgentId = "a" }, null, frames);
    Assert(events.OfType<UserTranscriptEvent>().FirstOrDefault()?.Transcript == "hello there", "user transcript parsed");
    Assert(events.OfType<AgentResponseEvent>().FirstOrDefault()?.Text == "hi, how can i help?", "agent response parsed");
}

// ---------- client tool call -> registered handler -> result sent ----------
{
    var call = """
        {"type":"client_tool_call","client_tool_call":{"tool_name":"get_time","tool_call_id":"tc_1","expects_response":true,"parameters":{"zone":"utc"}}}
        """;
    var (transport, events) = await RunConversation(
        new ConversationOptions { AgentId = "a" },
        c => c.RegisterTool("get_time", (parameters, _) =>
            Task.FromResult($"12:00 {parameters.GetProperty("zone").GetString()}")),
        call);

    Assert(events.OfType<ClientToolCallEvent>().Any(), "tool call event surfaced");
    var result = transport.Sent.Select(s => JsonDocument.Parse(s))
        .FirstOrDefault(d => d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "client_tool_result");
    Assert(result is not null, "tool result sent");
    Assert(result?.RootElement.GetProperty("tool_call_id").GetString() == "tc_1", "tool result has call id");
    Assert(result?.RootElement.GetProperty("result").GetString() == "12:00 utc", "tool handler result forwarded");
    Assert(result?.RootElement.GetProperty("is_error").GetBoolean() == false, "tool result not error");
}

// ---------- tool handler throwing -> is_error result ----------
{
    var call = """
        {"type":"client_tool_call","client_tool_call":{"tool_name":"boom","tool_call_id":"tc_2","expects_response":true,"parameters":{}}}
        """;
    var (transport, _) = await RunConversation(
        new ConversationOptions { AgentId = "a" },
        c => c.RegisterTool("boom", (_, _) => throw new InvalidOperationException("kaboom")),
        call);
    var result = transport.Sent.Select(s => JsonDocument.Parse(s))
        .FirstOrDefault(d => d.RootElement.TryGetProperty("type", out var t) && t.GetString() == "client_tool_result");
    Assert(result?.RootElement.GetProperty("is_error").GetBoolean() == true, "tool failure reported as error");
    Assert(result?.RootElement.GetProperty("result").GetString() == "kaboom", "tool failure message forwarded");
}

// ---------- interruption, vad, error, unknown ----------
{
    var frames = new[]
    {
        """{"type":"interruption","interruption_event":{"event_id":9}}""",
        """{"type":"vad_score","vad_score_event":{"vad_score":0.87}}""",
        """{"type":"client_error","error_event":{"message":"something broke"}}""",
        """{"type":"totally_new_event","payload":{"x":1}}""",
    };
    var (_, events) = await RunConversation(new ConversationOptions { AgentId = "a" }, null, frames);
    Assert(events.OfType<InterruptionEvent>().Any(), "interruption parsed");
    Assert(Math.Abs(events.OfType<VadScoreEvent>().First().Score - 0.87) < 0.001, "vad score parsed");
    Assert(events.OfType<ErrorEvent>().First().Message == "something broke", "error message parsed");
    var unknown = events.OfType<UnknownEvent>().FirstOrDefault();
    Assert(unknown is not null && unknown.Raw.GetProperty("payload").GetProperty("x").GetInt32() == 1,
        "unknown event keeps raw payload");
}

// ---------- outbound message shapes ----------
{
    var transport = Transport();
    var conversation = await AgentConversation.ConnectAsync(new ConversationOptions { AgentId = "a" }, transport);
    await conversation.SendUserMessageAsync("hello");
    await conversation.SendAudioChunkAsync(new byte[] { 9, 9 });
    await conversation.SendContextualUpdateAsync("user opened settings");
    await conversation.SendUserActivityAsync();
    await conversation.SendFeedbackAsync(41, like: true);

    var sent = transport.Sent.Skip(1).Select(s => JsonDocument.Parse(s)).ToList(); // skip initiation
    Assert(sent[0].RootElement.GetProperty("type").GetString() == "user_message"
        && sent[0].RootElement.GetProperty("text").GetString() == "hello", "user_message shape");
    Assert(sent[1].RootElement.TryGetProperty("user_audio_chunk", out var chunk)
        && chunk.GetString() == Convert.ToBase64String(new byte[] { 9, 9 })
        && !sent[1].RootElement.TryGetProperty("type", out _), "user_audio_chunk shape (no type field)");
    Assert(sent[2].RootElement.GetProperty("type").GetString() == "contextual_update", "contextual_update shape");
    Assert(sent[3].RootElement.GetProperty("type").GetString() == "user_activity", "user_activity shape");
    Assert(sent[4].RootElement.GetProperty("type").GetString() == "feedback"
        && sent[4].RootElement.GetProperty("score").GetString() == "like"
        && sent[4].RootElement.GetProperty("event_id").GetInt32() == 41, "feedback shape");
    await conversation.DisposeAsync();
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

/// <summary>Replays canned server frames and records client frames.</summary>
sealed class FakeTransport(IEnumerable<string> serverFrames) : IWebSocketTransport
{
    private readonly List<string> _serverFrames = serverFrames.ToList();

    public Uri? ConnectedUri { get; private set; }
    public List<string> Sent { get; } = [];

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        ConnectedUri = uri;
        return Task.CompletedTask;
    }

    public Task SendAsync(string json, CancellationToken cancellationToken)
    {
        Sent.Add(json);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ReceiveMessagesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var frame in _serverFrames)
        {
            await Task.Yield();
            yield return frame;
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
