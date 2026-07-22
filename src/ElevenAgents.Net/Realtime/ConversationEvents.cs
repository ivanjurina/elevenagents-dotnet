using System.Text.Json;

namespace ElevenAgents.Net.Realtime;

/// <summary>
/// Base type for events received from the agent over the realtime connection.
/// <see cref="Raw"/> always carries the full JSON payload, so event types this
/// library does not model yet are still fully usable via <see cref="UnknownEvent"/>.
/// </summary>
public abstract class ConversationEvent : IDisposable
{
    private readonly JsonDocument _document;

    private protected ConversationEvent(JsonDocument document) => _document = document;

    /// <summary>The event's <c>type</c> discriminator as sent by the server.</summary>
    public required string Type { get; init; }

    /// <summary>The full JSON payload of the event. Valid until this instance is disposed.</summary>
    public JsonElement Raw => _document.RootElement;

    /// <inheritdoc />
    public void Dispose() => _document.Dispose();

    internal static ConversationEvent Parse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        return type switch
        {
            "conversation_initiation_metadata" => ConversationStartedEvent.From(doc, type),
            "audio" => AudioEvent.From(doc, type),
            "agent_response" => AgentResponseEvent.From(doc, type),
            "agent_response_correction" => AgentResponseCorrectionEvent.From(doc, type),
            "agent_chat_response_part" => AgentChatResponsePartEvent.From(doc, type),
            "user_transcript" => UserTranscriptEvent.From(doc, type),
            "client_tool_call" => ClientToolCallEvent.From(doc, type),
            "interruption" => new InterruptionEvent(doc) { Type = type },
            "vad_score" => VadScoreEvent.From(doc, type),
            "ping" => PingEvent.From(doc, type),
            "client_error" or "error" => ErrorEvent.From(doc, type),
            _ => new UnknownEvent(doc) { Type = type },
        };
    }

    private protected static JsonElement Sub(JsonDocument doc, string name) =>
        doc.RootElement.TryGetProperty(name, out var e) ? e : default;
}

/// <summary>First event of a conversation: id and negotiated audio formats.</summary>
public sealed class ConversationStartedEvent : ConversationEvent
{
    private ConversationStartedEvent(JsonDocument d) : base(d) { }

    public string? ConversationId { get; private init; }
    public string? AgentOutputAudioFormat { get; private init; }
    public string? UserInputAudioFormat { get; private init; }

    internal static ConversationStartedEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "conversation_initiation_metadata_event");
        return new ConversationStartedEvent(doc)
        {
            Type = type,
            ConversationId = GetString(e, "conversation_id"),
            AgentOutputAudioFormat = GetString(e, "agent_output_audio_format"),
            UserInputAudioFormat = GetString(e, "user_input_audio_format"),
        };
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.GetString() : null;
}

/// <summary>A chunk of the agent's synthesized speech.</summary>
public sealed class AudioEvent : ConversationEvent
{
    private AudioEvent(JsonDocument d) : base(d) { }

    public int EventId { get; private init; }
    /// <summary>Base64-encoded audio in the format announced at conversation start.</summary>
    public string? AudioBase64 { get; private init; }

    /// <summary>Decodes <see cref="AudioBase64"/>.</summary>
    public byte[] GetAudioBytes() =>
        AudioBase64 is null ? [] : Convert.FromBase64String(AudioBase64);

    internal static AudioEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "audio_event");
        return new AudioEvent(doc)
        {
            Type = type,
            EventId = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("event_id", out var id) ? id.GetInt32() : 0,
            AudioBase64 = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("audio_base_64", out var a) ? a.GetString() : null,
        };
    }
}

/// <summary>The agent's full text response for a turn.</summary>
public sealed class AgentResponseEvent : ConversationEvent
{
    private AgentResponseEvent(JsonDocument d) : base(d) { }

    public int EventId { get; private init; }
    public string? Text { get; private init; }

    internal static AgentResponseEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "agent_response_event");
        return new AgentResponseEvent(doc)
        {
            Type = type,
            EventId = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("event_id", out var id) ? id.GetInt32() : 0,
            Text = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("agent_response", out var r) ? r.GetString() : null,
        };
    }
}

/// <summary>A correction to a previously sent agent response (e.g. after an interruption).</summary>
public sealed class AgentResponseCorrectionEvent : ConversationEvent
{
    private AgentResponseCorrectionEvent(JsonDocument d) : base(d) { }

    internal static AgentResponseCorrectionEvent From(JsonDocument doc, string type) =>
        new(doc) { Type = type };
}

