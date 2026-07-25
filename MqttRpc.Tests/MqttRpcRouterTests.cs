using System.Text.Json;
using MqttRpc;

namespace MqttRpc.Tests;

public sealed class MqttRpcRouterTests
{
    [Fact]
    public async Task DispatchesTypedHandler()
    {
        var router = new MqttRpcRouter()
            .Map<GreetingRequest, GreetingResponse>(
                "demo/greet",
                (request, _) => Task.FromResult(
                    new GreetingResponse($"Hello, {request.Name}!")));
        using var document = JsonDocument.Parse("""{"name":"Ada"}""");

        var result = await router.DispatchAsync(
            new MqttRpcInvocation("demo/greet", document.RootElement));

        Assert.True(result.Ok);
        var response = Assert.IsType<GreetingResponse>(result.Result);
        Assert.Equal("Hello, Ada!", response.Message);
    }

    [Fact]
    public async Task UnknownMethodReturnsStableError()
    {
        var router = new MqttRpcRouter();
        using var document = JsonDocument.Parse("{}");

        var result = await router.DispatchAsync(
            new MqttRpcInvocation("missing/method", document.RootElement));

        Assert.False(result.Ok);
        Assert.Equal(MqttRpcErrorCodes.MethodNotFound, result.Error?.Code);
    }

    [Fact]
    public async Task InvalidParametersBecomeRpcError()
    {
        var router = new MqttRpcRouter()
            .Map<CountRequest, CountResponse>(
                "demo/count",
                (request, _) => Task.FromResult(new CountResponse(request.Count)));
        using var document = JsonDocument.Parse("""{"count":"not-a-number"}""");

        var result = await router.DispatchAsync(
            new MqttRpcInvocation("demo/count", document.RootElement));

        Assert.False(result.Ok);
        Assert.Equal(MqttRpcErrorCodes.InvalidParams, result.Error?.Code);
    }

    [Fact]
    public async Task HandlerFaultKeepsApplicationError()
    {
        var router = new MqttRpcRouter()
            .Map<GreetingRequest, GreetingResponse>(
                "demo/reject",
                (_, _) => Task.FromException<GreetingResponse>(
                    new MqttRpcFaultException("DEMO_REJECTED", "Demo rejected.")));
        using var document = JsonDocument.Parse("""{"name":"Ada"}""");

        var result = await router.DispatchAsync(
            new MqttRpcInvocation("demo/reject", document.RootElement));

        Assert.False(result.Ok);
        Assert.Equal("DEMO_REJECTED", result.Error?.Code);
        Assert.Equal("Demo rejected.", result.Error?.Message);
    }

    private sealed record CountRequest(int Count);

    private sealed record CountResponse(int Count);

    private sealed record GreetingRequest(string Name);

    private sealed record GreetingResponse(string Message);
}
