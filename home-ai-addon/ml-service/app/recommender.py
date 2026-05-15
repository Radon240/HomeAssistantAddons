from __future__ import annotations

import hashlib

from app.models import (
    AnalyzeOptions,
    AnalyzeResponse,
    EventInput,
    Recommendation,
    SequenceStep,
    SuggestedAutomation,
)
from app.pattern_miner import PatternCandidate, mine_patterns
from app.river_tracker import OnlinePatternTracker
from app.sequence_builder import build_sessions
from app.temporal_analysis import (
    CADENCE_IRREGULAR,
    cadence_label_ru,
    detect_cadence,
)


def analyze_events(events: list[EventInput], options: AnalyzeOptions) -> AnalyzeResponse:
    sessions = build_sessions(
        events,
        max_gap_seconds=options.max_gap_seconds,
        lookback_hours=options.lookback_hours,
    )
    tracker = OnlinePatternTracker()
    candidates = mine_patterns(
        sessions,
        min_support=options.min_support,
        max_sequence_length=options.max_sequence_length,
        tracker=tracker,
    )

    recommendations: list[Recommendation] = []
    for candidate in candidates:
        if candidate.confidence < options.min_confidence:
            continue
        recommendation = _to_recommendation(
            candidate,
            session_count=len(sessions),
            options=options,
        )
        if recommendation is not None:
            recommendations.append(recommendation)

    recommendations.sort(
        key=lambda item: (
            item.cadence != CADENCE_IRREGULAR,
            item.cadence_confidence * 0.4 + item.confidence * 0.6,
            item.support_count,
        ),
        reverse=True,
    )

    return AnalyzeResponse(
        analyzed_event_count=len(events),
        session_count=len(sessions),
        pattern_candidates=len(candidates),
        recommendations=recommendations[:20],
        options_used=options.model_dump(by_alias=True),
    )


def _to_recommendation(
    candidate: PatternCandidate,
    session_count: int,
    options: AnalyzeOptions,
) -> Recommendation | None:
    if len(candidate.tokens) < 2:
        return None

    cadence_result = detect_cadence(list(candidate.occurrence_times))
    if options.require_periodic and cadence_result.cadence == CADENCE_IRREGULAR:
        return None
    if (
        cadence_result.cadence != CADENCE_IRREGULAR
        and cadence_result.confidence < options.min_cadence_confidence
    ):
        return None

    steps = [
        SequenceStep(
            label=token.label,
            entity_id=token.entity_id,
            new_state=token.new_state,
            friendly_name=token.friendly_name,
        )
        for token in candidate.tokens
    ]

    trigger = candidate.tokens[0]
    actions = candidate.tokens[1:]
    trigger_name = trigger.friendly_name or trigger.entity_id
    action_names = ", ".join(
        action.friendly_name or action.entity_id for action in actions
    )

    cadence_label = cadence_label_ru(cadence_result.cadence)
    title = f"{cadence_label}: {trigger_name} → {action_names}"

    schedule_part = (
        cadence_result.schedule_hint
        if cadence_result.cadence != CADENCE_IRREGULAR
        else "без устойчивого часового/дневного/недельного ритма"
    )
    description = (
        f"Сценарий из {len(steps)} шагов повторился {candidate.support_count} раз(а). "
        f"Расписание: {schedule_part}. "
        f"Уверенность сценария: {int(candidate.confidence * 100)}%, "
        f"уверенность расписания: {int(cadence_result.confidence * 100)}%."
    )

    combined_confidence = round(
        candidate.confidence * 0.6 + cadence_result.confidence * 0.4
        if cadence_result.cadence != CADENCE_IRREGULAR
        else candidate.confidence * 0.85,
        4,
    )

    pattern_id = hashlib.sha1("|".join(candidate.labels).encode("utf-8")).hexdigest()[:12]

    return Recommendation(
        id=f"rec-{pattern_id}",
        sequence=steps,
        support_count=candidate.support_count,
        session_count=session_count,
        confidence=combined_confidence,
        frequency_score=candidate.frequency_score,
        cadence=cadence_result.cadence,
        cadence_confidence=cadence_result.confidence,
        cadence_label=cadence_label,
        schedule_hint=cadence_result.schedule_hint,
        title=title,
        description=description,
        suggested_automation=SuggestedAutomation(
            trigger_entity_id=trigger.entity_id,
            trigger_to_state=trigger.new_state,
            action_entity_ids=[action.entity_id for action in actions],
            action_to_states=[action.new_state for action in actions],
        ),
    )
