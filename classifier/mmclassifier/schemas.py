from __future__ import annotations

from typing import Any, Dict, List

from pydantic import BaseModel, field_validator


class Question(BaseModel):
    type: str
    instructions: str
    criteria: Dict[str, str]

    @field_validator("type")
    @classmethod
    def _type_must_be_choice(cls, value: str) -> str:
        if value != "choice":
            raise ValueError("question type must be 'choice'")
        return value

    @field_validator("criteria")
    @classmethod
    def _needs_at_least_two_criteria(cls, value: Dict[str, str]) -> Dict[str, str]:
        if len(value) < 2:
            raise ValueError("question criteria must have at least 2 options")
        return value


class ClassifyRequest(BaseModel):
    model: str
    questions: Dict[str, Question]
    states: List[Dict[str, Any]]


class AnswerOut(BaseModel):
    choice: str
    confidence: float
    agreement: float
    probabilities: Dict[str, float]


class ClassifyResultOut(BaseModel):
    answers: Dict[str, AnswerOut]


class ClassifyResponse(BaseModel):
    model: str
    members: int
    results: List[ClassifyResultOut]


class SystemOneRequest(BaseModel):
    model: str
    state: Dict[str, Any]
    questions: Dict[str, Question]


class SystemOneAnswerOut(BaseModel):
    type: str = "choice"
    choice: str
    confidence: float
    probabilities: Dict[str, float]


class UsageOut(BaseModel):
    input_tokens: int = 0
    output_tokens: int = 0


class SystemOneResponse(BaseModel):
    model: str
    answers: Dict[str, SystemOneAnswerOut]
    usage: UsageOut = UsageOut()


class HealthResponse(BaseModel):
    device: str
    models: Dict[str, int]
