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


class Recommendation(BaseModel):
    id: str
    sequence: list[SequenceStep]
    support_count: int
    session_count: int
    confidence: float
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
    recommendations: list[Recommendation]
    options_used: dict[str, Any]
