import { useCallback, useEffect, useState } from "react";
import {
  fetchEntities,
  fetchEvents,
  type HomeAssistantEntityDto,
  type StateChangeEventDto
} from "../api/events";
import { HomeAssistantStatusCard } from "../components/HomeAssistantStatusCard";

export function EventsPage() {
  const [entityFilter, setEntityFilter] = useState("");
  const [events, setEvents] = useState<StateChangeEventDto[]>([]);
  const [entities, setEntities] = useState<HomeAssistantEntityDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadEntities = useCallback(async () => {
    try {
      const data = await fetchEntities();
      setEntities(data.items);
    } catch {
      setEntities([]);
    }
  }, []);

  const loadEvents = useCallback(async () => {
    setLoading(true);
    try {
      const data = await fetchEvents(100, entityFilter || undefined);
      setEvents(data.items);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, [entityFilter]);

  useEffect(() => {
    void loadEntities();
  }, [loadEntities]);

  useEffect(() => {
    void loadEvents();
    const id = window.setInterval(() => void loadEvents(), 3000);
    return () => window.clearInterval(id);
  }, [loadEvents]);

  return (
    <div className="row">
      <HomeAssistantStatusCard />
      <section className="card" style={{ display: "grid", gap: 12 }}>
        <h2 style={{ margin: 0 }}>События</h2>
        <p className="muted" style={{ margin: 0 }}>
          История из SQLite + обновление каждые 3 с. Фильтр: точный id, шаблон (light.*) или domain (light).
        </p>
        <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center" }}>
          <label style={{ display: "grid", gap: 4, flex: "1 1 220px" }}>
            <span className="muted" style={{ fontSize: 13 }}>
              entity_id
            </span>
            <input
              className="input"
              list="entity-suggestions"
              value={entityFilter}
              onChange={(e) => setEntityFilter(e.target.value)}
              placeholder="например light.* или sensor.temperature"
            />
          </label>
          <datalist id="entity-suggestions">
            {entities.slice(0, 200).map((e) => (
              <option key={e.entityId} value={e.entityId}>
                {e.friendlyName ?? e.entityId}
              </option>
            ))}
          </datalist>
          <button type="button" onClick={() => void loadEvents()} disabled={loading}>
            {loading ? "Загрузка…" : "Обновить"}
          </button>
          <button type="button" onClick={() => setEntityFilter("")}>
            Сбросить фильтр
          </button>
        </div>
        {error ? <div className="bad">{error}</div> : null}
        <ul className="event-list">
          {events.map((e) => (
            <li key={e.id} className="event-item">
              <div>
                <span className="mono">{e.entityId}</span>
                {e.friendlyName ? <span className="muted"> — {e.friendlyName}</span> : null}
              </div>
              <div className="muted" style={{ fontSize: 12 }}>
                {e.oldState ?? "—"} → <strong>{e.newState ?? "—"}</strong>
              </div>
              <div className="muted" style={{ fontSize: 11 }}>
                {new Date(e.receivedAtUtc).toLocaleString()}
              </div>
            </li>
          ))}
        </ul>
        {events.length === 0 && !loading ? (
          <div className="muted">Нет событий. Переключите устройство в HA или снимите фильтр.</div>
        ) : null}
      </section>
    </div>
  );
}
