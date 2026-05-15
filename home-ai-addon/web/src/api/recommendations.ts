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
  patternKey: string;
  sequence: SequenceStep[];
  supportCount: number;
  sessionCount: number;
  confidence: number;
  baseConfidence: number;
  feedbackScore: number;
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
  feedbackTrainingSamples: number;
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

export type FeedbackVerdict = "useful" | "not_useful";

export interface RecommendationFeedbackRequest {
  verdict: FeedbackVerdict;
  patternKey: string;
  cadence: string;
  supportCount: number;
  confidence: number;
  frequencyScore: number;
  entityIds: string[];
}

export interface FeedbackResponse {
  accepted: boolean;
  trainingSamples: number;
  message: string;
}

export function fetchRecommendations(): Promise<RecommendationsResponse> {
  return fetchJson<RecommendationsResponse>("/api/recommendations");
}

export function submitRecommendationFeedback(
  recommendationId: string,
  body: RecommendationFeedbackRequest
): Promise<FeedbackResponse> {
  return fetchJson<FeedbackResponse>(`/api/recommendations/${recommendationId}/feedback`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
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
