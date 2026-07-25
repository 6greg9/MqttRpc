using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace MqttRpc;

/// <summary>
/// Adapts MQTTnet server publishes to an <see cref="MqttRpcRouter"/> and emits correlated replies.
/// </summary>
public sealed class MqttNetRpcResponder : IDisposable
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly MqttRpcRouter _router;
    private readonly MqttServer _server;
    private readonly string _topicRoot;
    private bool _disposed;

    /// <summary>
    /// Attaches an MqttRpc responder to an application-owned MQTTnet server.
    /// </summary>
    /// <param name="server">The running or soon-to-be-started MQTTnet server.</param>
    /// <param name="router">The router containing RPC handlers.</param>
    /// <param name="topicRoot">The protocol topic root.</param>
    /// <param name="jsonOptions">Optional serializer settings for request and response payloads.</param>
    public MqttNetRpcResponder(
        MqttServer server,
        MqttRpcRouter router,
        string topicRoot = MqttRpcProtocol.DefaultTopicRoot,
        JsonSerializerOptions? jsonOptions = null)
    {
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _topicRoot = MqttRpcProtocol.NormalizeTopicRoot(topicRoot);
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

        _server.InterceptingPublishAsync += HandlePublishAsync;
    }

    /// <summary>
    /// Detaches the responder from the MQTTnet server.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _server.InterceptingPublishAsync -= HandlePublishAsync;
    }

    private async Task HandlePublishAsync(InterceptingPublishEventArgs eventArgs)
    {
        var requestMessage = eventArgs.ApplicationMessage;
        if (!MqttRpcProtocol.TryGetMethod(requestMessage.Topic, out var method, _topicRoot))
        {
            return;
        }

        if (!MqttRpcProtocol.IsReplyTopic(requestMessage.ResponseTopic, _topicRoot)
            || requestMessage.CorrelationData is not { Length: > 0 })
        {
            Trace.TraceWarning(
                "Ignored MqttRpc request '{0}' because responseTopic or correlationData is invalid.",
                requestMessage.Topic);
            return;
        }

        MqttRpcResult result;
        try
        {
            var envelope = JsonSerializer.Deserialize<MqttRpcRequestEnvelope>(
                requestMessage.Payload.ToArray(),
                _jsonOptions);

            if (envelope is null || envelope.Params.ValueKind is JsonValueKind.Undefined)
            {
                result = MqttRpcResult.Failure(new MqttRpcError(
                    MqttRpcErrorCodes.InvalidRequest,
                    "The request payload must contain a 'params' property."));
            }
            else
            {
                result = await _router.DispatchAsync(
                    new MqttRpcInvocation(method, envelope.Params),
                    eventArgs.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException exception)
        {
            result = MqttRpcResult.Failure(new MqttRpcError(
                MqttRpcErrorCodes.InvalidRequest,
                exception.Message));
        }

        var responsePayload = JsonSerializer.SerializeToUtf8Bytes(
            new MqttRpcResponseEnvelope(result.Ok, result.Result, result.Error),
            _jsonOptions);

        var responseMessage = new MqttApplicationMessageBuilder()
            .WithTopic(requestMessage.ResponseTopic)
            .WithPayload(responsePayload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithCorrelationData(requestMessage.CorrelationData)
            .WithContentType("application/json")
            .WithPayloadFormatIndicator(MqttPayloadFormatIndicator.CharacterData)
            .Build();

        await _server.InjectApplicationMessage(
            new InjectedMqttApplicationMessage(responseMessage)
            {
                SenderClientId = "mqttrpc-responder"
            },
            eventArgs.CancellationToken).ConfigureAwait(false);
    }
}
