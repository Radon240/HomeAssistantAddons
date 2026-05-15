import { useEffect, useState } from "react";
import { fetchJson } from "../api/client";

export type HomeAssistantStatusResponse = {
  integrationConfigured: boolean;
  usesSupervisorProxy: boolean;
  authSource: string;
  accessTokenConfigured: boolean;
  webSocketConnected: boolean;
  stateChangeEventsReceived: number;
  lastEventReceivedAtUtc: string | null;
  lastConnectedAtUtc: string | null;
  lastDisconnectAtUtc: string | null;
  lastError: string | null;
  recentStateChanges: HomeAssistantStateChangeDto[];
};

export type HomeAssistantStateChangeDto = {
  entityId: string;
  newState: string | null;
  oldState: string | null;
  friendlyName: string | null;
  timeFiredUtc: string;
  receivedAtUtc: string;
};

export async function fetchHomeAssistantStatus(): Promise<HomeAssistantStatusResponse> {
  return fetchJson<HomeAssistantStatusResponse>("/api/homeassistant/status");
}

export function HomeAssistantStatusCard() {
  const [data, setData] = useState<HomeAssistantStatusResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const tick = async () => {
      try {
        const next = await fetchHomeAssistantStatus();
        if (!cancelled) {
          setData(next);
          setError(null);
        }
      } catch (e) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : String(e));
        }
      }
    };

    void tick();
    const id = window.setInterval(() => void tick(), 2000);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, []);

  const connLabel = data?.webSocketConnected ? "подключён" : "не подключён";
  const connClass = data?.webSocketConnected ? "good" : data?.integrationConfigured ? "bad" : "muted";

  return (
    <section className="card" style={{ display: "grid", gap: 10 }}>
      <h3 style={{ margin: 0 }}>Home Assistant</h3>
      {error ? <div className="bad">Ошибка запроса: {error}</div> : null}
      {!data ? <div className="muted">Загрузка статуса…</div> : null}
      {data ? (
        <div className="row" style={{ gap: 8 }}>
          <div>
            <span className="muted">Интеграция: </span>
            <span className={data.integrationConfigured ? "good" : "muted"}>
              {data.integrationConfigured ? "настроена" : "не настроена"}
            </span>
          </div>
          <div>
            <span className="muted">WebSocket: </span>
            <span className={connClass}>{connLabel}</span>
          </div>
          <div>
            <span className="muted">Событий state_changed: </span>
            <span className="mono">{data.stateChangeEventsReceived}</span>
          </div>
          <div className="muted" style={{ fontSize: 13 }}>
            API: {data.usesSupervisorProxy ? "Supervisor (авто)" : "прямой URL"} · auth:{" "}
            {data.authSource}
          </div>
          {data.lastError ? (
            <div className="bad" style={{ fontSize: 13 }}>
              {data.lastError}
            </div>
          ) : null}
          {data.recentStateChanges.length > 0 ? (
            <details>
              <summary className="muted" style={{ cursor: "pointer" }}>
                Последние события ({data.recentStateChanges.length})
              </summary>
              <ul style={{ margin: "8px 0 0", paddingLeft: 18 }}>
                {data.recentStateChanges
                  .slice()
                  .reverse()
                  .slice(0, 8)
                  .map((e) => (
                    <li key={`${e.entityId}-${e.receivedAtUtc}`} style={{ marginBottom: 6 }}>
                      <span className="mono">{e.entityId}</span>
                      {e.friendlyName ? <span className="muted"> — {e.friendlyName}</span> : null}
                      <div className="muted" style={{ fontSize: 12 }}>
                        {e.newState ?? "—"}
                      </div>
                    </li>
                  ))}
              </ul>
            </details>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}
