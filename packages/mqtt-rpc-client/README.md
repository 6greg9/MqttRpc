# @6greg9/mqtt-rpc-client

TypeScript client for the MqttRpc MQTT 5 request/response protocol. It hides
subscriptions, response topics, correlation data, JSON envelopes, reconnect
handling, and call timeouts behind a small function-call interface.

## Install

Configure the GitHub Packages registry and provide a classic GitHub PAT with
`read:packages` through `NODE_AUTH_TOKEN`:

```ini
@6greg9:registry=https://npm.pkg.github.com
//npm.pkg.github.com/:_authToken=${NODE_AUTH_TOKEN}
```

```bash
pnpm add @6greg9/mqtt-rpc-client
```

The package depends on MQTT.js and works with browser bundlers such as Vite,
Webpack, Rollup, and esbuild.

## Use

```ts
import {
  MqttRpcClient,
  MqttRpcRemoteError,
  type MqttRpcStatusDetail,
} from "@6greg9/mqtt-rpc-client";

interface GetStatusRequest {
  deviceId: string;
}

interface DeviceStatus {
  deviceId: string;
  status: "online" | "offline";
}

const rpc = new MqttRpcClient({
  url: "ws://127.0.0.1:5678/mqtt",
});

rpc.addEventListener("status", (event) => {
  const status: MqttRpcStatusDetail = event.detail;
  console.log(status.state, status.message);
});

await rpc.connect();

try {
  const status = await rpc.call<DeviceStatus, GetStatusRequest>(
    "device/getStatus",
    { deviceId: "device-a" },
  );
  console.log(status);
} catch (error) {
  if (error instanceof MqttRpcRemoteError) {
    console.error(error.code, error.message, error.retryable);
  }
}
```

`mqttOptions` forwards authentication, TLS, keepalive, and reconnect settings
to MQTT.js. The client always enforces MQTT protocol version 5 and owns its
client ID because those values are part of the MqttRpc contract.

## Develop

```bash
pnpm install
pnpm run verify
pnpm run pack:check
```

`build` creates ESM, CommonJS, source maps, and TypeScript declarations under
`dist`. `build:demo` creates the self-contained browser bundle consumed by the
repository's `TestApp`.
