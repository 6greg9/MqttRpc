import { describe, expect, it } from "vitest";

import {
  MqttRpcConnectionError,
  MqttRpcRemoteError,
  MqttRpcTimeoutError,
} from "../src/errors.js";

describe("MqttRpc errors", () => {
  it("preserves the stable remote error contract", () => {
    const error = new MqttRpcRemoteError({
      code: "DEVICE_BUSY",
      message: "Device is busy.",
      retryable: true,
      details: { retryAfterMs: 500 },
    });

    expect(error).toMatchObject({
      name: "MqttRpcRemoteError",
      code: "DEVICE_BUSY",
      message: "Device is busy.",
      retryable: true,
      details: { retryAfterMs: 500 },
    });
  });

  it("provides distinguishable timeout and connection failures", () => {
    expect(new MqttRpcTimeoutError("system/echo", 50)).toMatchObject({
      name: "MqttRpcTimeoutError",
      method: "system/echo",
      timeoutMs: 50,
    });
    expect(new MqttRpcConnectionError("offline").name).toBe(
      "MqttRpcConnectionError",
    );
  });
});
