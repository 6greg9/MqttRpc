# MqttRpc

MqttRpc provides MQTT 5 request/response calls for .NET and MQTT.js clients.
It uses MQTT 5 `Response Topic` and `Correlation Data` while hiding
subscriptions, envelopes, correlation, and timeouts behind typed calls.

This is a preview package. The `mqttrpc/v1` wire protocol is usable, but its
public interface may still change before version 1.0.

## Install

```bash
dotnet add package MqttRpc --prerelease
```

## Register responder routes

```csharp
using MqttRpc;

var router = new MqttRpcRouter();

router.Map<GetStatusRequest, DeviceStatus>(
    "device/getStatus",
    async (request, cancellationToken) =>
        await devices.GetStatusAsync(request.DeviceId, cancellationToken));

using var responder = new MqttNetRpcResponder(mqttServer, router);
```

`mqttServer` is an MQTTnet `MqttServer` instance owned by the application.
MqttRpc does not start or stop the broker.

## Call from .NET

```csharp
await using var rpc = new MqttRpcClient(
    new MqttRpcClientOptions(
        new Uri("ws://127.0.0.1:5678/mqtt")));

await rpc.ConnectAsync(cancellationToken);

var status = await rpc.CallAsync<GetStatusRequest, DeviceStatus>(
    "device/getStatus",
    new GetStatusRequest("device-a"),
    cancellationToken: cancellationToken);
```

## Error handling

A responder handler can throw `MqttRpcFaultException` to return a stable
application error. A .NET caller receives it as `MqttRpcRemoteException`.
Malformed responses throw `MqttRpcProtocolException`; call timeouts throw
`TimeoutException`.

## Wire protocol

Requests use:

```text
topic           = mqttrpc/v1/request/{method}
responseTopic   = mqttrpc/v1/reply/{clientId}
correlationData = opaque bytes
contentType     = application/json
QoS             = 1
```

Request payload:

```json
{
  "params": {
    "deviceId": "device-a"
  }
}
```

Responses copy `correlationData` unchanged and return either:

```json
{
  "ok": true,
  "result": {
    "status": "online"
  },
  "error": null
}
```

or:

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

For the MQTT.js package and the desktop WebView demo, see the
[project repository](https://github.com/6greg9/MqttRpc).
