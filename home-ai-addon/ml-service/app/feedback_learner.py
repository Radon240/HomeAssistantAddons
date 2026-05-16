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


@dataclass(frozen=True)
class FeedbackStateSnapshot:
    training_samples: int
    pattern_useful: dict[str, int]
    pattern_not_useful: dict[str, int]
    entity_useful: dict[str, int]
    entity_not_useful: dict[str, int]
    dismissed_until: dict[str, str]


@dataclass(frozen=True)
class FeedbackResetResult:
    reset_all: bool
    removed_pattern_keys: int
    removed_entity_keys: int
    removed_dismissals: int
    training_samples: int


class FeedbackLearner:
    """Online preference model (River logistic regression) + entity/pattern counters."""

    def __init__(self, state_path: Path, dismiss_days: int = 14) -> None:
        self._state_path = state_path
        self._dismiss_days = dismiss_days
        self._model = self._new_model()
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
            self._register_dismissal(context)

        self._save()
        logger.info(
            "Recorded %s feedback for %s (samples=%s)",
            verdict,
            context.recommendation_id,
            self._training_samples,
        )

    def rank_adjustment(self, context: FeedbackContext) -> RankingAdjustment:
        if self._is_dismissed(context):
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

    def snapshot(self) -> FeedbackStateSnapshot:
        self._drop_expired_dismissals()
        return FeedbackStateSnapshot(
            training_samples=self._training_samples,
            pattern_useful={k: int(v.get()) for k, v in self._pattern_useful.items()},
            pattern_not_useful={k: int(v.get()) for k, v in self._pattern_not_useful.items()},
            entity_useful={k: int(v.get()) for k, v in self._entity_useful.items()},
            entity_not_useful={k: int(v.get()) for k, v in self._entity_not_useful.items()},
            dismissed_until={k: v.isoformat() for k, v in self._dismissed_until.items()},
        )

    def reset_all(self) -> FeedbackResetResult:
        removed_patterns = len(set(self._pattern_useful) | set(self._pattern_not_useful))
        removed_entities = len(set(self._entity_useful) | set(self._entity_not_useful))
        removed_dismissals = len(self._dismissed_until)
        self._pattern_useful.clear()
        self._pattern_not_useful.clear()
        self._entity_useful.clear()
        self._entity_not_useful.clear()
        self._dismissed_until.clear()
        self._training_samples = 0
        self._model = self._new_model()
        self._delete_state_file()
        logger.info("Reset all feedback learner state")
        return FeedbackResetResult(True, removed_patterns, removed_entities, removed_dismissals, 0)

    def reset_items(
        self,
        pattern_keys: tuple[str, ...] = (),
        recommendation_ids: tuple[str, ...] = (),
        entity_ids: tuple[str, ...] = (),
        clear_positive: bool = False,
        clear_negative: bool = True,
        clear_dismissals: bool = True,
    ) -> FeedbackResetResult:
        normalized_patterns = {key.strip() for key in pattern_keys if key and key.strip()}
        normalized_recommendations = {key.strip() for key in recommendation_ids if key and key.strip()}
        normalized_entities = {key.strip() for key in entity_ids if key and key.strip()}

        removed_patterns = 0
        removed_entities = 0
        removed_samples = 0

        for key in normalized_patterns:
            if clear_positive:
                removed_samples += self._remove_counter(self._pattern_useful, key)
            if clear_negative:
                removed_samples += self._remove_counter(self._pattern_not_useful, key)
            removed_patterns += 1

        for entity_id in normalized_entities:
            if clear_positive:
                removed_samples += self._remove_counter(self._entity_useful, entity_id)
            if clear_negative:
                removed_samples += self._remove_counter(self._entity_not_useful, entity_id)
            removed_entities += 1

        removed_dismissals = 0
        if clear_dismissals:
            removed_dismissals = self._remove_dismissals(
                normalized_patterns,
                normalized_recommendations,
                normalized_entities,
            )

        # River weights are intentionally not persisted; reset the in-memory ranker so removed
        # negative examples stop influencing the current process immediately.
        self._model = self._new_model()
        self._training_samples = max(0, self._training_samples - removed_samples)
        self._save()
        logger.info(
            "Reset feedback items: patterns=%s entities=%s dismissals=%s samples=%s",
            removed_patterns,
            removed_entities,
            removed_dismissals,
            removed_samples,
        )
        return FeedbackResetResult(
            False,
            removed_patterns,
            removed_entities,
            removed_dismissals,
            self._training_samples,
        )

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

    def _dismissal_keys(self, context: FeedbackContext) -> tuple[str, ...]:
        keys: list[str] = []
        if context.pattern_key.strip():
            keys.append(context.pattern_key)
        if context.recommendation_id.strip():
            keys.append(context.recommendation_id)
        if context.entity_ids:
            keys.append(self._entity_signature(context.entity_ids))
        return tuple(keys)

    @staticmethod
    def _entity_signature(entity_ids: tuple[str, ...]) -> str:
        return "entities:" + "|".join(sorted(entity_ids))

    def _register_dismissal(self, context: FeedbackContext) -> None:
        until = datetime.now(timezone.utc) + timedelta(days=self._dismiss_days)
        for key in self._dismissal_keys(context):
            self._dismissed_until[key] = until
        logger.info(
            "Dismissed recommendation keys until %s: %s",
            until.isoformat(),
            list(self._dismissal_keys(context)),
        )

    def _is_dismissed(self, context: FeedbackContext) -> bool:
        self._drop_expired_dismissals()
        for key in self._dismissal_keys(context):
            until = self._dismissed_until.get(key)
            if until is None:
                continue
            return True
        return False

    def _drop_expired_dismissals(self) -> None:
        now = datetime.now(timezone.utc)
        expired = [key for key, until in self._dismissed_until.items() if until <= now]
        for key in expired:
            del self._dismissed_until[key]

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

    @staticmethod
    def _remove_counter(store: dict[str, stats.Sum], key: str) -> int:
        counter = store.pop(key, None)
        return int(counter.get()) if counter is not None else 0

    def _remove_dismissals(
        self,
        pattern_keys: set[str],
        recommendation_ids: set[str],
        entity_ids: set[str],
    ) -> int:
        to_remove: set[str] = set()
        to_remove.update(key for key in pattern_keys if key in self._dismissed_until)
        to_remove.update(key for key in recommendation_ids if key in self._dismissed_until)
        for key in self._dismissed_until:
            if not key.startswith("entities:"):
                continue
            parts = set(key.removeprefix("entities:").split("|"))
            if parts & entity_ids:
                to_remove.add(key)
        for key in to_remove:
            self._dismissed_until.pop(key, None)
        return len(to_remove)

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

    def _delete_state_file(self) -> None:
        try:
            self._state_path.unlink(missing_ok=True)
        except Exception as exc:
            logger.warning("Failed to delete feedback state file %s: %s", self._state_path, exc)

    @staticmethod
    def _new_model():
        return compose.Pipeline(
            preprocessing.StandardScaler(),
            linear_model.LogisticRegression(optimizer=optim.SGD(0.08)),
        )


def extract_domains(entity_ids: list[str]) -> tuple[str, ...]:
    domains = {entity_id.split(".", 1)[0] for entity_id in entity_ids if "." in entity_id}
    return tuple(sorted(domains))
