using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using MQTTnet.Server;
using MqttRpc;

namespace MqttRpc.Tests;

public sealed class MqttNetRpcResponderTests
{
    [Fact]
    public async Task Mqtt5RequestReceivesCorrelatedResponse()
    {
        var port = GetAvailablePort();
        var serverFactory = new MqttServerFactory();
        var serverOptions = serverFactory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(port)
            .Build();
        using var server = serverFactory.CreateMqttServer(serverOptions);

        var router = new MqttRpcRouter()
            .Map<EchoRequest, EchoResponse>(
                "system/echo",
                (request, _) => Task.FromResult(new EchoResponse(request.Message)));
        using var responder = new MqttNetRpcResponder(server, router);

        var client = new MqttClientFactory().CreateMqttClient();
        var responseCompletion = new TaskCompletionSource<MqttApplicationMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.ApplicationMessageReceivedAsync += eventArgs =>
        {
            responseCompletion.TrySetResult(eventArgs.ApplicationMessage);
            return Task.CompletedTask;
        };

        try
        {
            await server.StartAsync();
            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("127.0.0.1", port)
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .WithClientId($"test-{Guid.NewGuid():N}")
                .Build();
            await client.ConnectAsync(clientOptions);

            var replyTopic = MqttRpcProtocol.BuildReplyTopic($"test-{Guid.NewGuid():N}");
            await client.SubscribeAsync(replyTopic);

            var correlationData = Guid.NewGuid().ToByteArray();
            var request = new MqttApplicationMessageBuilder()
                .WithTopic(MqttRpcProtocol.BuildRequestTopic("system/echo"))
                .WithPayload("""{"params":{"message":"hello"}}""")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithResponseTopic(replyTopic)
                .WithCorrelationData(correlationData)
                .Build();

            await client.PublishAsync(request);
            var response = await responseCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(replyTopic, response.Topic);
            Assert.Equal(correlationData, response.CorrelationData);

            using var document = JsonDocument.Parse(response.Payload.ToArray());
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "hello",
                document.RootElement
                    .GetProperty("result")
                    .GetProperty("message")
                    .GetString());
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync();
            }

            client.Dispose();
            await server.StopAsync(new MqttServerStopOptionsBuilder().Build());
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResponse(string Message);
}
