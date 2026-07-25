import { describe, expect, it } from "vitest";

import {
  normalizeMethod,
  normalizeTopicRoot,
  validateClientId,
} from "../src/protocol.js";

describe("MqttRpc protocol validation", () => {
  it("normalizes valid method and topic roots", () => {
    expect(normalizeMethod("/device/getStatus/")).toBe("device/getStatus");
    expect(normalizeTopicRoot("/mqttrpc/v1/")).toBe("mqttrpc/v1");
  });

  it.each([
    "device/+",
    "device/#",
    "device status",
    "device//status",
    "設備/status",
  ])("rejects invalid method %s", (method) => {
    expect(() => normalizeMethod(method)).toThrow(TypeError);
  });

  it("keeps client IDs safe as a single MQTT topic segment", () => {
    expect(validateClientId("web-client_1")).toBe("web-client_1");
    expect(() => validateClientId("web/client")).toThrow(TypeError);
  });
});
