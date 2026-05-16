import { fetchJson } from "./client";

export interface SemanticOverrideEntry {
  entityId: string;
  role: string | null;
  intent: string | null;
  canTrigger: boolean | null;
  canAction: boolean | null;
  noisy: boolean | null;
  significant: boolean | null;
  systemEvent: boolean | null;
  reason: string | null;
}

export interface SemanticOverridesResponse {
  overrides: SemanticOverrideEntry[];
}

export type UpsertSemanticOverrideRequest = SemanticOverrideEntry;

export function fetchSemanticOverrides(): Promise<SemanticOverridesResponse> {
  return fetchJson<SemanticOverridesResponse>("/api/semantic-overrides");
}

export function saveSemanticOverride(
  entityId: string,
  body: UpsertSemanticOverrideRequest
): Promise<SemanticOverridesResponse> {
  return fetchJson<SemanticOverridesResponse>(`/api/semantic-overrides/${encodeURIComponent(entityId)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

export function deleteSemanticOverride(entityId: string): Promise<SemanticOverridesResponse> {
  return fetchJson<SemanticOverridesResponse>(`/api/semantic-overrides/${encodeURIComponent(entityId)}`, {
    method: "DELETE"
  });
}
