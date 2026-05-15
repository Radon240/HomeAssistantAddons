from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field, field_validator


class EventInput(BaseModel):
    id: int
    entity_id: str = Field(alias="entityId")
    old_state: str | None = Field(default=None, alias="oldState")
    new_state: str | None = Field(default=None, alias="newState")
    friendly_name: str | None = Field(default=None, alias="friendlyName")
    time_fired_utc: datetime = Field(alias="timeFiredUtc")
    received_at_utc: datetime = Field(alias="receivedAtUtc")

    model_config = {"populate_by_name": True}


class AnalyzeOptions(BaseModel):
    feedback_dismiss_days: int = Field(default=14, ge=1, le=90, alias="feedbackDismissDays")
    min_support: int = Field(default=3, ge=2, le=100, alias="minSupport")
    min_confidence: float = Field(default=0.55, ge=0.0, le=1.0, alias="minConfidence")
    min_cadence_confidence: float = Field(
        default=0.4, ge=0.0, le=1.0, alias="minCadenceConfidence"
    )
    require_periodic: bool = Field(default=False, alias="requirePeriodic")
    max_gap_seconds: int = Field(default=300, ge=30, le=3600, alias="maxGapSeconds")
    max_sequence_length: int = Field(default=4, ge=2, le=8, alias="maxSequenceLength")
    lookback_hours: int = Field(default=336, ge=24, le=2160, alias="lookbackHours")

    model_config = {"populate_by_name": True}


class AnalyzeRequest(BaseModel):
    events: list[EventInput]
    options: AnalyzeOptions | None = None


class SequenceStep(BaseModel):
    label: str
    entity_id: str = Field(alias="entityId")
    new_state: str | None = Field(default=None, alias="newState")
    friendly_name: str | None = Field(default=None, alias="friendlyName")

    model_config = {"populate_by_name": True}


class SuggestedAutomation(BaseModel):
    trigger_entity_id: str = Field(alias="triggerEntityId")
    trigger_to_state: str | None = Field(default=None, alias="triggerToState")
    action_entity_ids: list[str] = Field(alias="actionEntityIds")
    action_to_states: list[str | None] = Field(alias="actionToStates")

    model_config = {"populate_by_name": True}


class FeedbackRequest(BaseModel):
    recommendation_id: str = Field(alias="recommendationId")
    pattern_key: str = Field(default="", alias="patternKey")
    verdict: str
    cadence: str = "irregular"
    support_count: int = Field(default=0, alias="supportCount")
    confidence: float = 0.0
    frequency_score: float = Field(default=0.0, alias="frequencyScore")
    entity_ids: list[str] = Field(default_factory=list, alias="entityIds")

    model_config = {"populate_by_name": True}

    @field_validator("recommendation_id", "pattern_key", "verdict", "cadence", mode="before")
    @classmethod
    def coerce_str(cls, value: object) -> str:
        if value is None:
            return ""
        return str(value).strip()

    @field_validator("verdict")
    @classmethod
    def normalize_verdict(cls, value: str) -> str:
        normalized = value.lower().replace("-", "_").replace(" ", "_")
        if normalized in {"notuseful", "not_helpful"}:
            normalized = "not_useful"
        return normalized

    @field_validator("cadence")
    @classmethod
    def normalize_cadence(cls, value: str) -> str:
        return value or "irregular"

    @field_validator("support_count", mode="before")
    @classmethod
    def coerce_support_count(cls, value: object) -> int:
        if value is None:
            return 0
        return int(value)

    @field_validator("confidence", "frequency_score", mode="before")
    @classmethod
    def coerce_float(cls, value: object) -> float:
        if value is None:
            return 0.0
        return float(value)

    @field_validator("entity_ids", mode="before")
    @classmethod
    def coerce_entity_ids(cls, value: object) -> list[str]:
        if value is None:
            return []
        if isinstance(value, list):
            return [str(item) for item in value if item is not None and str(item).strip()]
        return []


