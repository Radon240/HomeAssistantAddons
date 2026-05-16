from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse

from app.feedback_service import (
    apply_feedback_to_learner,
    get_feedback_learner,
    reset_feedback_items,
    reset_feedback_state,
)
from app.anomaly_detector import detect_anomalies
from app.models import (
    AnalyzeOptions,
    AnalyzeRequest,
    AnalyzeResponse,
    AnomalyDetectRequest,
    AnomalyDetectionOptions,
    AnomalyResponse,
    FeedbackRequest,
    FeedbackResetItemsRequest,
    FeedbackResponse,
    FeedbackStateResponse,
)
from app.recommender import analyze_events

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s [ml-service] %(message)s",
)
logger = logging.getLogger(__name__)

MAX_EVENTS_PER_REQUEST = 3000


@asynccontextmanager
async def lifespan(_: FastAPI):
    logger.info("Behavior analysis ML service started")
    yield
    logger.info("Behavior analysis ML service stopped")


app = FastAPI(
    title="Home AI Behavior Analysis",
    version="0.1.0",
    lifespan=lifespan,
    response_model_by_alias=True,
)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "Healthy", "service": "behavior-analysis"}


@app.post("/api/v1/feedback", response_model=FeedbackResponse)
async def submit_feedback(request: FeedbackRequest) -> FeedbackResponse:
    try:
        learner = await asyncio.to_thread(apply_feedback_to_learner, request)
        return FeedbackResponse(
            accepted=True,
            training_samples=learner.training_samples,
            message="Feedback recorded",
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:
        logger.exception("Feedback failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc


@app.get("/api/v1/feedback/stats")
def feedback_stats() -> dict[str, int]:
    learner = get_feedback_learner()
    return {"trainingSamples": learner.training_samples}


@app.get("/api/v1/feedback/state", response_model=FeedbackStateResponse)
def feedback_state() -> FeedbackStateResponse:
    snapshot = get_feedback_learner().snapshot()
    return FeedbackStateResponse(
        training_samples=snapshot.training_samples,
        pattern_useful=snapshot.pattern_useful,
        pattern_not_useful=snapshot.pattern_not_useful,
        entity_useful=snapshot.entity_useful,
        entity_not_useful=snapshot.entity_not_useful,
        dismissed_until=snapshot.dismissed_until,
    )


@app.delete("/api/v1/feedback", response_model=FeedbackResponse)
async def reset_feedback() -> FeedbackResponse:
    learner = await asyncio.to_thread(reset_feedback_state)
    return FeedbackResponse(
        accepted=True,
        training_samples=learner.training_samples,
        message="Feedback state reset",
    )


@app.post("/api/v1/feedback/reset-items", response_model=FeedbackResponse)
async def reset_feedback_selected(request: FeedbackResetItemsRequest) -> FeedbackResponse:
    if not request.pattern_keys and not request.recommendation_ids and not request.entity_ids:
        raise HTTPException(
            status_code=400,
            detail="Provide at least one patternKeys, recommendationIds or entityIds item.",
        )
    learner = await asyncio.to_thread(reset_feedback_items, request)
    return FeedbackResponse(
        accepted=True,
        training_samples=learner.training_samples,
        message="Selected feedback items reset",
    )


@app.post("/api/v1/anomalies", response_model=AnomalyResponse)
async def detect(request: AnomalyDetectRequest) -> AnomalyResponse:
    if not request.events:
        raise HTTPException(status_code=400, detail="events must not be empty")

    events = request.events
    if len(events) > MAX_EVENTS_PER_REQUEST:
        logger.info(
            "Truncating events from %s to %s for anomaly detection",
            len(events),
            MAX_EVENTS_PER_REQUEST,
        )
        events = events[-MAX_EVENTS_PER_REQUEST:]

    options = request.options or AnomalyDetectionOptions()
    try:
        result = await asyncio.to_thread(detect_anomalies, events, options)
        logger.info(
            "Anomaly detection on %s events -> %s anomalies",
            result.analyzed_event_count,
            result.anomaly_count,
        )
        return result
    except Exception as exc:
        logger.exception("Anomaly detection failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc


@app.post("/api/v1/analyze", response_model=AnalyzeResponse)
async def analyze(request: AnalyzeRequest) -> AnalyzeResponse:
    if not request.events:
        raise HTTPException(status_code=400, detail="events must not be empty")

    events = request.events
    if len(events) > MAX_EVENTS_PER_REQUEST:
        logger.info(
            "Truncating events from %s to %s for analysis",
            len(events),
            MAX_EVENTS_PER_REQUEST,
        )
        events = events[-MAX_EVENTS_PER_REQUEST:]

    options = request.options or AnalyzeOptions()
    try:
        result = await asyncio.to_thread(analyze_events, events, options)
        logger.info(
            "Analyzed %s events, sessions=%s, recommendations=%s",
            result.analyzed_event_count,
            result.session_count,
            len(result.recommendations),
        )
        return result
    except Exception as exc:
        logger.exception("Analysis failed")
        raise HTTPException(status_code=500, detail=str(exc)) from exc


@app.exception_handler(Exception)
async def unhandled_exception_handler(_: Exception, exc: Exception) -> JSONResponse:
    logger.exception("Unhandled error")
    return JSONResponse(status_code=500, content={"detail": str(exc)})
