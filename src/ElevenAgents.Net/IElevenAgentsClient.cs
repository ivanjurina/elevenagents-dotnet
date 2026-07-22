using System.Text.Json;

namespace ElevenAgents.Net;

/// <summary>Abstraction over <see cref="ElevenAgentsClient"/> for testing and DI.</summary>
public interface IElevenAgentsClient
{
    /// <summary>Gets a signed WebSocket URL for a private agent (server-side; valid 15 minutes).</summary>
    Task<string> GetSignedUrlAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Lists the agents in your workspace.</summary>
    Task<JsonDocument> ListAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one agent's full configuration.</summary>
    Task<JsonDocument> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>Lists past conversations, optionally filtered by agent.</summary>
    Task<JsonDocument> ListConversationsAsync(string? agentId = null, CancellationToken cancellationToken = default);

    /// <summary>Gets one conversation's details including the transcript.</summary>
    Task<JsonDocument> GetConversationAsync(string conversationId, CancellationToken cancellationToken = default);
}
