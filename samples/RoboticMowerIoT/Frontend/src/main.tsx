import { HubConnection, HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr";
import { StrictMode, useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

interface MowerSnapshot {
  mowerId: string;
  displayName: string;
  parkName: string;
  deviceId: string | null;
  enrollmentStatus: string;
  mode: string;
  bladeEnabled: boolean;
  latitude: number;
  longitude: number;
  heading: number;
  batteryPercent: number;
  lastCommand: string;
  lastUpdated: string;
  activeConnectionCount: number;
  backendReplica: string;
}

interface MowerCommand {
  mowerId: string;
  command: string;
  requestedBy: string;
  timestamp: string;
  backendReplica: string;
}

const hubUrl = import.meta.env.VITE_BACKEND_HUB_URL ?? "http://localhost:7161/hubs/mowers";
const parkName = import.meta.env.VITE_PARK_NAME ?? "North Meadow Park";
const parkBounds = {
  minLatitude: 40.7918,
  maxLatitude: 40.7942,
  minLongitude: -73.9629,
  maxLongitude: -73.9595
};

function App() {
  const [connection, setConnection] = useState<HubConnection | null>(null);
  const [connectionState, setConnectionState] = useState("Connecting");
  const [replica, setReplica] = useState("unknown");
  const [mowers, setMowers] = useState<Record<string, MowerSnapshot>>({});
  const [commands, setCommands] = useState<MowerCommand[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const nextConnection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    nextConnection.on("FleetSnapshot", (snapshots: MowerSnapshot[], backendReplica: string) => {
      setReplica(backendReplica);
      setMowers(Object.fromEntries(snapshots.map(snapshot => [snapshot.mowerId, snapshot])));
    });
    nextConnection.on("MowerRegistered", (snapshot: MowerSnapshot) => {
      setMowers(current => ({ ...current, [snapshot.mowerId]: snapshot }));
    });
    nextConnection.on("MowerTelemetry", (snapshot: MowerSnapshot) => {
      setReplica(snapshot.backendReplica);
      setMowers(current => ({ ...current, [snapshot.mowerId]: snapshot }));
    });
    nextConnection.on("MowerCommandIssued", (command: MowerCommand) => {
      setCommands(current => [command, ...current].slice(0, 8));
    });
    nextConnection.onreconnecting((error?: Error) => {
      setConnectionState("Reconnecting");
      setError(error?.message ?? null);
    });
    nextConnection.onreconnected(() => {
      setConnectionState("Connected");
      setError(null);
      void nextConnection.invoke("RequestFleetSnapshot");
    });
    nextConnection.onclose((error?: Error) => {
      setConnectionState("Closed");
      setError(error?.message ?? null);
    });

    setConnection(nextConnection);
    nextConnection.start()
      .then(async () => {
        setConnectionState("Connected");
        setError(null);
        await nextConnection.invoke("RequestFleetSnapshot");
      })
      .catch((exception: unknown) => {
        setConnectionState("Connection failed");
        setError(exception instanceof Error ? exception.message : String(exception));
      });

    return () => {
      void nextConnection.stop();
    };
  }, []);

  const mowerList = useMemo(
    () => Object.values(mowers).sort((left, right) => left.mowerId.localeCompare(right.mowerId)),
    [mowers]);
  const selectedMower = mowerList[0] ?? null;

  async function sendCommand(mowerId: string, command: "start" | "stop") {
    if (!connection || connection.state !== HubConnectionState.Connected) {
      return;
    }

    await connection.invoke("SetMowerCommand", mowerId, command, "park-operator");
  }

  return (
    <main className="app-shell">
      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">CloudShell IoT sample</p>
            <h1>{parkName}</h1>
          </div>
          <div className="connection">
            <span className={connectionState === "Connected" ? "indicator ok" : "indicator"} />
            <span>{connectionState}</span>
            <span>Replica {replica}</span>
          </div>
        </header>

        <section className="content-grid">
          <ParkMap mowers={mowerList} />

          <aside className="fleet-panel" aria-label="Mower fleet">
            <div className="panel-heading">
              <h2>Mowers</h2>
              <span>{mowerList.length}</span>
            </div>
            {error ? <p className="error">{error}</p> : null}
            <div className="mower-list">
              {mowerList.length === 0 ? (
                <p className="empty">Waiting for mower telemetry.</p>
              ) : mowerList.map(mower => (
                <article className="mower-card" key={mower.mowerId}>
                  <div>
                    <h3>{mower.displayName}</h3>
                    <span>{mower.enrollmentStatus}</span>
                  </div>
                  <dl>
                    <div>
                      <dt>Mode</dt>
                      <dd>{mower.mode}</dd>
                    </div>
                    <div>
                      <dt>Battery</dt>
                      <dd>{Math.round(mower.batteryPercent)}%</dd>
                    </div>
                    <div>
                      <dt>Blade</dt>
                      <dd>{mower.bladeEnabled ? "On" : "Off"}</dd>
                    </div>
                    <div>
                      <dt>Connections</dt>
                      <dd>{mower.activeConnectionCount}</dd>
                    </div>
                    <div>
                      <dt>Replica</dt>
                      <dd>{mower.backendReplica}</dd>
                    </div>
                  </dl>
                  <div className="actions">
                    <button type="button" onClick={() => void sendCommand(mower.mowerId, "start")}>
                      Start
                    </button>
                    <button type="button" onClick={() => void sendCommand(mower.mowerId, "stop")}>
                      Stop
                    </button>
                  </div>
                </article>
              ))}
            </div>
          </aside>
        </section>

        <section className="status-strip">
          <Metric label="Selected" value={selectedMower?.displayName ?? "None"} />
          <Metric label="Last command" value={selectedMower?.lastCommand ?? "None"} />
          <Metric label="Device id" value={selectedMower?.deviceId ?? "Not enrolled"} />
          <Metric
            label="Updated"
            value={selectedMower ? new Date(selectedMower.lastUpdated).toLocaleTimeString() : "Pending"}
          />
        </section>

        <section className="command-log" aria-label="Command log">
          <h2>Commands</h2>
          {commands.length === 0 ? (
            <p className="empty">No operator commands yet.</p>
          ) : commands.map(command => (
            <p key={`${command.mowerId}-${command.timestamp}`}>
              <strong>{command.command}</strong> sent to {command.mowerId} through replica {command.backendReplica}
            </p>
          ))}
        </section>
      </section>
    </main>
  );
}

function ParkMap({ mowers }: { mowers: MowerSnapshot[] }) {
  return (
    <section className="map-panel" aria-label="Park mower map">
      <svg viewBox="0 0 1000 640" role="img" aria-label="Fixed mowing area map">
        <defs>
          <pattern id="mowing-lines" width="48" height="48" patternUnits="userSpaceOnUse">
            <path d="M0 48 L48 0" stroke="#b7d2aa" strokeWidth="6" />
          </pattern>
        </defs>
        <rect x="0" y="0" width="1000" height="640" rx="24" fill="#dcefd5" />
        <path d="M70 85 H925 V545 H115 Q70 545 70 500 Z" fill="url(#mowing-lines)" />
        <path d="M70 85 H925 V545 H115 Q70 545 70 500 Z" fill="none" stroke="#557c4e" strokeWidth="8" />
        <path d="M165 165 C255 95 390 110 455 175 C545 265 650 230 815 175" fill="none" stroke="#8fbad0" strokeWidth="28" strokeLinecap="round" />
        <path d="M185 455 C300 385 455 435 585 375 C690 325 780 350 865 420" fill="none" stroke="#ffffff" strokeWidth="18" strokeLinecap="round" strokeDasharray="34 28" opacity="0.8" />
        <text x="90" y="60" className="map-label">North mowing zone</text>
        {mowers.map(mower => {
          const point = projectMower(mower);
          return (
            <g key={mower.mowerId} transform={`translate(${point.x} ${point.y}) rotate(${mower.heading})`}>
              <circle r="30" fill={mower.bladeEnabled ? "#2d7a51" : "#596277"} />
              <path d="M0 -42 L18 18 H-18 Z" fill="#ffffff" opacity="0.95" />
              <text x="42" y="8" className="mower-label" transform={`rotate(${-mower.heading})`}>
                {mower.displayName}
              </text>
            </g>
          );
        })}
      </svg>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function projectMower(mower: MowerSnapshot) {
  const xRatio = (mower.longitude - parkBounds.minLongitude) /
    (parkBounds.maxLongitude - parkBounds.minLongitude);
  const yRatio = 1 - ((mower.latitude - parkBounds.minLatitude) /
    (parkBounds.maxLatitude - parkBounds.minLatitude));
  return {
    x: 100 + Math.min(0.95, Math.max(0.05, xRatio)) * 820,
    y: 105 + Math.min(0.92, Math.max(0.08, yRatio)) * 430
  };
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
