import { useCallback, useEffect, useState } from "react";
import { AnomalyCard } from "../components/AnomalyCard";
import {
  fetchAnomalies,
  fetchAnomaliesStatus,
  runAnomalyDetection,
  type AnomaliesListResponse,
  type AnomaliesStatusResponse
} from "../api/anomalies";

export function AnomaliesPage() {
  const [data, setData] = useState<AnomaliesListResponse | null>(null);
  const [status, setStatus] = useState<AnomaliesStatusResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [detecting, setDetecting] = useState(false);
  const [info, setInfo] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [list, svc] = await Promise.all([fetchAnomalies(), fetchAnomaliesStatus()]);
      setData(list);
      setStatus(svc);
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

  async function handleDetect() {
    setDetecting(true);
    setInfo(null);
    try {
      const result = await runAnomalyDetection();
      if (result.anomalies) {
        setData({ count: result.anomalies.length, anomalies: result.anomalies });
      } else {
        await load();
      }
      setInfo(
        result.message ??
          `Проанализировано ${result.analyzedEventCount} событий, сохранено ${result.persistedCount} аномалий.`
      );
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setDetecting(false);
    }
  }

  const highCount = data?.anomalies.filter((a) => a.severity === "high").length ?? 0;
  const mediumCount = data?.anomalies.filter((a) => a.severity === "medium").length ?? 0;

  return (
    <div className="row">
      <section className="card">
        <div style={{ display: "flex", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
          <div>
            <h2 style={{ marginTop: 0 }}>Аномалии</h2>
            <p className="muted" style={{ margin: 0 }}>
              Необычная активность, время, потребление и поведение устройств (Z-score, rolling avg,
              Isolation Forest).
            </p>
          </div>
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <button type="button" onClick={() => void load()} disabled={loading || detecting}>
              {loading ? "Загрузка…" : "Обновить"}
            </button>
            <button type="button" onClick={() => void handleDetect()} disabled={loading || detecting}>
              {detecting ? "Анализ…" : "Запустить детекцию"}
            </button>
          </div>
        </div>
        {status ? (
          <p className="muted" style={{ marginBottom: 0, marginTop: 12 }}>
            Сервис: {status.enabled ? "включён" : "выключен"} · ML:{" "}
            {status.mlHealthy ? "доступен" : "недоступен"} · интервал {status.intervalMinutes} мин
            {data ? ` · записей: ${data.count} (высокая: ${highCount}, средняя: ${mediumCount})` : ""}
          </p>
        ) : null}
      </section>

      {info ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            {info}
          </p>
        </section>
      ) : null}

      {error ? (
        <section className="card bad">
          <strong>Ошибка</strong>
          <div className="mono" style={{ marginTop: 8 }}>
            {error}
          </div>
        </section>
      ) : null}

      {loading && !data ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            Загрузка аномалий…
          </p>
        </section>
      ) : null}

      {data && data.anomalies.length === 0 && !loading ? (
        <section className="card">
          <p className="muted" style={{ margin: 0 }}>
            Аномалии не обнаружены. Накопите больше событий или запустите детекцию вручную.
          </p>
        </section>
      ) : null}

      {data?.anomalies.map((item) => (
        <AnomalyCard key={`${item.detectionId}-${item.id}`} item={item} />
      ))}
    </div>
  );
}
