using MQTTnet;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MqttRpc;

public class MqttRpcClient : IAsyncDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttClientOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    public MqttRpcClient(string brokerHost, int brokerPort = 1883, string path = "/mqtt")
    {
        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        //_options = new MqttClientOptionsBuilder()
        //    .WithTcpServer(brokerHost, brokerPort)
        //    .Build();

        // WebSocket 連線設定（URL 格式為 ws(s)://host:port/path）


        //_options = new MqttClientOptionsBuilder()
        //    .WithWebSocketServer((o) =>
        //    {
        //        o.WithUri($"{brokerHost}:{brokerPort}{path}");

        //    })
        //    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500) // v5 也可試
        //    .Build();
        var url = $"{brokerHost}:{brokerPort}{path}";
        _options = new MqttClientOptionsBuilder()
                        //.WithWebSocketServer( o=>o.WithUri("ws://localhost:5678/mqtt"))
                        .WithWebSocketServer(o => o.WithUri($"{brokerHost}:{brokerPort}{path}"))
                        .WithClientId("web-client-1")
                        .Build();

        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
    }

    public async Task ConnectAsync()
    {
        if (!_client.IsConnected)
        {
            await _client.ConnectAsync(_options);
        }

        // 訂閱所有 response
        await _client.SubscribeAsync("rpc/response/+");
    }

    public async Task<JsonElement> CallAsync(string method, object payload, TimeSpan? timeout = null)
    {
        timeout = timeout ?? TimeSpan.FromSeconds(5);
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var message = new
        {
            requestId,
            method,
            payload,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(message);
        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic($"rpc/request/{method}")
            .WithPayload(Encoding.UTF8.GetBytes(json))
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(mqttMessage);

        if (timeout.HasValue)
        {
            using var cts = new CancellationTokenSource(timeout.Value);
            await using var _ = cts.Token.Register(() =>
            {
                if (_pending.TryRemove(requestId, out var timedOutTcs))
                {
                    timedOutTcs.TrySetException(new TimeoutException($"RPC {requestId} timed out"));
                }
            });

            return await tcs.Task;
        }
        else
        {
            return await tcs.Task;
        }
    }

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        if (topic.StartsWith("rpc/response/"))
        {
            var requestId = topic.Split('/').Last();
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            if (_pending.TryRemove(requestId, out var tcs))
            {
                try
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(payload);
                    tcs.TrySetResult(json);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _client.DisconnectAsync();
        }
        catch { }

        _client.Dispose();
    }
}
