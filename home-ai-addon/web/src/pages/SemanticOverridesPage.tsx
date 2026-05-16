import { FormEvent, useEffect, useState } from "react";
import {
  SemanticOverrideEntry,
  deleteSemanticOverride,
  fetchSemanticOverrides,
  saveSemanticOverride
} from "../api/semanticOverrides";

const roles = ["", "sensor", "actuator", "hybrid", "read_only"];
const intents = ["", "user_action", "environment_trigger", "device_action", "context", "system", "noise"];

const emptyForm: SemanticOverrideEntry = {
  entityId: "",
  role: null,
  intent: null,
  canTrigger: null,
  canAction: null,
  noisy: null,
  significant: null,
  systemEvent: null,
  reason: null
};

function toNullableBool(value: string): boolean | null {
  if (value === "true") {
    return true;
  }
  if (value === "false") {
    return false;
  }
  return null;
}

function boolValue(value: boolean | null): string {
  if (value === true) {
    return "true";
  }
  if (value === false) {
    return "false";
  }
  return "";
}

export function SemanticOverridesPage() {
  const [items, setItems] = useState<SemanticOverrideEntry[]>([]);
  const [form, setForm] = useState<SemanticOverrideEntry>(emptyForm);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const response = await fetchSemanticOverrides();
      setItems(response.overrides);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось загрузить overrides");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaving(true);
    setError(null);
    try {
      const response = await saveSemanticOverride(form.entityId, {
        ...form,
        role: form.role || null,
        intent: form.intent || null,
        reason: form.reason?.trim() || null
      });
      setItems(response.overrides);
      setForm(emptyForm);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось сохранить override");
    } finally {
      setSaving(false);
    }
  }

  async function remove(entityId: string) {
    setSaving(true);
    setError(null);
    try {
      const response = await deleteSemanticOverride(entityId);
      setItems(response.overrides);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Не удалось удалить override");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="row">
      <section className="card">
        <h2 style={{ marginTop: 0 }}>Semantic Overrides</h2>
        <p className="muted" style={{ marginBottom: 0 }}>
          Ручные правила поверх автоматической классификации устройств. Используйте их, если entity
          ошибочно считается шумом, сенсором или действием.
        </p>
      </section>

      {error ? <section className="card bad">{error}</section> : null}

      <section className="card">
        <h3 style={{ marginTop: 0 }}>Добавить или обновить override</h3>
        <form className="semantic-form" onSubmit={(event) => void submit(event)}>
          <input
            className="input"
            placeholder="entity_id, например binary_sensor.front_door"
            value={form.entityId}
            onChange={(event) => setForm({ ...form, entityId: event.target.value })}
            required
          />

          <select
            className="input"
            value={form.role ?? ""}
            onChange={(event) => setForm({ ...form, role: event.target.value || null })}
          >
            {roles.map((role) => (
              <option key={role} value={role}>
                {role || "role: auto"}
              </option>
            ))}
          </select>

          <select
            className="input"
            value={form.intent ?? ""}
            onChange={(event) => setForm({ ...form, intent: event.target.value || null })}
          >
            {intents.map((intent) => (
              <option key={intent} value={intent}>
                {intent || "intent: auto"}
              </option>
            ))}
          </select>

          {(["canTrigger", "canAction", "noisy", "significant", "systemEvent"] as const).map(
            (field) => (
              <select
                key={field}
                className="input"
                value={boolValue(form[field])}
                onChange={(event) => setForm({ ...form, [field]: toNullableBool(event.target.value) })}
              >
                <option value="">{field}: auto</option>
                <option value="true">{field}: true</option>
                <option value="false">{field}: false</option>
              </select>
            )
          )}

          <input
            className="input"
            placeholder="reason, например door sensor should trigger automations"
            value={form.reason ?? ""}
            onChange={(event) => setForm({ ...form, reason: event.target.value })}
          />

          <button type="submit" disabled={saving}>
            {saving ? "Сохранение..." : "Сохранить override"}
          </button>
        </form>
      </section>

      <section className="card">
        <div className="recommendation-header">
          <h3 style={{ marginTop: 0 }}>Активные overrides</h3>
          <button type="button" onClick={() => void load()} disabled={loading || saving}>
            Обновить
          </button>
        </div>
        {loading ? (
          <p className="muted">Загрузка...</p>
        ) : items.length === 0 ? (
          <p className="muted">Overrides пока не заданы.</p>
        ) : (
          <ul className="event-list">
            {items.map((item) => (
              <li className="event-item" key={item.entityId}>
                <div className="recommendation-header">
                  <div>
                    <strong className="mono">{item.entityId}</strong>
                    <div className="chip-row" style={{ marginTop: 8 }}>
                      {item.role ? <span className="chip">role: {item.role}</span> : null}
                      {item.intent ? <span className="chip">intent: {item.intent}</span> : null}
                      {item.canTrigger !== null ? <span className="chip">trigger: {String(item.canTrigger)}</span> : null}
                      {item.canAction !== null ? <span className="chip">action: {String(item.canAction)}</span> : null}
                      {item.noisy !== null ? <span className="chip">noisy: {String(item.noisy)}</span> : null}
                      {item.significant !== null ? <span className="chip">significant: {String(item.significant)}</span> : null}
                      {item.systemEvent !== null ? <span className="chip">system: {String(item.systemEvent)}</span> : null}
                    </div>
                    {item.reason ? <p className="muted">{item.reason}</p> : null}
                  </div>
                  <button type="button" onClick={() => void remove(item.entityId)} disabled={saving}>
                    Удалить
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
