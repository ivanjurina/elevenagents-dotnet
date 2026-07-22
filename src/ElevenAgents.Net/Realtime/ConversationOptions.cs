namespace ElevenAgents.Net.Realtime;

/// <summary>Options for starting a realtime conversation.</summary>
public sealed class ConversationOptions
{
    /// <summary>Agent id for public agents. For private agents use <see cref="SignedUrl"/>.</summary>
    public string? AgentId { get; set; }

    /// <summary>
    /// Signed WebSocket URL for private agents, obtained server-side via
    /// <c>ElevenAgentsClient.GetSignedUrlAsync</c>. Valid for 15 minutes.
    /// </summary>
    public string? SignedUrl { get; set; }

    /// <summary>Overrides the agent's system prompt for this conversation, if the agent allows it.</summary>
    public string? SystemPromptOverride { get; set; }

    /// <summary>Overrides the agent's first message for this conversation, if the agent allows it.</summary>
    public string? FirstMessageOverride { get; set; }

    /// <summary>Overrides the agent's language, e.g. "en".</summary>
    public string? LanguageOverride { get; set; }

    /// <summary>Overrides the agent's TTS voice id.</summary>
    public string? VoiceIdOverride { get; set; }

    /// <summary>Dynamic variables referenced by the agent's prompt, e.g. user_name.</summary>
    public IDictionary<string, string> DynamicVariables { get; } = new Dictionary<string, string>();
}
