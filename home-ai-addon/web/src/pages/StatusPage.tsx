import { useEffect, useMemo, useState } from "react";
import { fetchJson } from "../api/client";

type HealthResponse = {
  status: string;
  totalDuration: number;
  checks: Array<{
    name: string;
    status: string;
    durationMs: number;
    description?: string;
  }>;
};

type InfoResponse = {
  application?: string;
  environment: string;
  displayName: string;
  enableVerboseApi: boolean;
  databaseConnected: boolean;
  utc: string;
};

type PingResponse = {
  ok: boolean;
  utc: string;
};

type LoadState =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "ok"; at: string }
  | { kind: "error"; message: string };

export function StatusPage() {
  const [health, setHealth] = useState<LoadState>({ kind: "idle" });
  const [info, setInfo] = useState<LoadState>({ kind: "idle" });
  const [ping, setPing] = useState<LoadState>({ kind: "idle" });

  const [healthJson, setHealthJson] = useState<string>("");
  const [infoJson, setInfoJson] = useState<string>("");
  const [pingJson, setPingJson] = useState<string>("");

  const refresh = useMemo(() => {
    return async () => {
      setHealth({ kind: "loading" });
      setInfo({ kind: "loading" });
      setPing({ kind: "loading" });

      const at = new Date().toISOString();

      try {
        const h = await fetchJson<HealthResponse>("/health");
        setHealthJson(JSON.stringify(h, null, 2));
        setHealth({ kind: "ok", at });
      } catch (e) {
        setHealthJson("");
        setHealth({ kind: "error", message: e instanceof Error ? e.message : String(e) });
      }

      try {
        const i = await fetchJson<InfoResponse>("/api/info");
        setInfoJson(JSON.stringify(i, null, 2));
        setInfo({ kind: "ok", at });
      } catch (e) {
        setInfoJson("");
        setInfo({ kind: "error", message: e instanceof Error ? e.message : String(e) });
      }

      try {
        const p = await fetchJson<PingResponse>("/api/ping");
        setPingJson(JSON.stringify(p, null, 2));
        setPing({ kind: "ok", at });
      } catch (e) {
        setPingJson("");
        setPing({ kind: "error", message: e instanceof Error ? e.message : String(e) });
      }
    };
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  return (
    <div className="row">
      <section className="card" style={{ display: "grid", gap: 12 }}>
        <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
          <h2 style={{ margin: 0 }}>Статус</h2>
          <button type="button" onClick={() => void refresh()}>
            Обновить
          </button>
          <span className="muted">Запросы идут на тот же origin (Ingress-friendly).</span>
        </div>

        <div className="card" style={{ background: "rgba(0,0,0,0.18)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
            <strong>/health</strong>
            <span className={health.kind === "error" ? "bad" : health.kind === "ok" ? "good" : "muted"}>
              {health.kind === "loading" ? "загрузка…" : health.kind === "ok" ? "ok" : health.kind === "error" ? health.message : "—"}
            </span>
          </div>
          {healthJson ? <pre className="mono">{healthJson}</pre> : null}
        </div>

        <div className="card" style={{ background: "rgba(0,0,0,0.18)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
            <strong>/api/info</strong>
            <span className={info.kind === "error" ? "bad" : info.kind === "ok" ? "good" : "muted"}>
              {info.kind === "loading" ? "загрузка…" : info.kind === "ok" ? "ok" : info.kind === "error" ? info.message : "—"}
            </span>
          </div>
          {infoJson ? <pre className="mono">{infoJson}</pre> : null}
        </div>

        <div className="card" style={{ background: "rgba(0,0,0,0.18)" }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
            <strong>/api/ping</strong>
            <span className={ping.kind === "error" ? "bad" : ping.kind === "ok" ? "good" : "muted"}>
              {ping.kind === "loading" ? "загрузка…" : ping.kind === "ok" ? "ok" : ping.kind === "error" ? ping.message : "—"}
            </span>
          </div>
          {pingJson ? <pre className="mono">{pingJson}</pre> : null}
        </div>
      </section>
    </div>
  );
}
