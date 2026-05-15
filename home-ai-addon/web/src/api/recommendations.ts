import { fetchJson } from "./client";

export interface SequenceStep {
  label: string;
  entityId: string;
  newState: string | null;
  friendlyName: string | null;
}

export interface SuggestedAutomation {
  triggerEntityId: string;
  triggerToState: string | null;
  actionEntityIds: string[];
  actionToStates: (string | null)[];
}

export interface Recommendation {
  id: string;
  sequence: SequenceStep[];
  supportCount: number;
  sessionCount: number;
  confidence: number;
  frequencyScore: number;
  cadence: string;
  cadenceConfidence: number;
  cadenceLabel: string;
  scheduleHint: string;
  title: string;
  description: string;
  suggestedAutomation: SuggestedAutomation;
}

export interface RecommendationsResponse {
  analyzedEventCount: number;
  sessionCount: number;
  patternCandidates: number;
  scannedEventCount: number;
  excludedEventCount: number;
  recommendations: Recommendation[];
  message: string | null;
  analysisExcludeEntities: string[];
  analysisExcludeDomains: string[];
}

export interface AnalysisExclusionsResponse {
  uiExcludeEntities: string[];
  uiExcludeDomains: string[];
  configExcludeEntities: string[];
  configExcludeDomains: string[];
  effectiveExcludeEntities: string[];
  effectiveExcludeDomains: string[];
  hasExclusions: boolean;
}

export interface UpdateAnalysisExclusionsRequest {
  excludeEntities: string[];
  excludeDomains: string[];
}

export function fetchRecommendations(): Promise<RecommendationsResponse> {
  return fetchJson<RecommendationsResponse>("/api/recommendations");
}

export function fetchAnalysisExclusions(): Promise<AnalysisExclusionsResponse> {
  return fetchJson<AnalysisExclusionsResponse>("/api/recommendations/exclusions");
}

export function saveAnalysisExclusions(
  body: UpdateAnalysisExclusionsRequest
): Promise<AnalysisExclusionsResponse> {
  return fetchJson<AnalysisExclusionsResponse>("/api/recommendations/exclusions", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}
