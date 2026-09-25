from __future__ import annotations

import asyncio
import json
from typing import Any, Dict, List, Optional

from fastapi import APIRouter, Depends, Header, HTTPException, Request
from starlette.concurrency import run_in_threadpool

from mmclassifier.models import ModelNotFoundError, ModelRegistry
from mmclassifier.schemas import (
    ClassifyRequest,
    ClassifyResponse,
    HealthResponse,
    Question,
    SystemOneAnswerOut,
    SystemOneRequest,
    SystemOneResponse,
)

MAX_STATES = 256
MAX_QUESTIONS = 32
MAX_STATE_CHARS = 50_000


def get_registry(request: Request) -> ModelRegistry:
    return request.app.state.registry


def get_inference_lock(request: Request) -> asyncio.Lock:
    return request.app.state.inference_lock


def require_bearer_token(
    request: Request, authorization: Optional[str] = Header(default=None)
) -> None:
    api_key = request.app.state.api_key
    if not api_key:
        return
    if authorization != f"Bearer {api_key}":
        raise HTTPException(status_code=401, detail="Invalid or missing bearer token")


def check_limits(questions: Dict[str, Question], states: List[Dict[str, Any]]) -> None:
    if len(questions) > MAX_QUESTIONS:
        raise HTTPException(
            status_code=413,
            detail=f"Too many questions: {len(questions)} exceeds the limit of {MAX_QUESTIONS}",
        )
    if len(states) > MAX_STATES:
        raise HTTPException(
            status_code=413,
            detail=f"Too many states: {len(states)} exceeds the limit of {MAX_STATES}",
        )
    for index, state in enumerate(states):
        length = len(json.dumps(state, ensure_ascii=False))
        if length > MAX_STATE_CHARS:
            raise HTTPException(
                status_code=413,
                detail=f"State {index} is {length} characters, exceeding the limit of {MAX_STATE_CHARS}",
            )


def get_ensemble_or_404(registry: ModelRegistry, model_name: str):
    try:
        return registry.get(model_name)
    except ModelNotFoundError as exc:
        raise HTTPException(status_code=404, detail=str(exc)) from exc


def build_router() -> APIRouter:
    router = APIRouter()

    @router.get("/health", response_model=HealthResponse)
    def health(request: Request) -> HealthResponse:
        registry = get_registry(request)
        return HealthResponse(device=registry.device, models=registry.loaded_models())

    @router.post(
        "/v1/classify",
        response_model=ClassifyResponse,
        dependencies=[Depends(require_bearer_token)],
    )
    async def classify(payload: ClassifyRequest, request: Request) -> ClassifyResponse:
        registry = get_registry(request)
        lock = get_inference_lock(request)

        questions = {question_id: question.model_dump() for question_id, question in payload.questions.items()}
        check_limits(payload.questions, payload.states)

        async with lock:
            ensemble = await run_in_threadpool(get_ensemble_or_404, registry, payload.model)
            results = await run_in_threadpool(ensemble.predict, payload.states, questions)

        return ClassifyResponse(model=payload.model, members=ensemble.members, results=results)

    @router.post(
        "/v1/systemone",
        response_model=SystemOneResponse,
        dependencies=[Depends(require_bearer_token)],
    )
    async def systemone(payload: SystemOneRequest, request: Request) -> SystemOneResponse:
        registry = get_registry(request)
        lock = get_inference_lock(request)

        questions = {question_id: question.model_dump() for question_id, question in payload.questions.items()}
        check_limits(payload.questions, [payload.state])

        async with lock:
            ensemble = await run_in_threadpool(get_ensemble_or_404, registry, payload.model)
            [result] = await run_in_threadpool(ensemble.predict, [payload.state], questions)

        answers = {
            question_id: SystemOneAnswerOut(
                choice=answer["choice"],
                confidence=answer["confidence"],
                probabilities=answer["probabilities"],
            )
            for question_id, answer in result["answers"].items()
        }
        return SystemOneResponse(model=payload.model, answers=answers)

    return router
