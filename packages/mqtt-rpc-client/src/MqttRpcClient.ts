import { Buffer } from "buffer";
import mqtt from "mqtt";
import type { IPublishPacket, MqttClient } from "mqtt";

import {
  MqttRpcConnectionError,
  MqttRpcRemoteError,
  MqttRpcTimeoutError,
} from "./errors.js";
import {
  DEFAULT_TOPIC_ROOT,
  normalizeMethod,
  normalizeTopicRoot,
  validateClientId,
} from "./protocol.js";
import type {
  MqttRpcCallOptions,
  MqttRpcClientEventMap,
  MqttRpcClientOptions,
  MqttRpcResponseEnvelope,
  MqttRpcStatusDetail,
} from "./types.js";

interface PendingCall {
  method: string;
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
  startedAt: number;
}

const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();

export class MqttRpcClient extends EventTarget {
  readonly url: string;
  readonly clientId: string;
  readonly topicRoot: string;
  readonly defaultTimeoutMs: number;
  readonly replyTopic: string;

  private readonly mqttOptions: MqttRpcClientOptions["mqttOptions"];
  private readonly pending = new Map<string, PendingCall>();
  private client: MqttClient | null = null;
  private ready = false;
  private connectPromise: Promise<void> | null = null;

  constructor(options: MqttRpcClientOptions) {
    super();

    if (!/^wss?:\/\//.test(options.url)) {
      throw new TypeError("Broker URL must start with ws:// or wss://.");
    }

    const defaultTimeoutMs = options.defaultTimeoutMs ?? 5_000;
    validateTimeout(defaultTimeoutMs);

    this.url = options.url;
    this.clientId = validateClientId(
      options.clientId ?? `web-${crypto.randomUUID()}`,
    );
    this.topicRoot = normalizeTopicRoot(
      options.topicRoot ?? DEFAULT_TOPIC_ROOT,
    );
    this.defaultTimeoutMs = defaultTimeoutMs;
    this.replyTopic = `${this.topicRoot}/reply/${this.clientId}`;
    this.mqttOptions = options.mqttOptions;
  }

  get isConnected(): boolean {
    return this.ready && this.client?.connected === true;
  }

