using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ElevenAgents.Net.Realtime;

/// <summary>
/// A realtime conversation with an ElevenLabs agent over WebSocket.
/// Create with <see cref="ConnectAsync(ConversationOptions, CancellationToken)"/>, then consume
/// <see cref="ReceiveEventsAsync"/> while sending input with the Send* methods.
/// Pings are answered automatically; registered tools are invoked automatically.
/// </summary>
public sealed class AgentConversation : IAsyncDisposable
{
    private const string DefaultEndpoint = "wss://api.elevenlabs.io/v1/convai/conversation";

    private readonly IWebSocketTransport _transport;
    private readonly ConversationOptions _options;
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<string>>> _tools = new();

    private AgentConversation(IWebSocketTransport transport, ConversationOptions options)
    {
        _transport = transport;
        _options = options;
    }

    /// <summary>The conversation id, available after the first server event.</summary>
    public string? ConversationId { get; private set; }

    /// <summary>Audio format the agent sends (e.g. pcm_16000), available after the first server event.</summary>
    public string? AgentAudioFormat { get; private set; }

    /// <summary>Connects to an agent and sends the conversation initiation payload.</summary>
    public static Task<AgentConversation> ConnectAsync(ConversationOptions options, CancellationToken cancellationToken = default) =>
        ConnectAsync(options, new ClientWebSocketTransport(), cancellationToken);

    /// <summary>Connects using a custom transport. Used by tests; most callers want the other overload.</summary>
    public static async Task<AgentConversation> ConnectAsync(
        ConversationOptions options, IWebSocketTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transport);

        var uri = options.SignedUrl is not null
            ? new Uri(options.SignedUrl)
            : new Uri($"{DefaultEndpoint}?agent_id={Uri.EscapeDataString(
                options.AgentId ?? throw new ArgumentException("Set AgentId or SignedUrl.", nameof(options)))}");

        var conversation = new AgentConversation(transport, options);
        await transport.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        await conversation.SendInitiationAsync(cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    /// <summary>
    /// Registers a client tool the agent can call. The handler receives the tool parameters
    /// and returns the result string reported back to the agent.
    /// </summary>
    public void RegisterTool(string name, Func<JsonElement, CancellationToken, Task<string>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _tools[name] = handler;
    }

    /// <summary>
    /// Yields events from the agent until the connection closes or the token is cancelled.
    /// Pings are answered and registered tool calls are executed before the event is yielded.
    /// Dispose each event when done with it (e.g. with <c>using</c>).
    /// </summary>
    public async IAsyncEnumerable<ConversationEvent> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var json in _transport.ReceiveMessagesAsync(cancellationToken).ConfigureAwait(false))
        {
            var evt = ConversationEvent.Parse(json);

            switch (evt)
            {
                case ConversationStartedEvent started:
                    ConversationId = started.ConversationId;
                    AgentAudioFormat = started.AgentOutputAudioFormat;
                    break;

                case PingEvent ping:
                    await SendJsonAsync(w =>
                    {
                        w.WriteString("type", "pong");
                        w.WriteNumber("event_id", ping.EventId);
                    }, cancellationToken).ConfigureAwait(false);
                    break;

                case ClientToolCallEvent call when _tools.TryGetValue(call.ToolName, out var handler):
                    string result;
                    var isError = false;
                    try
                    {
                        result = await handler(call.Parameters, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        result = ex.Message;
                        isError = true;
                    }
                    if (call.ExpectsResponse)
                        await SendToolResultAsync(call.ToolCallId, result, isError, cancellationToken).ConfigureAwait(false);
                    break;
            }

            yield return evt;
        }
    }

    /// <summary>Sends a user text message (chat mode or alongside voice).</summary>
    public Task SendUserMessageAsync(string text, CancellationToken cancellationToken = default) =>
        SendJsonAsync(w =>
        {
            w.WriteString("type", "user_message");
            w.WriteString("text", text);
        }, cancellationToken);

    /// <summary>Sends a chunk of user audio in the format negotiated at conversation start.</summary>
    public Task SendAudioChunkAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default) =>
        SendJsonAsync(w =>
        {
            w.WriteString("user_audio_chunk", Convert.ToBase64String(audio.Span));
        }, cancellationToken);

    /// <summary>Reports the result of a client tool call back to the agent.</summary>
    public Task SendToolResultAsync(string toolCallId, string result, bool isError = false, CancellationToken cancellationToken = default) =>
        SendJsonAsync(w =>
        {
            w.WriteString("type", "client_tool_result");
            w.WriteString("tool_call_id", toolCallId);
            w.WriteString("result", result);
            w.WriteBoolean("is_error", isError);
        }, cancellationToken);

    /// <summary>Sends non-interrupting context (e.g. "user is viewing the pricing page").</summary>
    public Task SendContextualUpdateAsync(string text, CancellationToken cancellationToken = default) =>
        SendJsonAsync(w =>
        {
            w.WriteString("type", "contextual_update");
            w.WriteString("text", text);
        }, cancellationToken);

    /// <summary>Signals user presence to prevent the agent from interrupting.</summary>
    public Task SendUserActivityAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(w => w.WriteString("type", "user_activity"), cancellationToken);

    /// <summary>Sends like/dislike feedback for an agent response event.</summary>
    public Task SendFeedbackAsync(int eventId, bool like, CancellationToken cancellationToken = default) =>
        SendJsonAsync(w =>
        {
            w.WriteString("type", "feedback");
            w.WriteNumber("event_id", eventId);
            w.WriteString("score", like ? "like" : "dislike");
        }, cancellationToken);

    private Task SendInitiationAsync(CancellationToken cancellationToken) =>
        SendJsonAsync(w =>
        {
            w.WriteString("type", "conversation_initiation_client_data");

            var hasOverride = _options.SystemPromptOverride is not null
                || _options.FirstMessageOverride is not null
                || _options.LanguageOverride is not null
                || _options.VoiceIdOverride is not null;
            if (hasOverride)
            {
                w.WriteStartObject("conversation_config_override");
                w.WriteStartObject("agent");
                if (_options.FirstMessageOverride is not null) w.WriteString("first_message", _options.FirstMessageOverride);
                if (_options.LanguageOverride is not null) w.WriteString("language", _options.LanguageOverride);
                if (_options.SystemPromptOverride is not null)
                {
                    w.WriteStartObject("prompt");
                    w.WriteString("prompt", _options.SystemPromptOverride);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
                if (_options.VoiceIdOverride is not null)
                {
                    w.WriteStartObject("tts");
                    w.WriteString("voice_id", _options.VoiceIdOverride);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }

            if (_options.DynamicVariables.Count > 0)
            {
                w.WriteStartObject("dynamic_variables");
                foreach (var (key, value) in _options.DynamicVariables)
                    w.WriteString(key, value);
                w.WriteEndObject();
            }
        }, cancellationToken);

    private async Task SendJsonAsync(Action<Utf8JsonWriter> writeBody, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }
        await _transport.SendAsync(System.Text.Encoding.UTF8.GetString(stream.ToArray()), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try { await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* closing best-effort */ }
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
