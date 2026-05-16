import { useCallback, useEffect, useMemo, useState } from "react";
import {
  fetchFeedbackState,
  resetAllFeedback,
  resetFeedbackItems,
  type FeedbackCounterEntry,
  type FeedbackStateResponse
} from "../api/recommendations";

interface FeedbackLearningPanelProps {
  onChanged?: () => void;
}

function formatUntil(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString("ru-RU");
}

function CounterList({
  title,
  items,
  emptyText,
  onReset
}: {
  title: string;
  items: FeedbackCounterEntry[];
  emptyText: string;
  onReset: (key: string) => void;
}) {
  return (
    <div style={{ display: "grid", gap: 6 }}>
      <strong style={{ fontSize: 13 }}>{title}</strong>
      {items.length === 0 ? (
        <span className="muted" style={{ fontSize: 12 }}>
          {emptyText}
        </span>
      ) : (
        <div className="chip-row">
          {items.slice(0, 20).map((item) => (
            <span key={item.key} className="chip">
              <span className="mono">
                {item.key} ({item.count})
              </span>
              <button
                type="button"
                className="chip-remove"
                title="Сбросить этот пример"
                onClick={() => onReset(item.key)}
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

export function FeedbackLearningPanel({ onChanged }: FeedbackLearningPanelProps) {
  const [state, setState] = useState<FeedbackStateResponse | null>(null);
  const [manualKey, setManualKey] = useState("");
  const [manualEntity, setManualEntity] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const next = await fetchFeedbackState();
      setState(next);
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const negativeCount = useMemo(() => {
    if (!state) {
      return 0;
    }
    return (
      state.patternNotUseful.reduce((sum, item) => sum + item.count, 0) +
      state.entityNotUseful.reduce((sum, item) => sum + item.count, 0)
    );
  }, [state]);

  async function run(action: () => Promise<{ message: string; trainingSamples: number }>) {
    setBusy(true);
    setMessage(null);
    try {
      const result = await action();
      setMessage(`${result.message}. Обучающих примеров: ${result.trainingSamples}`);
      await load();
      onChanged?.();
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  function resetPattern(key: string) {
    void run(() =>
      resetFeedbackItems({
        patternKeys: [key],
        clearNegative: true,
        clearDismissals: true
      })
    );
  }

  function resetEntity(key: string) {
    void run(() =>
      resetFeedbackItems({
        entityIds: [key],
        clearNegative: true,
        clearDismissals: true
      })
    );
  }

  function resetDismissal(key: string) {
    const body = key.startsWith("entities:")
      ? { entityIds: key.replace("entities:", "").split("|"), clearNegative: false, clearDismissals: true }
      : { recommendationIds: [key], patternKeys: [key], clearNegative: false, clearDismissals: true };
    void run(() => resetFeedbackItems(body));
  }

  return (
    <section className="card" style={{ display: "grid", gap: 12 }}>
      <div>
        <h3 style={{ margin: 0 }}>Обучение на отзывах</h3>
        <p className="muted" style={{ margin: "6px 0 0", fontSize: 13 }}>
          Здесь можно сбросить весь feedback learner или удалить конкретные плохие примеры,
          которые скрыли/понизили рекомендации.
        </p>
      </div>

      {state ? (
        <p className="muted" style={{ margin: 0, fontSize: 13 }}>
          Обучающих примеров: {state.trainingSamples} · плохих сигналов: {negativeCount} · скрытых
          ключей: {state.dismissedUntil.length}
        </p>
      ) : (
        <p className="muted" style={{ margin: 0, fontSize: 13 }}>
          Загрузка состояния обучения…
        </p>
      )}

      {error ? <div className="bad">{error}</div> : null}
      {message ? <div className="good">{message}</div> : null}

      <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
        <button type="button" disabled={busy} onClick={() => void load()}>
          Обновить состояние
        </button>
        <button
          type="button"
          className="feedback-btn feedback-btn-dismiss"
          disabled={busy || !state || state.trainingSamples === 0}
          onClick={() => {
            if (window.confirm("Полностью сбросить обучение на отзывах?")) {
              void run(() => resetAllFeedback());
            }
          }}
        >
          Сбросить всё обучение
        </button>
      </div>

      {state ? (
        <>
          <CounterList
            title="Плохие pattern examples"
            items={state.patternNotUseful}
            emptyText="Нет плохих pattern-примеров"
            onReset={resetPattern}
          />
          <CounterList
            title="Плохие entity examples"
            items={state.entityNotUseful}
            emptyText="Нет плохих entity-примеров"
            onReset={resetEntity}
          />
          <div style={{ display: "grid", gap: 6 }}>
            <strong style={{ fontSize: 13 }}>Скрытые рекомендации</strong>
            {state.dismissedUntil.length === 0 ? (
              <span className="muted" style={{ fontSize: 12 }}>
                Нет активных dismissed-ключей
              </span>
            ) : (
              <div className="chip-row">
                {state.dismissedUntil.slice(0, 20).map((item) => (
                  <span key={item.key} className="chip chip-domain">
                    <span className="mono">
                      {item.key} до {formatUntil(item.until)}
                    </span>
                    <button
                      type="button"
                      className="chip-remove"
                      title="Вернуть рекомендацию"
                      onClick={() => resetDismissal(item.key)}
                    >
                      ×
                    </button>
                  </span>
                ))}
              </div>
            )}
          </div>
        </>
      ) : null}

      <div style={{ display: "grid", gap: 8 }}>
        <label style={{ display: "grid", gap: 4 }}>
          <span className="muted" style={{ fontSize: 13 }}>
            Сбросить patternKey вручную
          </span>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <input
              className="input"
              style={{ flex: "1 1 260px" }}
              value={manualKey}
              onChange={(e) => setManualKey(e.target.value)}
              placeholder="binary_sensor.door#on|light.hall#on"
              disabled={busy}
            />
            <button
              type="button"
              disabled={busy || !manualKey.trim()}
              onClick={() => {
                resetPattern(manualKey.trim());
                setManualKey("");
              }}
            >
              Сбросить pattern
            </button>
          </div>
        </label>
        <label style={{ display: "grid", gap: 4 }}>
          <span className="muted" style={{ fontSize: 13 }}>
            Сбросить плохие сигналы по entity
          </span>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <input
              className="input"
              style={{ flex: "1 1 220px" }}
              value={manualEntity}
              onChange={(e) => setManualEntity(e.target.value)}
              placeholder="light.hall"
              disabled={busy}
            />
            <button
              type="button"
              disabled={busy || !manualEntity.trim()}
              onClick={() => {
                resetEntity(manualEntity.trim());
                setManualEntity("");
              }}
            >
              Сбросить entity
            </button>
          </div>
        </label>
      </div>
    </section>
  );
}
