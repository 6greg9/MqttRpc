import type { IClientOptions } from "mqtt";

export interface MqttRpcClientOptions {
  url: string;
  clientId?: string;
  topicRoot?: string;
  defaultTimeoutMs?: number;
  mqttOptions?: Omit<IClientOptions, "clientId" | "protocolVersion">;
}

export interface MqttRpcCallOptions {
  timeoutMs?: number;
}

export interface MqttRpcErrorPayload {
  code: string;
  message: string;
  retryable?: boolean;
  details?: unknown;
}

export type MqttRpcConnectionState =
  | "connecting"
  | "connected"
  | "offline"
  | "reconnecting"
  | "closed"
  | "warning"
  | "error";

export interface MqttRpcStatusDetail {
  state: MqttRpcConnectionState;
  message: string;
  error?: Error;
}

export interface MqttRpcResponseDetail {
  method: string;
  latencyMs: number;
  ok: boolean;
}

export interface MqttRpcClientEventMap {
  status: CustomEvent<MqttRpcStatusDetail>;
  response: CustomEvent<MqttRpcResponseDetail>;
}

export interface MqttRpcSuccessEnvelope<TResponse> {
  ok: true;
  result: TResponse;
  error: null;
}

export interface MqttRpcFailureEnvelope {
  ok: false;
  result: null;
  error: MqttRpcErrorPayload;
}

export type MqttRpcResponseEnvelope<TResponse> =
  | MqttRpcSuccessEnvelope<TResponse>
  | MqttRpcFailureEnvelope;
