namespace MqttRpc;

/// <summary>
/// Configures an MQTT 5 WebSocket connection used by <see cref="MqttRpcClient"/>.
/// </summary>
public sealed class MqttRpcClientOptions
{
    /// <summary>
    /// Initializes client options for an MQTT WebSocket endpoint.
    /// </summary>
    /// <param name="webSocketUri">An absolute <c>ws://</c> or <c>wss://</c> broker URI.</param>
    public MqttRpcClientOptions(Uri webSocketUri)
    {
        WebSocketUri = webSocketUri ?? throw new ArgumentNullException(nameof(webSocketUri));
    }

    /// <summary>
    /// Gets the MQTT client identifier. A unique identifier is generated when this value is null.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets the timeout used when a call does not supply its own timeout.
    /// </summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets the root topic for the MqttRpc wire protocol.
    /// </summary>
    public string TopicRoot { get; init; } = MqttRpcProtocol.DefaultTopicRoot;

    /// <summary>
    /// Gets the MQTT broker WebSocket URI.
    /// </summary>
    public Uri WebSocketUri { get; }

    internal void Validate()
    {
        if (!WebSocketUri.IsAbsoluteUri
            || WebSocketUri.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException(
                "The broker URI must be an absolute ws:// or wss:// URI.",
                nameof(WebSocketUri));
        }

        if (DefaultTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultTimeout),
                "The default timeout must be greater than zero.");
        }

        MqttRpcProtocol.NormalizeTopicRoot(TopicRoot);

        if (ClientId is not null)
        {
            MqttRpcProtocol.BuildReplyTopic(ClientId, TopicRoot);
        }
    }
}
