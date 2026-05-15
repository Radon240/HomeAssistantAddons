from __future__ import annotations

import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse

from app.feedback_service import apply_feedback_to_learner, get_feedback_learner
from app.models import AnalyzeOptions, AnalyzeRequest, AnalyzeResponse, FeedbackRequest, FeedbackResponse
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
