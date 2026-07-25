using System.Text.Json;
using MqttRpc;

namespace TestApp;

public static class DemoRpcRoutes
{
    public static MqttRpcRouter CreateRouter()
    {
        var router = new MqttRpcRouter();

        router.Map<GetStatusRequest, DeviceStatusResponse>(
            "device/getStatus",
            (request, _) =>
            {
                var deviceId = string.IsNullOrWhiteSpace(request.DeviceId)
                    ? "demo-device"
                    : request.DeviceId;
                var temperature = Math.Round(
                    22.4 + DateTimeOffset.UtcNow.Second % 10 * 0.12,
                    1);

                return Task.FromResult(new DeviceStatusResponse(
                    deviceId,
                    "online",
                    temperature,
                    DateTimeOffset.UtcNow));
            });

        router.Map<RebootRequest, RebootResponse>(
            "device/reboot",
            async (request, cancellationToken) =>
            {
                await Task.Delay(350, cancellationToken);
                var reason = string.IsNullOrWhiteSpace(request.Reason)
                    ? "Demo request"
                    : request.Reason;

                return new RebootResponse(
                    true,
                    $"Reboot command accepted: {reason}",
                    DateTimeOffset.UtcNow);
            });

        router.Map<JsonElement, JsonElement>(
            "system/echo",
            (request, _) => Task.FromResult(request.Clone()));

        return router;
    }
}

public sealed record GetStatusRequest(string DeviceId);

public sealed record DeviceStatusResponse(
    string DeviceId,
    string Status,
    double TemperatureCelsius,
    DateTimeOffset UpdatedAt);

public sealed record RebootRequest(string Reason);

public sealed record RebootResponse(
    bool Accepted,
    string Message,
    DateTimeOffset ServerTime);
