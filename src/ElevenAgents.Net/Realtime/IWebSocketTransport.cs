using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace ElevenAgents.Net.Realtime;

/// <summary>
/// Minimal WebSocket abstraction so <see cref="AgentConversation"/> can be tested
/// without a network connection.
/// </summary>
public interface IWebSocketTransport : IAsyncDisposable
{
    /// <summary>Opens the connection.</summary>
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    /// <summary>Sends one text frame containing UTF-8 JSON.</summary>
    Task SendAsync(string json, CancellationToken cancellationToken);

    /// <summary>Yields complete text frames until the connection closes.</summary>
    IAsyncEnumerable<string> ReceiveMessagesAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection gracefully.</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}

/// <summary>Default transport over <see cref="ClientWebSocket"/>.</summary>
public sealed class ClientWebSocketTransport : IWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();

    /// <inheritdoc />
    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(uri, cancellationToken);

    /// <inheritdoc />
    public Task SendAsync(string json, CancellationToken cancellationToken) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ReceiveMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16 * 1024];
        var message = new MemoryStream();

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
                yield break; // connection dropped
            }

            if (result.MessageType == WebSocketMessageType.Close)
                yield break;

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            yield return json;
        }
    }

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
