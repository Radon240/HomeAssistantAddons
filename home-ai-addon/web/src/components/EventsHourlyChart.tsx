import { useEffect, useState } from "react";
import { fetchHourlyStats, type HourlyEventBucket } from "../api/events";

export function EventsHourlyChart({ hours = 1 }: { hours?: number }) {
  const [buckets, setBuckets] = useState<HourlyEventBucket[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        const data = await fetchHourlyStats(hours);
        if (!cancelled) {
          setBuckets(data.buckets);
          setError(null);
        }
      } catch (e) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : String(e));
        }
      }
    };

    void load();
    const id = window.setInterval(() => {
      if (document.visibilityState === "visible") {
        void load();
      }
    }, 15000);
    return () => {
      cancelled = true;
      window.clearInterval(id);
    };
  }, [hours]);

  const max = Math.max(1, ...buckets.map((b) => b.count));

  return (
    <section className="card" style={{ display: "grid", gap: 10 }}>
      <h3 style={{ margin: 0 }}>События за {hours} ч</h3>
      {error ? <div className="bad">{error}</div> : null}
      {buckets.length === 0 ? (
        <div className="muted">Пока нет сохранённых событий за этот период.</div>
      ) : (
        <div className="chart-row" role="img" aria-label="График событий по часам">
          {buckets.map((b) => {
            const height = Math.max(4, Math.round((b.count / max) * 72));
            const label = new Date(b.hourUtc).toLocaleTimeString([], {
              hour: "2-digit",
              minute: "2-digit"
            });
            return (
              <div key={b.hourUtc} className="chart-bar-wrap" title={`${label}: ${b.count}`}>
                <div className="chart-bar" style={{ height }} />
                <span className="chart-count">{b.count}</span>
                <span className="chart-label">{label}</span>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
