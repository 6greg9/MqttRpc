import { Buffer } from "buffer";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const mqttMock = vi.hoisted(() => ({
  connect: vi.fn(),
}));

vi.mock("mqtt", () => ({
  default: {
    connect: mqttMock.connect,
  },
}));

import {
  MqttRpcClient,
  MqttRpcRemoteError,
  MqttRpcTimeoutError,
} from "../src/index.js";

type Listener = (...args: unknown[]) => void;

class FakeMqttClient {
  connected = false;
  readonly published: unknown[][] = [];
  readonly subscriptions: string[] = [];
  private readonly listeners = new Map<string, Set<Listener>>();

  on(event: string, listener: Listener): this {
    const eventListeners = this.listeners.get(event) ?? new Set<Listener>();
    eventListeners.add(listener);
    this.listeners.set(event, eventListeners);
    return this;
  }

  once(event: string, listener: Listener): this {
    const onceListener: Listener = (...args) => {
      this.listeners.get(event)?.delete(onceListener);
      listener(...args);
    };
    return this.on(event, onceListener);
  }

  emit(event: string, ...args: unknown[]): void {
    for (const listener of [...(this.listeners.get(event) ?? [])]) {
      listener(...args);
    }
  }

  subscribe(
    topic: string,
    _options: unknown,
    callback: (error?: Error) => void,
  ): void {
    this.subscriptions.push(topic);
    callback();
  }

  publish(
    ...args: [
      topic: string,
      payload: string,
      options: {
        properties: {
          correlationData: Buffer;
          responseTopic: string;
        };
      },
      callback: (error?: Error) => void,
    ]
  ): void {
    this.published.push(args);
    args[3]();
  }

  end(
    _force: boolean,
    _options: unknown,
    callback: (error?: Error) => void,
  ): void {
    this.connected = false;
    callback();
  }
}

describe("MqttRpcClient", () => {
  let transport: FakeMqttClient;
  let rpc: MqttRpcClient;

  beforeEach(async () => {
    transport = new FakeMqttClient();
    mqttMock.connect.mockReturnValue(transport);
    rpc = new MqttRpcClient({
      url: "ws://127.0.0.1:5678/mqtt",
      clientId: "web-test",
    });

    const connecting = rpc.connect();
    transport.connected = true;
    transport.emit("connect");
    await connecting;
  });

  afterEach(async () => {
    await rpc.disconnect();
    mqttMock.connect.mockReset();
  });

  it("publishes an MQTT 5 request and resolves its correlated response", async () => {
    const resultPromise = rpc.call<{ message: string }, { message: string }>(
      "system/echo",
      { message: "hello" },
    );

    expect(transport.subscriptions).toEqual(["mqttrpc/v1/reply/web-test"]);
    expect(transport.published).toHaveLength(1);

    const [topic, rawPayload, options] = transport.published[0] as [
      string,
      string,
      {
        properties: {
          correlationData: Buffer;
          responseTopic: string;
        };
      },
    ];
    expect(topic).toBe("mqttrpc/v1/request/system/echo");
    expect(JSON.parse(rawPayload)).toEqual({
      params: { message: "hello" },
    });
    expect(options.properties.responseTopic).toBe(rpc.replyTopic);

    transport.emit(
      "message",
      rpc.replyTopic,
      Buffer.from(
        JSON.stringify({
          ok: true,
          result: { message: "hello" },
          error: null,
        }),
      ),
      {
        properties: {
          correlationData: options.properties.correlationData,
        },
      },
    );

    await expect(resultPromise).resolves.toEqual({ message: "hello" });
  });

  it("rejects a correlated failure as MqttRpcRemoteError", async () => {
    const resultPromise = rpc.call("device/reboot", {});
    const options = transport.published[0]?.[2] as {
      properties: { correlationData: Buffer };
    };

    transport.emit(
      "message",
      rpc.replyTopic,
      Buffer.from(
        JSON.stringify({
          ok: false,
          result: null,
          error: {
            code: "DEVICE_BUSY",
            message: "Device is busy.",
            retryable: true,
          },
        }),
      ),
      {
        properties: {
          correlationData: options.properties.correlationData,
        },
      },
    );

    const error = await resultPromise.catch((reason: unknown) => reason);
    expect(error).toBeInstanceOf(MqttRpcRemoteError);
    expect(error).toMatchObject({
      name: "MqttRpcRemoteError",
      code: "DEVICE_BUSY",
      retryable: true,
    });
  });

  it("rejects calls that exceed their timeout", async () => {
    await expect(
      rpc.call("system/echo", {}, { timeoutMs: 5 }),
    ).rejects.toBeInstanceOf(MqttRpcTimeoutError);
  });
});
