import { fetchJson } from "./client";

export type AnomalySeverity = "low" | "medium" | "high";

export type AnomalyType =
  | "activity_spike"
  | "unusual_time"
  | "consumption_spike"
  | "device_behavior";

export interface AnomalyAlert {
  id: number;
  detectionId: string;
  entityId: string;
  anomalyType: AnomalyType;
  severity: AnomalySeverity;
  score: number;
  method: string;
  title: string;
  explanation: string;
  detectedAtUtc: string;
  persistedAtUtc: string;
  relatedEventIds: number[];
  metrics: Record<string, unknown>;
}

export interface AnomaliesListResponse {
  count: number;
  anomalies: AnomalyAlert[];
}

export interface AnomaliesStatusResponse {
  enabled: boolean;
  mlHealthy: boolean;
  intervalMinutes: number;
  message: string;
}

export interface AnomalyDetectRunResponse {
  success: boolean;
  scannedEventCount: number;
  excludedEventCount: number;
  analyzedEventCount: number;
  persistedCount: number;
  message: string | null;
  anomalies?: AnomalyAlert[];
}

export function fetchAnomalies(limit = 100): Promise<AnomaliesListResponse> {
  return fetchJson<AnomaliesListResponse>(`/api/anomalies?limit=${limit}`);
}

export function fetchAnomaliesStatus(): Promise<AnomaliesStatusResponse> {
  return fetchJson<AnomaliesStatusResponse>("/api/anomalies/status");
}

export function runAnomalyDetection(): Promise<AnomalyDetectRunResponse> {
  return fetchJson<AnomalyDetectRunResponse>("/api/anomalies/detect", {
    method: "POST"
  });
}
