import {
  MqttRpcClient,
  MqttRpcRemoteError,
} from "./vendor/mqttrpc-client/index.js";

const elements = {
  brokerUrl: document.querySelector("#broker-url"),
  connectButton: document.querySelector("#connect-button"),
  disconnectButton: document.querySelector("#disconnect-button"),
  statusDot: document.querySelector("#status-dot"),
  statusLabel: document.querySelector("#status-label"),
  clientId: document.querySelector("#client-id"),
  replyTopic: document.querySelector("#reply-topic"),
  lastLatency: document.querySelector("#last-latency"),
  deviceId: document.querySelector("#device-id"),
  rebootReason: document.querySelector("#reboot-reason"),
  statusButton: document.querySelector("#status-button"),
  rebootButton: document.querySelector("#reboot-button"),
  typedResult: document.querySelector("#typed-result"),
  typedResultState: document.querySelector("#typed-result-state"),
  customMethod: document.querySelector("#custom-method"),
  customParams: document.querySelector("#custom-params"),
  customButton: document.querySelector("#custom-button"),
  customResult: document.querySelector("#custom-result"),
  customResultState: document.querySelector("#custom-result-state"),
  eventLog: document.querySelector("#event-log"),
  clearLogButton: document.querySelector("#clear-log-button"),
  rpcButtons: [...document.querySelectorAll(".rpc-action")],
};

const websocketScheme = location.protocol === "https:" ? "wss:" : "ws:";
elements.brokerUrl.value = `${websocketScheme}//${location.host}/mqtt`;

let rpcClient = null;

function addLog(level, message) {
  const item = document.createElement("li");
  const timestamp = document.createElement("time");
  const badge = document.createElement("span");
  const text = document.createElement("span");

  timestamp.textContent = new Date().toLocaleTimeString([], {
    hour12: false,
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
  badge.className = `log-level ${level.toLowerCase()}`;
  badge.textContent = level.toUpperCase();
  text.className = "log-message";
  text.textContent = message;

  item.append(timestamp, badge, text);
  elements.eventLog.prepend(item);
  while (elements.eventLog.children.length > 80) {
    elements.eventLog.lastElementChild.remove();
  }
}

function setConnectionState(state, label) {
  const connected = state === "connected";
  elements.statusLabel.textContent = label;
  elements.statusDot.classList.toggle("connected", connected);
  elements.statusDot.classList.toggle("error", state === "error");
  elements.connectButton.disabled = connected || state === "connecting";
  elements.disconnectButton.disabled = !rpcClient;
  elements.rpcButtons.forEach((button) => {
    button.disabled = !connected;
  });
}

function setResult(target, stateTarget, state, value) {
  stateTarget.textContent = state.toUpperCase();
  stateTarget.classList.toggle("success", state === "success");
  stateTarget.classList.toggle("failure", state === "failure");
  target.textContent =
    typeof value === "string" ? value : JSON.stringify(value, null, 2);
}

function describeError(error) {
  if (error instanceof MqttRpcRemoteError) {
    return {
      type: error.name,
      code: error.code,
      message: error.message,
      retryable: error.retryable,
      details: error.details,
    };
  }

  return {
    type: error.name ?? "Error",
    message: error.message ?? String(error),
  };
}

async function connect() {
  if (rpcClient) {
    await rpcClient.disconnect();
  }

  rpcClient = new MqttRpcClient({
    url: elements.brokerUrl.value.trim(),
  });
  elements.clientId.textContent = rpcClient.clientId;
  elements.replyTopic.textContent = rpcClient.replyTopic;
  elements.disconnectButton.disabled = false;

  rpcClient.addEventListener("status", (event) => {
    const { state, message } = event.detail;
    setConnectionState(state, state === "connected" ? "已連線" : message);
    addLog(state === "error" ? "error" : "mqtt", message);
  });
  rpcClient.addEventListener("response", (event) => {
    const { method, latencyMs, ok } = event.detail;
    elements.lastLatency.textContent = `${latencyMs.toFixed(1)} ms`;
    addLog("rpc", `${method} → ${ok ? "OK" : "ERROR"} (${latencyMs.toFixed(1)} ms)`);
  });

  try {
    await rpcClient.connect();
  } catch (error) {
    setConnectionState("error", "連線失敗");
    addLog("error", error.message);
  }
}

async function disconnect() {
  if (!rpcClient) {
    return;
  }

  await rpcClient.disconnect();
  rpcClient = null;
  elements.clientId.textContent = "—";
  elements.replyTopic.textContent = "—";
  setConnectionState("closed", "尚未連線");
  elements.disconnectButton.disabled = true;
}

async function callTyped(method, params) {
  setResult(elements.typedResult, elements.typedResultState, "calling", {
    method,
    params,
  });
  addLog("rpc", `Calling ${method}`);

  try {
    const result = await rpcClient.call(method, params);
    setResult(elements.typedResult, elements.typedResultState, "success", result);
  } catch (error) {
    const describedError = describeError(error);
    setResult(
      elements.typedResult,
      elements.typedResultState,
      "failure",
      describedError,
    );
    addLog("error", `${method}: ${describedError.message}`);
  }
}

elements.connectButton.addEventListener("click", connect);
elements.disconnectButton.addEventListener("click", disconnect);
elements.statusButton.addEventListener("click", () => {
  callTyped("device/getStatus", {
    deviceId: elements.deviceId.value.trim(),
  });
});
elements.rebootButton.addEventListener("click", () => {
  callTyped("device/reboot", {
    reason: elements.rebootReason.value.trim(),
  });
});
elements.customButton.addEventListener("click", async () => {
  let params;
  try {
    params = JSON.parse(elements.customParams.value);
  } catch (error) {
    setResult(
      elements.customResult,
      elements.customResultState,
      "failure",
      `Params JSON error: ${error.message}`,
    );
    return;
  }

  const method = elements.customMethod.value.trim();
  setResult(elements.customResult, elements.customResultState, "calling", {
    method,
    params,
  });
  addLog("rpc", `Calling ${method}`);

  try {
    const result = await rpcClient.call(method, params);
    setResult(elements.customResult, elements.customResultState, "success", result);
  } catch (error) {
    const describedError = describeError(error);
    setResult(
      elements.customResult,
      elements.customResultState,
      "failure",
      describedError,
    );
    addLog("error", `${method}: ${describedError.message}`);
  }
});
elements.clearLogButton.addEventListener("click", () => {
  elements.eventLog.replaceChildren();
});

addLog("ui", "MqttRpc Lab loaded");
setConnectionState("closed", "尚未連線");
connect();
