import { useEffect, useState } from "react";
import { fetchMetrics, type MetricsResponse } from "../api/events";

export function MetricsCard() {
  const [data, setData] = useState<MetricsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const tick = async () => {
      try {
        const next = await fetchMetrics();
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
    const id = window.setInterval(() => void tick(), 3000);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, []);

  return (
    <section className="card" style={{ display: "grid", gap: 8 }}>
      <h3 style={{ margin: 0 }}>Метрики</h3>
      {error ? <div className="bad">{error}</div> : null}
      {!data ? <div className="muted">Загрузка…</div> : null}
      {data ? (
        <div className="mono" style={{ fontSize: 13, display: "grid", gap: 4 }}>
          <div>WebSocket: {data.webSocketConnected ? "on" : "off"}</div>
          <div>Сохранено в SQLite: {data.eventsPersisted}</div>
          <div>Отфильтровано (config): {data.eventsFiltered}</div>
          <div>Переподключений: {data.reconnectCount}</div>
          <div>
            Последнее событие:{" "}
            {data.lastEventAgeSeconds != null
              ? `${Math.round(data.lastEventAgeSeconds)} с назад`
              : "—"}
          </div>
        </div>
      ) : null}
    </section>
  );
}
