# MqttRpc

MqttRpc is a small MQTT 5 request/response framework for desktop applications
that host a web UI. The demo runs an ASP.NET Core static-file host, an embedded
MQTTnet broker, and WebView2 in the same WinForms process. The browser uses
MQTT.js over WebSocket and calls typed C# handlers as if they were functions.

## Run the demo

```powershell
dotnet run --project TestApp
```

The desktop host listens only on `127.0.0.1:5678`. WebView2 opens the local
site, and the page connects to `ws://127.0.0.1:5678/mqtt`.

The page includes three calls:

- `device/getStatus`
- `device/reboot`
- `system/echo`

The TypeScript client project bundles MQTT.js 5.15.2 into the static demo
artifact under `TestApp/wwwroot/vendor/mqttrpc-client`, so the desktop app does
not need a CDN or an internet connection at runtime.

## NuGet packages

| Package | Responsibility |
| --- | --- |
| `MqttRpc` | Protocol, contracts, typed router, MQTTnet responder, and .NET caller. |
| `MqttRpc.AspNetCore` | Optional WebSocket-only embedded broker and responder lifecycle adapter. |

`TestApp` remains a sample. Its WinForms, WebView2, routes, and `wwwroot` files
are not included in either NuGet package.

Create the preview packages:

```powershell
dotnet test MqttRpc.sln -c Release
dotnet pack MqttRpc/MqttRpc.csproj -c Release
dotnet pack MqttRpc.AspNetCore/MqttRpc.AspNetCore.csproj -c Release
```

Packages and symbols are written to `artifacts/packages`:

```text
MqttRpc.0.1.0-alpha.1.nupkg
MqttRpc.0.1.0-alpha.1.snupkg
MqttRpc.AspNetCore.0.1.0-alpha.1.nupkg
MqttRpc.AspNetCore.0.1.0-alpha.1.snupkg
```

Install that local build into another project:

```powershell
dotnet add package MqttRpc.AspNetCore `
  --version 0.1.0-alpha.1 `
  --source ./artifacts/packages
```

`MqttRpc.AspNetCore` brings in `MqttRpc` as a package dependency. Both packages
contain a `net8.0` assembly, XML documentation, package README, and their
MQTTnet dependencies. Their license is MIT.

## C# modules

| Module | Project | Responsibility |
| --- | --- | --- |
| `MqttRpcProtocol` | NuGet | Validates method names and constructs versioned request/reply topics. |
| `MqttRpcRouter` | NuGet | Maps a method to a typed handler and converts handler failures into stable RPC errors. |
| `MqttNetRpcResponder` | NuGet | Adapts MQTTnet server messages to router invocations and publishes correlated replies. |
| `MqttRpcClient` | NuGet | Provides typed .NET calls while hiding MQTT subscriptions, JSON, correlation and timeouts. |
| `MqttRpcAspNetCoreExtensions` | AspNetCore NuGet | Registers the embedded broker, maps its WebSocket endpoint, and owns responder disposal. |
| `DesktopAppHost` | TestApp only | Owns Kestrel URL, static files, health route, and start/stop lifecycle for the demo. |

MQTTnet is the transport adapter. The router can be tested without a broker,
and another transport adapter can invoke the same router later.

## Host with ASP.NET Core

```csharp
using MqttRpc.AspNetCore;

var router = DemoRpcRoutes.CreateRouter();
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMqttRpcWebSocketServer(router);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapMqttRpcWebSocket("/mqtt");

await app.RunAsync();
```

The adapter disables MQTTnet's default TCP endpoint. The application still
decides its Kestrel addresses, TLS, static files, health checks, authentication,
and desktop-window lifecycle.

## TypeScript client project

The reusable browser client is an independent npm package under
`packages/mqtt-rpc-client`. Its public interface is intentionally small:

- `connect()` owns the MQTT 5 connection and reply subscription;
- `call<TResponse, TParams>()` owns serialization, correlation, and timeout;
- `disconnect()` rejects pending calls and closes the transport;
- typed `status` and `response` events expose lifecycle information without
  leaking the internal pending-call map.

