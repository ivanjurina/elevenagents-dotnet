namespace ElevenAgents.Net;

/// <summary>Configuration for <see cref="ElevenAgentsClient"/>.</summary>
public sealed class ElevenAgentsClientOptions
{
    /// <summary>Your secret API key from the ElevenLabs dashboard.</summary>
    public required string ApiKey { get; set; }

    /// <summary>Base URL of the API. Override for testing.</summary>
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io";

    /// <summary>HTTP timeout used when the client owns its HttpClient. Default: 60s.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
