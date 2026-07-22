using System.Text.Json;

namespace ElevenAgents.Net;

/// <summary>
/// REST client for the ElevenLabs Agents platform: signed URLs for private agents,
/// agent listing, and conversation history. The realtime conversation itself lives in
/// <see cref="Realtime.AgentConversation"/>.
/// </summary>
public sealed class ElevenAgentsClient : IElevenAgentsClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ElevenAgentsClientOptions _options;

    /// <summary>Creates a client with its own <see cref="HttpClient"/>.</summary>
    public ElevenAgentsClient(string apiKey)
        : this(new ElevenAgentsClientOptions { ApiKey = apiKey }) { }

    /// <summary>Creates a client with its own <see cref="HttpClient"/> and the given options.</summary>
    public ElevenAgentsClient(ElevenAgentsClientOptions options)
        : this(new HttpClient { Timeout = options.Timeout }, options)
    {
        _ownsHttpClient = true;
    }

    /// <summary>Creates a client over an externally managed <see cref="HttpClient"/> (e.g. IHttpClientFactory).</summary>
    public ElevenAgentsClient(HttpClient httpClient, ElevenAgentsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("An API key is required.", nameof(options));

        _http = httpClient;
        _options = options;
    }

    /// <summary>
    /// Gets a signed WebSocket URL for a private agent. Call this server-side; hand the URL
    /// to the client that opens the conversation. Valid for 15 minutes.
    /// </summary>
    public async Task<string> GetSignedUrlAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using var doc = await GetJsonAsync($"/v1/convai/conversation/get-signed-url?agent_id={Uri.EscapeDataString(agentId)}", cancellationToken).ConfigureAwait(false);
        return doc.RootElement.TryGetProperty("signed_url", out var url)
            ? url.GetString() ?? throw new ElevenAgentsException("signed_url was null.")
            : throw new ElevenAgentsException("Response did not contain signed_url.");
    }

    /// <summary>Lists the agents in your workspace. Returns the raw JSON document.</summary>
    public Task<JsonDocument> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync("/v1/convai/agents", cancellationToken);

    /// <summary>Gets one agent's full configuration. Returns the raw JSON document.</summary>
    public Task<JsonDocument> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return GetJsonAsync($"/v1/convai/agents/{Uri.EscapeDataString(agentId)}", cancellationToken);
    }

    /// <summary>Lists past conversations, optionally filtered by agent. Returns the raw JSON document.</summary>
    public Task<JsonDocument> ListConversationsAsync(string? agentId = null, CancellationToken cancellationToken = default)
    {
        var query = agentId is null ? "" : $"?agent_id={Uri.EscapeDataString(agentId)}";
        return GetJsonAsync($"/v1/convai/conversations{query}", cancellationToken);
    }

    /// <summary>Gets one conversation's details including the transcript. Returns the raw JSON document.</summary>
    public Task<JsonDocument> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        return GetJsonAsync($"/v1/convai/conversations/{Uri.EscapeDataString(conversationId)}", cancellationToken);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Add("xi-api-key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex)
        {
            throw new ElevenAgentsException($"HTTP request to ElevenLabs failed: {ex.Message}", ex);
        }

        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ElevenAgentsException(TryExtractError(content) ?? $"ElevenLabs returned {(int)response.StatusCode}.")
                {
                    StatusCode = response.StatusCode,
                };
            return JsonDocument.Parse(content);
        }
    }

    private static string? TryExtractError(string content)
    {
        if (content.Length == 0 || content[0] != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                return detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.GetRawText();
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient) _http.Dispose();
    }
}
