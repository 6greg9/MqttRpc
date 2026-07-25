using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MqttRpc.AspNetCore;

namespace MqttRpc.AspNetCore.Tests;

public sealed class MqttRpcAspNetCoreIntegrationTests
{
    [Fact]
    public async Task WebSocketEndpointExecutesCorrelatedRpcCall()
    {
        var router = new MqttRpcRouter()
            .Map<EchoRequest, EchoResponse>(
                "system/echo",
                (request, _) => Task.FromResult(new EchoResponse(request.Message)));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(
            options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddMqttRpcWebSocketServer(router);

        await using var application = builder.Build();
        application.MapMqttRpcWebSocket("/mqtt");

        await application.StartAsync();
        try
        {
            var addresses = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?.Addresses;
            var httpAddress = Assert.Single(addresses!);
            var webSocketUri = new UriBuilder(httpAddress)
            {
                Scheme = "ws",
                Path = "/mqtt"
            }.Uri;

            await using var client = new MqttRpcClient(
                new MqttRpcClientOptions(webSocketUri)
                {
                    ClientId = $"aspnet-test-{Guid.NewGuid():N}"
                });
            await client.ConnectAsync();

            var response = await client.CallAsync<EchoRequest, EchoResponse>(
                "system/echo",
                new EchoRequest("hello"));

            Assert.Equal("hello", response.Message);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    [Fact]
    public void RegistrationAndMappingRejectAmbiguousConfiguration()
    {
        var router = new MqttRpcRouter();
        var builder = WebApplication.CreateBuilder();

        builder.Services.AddMqttRpcWebSocketServer(router);
        Assert.Throws<InvalidOperationException>(
            () => builder.Services.AddMqttRpcWebSocketServer(router));

        var application = builder.Build();
        Assert.Throws<ArgumentException>(
            () => application.MapMqttRpcWebSocket("mqtt"));
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResponse(string Message);
}
