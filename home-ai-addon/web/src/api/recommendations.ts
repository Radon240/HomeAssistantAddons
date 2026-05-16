import { fetchJson } from "./client";

export interface SequenceStep {
  label: string;
  entityId: string;
  newState: string | null;
  friendlyName: string | null;
  areaId: string | null;
  areaName: string | null;
}

export interface SuggestedAutomation {
  triggerEntityId: string;
  triggerToState: string | null;
  actionEntityIds: string[];
  actionToStates: (string | null)[];
}

export interface ExplanationFactor {
  key: string;
  label: string;
  value: string;
  weight: number;
  score: number;
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
  lift: number;
  supportRatio: number;
  cadence: string;
  cadenceConfidence: number;
  cadenceLabel: string;
  scheduleHint: string;
  title: string;
  description: string;
  whyGenerated: string;
  explanationFactors: ExplanationFactor[];
  medianStepGapsSeconds: number[];
  weekdayHint: string | null;
  areaHint: string | null;
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

export interface DiagnosticsCounter {
  key: string;
  count: number;
}

export interface RecommendationDiagnosticsResponse {
  analyzedEventCount: number;
  eligibleEventCount: number;
  sessionCount: number;
  rawSequenceCandidateCount: number;
  semanticRejectedCandidateCount: number;
  sensorToSensorCandidateCount: number;
  meaningfulCandidateCount: number;
  qualityFilteredCandidateCount: number;
  recommendationCount: number;
  scannedEventCount: number;
  excludedEventCount: number;
  filterReasons: DiagnosticsCounter[];
  semanticRoles: DiagnosticsCounter[];
  semanticIntents: DiagnosticsCounter[];
  message: string | null;
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

export interface FeedbackCounterEntry {
  key: string;
  count: number;
}

export interface DismissedFeedbackEntry {
  key: string;
  until: string;
}

export interface FeedbackStateResponse {
  trainingSamples: number;
  patternUseful: FeedbackCounterEntry[];
  patternNotUseful: FeedbackCounterEntry[];
  entityUseful: FeedbackCounterEntry[];
  entityNotUseful: FeedbackCounterEntry[];
  dismissedUntil: DismissedFeedbackEntry[];
}

export interface ResetFeedbackItemsRequest {
  patternKeys?: string[];
  recommendationIds?: string[];
  entityIds?: string[];
  clearPositive?: boolean;
  clearNegative?: boolean;
  clearDismissals?: boolean;
}

export function fetchRecommendations(): Promise<RecommendationsResponse> {
  return fetchJson<RecommendationsResponse>("/api/recommendations");
}

export function fetchRecommendationDiagnostics(): Promise<RecommendationDiagnosticsResponse> {
  return fetchJson<RecommendationDiagnosticsResponse>("/api/recommendations/diagnostics");
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

export function fetchFeedbackState(): Promise<FeedbackStateResponse> {
  return fetchJson<FeedbackStateResponse>("/api/recommendations/feedback/state");
}

export function resetAllFeedback(): Promise<FeedbackResponse> {
  return fetchJson<FeedbackResponse>("/api/recommendations/feedback", {
    method: "DELETE"
  });
}

export function resetFeedbackItems(body: ResetFeedbackItemsRequest): Promise<FeedbackResponse> {
  return fetchJson<FeedbackResponse>("/api/recommendations/feedback/reset-items", {
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
