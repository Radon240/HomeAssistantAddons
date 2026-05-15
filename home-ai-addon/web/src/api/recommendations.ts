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

export interface AnalysisFiltersResponse {
  excludeEntities: string[];
  excludeDomains: string[];
  hasExclusions: boolean;
  hint: string;
}

export function fetchRecommendations(): Promise<RecommendationsResponse> {
  return fetchJson<RecommendationsResponse>("/api/recommendations");
}
