import { useCallback, useEffect, useState } from "react";
import { fetchEntities, type HomeAssistantEntityDto } from "../api/events";
import {
  fetchAnalysisExclusions,
  saveAnalysisExclusions,
  type AnalysisExclusionsResponse
} from "../api/recommendations";

const DOMAIN_PRESETS = ["sensor", "device_tracker", "weather", "sun", "update", "binary_sensor"];

interface AnalysisExclusionsPanelProps {
  onSaved?: () => void;
}

export function AnalysisExclusionsPanel({ onSaved }: AnalysisExclusionsPanelProps) {
  const [snapshot, setSnapshot] = useState<AnalysisExclusionsResponse | null>(null);
  const [uiEntities, setUiEntities] = useState<string[]>([]);
  const [uiDomains, setUiDomains] = useState<string[]>([]);
  const [entities, setEntities] = useState<HomeAssistantEntityDto[]>([]);
  const [newEntity, setNewEntity] = useState("");
  const [newDomain, setNewDomain] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [exclusions, entityList] = await Promise.all([
        fetchAnalysisExclusions(),
        fetchEntities().catch(() => ({ count: 0, items: [] as HomeAssistantEntityDto[] }))
      ]);
      setSnapshot(exclusions);
      setUiEntities([...exclusions.uiExcludeEntities]);
      setUiDomains([...exclusions.uiExcludeDomains]);
      setEntities(entityList.items);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const persist = async (entitiesList: string[], domainsList: string[]) => {
    setSaving(true);
    try {
      const updated = await saveAnalysisExclusions({
        excludeEntities: entitiesList,
        excludeDomains: domainsList
      });
      setSnapshot(updated);
      setUiEntities([...updated.uiExcludeEntities]);
      setUiDomains([...updated.uiExcludeDomains]);
      setError(null);
      onSaved?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  };

  const addEntity = () => {
    const value = newEntity.trim();
    if (!value) return;
    if (uiEntities.some((x) => x.toLowerCase() === value.toLowerCase())) {
      setNewEntity("");
      return;
    }
    const next = [...uiEntities, value];
    setUiEntities(next);
    setNewEntity("");
    void persist(next, uiDomains);
  };

  const addDomain = (value: string) => {
    const trimmed = value.trim();
    if (!trimmed) return;
    if (uiDomains.some((x) => x.toLowerCase() === trimmed.toLowerCase())) return;
    const next = [...uiDomains, trimmed];
    setUiDomains(next);
    setNewDomain("");
    void persist(uiEntities, next);
  };

  const removeEntity = (pattern: string) => {
    const next = uiEntities.filter((x) => x !== pattern);
    setUiEntities(next);
    void persist(next, uiDomains);
  };

  const removeDomain = (pattern: string) => {
    const next = uiDomains.filter((x) => x !== pattern);
    setUiDomains(next);
    void persist(uiEntities, next);
  };

  return (
    <section className="card" style={{ display: "grid", gap: 12 }}>
      <div>
        <h3 style={{ margin: 0 }}>Исключения из анализа</h3>
        <p className="muted" style={{ margin: "6px 0 0", fontSize: 13 }}>
          Устройства из списка не попадают в ML-модель. События в SQLite и на вкладке «События» остаются.
        </p>
      </div>

      {loading ? <p className="muted">Загрузка…</p> : null}
      {error ? <div className="bad">{error}</div> : null}

      {snapshot ? (
        <>
          <div style={{ display: "grid", gap: 8 }}>
            <span className="muted" style={{ fontSize: 13 }}>
              Ваши исключения (UI)
            </span>
            <div className="chip-row">
              {uiEntities.map((item) => (
                <span key={`e-${item}`} className="chip">
                  <span className="mono">{item}</span>
                  <button type="button" className="chip-remove" onClick={() => removeEntity(item)}>
                    ×
                  </button>
                </span>
              ))}
              {uiDomains.map((item) => (
                <span key={`d-${item}`} className="chip chip-domain">
                  <span className="mono">domain:{item}</span>
                  <button type="button" className="chip-remove" onClick={() => removeDomain(item)}>
                    ×
                  </button>
                </span>
              ))}
              {uiEntities.length === 0 && uiDomains.length === 0 ? (
                <span className="muted" style={{ fontSize: 13 }}>
                  Нет исключений — в модель идут все события из базы
                </span>
              ) : null}
            </div>
          </div>

          {(snapshot.configExcludeEntities.length > 0 ||
            snapshot.configExcludeDomains.length > 0) && (
            <div style={{ display: "grid", gap: 6 }}>
              <span className="muted" style={{ fontSize: 13 }}>
                Из конфигурации add-on (только чтение)
              </span>
              <div className="chip-row">
                {snapshot.configExcludeEntities.map((item) => (
                  <span key={`ce-${item}`} className="chip chip-readonly">
                    {item}
                  </span>
                ))}
                {snapshot.configExcludeDomains.map((item) => (
                  <span key={`cd-${item}`} className="chip chip-readonly">
                    domain:{item}
                  </span>
                ))}
              </div>
            </div>
          )}

          <div style={{ display: "grid", gap: 10 }}>
            <label style={{ display: "grid", gap: 4 }}>
              <span className="muted" style={{ fontSize: 13 }}>
                Добавить entity (шаблон или id)
              </span>
              <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                <input
                  className="input"
                  style={{ flex: "1 1 200px" }}
                  list="exclusion-entity-suggestions"
                  value={newEntity}
                  onChange={(e) => setNewEntity(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      addEntity();
                    }
                  }}
                  placeholder="sensor.* или light.kitchen"
                  disabled={saving}
                />
                <button type="button" onClick={addEntity} disabled={saving || !newEntity.trim()}>
                  Добавить
                </button>
              </div>
            </label>
            <datalist id="exclusion-entity-suggestions">
              {entities.slice(0, 300).map((e) => (
                <option key={e.entityId} value={e.entityId} />
              ))}
            </datalist>

            <label style={{ display: "grid", gap: 4 }}>
              <span className="muted" style={{ fontSize: 13 }}>
                Добавить domain
              </span>
              <div style={{ display: "flex", gap: 8, flexWrap: "wrap", alignItems: "center" }}>
                <input
                  className="input"
                  style={{ flex: "1 1 140px" }}
                  value={newDomain}
                  onChange={(e) => setNewDomain(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      addDomain(newDomain);
                    }
                  }}
                  placeholder="sensor"
                  disabled={saving}
                />
                <button
                  type="button"
                  onClick={() => addDomain(newDomain)}
                  disabled={saving || !newDomain.trim()}
                >
                  Добавить
                </button>
              </div>
              <div className="preset-row">
                {DOMAIN_PRESETS.map((domain) => (
                  <button
                    key={domain}
                    type="button"
                    className="preset-btn"
                    disabled={saving}
                    onClick={() => addDomain(domain)}
                  >
                    {domain}
                  </button>
                ))}
              </div>
            </label>
          </div>

          {saving ? <p className="muted" style={{ margin: 0, fontSize: 12 }}>Сохранение…</p> : null}
          {snapshot.hasExclusions ? (
            <p className="muted" style={{ margin: 0, fontSize: 12 }}>
              Всего активных правил: {snapshot.effectiveExcludeEntities.length} entity,{" "}
              {snapshot.effectiveExcludeDomains.length} domain
            </p>
          ) : null}
        </>
      ) : null}
    </section>
  );
}