class FeedbackResponse(BaseModel):
    accepted: bool
    training_samples: int = Field(alias="trainingSamples")
    message: str

    model_config = {"populate_by_name": True}


class Recommendation(BaseModel):
    id: str
    pattern_key: str = Field(alias="patternKey")
    sequence: list[SequenceStep]
    support_count: int = Field(alias="supportCount")
    session_count: int = Field(alias="sessionCount")
    confidence: float
    base_confidence: float = Field(alias="baseConfidence")
    feedback_score: float = Field(default=1.0, alias="feedbackScore")
    frequency_score: float = Field(alias="frequencyScore")
    cadence: str
    cadence_confidence: float = Field(alias="cadenceConfidence")
    cadence_label: str = Field(alias="cadenceLabel")
    schedule_hint: str = Field(alias="scheduleHint")
    title: str
    description: str
    suggested_automation: SuggestedAutomation = Field(alias="suggestedAutomation")

    model_config = {"populate_by_name": True}


class AnalyzeResponse(BaseModel):
    analyzed_event_count: int = Field(alias="analyzedEventCount")
    session_count: int = Field(alias="sessionCount")
    pattern_candidates: int = Field(alias="patternCandidates")
    feedback_training_samples: int = Field(default=0, alias="feedbackTrainingSamples")
    recommendations: list[Recommendation]
    options_used: dict[str, Any] = Field(alias="optionsUsed")

    model_config = {"populate_by_name": True}


class AnomalyDetectionOptions(BaseModel):
    min_events: int = Field(default=50, ge=10, le=5000, alias="minEvents")
    min_events_per_entity: int = Field(
        default=8, ge=3, le=500, alias="minEventsPerEntity"
    )
    min_hourly_samples: int = Field(default=4, ge=2, le=48, alias="minHourlySamples")
    rolling_window_hours: int = Field(default=24, ge=4, le=168, alias="rollingWindowHours")
    z_score_threshold: float = Field(default=2.5, ge=1.5, le=6.0, alias="zScoreThreshold")
    unusual_hour_max_ratio: float = Field(
        default=0.05, ge=0.01, le=0.25, alias="unusualHourMaxRatio"
    )
    min_score: float = Field(default=0.55, ge=0.0, le=1.0, alias="minScore")
    medium_severity_threshold: float = Field(
        default=0.7, ge=0.0, le=1.0, alias="mediumSeverityThreshold"
    )
    high_severity_threshold: float = Field(
        default=0.85, ge=0.0, le=1.0, alias="highSeverityThreshold"
    )
    max_results: int = Field(default=30, ge=1, le=100, alias="maxResults")
    isolation_forest_estimators: int = Field(
        default=50, ge=20, le=200, alias="isolationForestEstimators"
    )
    isolation_forest_contamination: float = Field(
        default=0.08, ge=0.02, le=0.25, alias="isolationForestContamination"
    )
    isolation_forest_min_samples: int = Field(
        default=20, ge=10, le=500, alias="isolationForestMinSamples"
    )

    model_config = {"populate_by_name": True}


class AnomalyDetectRequest(BaseModel):
    events: list[EventInput]
    options: AnomalyDetectionOptions | None = None


class AnomalyItem(BaseModel):
    id: str
    entity_id: str = Field(alias="entityId")
    anomaly_type: str = Field(alias="anomalyType")
    severity: str
    score: float
    method: str
    title: str
    explanation: str
    detected_at_utc: datetime = Field(alias="detectedAtUtc")
    related_event_ids: list[int] = Field(default_factory=list, alias="relatedEventIds")
    metrics: dict[str, Any] = Field(default_factory=dict)

    model_config = {"populate_by_name": True}


class AnomalyResponse(BaseModel):
    analyzed_event_count: int = Field(alias="analyzedEventCount")
    anomaly_count: int = Field(alias="anomalyCount")
    anomalies: list[AnomalyItem]
    options_used: dict[str, Any] = Field(alias="optionsUsed")

    model_config = {"populate_by_name": True}
