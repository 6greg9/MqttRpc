using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using MqttRpc;
using MqttRpc.AspNetCore;

namespace TestApp;

public sealed class DesktopAppHost : IAsyncDisposable
{
    private readonly WebApplication _application;
    private bool _disposed;

    private DesktopAppHost(WebApplication application, Uri baseAddress)
    {
        _application = application;
        BaseAddress = baseAddress;
        MqttWebSocketUri = new UriBuilder(baseAddress)
        {
            Scheme = "ws",
            Path = "/mqtt"
        }.Uri;
    }

    public Uri BaseAddress { get; }

    public Uri MqttWebSocketUri { get; }

    public static async Task<DesktopAppHost> StartAsync(
        MqttRpcRouter router,
        int port = 5678,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var baseAddress = new Uri($"http://127.0.0.1:{port}/");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.WebHost.UseUrls(baseAddress.GetLeftPart(UriPartial.Authority));
        builder.Services.AddMqttRpcWebSocketServer(router);

        var application = builder.Build();
        var desktopHost = new DesktopAppHost(application, baseAddress);

        application.UseDefaultFiles();
        application.UseStaticFiles();
        application.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        application.MapMqttRpcWebSocket("/mqtt");

        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        return desktopHost;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await _application.StopAsync().ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
