import mqtt from "mqtt";
import os from "node:os";

const mowerId = readEnv("MOWER_ID", "mower-001");
const mowerName = readEnv("MOWER_NAME", "Mower 001");
const parkName = readEnv("PARK_NAME", "North Meadow Park");
const registryEndpoint = readEnv("CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT", "http://localhost:7160");
const registryMqttEndpoint = readEnv("CLOUDSHELL_DEVICE_REGISTRY_MQTT_ENDPOINT", "mqtt://localhost:7163");
const registryResourceId = readEnv("CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID", "iot.device-registry:park-devices");
const enrollmentToken = readEnv("CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN", "local-development-mower-enrollment-token");
const syncIntervalMilliseconds = Number.parseInt(readEnv("MOWER_SYNC_INTERVAL_MS", "2000"), 10);
const parkBounds = {
  minLatitude: 40.7918,
  maxLatitude: 40.7942,
  minLongitude: -73.9629,
  maxLongitude: -73.9595
};

const state = {
  latitude: 40.7933,
  longitude: -73.9617,
  heading: 92,
  batteryPercent: 96,
  mode: "idle",
  bladeEnabled: false,
  lastCommand: "none",
  lastCommandId: "none",
  mowingPattern: "lanes",
  patternChangeId: "none",
  desiredVersion: 0,
  spiralStep: 0,
  directionLatitude: 1,
  directionLongitude: 1
};

const enrollment = await enrollMower();
const client = mqtt.connect(registryMqttEndpoint, {
  protocolVersion: 5,
  clientId: createMqttClientId(enrollment.deviceId),
  username: enrollment.clientId,
  password: enrollment.clientSecret,
  clean: true,
  reconnectPeriod: 1500
});

client.on("connect", () => {
  console.log(`Connected ${mowerId} to Device Registry MQTT at ${registryMqttEndpoint}.`);
  void syncState();
});
client.on("reconnect", () => {
  console.log("Reconnecting to Device Registry MQTT.");
});
client.on("error", error => {
  console.error(`Device Registry MQTT error: ${error.message}`);
});

setInterval(async () => {
  moveWithinPark();
  await syncState();
}, Number.isFinite(syncIntervalMilliseconds) && syncIntervalMilliseconds > 0
  ? syncIntervalMilliseconds
  : 2000);

async function enrollMower() {
  const subject = `mower/${mowerId}`;
  const response = await fetch(
    `${registryEndpoint.replace(/\/$/, "")}/api/devices/registries/${encodeURIComponent(registryResourceId)}/enroll`,
    {
      method: "POST",
      headers: {
        "content-type": "application/json"
      },
      body: JSON.stringify({
        subject,
        enrollmentToken,
        claims: {
          manufacturer: "cloudshell",
          deviceType: "robotic-mower",
          model: "park-mower-sim"
        },
        properties: {
          mowerId,
          mowerName,
          parkName,
          deviceType: "robotic-mower",
          platform: os.platform(),
          machineName: os.hostname(),
          runtime: `node ${process.version}`
        }
      })
    });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`Device enrollment failed with ${response.status}: ${body}`);
  }

  const value = await response.json();
  console.log(`Enrolled ${mowerId} as device ${value.deviceId}.`);
  return value;
}

