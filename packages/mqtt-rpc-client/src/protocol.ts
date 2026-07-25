export const DEFAULT_TOPIC_ROOT = "mqttrpc/v1";

const METHOD_PATTERN = /^[A-Za-z0-9._-]+(?:\/[A-Za-z0-9._-]+)*$/;
const SEGMENT_PATTERN = /^[A-Za-z0-9._-]+$/;

export function normalizeMethod(method: string): string {
  const normalized = method.replace(/^\/+|\/+$/g, "");

  if (
    normalized.length === 0 ||
    normalized.length > 160 ||
    !METHOD_PATTERN.test(normalized) ||
    normalized.split("/").some((segment) => segment.length > 64)
  ) {
    throw new TypeError(
      "RPC method segments may only contain ASCII letters, numbers, '.', '_' and '-'.",
    );
  }

  return normalized;
}

export function normalizeTopicRoot(topicRoot: string): string {
  const normalized = topicRoot.replace(/^\/+|\/+$/g, "");
  const segments = normalized.split("/");

  if (
    normalized.length === 0 ||
    segments.some(
      (segment) =>
        segment.length === 0 ||
        segment.length > 64 ||
        !SEGMENT_PATTERN.test(segment),
    )
  ) {
    throw new TypeError(
      "Topic root segments may only contain ASCII letters, numbers, '.', '_' and '-'.",
    );
  }

  return normalized;
}

export function validateClientId(clientId: string): string {
  if (
    clientId.length === 0 ||
    clientId.length > 64 ||
    !SEGMENT_PATTERN.test(clientId)
  ) {
    throw new TypeError(
      "clientId may only contain up to 64 ASCII letters, numbers, '.', '_' and '-'.",
    );
  }

  return clientId;
}