  override addEventListener<K extends keyof MqttRpcClientEventMap>(
    type: K,
    listener: (
      this: MqttRpcClient,
      event: MqttRpcClientEventMap[K],
    ) => unknown,
    options?: boolean | AddEventListenerOptions,
  ): void;
  override addEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | AddEventListenerOptions,
  ): void;
  override addEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | AddEventListenerOptions,
  ): void {
    super.addEventListener(type, callback, options);
  }

  override removeEventListener<K extends keyof MqttRpcClientEventMap>(
    type: K,
    listener: (
      this: MqttRpcClient,
      event: MqttRpcClientEventMap[K],
    ) => unknown,
    options?: boolean | EventListenerOptions,
  ): void;
  override removeEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | EventListenerOptions,
  ): void;
  override removeEventListener(
    type: string,
    callback: EventListenerOrEventListenerObject | null,
    options?: boolean | EventListenerOptions,
  ): void {
    super.removeEventListener(type, callback, options);
  }

  async connect(): Promise<void> {
    if (this.isConnected) {
      return;
    }
    if (this.connectPromise) {
      return this.connectPromise;
    }

    this.emitStatus("connecting", `Connecting to ${this.url}`);

    const client = mqtt.connect(this.url, {
      ...this.mqttOptions,
      protocolVersion: 5,
      clientId: this.clientId,
      clean: this.mqttOptions?.clean ?? true,
      reconnectPeriod: this.mqttOptions?.reconnectPeriod ?? 1_200,
      connectTimeout: this.mqttOptions?.connectTimeout ?? 5_000,
      keepalive: this.mqttOptions?.keepalive ?? 30,
    });
    this.client = client;
    this.bindClientEvents(client);

    this.connectPromise = new Promise<void>((resolve, reject) => {
      let initialConnectionSettled = false;

      const failInitialConnection = (error: Error) => {
        if (initialConnectionSettled) {
          return;
        }

        initialConnectionSettled = true;
        reject(
          new MqttRpcConnectionError(
            `Could not connect the MqttRpc client: ${error.message}`,
            { cause: error },
          ),
        );
      };

      client.once("error", failInitialConnection);
      client.on("connect", () => {
        client.subscribe(this.replyTopic, { qos: 1 }, (error) => {
          if (error) {
            this.ready = false;
            this.emitStatus(
              "error",
              `Reply subscription failed: ${error.message}`,
              error,
            );
            failInitialConnection(error);
            return;
          }

          this.ready = true;
          this.emitStatus("connected", `Subscribed to ${this.replyTopic}`);
          if (!initialConnectionSettled) {
            initialConnectionSettled = true;
            resolve();
          }
        });
      });
    }).finally(() => {
      this.connectPromise = null;
    });

    return this.connectPromise;
  }

  async disconnect(): Promise<void> {
    const client = this.client;
    if (!client) {
      return;
    }

    this.rejectPending(
      new MqttRpcConnectionError("MqttRpc client disconnected."),
    );
    this.ready = false;
    this.client = null;

    await new Promise<void>((resolve, reject) => {
      client.end(false, {}, (error) => {
        if (error) {
          reject(error);
          return;
        }
        resolve();
      });
    });
    this.emitStatus("closed", "Disconnected by user");
  }

  call<TResponse = unknown, TParams = unknown>(
    method: string,
    params: TParams,
    options: MqttRpcCallOptions = {},
  ): Promise<TResponse> {
    const client = this.client;
    if (!this.ready || !client?.connected) {
      return Promise.reject(
        new MqttRpcConnectionError(
          "Connect the MqttRpc client before making a call.",
        ),
      );
    }

    let normalizedMethod: string;
    try {
      normalizedMethod = normalizeMethod(method);
    } catch (error) {
      return Promise.reject(error);
    }

    const timeoutMs = options.timeoutMs ?? this.defaultTimeoutMs;
    try {
      validateTimeout(timeoutMs);
    } catch (error) {
      return Promise.reject(error);
    }

    const correlationId = crypto.randomUUID();
    const requestTopic = `${this.topicRoot}/request/${normalizedMethod}`;
    const payload = JSON.stringify({ params });

    return new Promise<TResponse>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(correlationId);
        reject(new MqttRpcTimeoutError(normalizedMethod, timeoutMs));
      }, timeoutMs);

      this.pending.set(correlationId, {
        method: normalizedMethod,
        resolve: (value) => resolve(value as TResponse),
        reject,
        timer,
        startedAt: performance.now(),
      });

      client.publish(
        requestTopic,
        payload,
        {
          qos: 1,
          retain: false,
          properties: {
            responseTopic: this.replyTopic,
            correlationData: Buffer.from(textEncoder.encode(correlationId)),
            contentType: "application/json",
            payloadFormatIndicator: true,
            messageExpiryInterval: Math.max(1, Math.ceil(timeoutMs / 1_000)),
          },
        },
        (error) => {
          if (!error) {
            return;
          }

          const pendingCall = this.takePending(correlationId);
          pendingCall?.reject(error);
        },
      );
    });
  }

  private bindClientEvents(client: MqttClient): void {
    client.on("message", (topic, payload, packet) => {
      this.handleMessage(topic, payload, packet);
    });
    client.on("offline", () => {
      this.ready = false;
      this.emitStatus("offline", "Broker connection is offline");
    });
    client.on("reconnect", () => {
      this.emitStatus("reconnecting", "Reconnecting to broker");
    });
    client.on("close", () => {
      this.ready = false;
      this.emitStatus("closed", "Broker connection closed");
    });
    client.on("error", (error) => {
      this.emitStatus("error", error.message, error);
    });
  }

  private handleMessage(
    topic: string,
    payload: Uint8Array,
    packet: IPublishPacket,
  ): void {
    if (topic !== this.replyTopic) {
      return;
    }

    const correlationData = packet.properties?.correlationData;
    if (!correlationData) {
      this.emitStatus("warning", "Ignored response without correlationData");
      return;
    }

    const correlationId = textDecoder.decode(correlationData);
    const pendingCall = this.takePending(correlationId);
    if (!pendingCall) {
      this.emitStatus(
        "warning",
        `Ignored late or unknown response ${correlationId}`,
      );
      return;
    }

    let response: MqttRpcResponseEnvelope<unknown>;
    try {
      response = JSON.parse(textDecoder.decode(payload)) as MqttRpcResponseEnvelope<unknown>;
    } catch (error) {
      pendingCall.reject(
        new TypeError("RPC response is not valid JSON.", { cause: error }),
      );
      return;
    }

    const latencyMs = performance.now() - pendingCall.startedAt;
    this.dispatchEvent(
      new CustomEvent("response", {
        detail: {
          method: pendingCall.method,
          latencyMs,
          ok: response.ok === true,
        },
      }),
    );

    if (response.ok === true) {
      pendingCall.resolve(response.result);
      return;
    }

    pendingCall.reject(new MqttRpcRemoteError(response.error));
  }

  private takePending(correlationId: string): PendingCall | undefined {
    const pendingCall = this.pending.get(correlationId);
    if (pendingCall) {
      clearTimeout(pendingCall.timer);
      this.pending.delete(correlationId);
    }
    return pendingCall;
  }

  private rejectPending(error: Error): void {
    for (const pendingCall of this.pending.values()) {
      clearTimeout(pendingCall.timer);
      pendingCall.reject(error);
    }
    this.pending.clear();
  }

  private emitStatus(
    state: MqttRpcStatusDetail["state"],
    message: string,
    error?: Error,
  ): void {
    const detail: MqttRpcStatusDetail =
      error === undefined ? { state, message } : { state, message, error };
    this.dispatchEvent(new CustomEvent("status", { detail }));
  }
}

function validateTimeout(timeoutMs: number): void {
  if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) {
    throw new RangeError("timeoutMs must be greater than zero.");
  }
}