async function syncState() {
  if (!client.connected) {
    return;
  }

  const responseTopic = buildResponseTopic();
  try {
    const response = await requestMqttSync(responseTopic);
    state.desiredVersion = response.desired.version ?? state.desiredVersion;
    applyDesiredState(response.desired.state ?? {});
    console.log(
      `Synced ${mowerId}: ${state.mode}, ${state.latitude.toFixed(5)}, ${state.longitude.toFixed(5)}, desired v${state.desiredVersion}.`);
  } catch (error) {
    console.error(`Device Registry MQTT sync failed: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function requestMqttSync(responseTopic) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      client.unsubscribe(responseTopic);
      reject(new Error("Timed out waiting for Device Registry MQTT sync response."));
    }, 10000);

    const onMessage = (topic, payload) => {
      if (topic !== responseTopic) {
        return;
      }

      clearTimeout(timeout);
      client.unsubscribe(responseTopic);
      client.off("message", onMessage);
      try {
        resolve(JSON.parse(payload.toString("utf8")));
      } catch (error) {
        reject(error);
      }
    };

    client.on("message", onMessage);
    client.subscribe(responseTopic, { qos: 1 }, subscribeError => {
      if (subscribeError) {
        clearTimeout(timeout);
        client.off("message", onMessage);
        reject(subscribeError);
        return;
      }

      client.publish(
        buildSyncTopic(),
        JSON.stringify({
          reportedState: createReportedState(),
          properties: {
            "sample.sync": "robotic-mower-device-app",
            "sample.transport": "mqtt"
          },
          source: "robotic-mower-simulator",
          lastKnownDesiredVersion: state.desiredVersion
        }),
        {
          qos: 1,
          properties: {
            responseTopic
          }
        },
        publishError => {
          if (publishError) {
            clearTimeout(timeout);
            client.unsubscribe(responseTopic);
            client.off("message", onMessage);
            reject(publishError);
          }
        });
    });
  });
}

function applyDesiredState(desired) {
  const command = typeof desired.command === "string" ? desired.command : null;
  const commandId = typeof desired.commandId === "string" ? desired.commandId : command ?? "none";
  applyDesiredPattern(desired);
  if (!command || commandId === state.lastCommandId) {
    return;
  }

  state.lastCommand = command;
  state.lastCommandId = commandId;
  if (command === "start") {
    state.mode = "mowing";
    state.bladeEnabled = true;
  } else if (command === "stop") {
    state.mode = "stopped";
    state.bladeEnabled = false;
  }

  console.log(`Applied desired command ${command} (${commandId}).`);
}

function applyDesiredPattern(desired) {
  const pattern = normalizePattern(desired.pattern);
  const patternChangeId = typeof desired.patternChangeId === "string"
    ? desired.patternChangeId
    : `${pattern}:${state.desiredVersion}`;
  if (pattern === state.mowingPattern && patternChangeId === state.patternChangeId) {
    return;
  }

  state.mowingPattern = pattern;
  state.patternChangeId = patternChangeId;
  state.spiralStep = 0;
  console.log(`Applied desired mowing pattern ${pattern} (${patternChangeId}).`);
}

function createReportedState() {
  return {
    mowerId,
    displayName: mowerName,
    parkName,
    mode: state.mode,
    bladeEnabled: state.bladeEnabled,
    latitude: state.latitude,
    longitude: state.longitude,
    heading: state.heading,
    batteryPercent: state.batteryPercent,
    lastCommand: state.lastCommand,
    lastCommandId: state.lastCommandId,
    mowingPattern: state.mowingPattern,
    patternChangeId: state.patternChangeId,
    reportedAt: new Date().toISOString()
  };
}

function moveWithinPark() {
  if (state.bladeEnabled) {
    moveByPattern();
    state.batteryPercent = Math.max(5, state.batteryPercent - 0.08);
  } else {
    state.batteryPercent = Math.min(100, state.batteryPercent + 0.03);
  }

  if (state.latitude > parkBounds.maxLatitude || state.latitude < parkBounds.minLatitude) {
    state.directionLatitude *= -1;
  }

  if (state.longitude > parkBounds.maxLongitude || state.longitude < parkBounds.minLongitude) {
    state.directionLongitude *= -1;
  }

  state.latitude = clamp(state.latitude, parkBounds.minLatitude, parkBounds.maxLatitude);
  state.longitude = clamp(state.longitude, parkBounds.minLongitude, parkBounds.maxLongitude);
  if (state.mowingPattern === "lanes") {
    state.heading = state.directionLongitude > 0 ? 92 : 272;
  }
}

function moveByPattern() {
  if (state.mowingPattern === "spiral") {
    state.spiralStep += 1;
    const radius = Math.min(0.00055, 0.000035 * state.spiralStep);
    const angle = state.spiralStep * 0.85;
    const centerLatitude = 40.7933;
    const centerLongitude = -73.9617;
    state.latitude = centerLatitude + Math.sin(angle) * radius;
    state.longitude = centerLongitude + Math.cos(angle) * radius * 1.35;
    state.heading = (angle * 180 / Math.PI + 90) % 360;
    return;
  }

  if (state.mowingPattern === "wander") {
    if (Math.random() > 0.72) {
      state.directionLatitude = Math.random() > 0.5 ? 1 : -1;
      state.directionLongitude = Math.random() > 0.5 ? 1 : -1;
    }

    state.latitude += state.directionLatitude * (0.00008 + Math.random() * 0.00006);
    state.longitude += state.directionLongitude * (0.00008 + Math.random() * 0.00008);
    state.heading = Math.atan2(state.directionLongitude, state.directionLatitude) * 180 / Math.PI;
    return;
  }

  state.latitude += state.directionLatitude * 0.00012;
  state.longitude += state.directionLongitude * 0.00017;
}

function buildSyncTopic() {
  return [
    "cloudshell",
    "device-registries",
    encodeURIComponent(registryResourceId),
    "devices",
    encodeURIComponent(enrollment.deviceId),
    "sync"
  ].join("/");
}

function buildResponseTopic() {
  return [
    "cloudshell",
    "device-registries",
    encodeURIComponent(registryResourceId),
    "devices",
    encodeURIComponent(enrollment.deviceId),
    "responses",
    `${Date.now()}-${Math.random().toString(16).slice(2)}`
  ].join("/");
}

function createMqttClientId(deviceId) {
  const normalized = deviceId
    .replace(/[^a-z0-9]/gi, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase();
  return `cloudshell-mower-${normalized}`;
}

function readEnv(name, fallback) {
  const value = process.env[name];
  return value && value.trim().length > 0 ? value.trim() : fallback;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function normalizePattern(value) {
  switch (typeof value === "string" ? value.toLowerCase() : "lanes") {
    case "spiral":
      return "spiral";
    case "wander":
    case "random":
      return "wander";
    default:
      return "lanes";
  }
}
