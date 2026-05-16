from __future__ import annotations

import os
from pathlib import Path

from app.feedback_learner import FeedbackContext, FeedbackLearner, VERDICT_NOT_USEFUL, VERDICT_USEFUL
from app.models import FeedbackResetItemsRequest, FeedbackRequest

_learner: FeedbackLearner | None = None


def get_feedback_learner() -> FeedbackLearner:
    global _learner
    if _learner is None:
        dismiss_days = int(os.environ.get("ML_FEEDBACK_DISMISS_DAYS", "14"))
        _learner = FeedbackLearner(resolve_state_path(), dismiss_days=dismiss_days)
    return _learner


def resolve_state_path() -> Path:
    configured = os.environ.get("ML_FEEDBACK_STATE_PATH")
    if configured:
        return Path(configured)
    return Path("/data/ml-feedback-state.json")


def context_from_request(request: FeedbackRequest) -> FeedbackContext:
    from app.feedback_learner import extract_domains

    entity_ids = tuple(request.entity_ids)
    return FeedbackContext(
        pattern_key=request.pattern_key,
        recommendation_id=request.recommendation_id,
        cadence=request.cadence,
        support_count=request.support_count,
        confidence=request.confidence,
        frequency_score=request.frequency_score,
        entity_ids=entity_ids,
        domains=extract_domains(list(entity_ids)),
    )


def apply_feedback_to_learner(request: FeedbackRequest) -> FeedbackLearner:
    learner = get_feedback_learner()
    if not request.pattern_key:
        raise ValueError("patternKey must not be empty")
    if request.verdict not in {VERDICT_USEFUL, VERDICT_NOT_USEFUL}:
        raise ValueError("verdict must be 'useful' or 'not_useful'")
    learner.record_feedback(context_from_request(request), request.verdict)
    return learner


def reset_feedback_state() -> FeedbackLearner:
    learner = get_feedback_learner()
    learner.reset_all()
    return learner


def reset_feedback_items(request: FeedbackResetItemsRequest) -> FeedbackLearner:
    learner = get_feedback_learner()
    learner.reset_items(
        pattern_keys=tuple(request.pattern_keys),
        recommendation_ids=tuple(request.recommendation_ids),
        entity_ids=tuple(request.entity_ids),
        clear_positive=request.clear_positive,
        clear_negative=request.clear_negative,
        clear_dismissals=request.clear_dismissals,
    )
    return learner
