from __future__ import annotations

import argparse
import json
import math
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any

from analysis_d2e.src.io_utils import read_jsonl, safe_float, write_csv, write_json
from analysis_d2e.src.offense_defense_mapping import EVENT_LABELS
from analysis_d2e.src.paths import MODELS_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.threat_state_features import STATE_FEATURE_KEYS, state_feature_vector


TRAINABLE_LABELS = ("offense", "explicit_defense", "movement_only")
MODEL_FEATURE_POLICY = "state_proxy_only_no_raw_input_feature"


def _softmax(scores: dict[str, float]) -> dict[str, float]:
    max_score = max(scores.values())
    exps = {key: math.exp(value - max_score) for key, value in scores.items()}
    total = sum(exps.values()) or 1.0
    return {key: value / total for key, value in exps.items()}


def _mean_vector(features: list[list[float]]) -> list[float]:
    if not features:
        return [0.0] * len(STATE_FEATURE_KEYS)
    return [sum(feature[i] for feature in features) / len(features) for i in range(len(features[0]))]


def _distance(a: list[float], b: list[float]) -> float:
    return math.sqrt(sum((left - right) ** 2 for left, right in zip(a, b)))


class OffenseDefensePriorModel:
    def __init__(self, centroids: dict[str, list[float]] | None = None, base_prior: dict[str, float] | None = None, temperature: float = 3.0) -> None:
        self.centroids = centroids or {}
        self.base_prior = base_prior or {label: 1.0 / len(TRAINABLE_LABELS) for label in TRAINABLE_LABELS}
        self.temperature = temperature

    def fit(self, rows: list[dict[str, Any]]) -> None:
        grouped: dict[str, list[list[float]]] = defaultdict(list)
        for row in rows:
            label = str(row.get("event_label", "unknown_ignore"))
            if label in TRAINABLE_LABELS:
                grouped[label].append(state_feature_vector(row))
        if not any(grouped.values()):
            raise ValueError("No trainable offense/defense clips available")
        total = sum(len(grouped[label]) for label in TRAINABLE_LABELS)
        for label in TRAINABLE_LABELS:
            self.centroids[label] = _mean_vector(grouped[label])
            self.base_prior[label] = max(1e-6, len(grouped[label]) / max(1, total))

    def predict(self, row: dict[str, Any]) -> dict[str, float]:
        feature = state_feature_vector(row)
        scores = {}
        for label in TRAINABLE_LABELS:
            centroid = self.centroids.get(label, [0.0] * len(feature))
            scores[label] = -self.temperature * _distance(feature, centroid) + math.log(max(1e-6, self.base_prior.get(label, 1e-6)))
        return _softmax(scores)

    def to_dict(self) -> dict[str, Any]:
        return {
            "status": "trained",
            "labels": list(TRAINABLE_LABELS),
            "feature_keys": list(STATE_FEATURE_KEYS),
            "feature_policy": MODEL_FEATURE_POLICY,
            "centroids": self.centroids,
            "base_prior": self.base_prior,
            "temperature": self.temperature,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "OffenseDefensePriorModel":
        return cls(
            centroids={key: [float(value) for value in values] for key, values in data.get("centroids", {}).items()},
            base_prior={key: float(value) for key, value in data.get("base_prior", {}).items()},
            temperature=safe_float(data.get("temperature"), 3.0),
        )


def _split_by_episode(rows: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    episodes = sorted({str(row.get("episode_id", "unknown")) for row in rows})
    if len(episodes) < 2:
        split = max(1, int(len(rows) * 0.8))
        return rows[:split], rows[split:]
    test_count = max(1, int(math.ceil(len(episodes) * 0.2)))
    test_episodes = set(episodes[-test_count:])
    train = [row for row in rows if str(row.get("episode_id", "unknown")) not in test_episodes]
    test = [row for row in rows if str(row.get("episode_id", "unknown")) in test_episodes]
    return train, test


def _macro_f1(confusion: Counter[tuple[str, str]]) -> float:
    scores = []
    for label in TRAINABLE_LABELS:
        tp = confusion[(label, label)]
        fp = sum(confusion[(true, label)] for true in TRAINABLE_LABELS if true != label)
        fn = sum(confusion[(label, pred)] for pred in TRAINABLE_LABELS if pred != label)
        precision = tp / max(1, tp + fp)
        recall = tp / max(1, tp + fn)
        scores.append(0.0 if precision + recall == 0.0 else 2 * precision * recall / (precision + recall))
    return sum(scores) / max(1, len(scores))


def evaluate_model(model: OffenseDefensePriorModel, rows: list[dict[str, Any]]) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    confusion: Counter[tuple[str, str]] = Counter()
    predictions = []
    correct = 0
    for row in rows:
        true = str(row.get("event_label", "unknown_ignore"))
        if true not in TRAINABLE_LABELS:
            continue
        pred_dist = model.predict(row)
        pred = max(TRAINABLE_LABELS, key=lambda label: pred_dist.get(label, 0.0))
        correct += int(pred == true)
        confusion[(true, pred)] += 1
        predictions.append(
            {
                "clip_id": row.get("clip_id"),
                "true_label": true,
                "pred_label": pred,
                "offense_prior": pred_dist.get("offense", 0.0),
                "explicit_defense_prior": pred_dist.get("explicit_defense", 0.0),
                "movement_only_prior": pred_dist.get("movement_only", 0.0),
                "threat_score": row.get("threat_score"),
            }
        )
    metrics = {
        "test_rows": len(predictions),
        "accuracy": correct / max(1, len(predictions)),
        "macro_f1": _macro_f1(confusion),
    }
    confusion_rows = [
        {"true_label": true, "pred_label": pred, "count": confusion[(true, pred)]}
        for true in TRAINABLE_LABELS
        for pred in TRAINABLE_LABELS
    ]
    return {**metrics, "confusion_rows": confusion_rows}, predictions


def train_offense_defense_prior(clips_path: Path | None = None, min_confidence: float = 0.5, write_outputs: bool = True) -> dict[str, Any]:
    ensure_dirs()
    clips_path = clips_path or (PROCESSED_DIR / "core_keeper_2key_event_clips.jsonl")
    all_rows = read_jsonl(clips_path)
    rows = [
        row
        for row in all_rows
        if str(row.get("event_label")) in TRAINABLE_LABELS and safe_float(row.get("event_label_confidence"), 0.0) >= min_confidence
    ]
    train, test = _split_by_episode(rows)
    if not train or not test:
        result = {
            "status": "insufficient_data",
            "feature_policy": MODEL_FEATURE_POLICY,
            "raw_clip_count": len(all_rows),
            "trainable_clip_count": len(rows),
            "train_count": len(train),
            "test_count": len(test),
            "min_confidence": min_confidence,
        }
        if write_outputs:
            write_csv(REPORTS_DIR / "core_keeper_offense_defense_metrics.csv", [result])
        return result
    model = OffenseDefensePriorModel()
    model.fit(train)
    metrics, predictions = evaluate_model(model, test)
    label_counts = Counter(row.get("event_label") for row in rows)
    result = {
        "status": "trained",
        "feature_policy": MODEL_FEATURE_POLICY,
        "raw_clip_count": len(all_rows),
        "trainable_clip_count": len(rows),
        "train_count": len(train),
        "test_count": len(test),
        "min_confidence": min_confidence,
        "accuracy": metrics["accuracy"],
        "macro_f1": metrics["macro_f1"],
        "offense_rows": label_counts.get("offense", 0),
        "explicit_defense_rows": label_counts.get("explicit_defense", 0),
        "movement_only_rows": label_counts.get("movement_only", 0),
    }
    model_json = model.to_dict()
    model_json.update(result)
    if write_outputs:
        write_json(MODELS_DIR / "core_keeper_offense_defense_prior_model.json", model_json)
        write_csv(REPORTS_DIR / "core_keeper_offense_defense_metrics.csv", [result])
        write_csv(REPORTS_DIR / "core_keeper_offense_defense_confusion.csv", metrics["confusion_rows"])
        write_csv(REPORTS_DIR / "core_keeper_offense_defense_predictions.csv", predictions)
        write_csv(
            REPORTS_DIR / "core_keeper_offense_defense_feature_audit.csv",
            [
                {
                    "feature_policy": MODEL_FEATURE_POLICY,
                    "uses_raw_input_as_feature": False,
                    "uses_event_label_as_feature": False,
                    "raw_input_role": "weak_action_label_proxy_only",
                    "state_feature_source": "frame_salience_proxy_and_available_metadata",
                    "feature_keys": ",".join(STATE_FEATURE_KEYS),
                }
            ],
        )
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--clips", type=Path, default=None)
    parser.add_argument("--min-confidence", type=float, default=0.5)
    args = parser.parse_args()
    print(json.dumps(train_offense_defense_prior(args.clips, args.min_confidence), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
