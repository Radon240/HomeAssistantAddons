from __future__ import annotations

import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse

from app.models import AnalyzeOptions, AnalyzeRequest, AnalyzeResponse
from app.recommender import analyze_events

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s [ml-service] %(message)s",
)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(_: FastAPI):
    logger.info("Behavior analysis ML service started")
    yield
    logger.info("Behavior analysis ML service stopped")


app = FastAPI(
    title="Home AI Behavior Analysis",
    version="0.1.0",
    lifespan=lifespan,
)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "Healthy", "service": "behavior-analysis"}


@app.post("/api/v1/analyze", response_model=AnalyzeResponse)
def analyze(request: AnalyzeRequest) -> AnalyzeResponse:
    if not request.events:
        raise HTTPException(status_code=400, detail="events must not be empty")

    options = request.options or AnalyzeOptions()
    try:
        result = analyze_events(request.events, options)
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
