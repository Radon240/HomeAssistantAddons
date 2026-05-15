from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path

from river import compose, linear_model, optim, preprocessing
from river import stats

logger = logging.getLogger(__name__)

VERDICT_USEFUL = "useful"
VERDICT_NOT_USEFUL = "not_useful"


@dataclass(frozen=True)
class FeedbackContext:
    pattern_key: str
    recommendation_id: str
    cadence: str
    support_count: int
    confidence: float
    frequency_score: float
    entity_ids: tuple[str, ...]
    domains: tuple[str, ...]


@dataclass(frozen=True)
class RankingAdjustment:
    hidden: bool
    reward_score: float
    model_probability: float
    entity_penalty: float
    final_multiplier: float


class FeedbackLearner:
    """Online preference model (River logistic regression) + entity/pattern counters."""

    def __init__(self, state_path: Path, dismiss_days: int = 14) -> None:
        self._state_path = state_path
        self._dismiss_days = dismiss_days
        self._model = compose.Pipeline(
            preprocessing.StandardScaler(),
            linear_model.LogisticRegression(optimizer=optim.SGD(0.08)),
        )
        self._pattern_useful: dict[str, stats.Sum] = {}
        self._pattern_not_useful: dict[str, stats.Sum] = {}
        self._entity_useful: dict[str, stats.Sum] = {}
        self._entity_not_useful: dict[str, stats.Sum] = {}
        self._dismissed_until: dict[str, datetime] = {}
        self._training_samples = 0
        self._load()

    def record_feedback(self, context: FeedbackContext, verdict: str) -> None:
        label = verdict == VERDICT_USEFUL
        features = self._features(context)
        self._model.learn_one(features, label)
        self._training_samples += 1

        pattern_counter = self._pattern_useful if label else self._pattern_not_useful
        self._touch(pattern_counter, context.pattern_key).update(1)

        for entity_id in context.entity_ids:
            entity_counter = self._entity_useful if label else self._entity_not_useful
            self._touch(entity_counter, entity_id).update(1)

        if verdict == VERDICT_NOT_USEFUL:
            self._dismissed_until[context.pattern_key] = datetime.now(timezone.utc) + timedelta(
                days=self._dismiss_days
            )

        self._save()
        logger.info(
            "Recorded %s feedback for %s (samples=%s)",
            verdict,
            context.recommendation_id,
            self._training_samples,
        )

    def rank_adjustment(self, context: FeedbackContext) -> RankingAdjustment:
        if self._is_dismissed(context.pattern_key):
            return RankingAdjustment(
                hidden=True,
                reward_score=0.0,
                model_probability=0.0,
                entity_penalty=1.0,
                final_multiplier=0.0,
            )

        features = self._features(context)
        probability = self._predict_useful_probability(features)
        reward_score = self._pattern_reward(context.pattern_key)
        entity_penalty = self._entity_penalty(context.entity_ids)

        # Bandit-style blend: online model + smoothed pattern/entity rewards.
        final_multiplier = (
            0.45 * (0.35 + 0.65 * probability)
            + 0.35 * (0.4 + 0.6 * reward_score)
            + 0.20 * (1.0 - entity_penalty)
        )
        final_multiplier = max(0.15, min(1.25, final_multiplier))

        return RankingAdjustment(
            hidden=False,
            reward_score=reward_score,
            model_probability=probability,
            entity_penalty=entity_penalty,
            final_multiplier=final_multiplier,
        )

    def set_dismiss_days(self, days: int) -> None:
        self._dismiss_days = max(1, days)

    @property
    def training_samples(self) -> int:
        return self._training_samples

    def _predict_useful_probability(self, features: dict[str, float | int]) -> float:
        if self._training_samples < 2:
            return 0.5
        proba = self._model.predict_proba_one(features)
        return float(proba.get(True, proba.get("True", 0.5)))

    def _pattern_reward(self, pattern_key: str) -> float:
        useful = self._counter_value(self._pattern_useful, pattern_key)
        not_useful = self._counter_value(self._pattern_not_useful, pattern_key)
        total = useful + not_useful
        if total == 0:
            return 0.5
        return useful / total

    def _entity_penalty(self, entity_ids: tuple[str, ...]) -> float:
        if not entity_ids:
            return 0.0
        penalties: list[float] = []
        for entity_id in entity_ids:
            useful = self._counter_value(self._entity_useful, entity_id)
            not_useful = self._counter_value(self._entity_not_useful, entity_id)
            total = useful + not_useful
            if total == 0:
                continue
            penalties.append(not_useful / total)
        if not penalties:
            return 0.0
        return sum(penalties) / len(penalties)

    def _is_dismissed(self, pattern_key: str) -> bool:
        until = self._dismissed_until.get(pattern_key)
        if until is None:
            return False
        if until <= datetime.now(timezone.utc):
            del self._dismissed_until[pattern_key]
            return False
        return True

    @staticmethod
    def _features(context: FeedbackContext) -> dict[str, float | int]:
        features: dict[str, float | int] = {
            "support": context.support_count,
            "confidence": context.confidence,
            "frequency": context.frequency_score,
            "sequence_length": len(context.entity_ids),
            f"cadence_{context.cadence}": 1,
        }
        for entity_id in context.entity_ids:
            features[f"entity={entity_id}"] = 1
        for domain in context.domains:
            features[f"domain={domain}"] = 1
        return features

    @staticmethod
    def _touch(store: dict[str, stats.Sum], key: str) -> stats.Sum:
        if key not in store:
            store[key] = stats.Sum()
        return store[key]

    @staticmethod
    def _counter_value(store: dict[str, stats.Sum], key: str) -> int:
        counter = store.get(key)
        return int(counter.get()) if counter is not None else 0

    def _load(self) -> None:
        if not self._state_path.exists():
            return
        try:
            raw = json.loads(self._state_path.read_text(encoding="utf-8"))
            self._training_samples = int(raw.get("trainingSamples", 0))
            for key, value in raw.get("patternUseful", {}).items():
                self._pattern_useful[key] = stats.Sum(value)
            for key, value in raw.get("patternNotUseful", {}).items():
                self._pattern_not_useful[key] = stats.Sum(value)
            for key, value in raw.get("entityUseful", {}).items():
                self._entity_useful[key] = stats.Sum(value)
            for key, value in raw.get("entityNotUseful", {}).items():
                self._entity_not_useful[key] = stats.Sum(value)
            for key, iso in raw.get("dismissedUntil", {}).items():
                self._dismissed_until[key] = datetime.fromisoformat(iso)
        except Exception as exc:
            logger.warning("Failed to load feedback state: %s", exc)

    def _save(self) -> None:
        self._state_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "trainingSamples": self._training_samples,
            "patternUseful": {k: int(v.get()) for k, v in self._pattern_useful.items()},
            "patternNotUseful": {k: int(v.get()) for k, v in self._pattern_not_useful.items()},
            "entityUseful": {k: int(v.get()) for k, v in self._entity_useful.items()},
            "entityNotUseful": {k: int(v.get()) for k, v in self._entity_not_useful.items()},
            "dismissedUntil": {
                k: v.isoformat() for k, v in self._dismissed_until.items()
            },
        }
        self._state_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )


def extract_domains(entity_ids: list[str]) -> tuple[str, ...]:
    domains = {entity_id.split(".", 1)[0] for entity_id in entity_ids if "." in entity_id}
    return tuple(sorted(domains))
