from __future__ import annotations

from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field


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
    entity_id: str
    new_state: str | None
    friendly_name: str | None = None


class SuggestedAutomation(BaseModel):
    trigger_entity_id: str
    trigger_to_state: str | None
    action_entity_ids: list[str]
    action_to_states: list[str | None]


class FeedbackRequest(BaseModel):
    recommendation_id: str = Field(alias="recommendationId")
    pattern_key: str = Field(alias="patternKey")
    verdict: str
    cadence: str = "irregular"
    support_count: int = Field(default=0, alias="supportCount")
    confidence: float = 0.0
    frequency_score: float = Field(default=0.0, alias="frequencyScore")
    entity_ids: list[str] = Field(default_factory=list, alias="entityIds")

    model_config = {"populate_by_name": True}


class FeedbackResponse(BaseModel):
    accepted: bool
    training_samples: int = Field(alias="trainingSamples")
    message: str

    model_config = {"populate_by_name": True}


class Recommendation(BaseModel):
    id: str
    pattern_key: str = Field(alias="patternKey")
    sequence: list[SequenceStep]
    support_count: int
    session_count: int
    confidence: float
    base_confidence: float = Field(alias="baseConfidence")
    feedback_score: float = Field(default=1.0, alias="feedbackScore")
    frequency_score: float
    cadence: str
    cadence_confidence: float = Field(alias="cadenceConfidence")
    cadence_label: str = Field(alias="cadenceLabel")
    schedule_hint: str = Field(alias="scheduleHint")
    title: str
    description: str
    suggested_automation: SuggestedAutomation

    model_config = {"populate_by_name": True}


class AnalyzeResponse(BaseModel):
    analyzed_event_count: int
    session_count: int
    pattern_candidates: int
    feedback_training_samples: int = Field(default=0, alias="feedbackTrainingSamples")
    recommendations: list[Recommendation]
    options_used: dict[str, Any]
