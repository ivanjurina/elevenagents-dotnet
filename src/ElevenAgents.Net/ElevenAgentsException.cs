using System.Net;

namespace ElevenAgents.Net;

/// <summary>Thrown when ElevenLabs returns an error or a request fails.</summary>
public sealed class ElevenAgentsException : Exception
{
    /// <summary>Creates the exception with an error message.</summary>
    public ElevenAgentsException(string message) : base(message) { }

    /// <summary>Creates the exception with an error message and the underlying cause.</summary>
    public ElevenAgentsException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>HTTP status code returned by ElevenLabs, when available.</summary>
    public HttpStatusCode? StatusCode { get; init; }
}
