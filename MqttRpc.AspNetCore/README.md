# MqttRpc.AspNetCore

ASP.NET Core adapter for the MqttRpc MQTT 5 request/response framework. It
registers a WebSocket-only embedded MQTTnet broker, maps the MQTT connection
endpoint, attaches the responder, and disposes it with the application.

This is a preview package. Its public interface may still change before
version 1.0.

## Install

```bash
dotnet add package MqttRpc.AspNetCore --prerelease
```

## Use

```csharp
using MqttRpc;
using MqttRpc.AspNetCore;

var router = new MqttRpcRouter();
router.Map<EchoRequest, EchoResponse>(
    "system/echo",
    (request, _) => Task.FromResult(new EchoResponse(request.Message)));

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMqttRpcWebSocketServer(router);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMqttRpcWebSocket("/mqtt");

await app.RunAsync();
```

The application still owns:

- Kestrel URLs, ports, and TLS;
- static files and fallback routing;
- health endpoints;
- WebView or desktop-window lifecycle;
- network exposure and access control.

`AddMqttRpcWebSocketServer` disables MQTTnet's default TCP endpoint, so the
embedded broker is reachable only through the mapped ASP.NET Core WebSocket
endpoint.

## Local desktop safety

For a desktop WebView application, bind Kestrel to loopback:

```csharp
builder.WebHost.UseUrls("http://127.0.0.1:5678");
```

Do not expose the endpoint to a remote network without adding TLS,
authentication, topic authorization, payload limits, and concurrency limits.

For protocol details, typed client usage, and the MQTT.js package, see the
[project repository](https://github.com/6greg9/MqttRpc).
