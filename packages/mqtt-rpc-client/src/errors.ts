import type { MqttRpcErrorPayload } from "./types.js";

export class MqttRpcRemoteError extends Error {
  readonly code: string;
  readonly retryable: boolean;
  readonly details: unknown;

  constructor(error?: Partial<MqttRpcErrorPayload> | null) {
    super(error?.message ?? "The remote RPC handler failed.");
    this.name = "MqttRpcRemoteError";
    this.code = error?.code ?? "INTERNAL_ERROR";
    this.retryable = error?.retryable ?? false;
    this.details = error?.details;
  }
}

export class MqttRpcTimeoutError extends Error {
  readonly method: string;
  readonly timeoutMs: number;

  constructor(method: string, timeoutMs: number) {
    super(`RPC ${method} timed out after ${timeoutMs} ms.`);
    this.name = "MqttRpcTimeoutError";
    this.method = method;
    this.timeoutMs = timeoutMs;
  }
}

export class MqttRpcConnectionError extends Error {
  constructor(message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = "MqttRpcConnectionError";
  }
}
