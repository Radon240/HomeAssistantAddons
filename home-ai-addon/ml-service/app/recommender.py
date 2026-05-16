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
from app.pattern_scoring import passes_quality_gates, score_pattern
from app.river_tracker import OnlinePatternTracker
from app.sequence_builder import ActionToken, build_sessions
from app.feedback_learner import FeedbackContext, extract_domains
from app.feedback_service import get_feedback_learner
from app.temporal_analysis import (
    CADENCE_IRREGULAR,
    cadence_label_ru,
    detect_cadence,
)


def analyze_events(events: list[EventInput], options: AnalyzeOptions) -> AnalyzeResponse:
    sessions, candidates = build_recommendation_pipeline(events, options)
    recommendations, training_samples = rank_recommendations(
        candidates,
        session_count=len(sessions),
        options=options,
    )

    return AnalyzeResponse(
        analyzed_event_count=len(events),
        session_count=len(sessions),
        pattern_candidates=len(candidates),
        feedback_training_samples=training_samples,
        recommendations=recommendations[:20],
        options_used=options.model_dump(by_alias=True),
    )


def build_recommendation_pipeline(
    events: list[EventInput],
    options: AnalyzeOptions,
) -> tuple[list[list[ActionToken]], list[PatternCandidate]]:
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
        max_step_gap_seconds=options.max_step_gap_seconds,
        tracker=tracker,
    )

    return sessions, candidates


def rank_recommendations(
    candidates: list[PatternCandidate],
    session_count: int,
    options: AnalyzeOptions,
) -> tuple[list[Recommendation], int]:
    learner = get_feedback_learner()
    learner.set_dismiss_days(options.feedback_dismiss_days)

    recommendations: list[Recommendation] = []
    for candidate in candidates:
        recommendation = _to_recommendation(
            candidate,
            session_count=session_count,
            options=options,
            learner=learner,
        )
        if recommendation is not None:
            recommendations.append(recommendation)

    recommendations.sort(
        key=lambda item: (
            item.cadence != CADENCE_IRREGULAR,
            item.lift,
            item.feedback_score * item.confidence,
            item.support_count,
        ),
        reverse=True,
    )

    return recommendations, learner.training_samples


def _to_recommendation(
    candidate: PatternCandidate,
    session_count: int,
    options: AnalyzeOptions,
    learner,
) -> Recommendation | None:
    if len(candidate.entity_keys) < 2:
        return None

    cadence_result = detect_cadence(list(candidate.occurrence_times))
    if options.require_periodic and cadence_result.cadence == CADENCE_IRREGULAR:
        return None
    if (
        cadence_result.cadence != CADENCE_IRREGULAR
        and cadence_result.confidence < options.min_cadence_confidence
    ):
        return None

    scored = score_pattern(candidate, cadence_result, session_count, options)
    if not passes_quality_gates(candidate, scored, options):
        return None

    steps = [
        SequenceStep(
            label=token.label,
            entity_id=token.entity_id,
            new_state=token.new_state,
            friendly_name=token.friendly_name,
            area_id=token.area_id,
            area_name=token.area_name,
            origin=token.intelligence.origin.value,
            intent_score=token.intelligence.intent_score,
            state_importance=token.intelligence.state_importance,
            event_weight=token.intelligence.event_weight,
            intelligence_explanation=token.intelligence.explanation,
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
        else candidate.weekday_hint or "без устойчивого расписания"
    )
    gap_part = ""
    if candidate.median_step_gaps:
        gap_part = (
            " Интервалы: "
            + ", ".join(f"{int(g)}с" for g in candidate.median_step_gaps)
            + "."
        )

    description = (
        f"Сценарий из {len(steps)} шагов повторился {candidate.support_count} раз(а). "
        f"Расписание: {schedule_part}. "
        f"Уверенность: {int(scored.base_confidence * 100)}%, lift: {candidate.lift:.1f}."
        f" {candidate.area_hint or 'Area metadata не задана.'}"
        f"{gap_part}"
    )

    pattern_key = "|".join(candidate.entity_keys)
    pattern_id = hashlib.sha1(pattern_key.encode("utf-8")).hexdigest()[:12]
    entity_ids = [token.entity_id for token in candidate.tokens]

    feedback_context = FeedbackContext(
        pattern_key=pattern_key,
        recommendation_id=f"rec-{pattern_id}",
        cadence=cadence_result.cadence,
        support_count=candidate.support_count,
        confidence=scored.base_confidence,
        frequency_score=candidate.frequency_score,
        entity_ids=tuple(entity_ids),
        domains=extract_domains(entity_ids),
    )
    adjustment = learner.rank_adjustment(feedback_context)
    if adjustment.hidden:
        return None

    adjusted_confidence = round(
        min(0.99, scored.base_confidence * adjustment.final_multiplier),
        4,
    )

    return Recommendation(
        id=f"rec-{pattern_id}",
        pattern_key=pattern_key,
        sequence=steps,
        support_count=candidate.support_count,
        session_count=session_count,
        confidence=adjusted_confidence,
        base_confidence=scored.base_confidence,
        feedback_score=round(adjustment.final_multiplier, 4),
        frequency_score=candidate.frequency_score,
        lift=candidate.lift,
        support_ratio=scored.support_ratio,
        cadence=cadence_result.cadence,
        cadence_confidence=cadence_result.confidence,
        cadence_label=cadence_label,
        schedule_hint=cadence_result.schedule_hint,
        title=title,
        description=description,
        why_generated=scored.why_generated,
        explanation_factors=list(scored.explanation_factors),
        median_step_gaps_seconds=list(candidate.median_step_gaps),
        weekday_hint=candidate.weekday_hint,
        area_hint=candidate.area_hint,
        suggested_automation=SuggestedAutomation(
            trigger_entity_id=trigger.entity_id,
            trigger_to_state=trigger.new_state,
            action_entity_ids=[action.entity_id for action in actions],
            action_to_states=[action.new_state for action in actions],
        ),
    )
