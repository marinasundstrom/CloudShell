import { StrictMode, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

interface BackendStatus {
  service: string;
  message: string;
  mode: string;
  settingsEndpoint: string | null;
  timestamp: string;
}

const backendUrl = import.meta.env.VITE_BACKEND_URL ?? "http://127.0.0.1:5185";

function App() {
  const [status, setStatus] = useState<BackendStatus | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();

    fetch(`${backendUrl.replace(/\/$/, "")}/api/status`, {
      signal: controller.signal
    })
      .then(async response => {
        if (!response.ok) {
          throw new Error(`Backend returned ${response.status}`);
        }

        return await response.json() as BackendStatus;
      })
      .then(value => {
        setStatus(value);
        setError(null);
      })
      .catch((exception: unknown) => {
        if (!controller.signal.aborted) {
          setError(exception instanceof Error ? exception.message : String(exception));
        }
      });

    return () => controller.abort();
  }, []);

  return (
    <main className="shell">
      <section className="hero">
        <p className="eyebrow">CloudShell sample</p>
        <h1>React TypeScript frontend with a backend resource</h1>
        <p className="summary">
          The TypeScript launcher declares this React app, a Node backend API,
          Configuration Store settings, and a load-balancer route.
        </p>
      </section>

      <section className="panel" aria-live="polite">
        <div>
          <span className={status ? "status status-ok" : "status"} />
          <span>{status ? "Backend connected" : "Waiting for backend"}</span>
        </div>

        {error ? (
          <p className="error">{error}</p>
        ) : (
          <dl>
            <div>
              <dt>Backend</dt>
              <dd>{status?.service ?? "Not connected"}</dd>
            </div>
            <div>
              <dt>Configuration</dt>
              <dd>{status?.message ?? "Pending"}</dd>
            </div>
            <div>
              <dt>Mode</dt>
              <dd>{status?.mode ?? "Pending"}</dd>
            </div>
            <div>
              <dt>Settings endpoint</dt>
              <dd>{status?.settingsEndpoint ?? "Not provided"}</dd>
            </div>
          </dl>
        )}
      </section>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
