using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MqttRpc;

/// <summary>
/// Calls MqttRpc methods over an MQTT 5 WebSocket connection.
/// </summary>
public sealed class MqttRpcClient : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly MqttClientOptions _mqttOptions;
    private readonly MqttRpcClientOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pending = new();
    private readonly string _replyTopic;
    private bool _disposed;

    /// <summary>
    /// Initializes a typed MqttRpc caller.
    /// </summary>
    /// <param name="options">Connection, topic, identity, and timeout settings.</param>
    /// <param name="jsonOptions">Optional serializer settings for request and response payloads.</param>
    public MqttRpcClient(
        MqttRpcClientOptions options,
        JsonSerializerOptions? jsonOptions = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var clientId = options.ClientId ?? $"dotnet-{Guid.NewGuid():N}";
        _replyTopic = MqttRpcProtocol.BuildReplyTopic(clientId, options.TopicRoot);

        _client = new MqttClientFactory().CreateMqttClient();
        _mqttOptions = new MqttClientOptionsBuilder()
            .WithWebSocketServer(webSocket => webSocket.WithUri(options.WebSocketUri.AbsoluteUri))
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithClientId(clientId)
            .Build();

        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
        _client.DisconnectedAsync += HandleDisconnectedAsync;
    }

    /// <summary>
    /// Gets whether the underlying MQTT client is currently connected.
    /// </summary>
    public bool IsConnected => _client.IsConnected;

    /// <summary>
    /// Connects to the broker and subscribes to this client's reply topic.
    /// </summary>
    /// <param name="cancellationToken">Cancels connection or subscription.</param>
    /// <returns>A task that completes when calls can be issued.</returns>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client.IsConnected)
            {
                return;
            }

            await _client.ConnectAsync(_mqttOptions, cancellationToken).ConfigureAwait(false);
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(_replyTopic, MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();
            await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>
    /// Calls a remote RPC method and waits for its correlated response.
    /// </summary>
    /// <typeparam name="TRequest">The request parameters type.</typeparam>
    /// <typeparam name="TResponse">The expected response result type.</typeparam>
    /// <param name="method">The slash-separated RPC method name.</param>
    /// <param name="parameters">The request parameters.</param>
    /// <param name="timeout">An optional per-call timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The deserialized remote result.</returns>
    /// <exception cref="MqttRpcRemoteException">The remote handler returned an error.</exception>
    /// <exception cref="MqttRpcProtocolException">The response did not conform to the protocol.</exception>
    /// <exception cref="TimeoutException">The call exceeded its timeout.</exception>
    public async Task<TResponse> CallAsync<TRequest, TResponse>(
        string method,
        TRequest parameters,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("ConnectAsync must complete before making an RPC call.");
        }

        var normalizedMethod = MqttRpcProtocol.NormalizeMethod(method);
        var effectiveTimeout = timeout ?? _options.DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var correlationData = Guid.NewGuid().ToByteArray();
        var correlationKey = Convert.ToHexString(correlationData);
        var completion = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(correlationKey, completion))
        {
            throw new InvalidOperationException("Could not allocate an RPC correlation identifier.");
        }

        var requestPayload = JsonSerializer.SerializeToUtf8Bytes(
            new MqttRpcRequestEnvelope<TRequest>(parameters),
            _jsonOptions);
        var expirySeconds = (uint)Math.Clamp(
            Math.Ceiling(effectiveTimeout.TotalSeconds),
            1,
            uint.MaxValue);

        var requestMessage = new MqttApplicationMessageBuilder()
            .WithTopic(MqttRpcProtocol.BuildRequestTopic(normalizedMethod, _options.TopicRoot))
            .WithPayload(requestPayload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithResponseTopic(_replyTopic)
            .WithCorrelationData(correlationData)
            .WithContentType("application/json")
            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
            .WithMessageExpiryInterval(expirySeconds)
            .Build();

        using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        using var registration = linkedSource.Token.Register(
            () => completion.TrySetCanceled(linkedSource.Token));

        try
        {
            await _client.PublishAsync(requestMessage, linkedSource.Token).ConfigureAwait(false);
            var responsePayload = await completion.Task.ConfigureAwait(false);

            MqttRpcResponseEnvelope<TResponse>? response;
            try
            {
                response = JsonSerializer.Deserialize<MqttRpcResponseEnvelope<TResponse>>(
                    responsePayload,
                    _jsonOptions);
            }
            catch (JsonException exception)
            {
                throw new MqttRpcProtocolException("The RPC response is not valid JSON.", exception);
            }

            if (response is null)
            {
                throw new MqttRpcProtocolException("The RPC response body is empty.");
            }

            if (!response.Ok)
            {
                throw new MqttRpcRemoteException(
                    response.Error
                    ?? new MqttRpcError(
                        MqttRpcErrorCodes.InternalError,
                        "The remote endpoint returned an unspecified error."));
            }

            return response.Result!;
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"RPC method '{normalizedMethod}' timed out after {effectiveTimeout}.");
        }
        finally
        {
            _pending.TryRemove(correlationKey, out _);
        }
    }

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        var message = eventArgs.ApplicationMessage;
        if (!string.Equals(message.Topic, _replyTopic, StringComparison.Ordinal)
            || message.CorrelationData is not { Length: > 0 })
        {
            return Task.CompletedTask;
        }

        var correlationKey = Convert.ToHexString(message.CorrelationData);
        if (_pending.TryRemove(correlationKey, out var completion))
        {
            completion.TrySetResult(message.Payload.ToArray());
            eventArgs.IsHandled = true;
        }

        return Task.CompletedTask;
    }

    private Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        var exception = eventArgs.Exception
            ?? new InvalidOperationException("The MQTT connection was closed.");
        foreach (var pendingCall in _pending)
        {
            pendingCall.Value.TrySetException(exception);
        }

        _pending.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disconnects and releases the underlying MQTT client.
    /// </summary>
    /// <returns>A task that completes after disposal finishes.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.ApplicationMessageReceivedAsync -= HandleMessageAsync;
        _client.DisconnectedAsync -= HandleDisconnectedAsync;

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Disposal should remain best-effort.
        }

        _client.Dispose();
        _connectGate.Dispose();
    }
}