/// <summary>Streaming text chunk of the agent's chat-mode response.</summary>
public sealed class AgentChatResponsePartEvent : ConversationEvent
{
    private AgentChatResponsePartEvent(JsonDocument d) : base(d) { }

    public string? TextDelta { get; private init; }

    internal static AgentChatResponsePartEvent From(JsonDocument doc, string type)
    {
        // shape: { "text_response_part": { "text": "..." } } (verify live; fall back to raw)
        string? delta = null;
        var e = Sub(doc, "text_response_part");
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("text", out var v))
            delta = v.GetString();
        return new AgentChatResponsePartEvent(doc) { Type = type, TextDelta = delta };
    }
}

/// <summary>Realtime transcription of the user's speech.</summary>
public sealed class UserTranscriptEvent : ConversationEvent
{
    private UserTranscriptEvent(JsonDocument d) : base(d) { }

    public int EventId { get; private init; }
    public string? Transcript { get; private init; }

    internal static UserTranscriptEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "user_transcription_event");
        return new UserTranscriptEvent(doc)
        {
            Type = type,
            EventId = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("event_id", out var id) ? id.GetInt32() : 0,
            Transcript = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("user_transcript", out var t) ? t.GetString() : null,
        };
    }
}

/// <summary>The agent asks the client to run a tool and (optionally) report the result.</summary>
public sealed class ClientToolCallEvent : ConversationEvent
{
    private ClientToolCallEvent(JsonDocument d) : base(d) { }

    public string ToolName { get; private init; } = "";
    public string ToolCallId { get; private init; } = "";
    public bool ExpectsResponse { get; private init; }
    /// <summary>Tool parameters as sent by the agent. Valid until this event is disposed.</summary>
    public JsonElement Parameters { get; private init; }

    internal static ClientToolCallEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "client_tool_call");
        string? name = null, id = null;
        var expects = false;
        JsonElement parameters = default;
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("tool_name", out var n)) name = n.GetString();
            if (e.TryGetProperty("tool_call_id", out var i)) id = i.GetString();
            if (e.TryGetProperty("expects_response", out var x)) expects = x.GetBoolean();
            if (e.TryGetProperty("parameters", out var p)) parameters = p;
        }
        return new ClientToolCallEvent(doc)
        {
            Type = type,
            ToolName = name ?? "",
            ToolCallId = id ?? "",
            ExpectsResponse = expects,
            Parameters = parameters,
        };
    }
}

/// <summary>The agent's current spoken response was interrupted by the user.</summary>
public sealed class InterruptionEvent : ConversationEvent
{
    internal InterruptionEvent(JsonDocument d) : base(d) { }
}

/// <summary>Voice activity detection score for the user's audio.</summary>
public sealed class VadScoreEvent : ConversationEvent
{
    private VadScoreEvent(JsonDocument d) : base(d) { }

    public double Score { get; private init; }

    internal static VadScoreEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "vad_score_event");
        return new VadScoreEvent(doc)
        {
            Type = type,
            Score = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("vad_score", out var s) ? s.GetDouble() : 0,
        };
    }
}

/// <summary>Server latency ping. The library answers with a pong automatically.</summary>
public sealed class PingEvent : ConversationEvent
{
    private PingEvent(JsonDocument d) : base(d) { }

    public int EventId { get; private init; }

    internal static PingEvent From(JsonDocument doc, string type)
    {
        var e = Sub(doc, "ping_event");
        return new PingEvent(doc)
        {
            Type = type,
            EventId = e.ValueKind == JsonValueKind.Object && e.TryGetProperty("event_id", out var id) ? id.GetInt32() : 0,
        };
    }
}

/// <summary>An error reported by the server during the conversation.</summary>
public sealed class ErrorEvent : ConversationEvent
{
    private ErrorEvent(JsonDocument d) : base(d) { }

    public string? Message { get; private init; }

    internal static ErrorEvent From(JsonDocument doc, string type)
    {
        string? message = null;
        if (doc.RootElement.TryGetProperty("error_event", out var e) &&
            e.ValueKind == JsonValueKind.Object &&
            e.TryGetProperty("message", out var m))
            message = m.GetString();
        message ??= doc.RootElement.TryGetProperty("message", out var m2) ? m2.GetString() : null;
        return new ErrorEvent(doc) { Type = type, Message = message };
    }
}

/// <summary>Any event type this library does not model yet. Inspect <see cref="ConversationEvent.Raw"/>.</summary>
public sealed class UnknownEvent : ConversationEvent
{
    internal UnknownEvent(JsonDocument d) : base(d) { }
}
