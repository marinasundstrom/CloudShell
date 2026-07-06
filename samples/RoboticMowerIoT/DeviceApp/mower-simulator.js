import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import os from "node:os";

const mowerId = readEnv("MOWER_ID", "mower-001");
const mowerName = readEnv("MOWER_NAME", "Mower 001");
const parkName = readEnv("PARK_NAME", "North Meadow Park");
const backendUrl = readEnv("MOWER_BACKEND_URL", "http://localhost:7161");
const registryEndpoint = readEnv("CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT", "http://localhost:7160");
const registryResourceId = readEnv("CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID", "iot.device-registry:park-devices");
const enrollmentToken = readEnv("CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN", "local-development-mower-enrollment-token");
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
  directionLatitude: 1,
  directionLongitude: 1
};

const enrollment = await enrollMower();
const connection = new HubConnectionBuilder()
  .withUrl(`${backendUrl.replace(/\/$/, "")}/hubs/mowers`)
  .withAutomaticReconnect()
  .build();

connection.on("MowerCommand", command => {
  if (!command || command.mowerId !== mowerId) {
    return;
  }

  state.lastCommand = command.command;
  if (command.command === "start") {
    state.mode = "mowing";
    state.bladeEnabled = true;
  } else {
    state.mode = "stopped";
    state.bladeEnabled = false;
  }

  console.log(`Received ${command.command} from backend replica ${command.backendReplica}.`);
});

connection.onreconnecting(error => {
  console.log(`SignalR reconnecting: ${error?.message ?? "connection interrupted"}`);
});
connection.onreconnected(async () => {
  console.log("SignalR reconnected.");
  await registerMower();
});

await connection.start();
console.log(`Connected ${mowerId} to ${backendUrl}.`);
await registerMower();
await reportTelemetry();

setInterval(async () => {
  moveWithinPark();
  await reportTelemetry();
}, 2000);

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

async function registerMower() {
  if (connection.state !== HubConnectionState.Connected) {
    return;
  }

  await connection.invoke("RegisterMower", {
    mowerId,
    displayName: mowerName,
    parkName,
    deviceId: enrollment.deviceId,
    enrollmentStatus: enrollment.status ?? "enrolled"
  });
}

async function reportTelemetry() {
  if (connection.state !== HubConnectionState.Connected) {
    return;
  }

  await connection.invoke("ReportTelemetry", {
    mowerId,
    deviceId: enrollment.deviceId,
    enrollmentStatus: enrollment.status ?? "enrolled",
    mode: state.mode,
    bladeEnabled: state.bladeEnabled,
    latitude: state.latitude,
    longitude: state.longitude,
    heading: state.heading,
    batteryPercent: state.batteryPercent,
    lastCommand: state.lastCommand,
    timestamp: new Date().toISOString()
  });
}

function moveWithinPark() {
  if (state.bladeEnabled) {
    state.latitude += state.directionLatitude * 0.00012;
    state.longitude += state.directionLongitude * 0.00017;
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
  state.heading = state.directionLongitude > 0 ? 92 : 272;
}

function readEnv(name, fallback) {
  const value = process.env[name];
  return value && value.trim().length > 0 ? value.trim() : fallback;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}