Install and verify it from the repository:

```powershell
cd packages/mqtt-rpc-client
pnpm install
pnpm run verify
pnpm run pack:check
```

`build` produces ESM, CommonJS, source maps, and TypeScript declarations under
`dist`. `build:demo` produces the self-contained browser bundle consumed by
`TestApp`. The GitHub Packages package name is
`@6greg9/mqtt-rpc-client`.

## Register C# handlers

```csharp
var router = new MqttRpcRouter();

router.Map<GetStatusRequest, DeviceStatusResponse>(
    "device/getStatus",
    async (request, cancellationToken) =>
        await devices.GetStatusAsync(request.DeviceId, cancellationToken));
```

A handler may throw `MqttRpcFaultException` to return a stable application
error. Unexpected exceptions are converted to `INTERNAL_ERROR` without leaking
server implementation details.

## Call from .NET

```csharp
await using var rpc = new MqttRpcClient(
    new MqttRpcClientOptions(new Uri("ws://127.0.0.1:5678/mqtt")));

await rpc.ConnectAsync(cancellationToken);

var status = await rpc.CallAsync<GetStatusRequest, DeviceStatusResponse>(
    "device/getStatus",
    new GetStatusRequest("device-a"),
    cancellationToken: cancellationToken);
```

## Call from MQTT.js

```ts
import { MqttRpcClient } from "@6greg9/mqtt-rpc-client";

interface GetStatusRequest {
  deviceId: string;
}

interface DeviceStatus {
  deviceId: string;
  status: string;
}

const rpc = new MqttRpcClient({
  url: "ws://127.0.0.1:5678/mqtt",
});

await rpc.connect();

const status = await rpc.call<DeviceStatus, GetStatusRequest>(
  "device/getStatus",
  { deviceId: "device-a" },
);
```

The source implementation is in `packages/mqtt-rpc-client/src`. `app.js` is
only the demo UI and imports the generated browser bundle.

## MqttRpc v1 message shape

Request topic:

```text
mqttrpc/v1/request/{method}
```

MQTT 5 request properties:

```text
responseTopic   = mqttrpc/v1/reply/{clientId}
correlationData = opaque bytes
contentType     = application/json
messageExpiry   = call timeout
```

Request payload:

```json
{
  "params": {
    "deviceId": "device-a"
  }
}
```

Success response:

```json
{
  "ok": true,
  "result": {
    "status": "online"
  },
  "error": null
}
```

Error response:

```json
{
  "ok": false,
  "result": null,
  "error": {
    "code": "METHOD_NOT_FOUND",
    "message": "RPC method is not registered.",
    "retryable": false,
    "details": null
  }
}
```

The responder copies `correlationData` into the response unchanged. Clients
subscribe once to their own reply topic and use the correlation bytes to
resolve concurrent calls.

## Why MQTTnet.Extensions.Rpc is not used here

`MQTTnet.Extensions.Rpc` is a useful .NET caller helper, but it does not supply
the C# responder/router needed by this browser-to-C# demo. Its per-call topic
strategy would also require the MQTT.js client to implement a second protocol.

If a future integration must call an existing MQTTnet RPC device, add an
adapter for that protocol instead of mixing both topic strategies into
`MqttRpcClient`.

## Tests

```powershell
dotnet test MqttRpc.sln

cd packages/mqtt-rpc-client
pnpm run verify
```

The test suite covers topic validation, typed routing, stable errors, and a
real MQTTnet broker request/response round trip with MQTT 5 correlation. The
TypeScript suite additionally verifies the package interface, MQTT 5 request
properties, correlation matching, remote errors, and timeouts.

## Before production use

This repository is still a local demo. Before allowing remote connections:

- add MQTT authentication and topic authorization;
- use WSS/TLS;
- define command idempotency because QoS 1 can redeliver;
- add payload size and concurrency limits;
- add reconnect policy and observability;
- decide how protocol versions remain backward compatible.
