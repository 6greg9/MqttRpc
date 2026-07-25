using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet.AspNetCore;
using MQTTnet.Server;

namespace MqttRpc.AspNetCore;

/// <summary>
/// Adds an embedded MQTT WebSocket broker and MqttRpc responder to ASP.NET Core.
/// </summary>
public static class MqttRpcAspNetCoreExtensions
{
    /// <summary>
    /// Registers a WebSocket-only MQTTnet broker and the MqttRpc responder lifecycle.
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="router">The application-owned router containing RPC handlers.</param>
    /// <param name="topicRoot">The protocol topic root handled by this responder.</param>
    /// <returns>The supplied service collection.</returns>
    /// <remarks>
    /// Call this once while building the application. This adapter deliberately disables
    /// MQTTnet's default TCP endpoint; the application exposes MQTT through
    /// <see cref="MapMqttRpcWebSocket(WebApplication, string)"/>.
    /// </remarks>
    public static IServiceCollection AddMqttRpcWebSocketServer(
        this IServiceCollection services,
        MqttRpcRouter router,
        string topicRoot = MqttRpcProtocol.DefaultTopicRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(router);

        if (services.Any(
            descriptor => descriptor.ServiceType == typeof(MqttRpcServerAttachment)))
        {
            throw new InvalidOperationException(
                "AddMqttRpcWebSocketServer may only be called once per service collection.");
        }

        var normalizedTopicRoot = MqttRpcProtocol.NormalizeTopicRoot(topicRoot);
        services.AddSingleton(
            _ => new MqttRpcServerAttachment(router, normalizedTopicRoot));
        services
            .AddHostedMqttServer(options => options.WithoutDefaultEndpoint())
            .AddMqttConnectionHandler()
            .AddConnections();

        return services;
    }

    /// <summary>
    /// Maps the MQTT WebSocket endpoint and attaches the registered MqttRpc responder.
    /// </summary>
    /// <param name="application">The ASP.NET Core application.</param>
    /// <param name="pattern">The absolute endpoint path, such as <c>/mqtt</c>.</param>
    /// <returns>An endpoint builder that can be used to add ASP.NET Core conventions.</returns>
    /// <remarks>
    /// Call this once after <see cref="AddMqttRpcWebSocketServer"/> and before starting
    /// the application.
    /// </remarks>
    public static IEndpointConventionBuilder MapMqttRpcWebSocket(
        this WebApplication application,
        string pattern = "/mqtt")
    {
        ArgumentNullException.ThrowIfNull(application);
        ValidatePattern(pattern);

        var attachment =
            application.Services.GetRequiredService<MqttRpcServerAttachment>();
        attachment.MarkMapped();

        var endpoint = application.MapConnectionHandler<MqttConnectionHandler>(
            pattern,
            options =>
            {
                options.WebSockets.SubProtocolSelector = protocols =>
                    protocols.FirstOrDefault(
                        protocol => string.Equals(
                            protocol,
                            "mqtt",
                            StringComparison.OrdinalIgnoreCase))
                    ?? "mqtt";
            });

        application.UseMqttServer(attachment.Attach);
        return endpoint;
    }

    private static void ValidatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)
            || pattern[0] != '/'
            || pattern.Contains('?')
            || pattern.Contains('#'))
        {
            throw new ArgumentException(
                "The MQTT WebSocket pattern must be an absolute path such as '/mqtt'.",
                nameof(pattern));
        }
    }

    private sealed class MqttRpcServerAttachment : IDisposable
    {
        private readonly object _gate = new();
        private readonly MqttRpcRouter _router;
        private readonly string _topicRoot;
        private MqttNetRpcResponder? _responder;
        private MqttServer? _server;
        private bool _disposed;
        private bool _mapped;

        public MqttRpcServerAttachment(MqttRpcRouter router, string topicRoot)
        {
            _router = router;
            _topicRoot = topicRoot;
        }

        public void MarkMapped()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_mapped)
                {
                    throw new InvalidOperationException(
                        "MapMqttRpcWebSocket may only be called once per application.");
                }

                _mapped = true;
            }
        }

        public void Attach(MqttServer server)
        {
            ArgumentNullException.ThrowIfNull(server);

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (ReferenceEquals(_server, server))
                {
                    return;
                }

                if (_server is not null)
                {
                    throw new InvalidOperationException(
                        "The MqttRpc responder is already attached to another MQTT server.");
                }

                _server = server;
                _responder = new MqttNetRpcResponder(server, _router, _topicRoot);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _responder?.Dispose();
                _responder = null;
                _server = null;
            }
        }
    }
}
