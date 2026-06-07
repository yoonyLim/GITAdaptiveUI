from __future__ import annotations

import argparse
import json
import math
import statistics
from pathlib import Path
from collections import defaultdict
from typing import Any

from analysis_d2e.src.io_utils import read_jsonl, safe_float, write_csv, write_json
from analysis_d2e.src.paths import MODELS_DIR, PROCESSED_DIR, REPORTS_DIR, ensure_dirs
from analysis_d2e.src.schemas import ATOMIC_ACTIONS


FEATURE_POLICY = "frame_history_only_no_raw_input_or_phase_features"
_IMAGE_FEATURE_CACHE: dict[str, list[float]] = {}


def _mean(values: list[float]) -> float:
    return sum(values) / max(1, len(values))


def _std(values: list[float]) -> float:
    if not values:
        return 0.0
    avg = _mean(values)
    return math.sqrt(sum((value - avg) ** 2 for value in values) / len(values))


def _image_features(path: Path) -> list[float]:
    cache_key = str(path)
    if cache_key in _IMAGE_FEATURE_CACHE:
        return _IMAGE_FEATURE_CACHE[cache_key]
    try:
        from PIL import Image

        image = Image.open(path).convert("RGB").resize((32, 32))
        pixels = list(image.getdata())
        count = max(1, len(pixels))
        channels = [[pixel[channel] / 255.0 for pixel in pixels] for channel in range(3)]
        means = [_mean(channel) for channel in channels]
        stds = [_std(channel) for channel in channels]
        brightness = [sum(pixel) / (255.0 * 3) for pixel in pixels]
        overall = _mean(brightness)
        bright_std = _std(brightness)
        quadrants = []
        for qy in (0, 16):
            for qx in (0, 16):
                vals = []
                for y in range(qy, qy + 16):
                    for x in range(qx, qx + 16):
                        vals.append(brightness[y * 32 + x])
                quadrants.append(_mean(vals))
        center_vals = []
        for y in range(10, 22):
            for x in range(10, 22):
                center_vals.append(brightness[y * 32 + x])
        horizontal_edges = []
        vertical_edges = []
        for y in range(32):
            for x in range(31):
                horizontal_edges.append(abs(brightness[y * 32 + x + 1] - brightness[y * 32 + x]))
        for y in range(31):
            for x in range(32):
                vertical_edges.append(abs(brightness[(y + 1) * 32 + x] - brightness[y * 32 + x]))
        rg_gap = means[0] - means[1]
        gb_gap = means[1] - means[2]
        result = (
            means
            + stds
            + [overall, bright_std, min(brightness), max(brightness)]
            + quadrants
            + [_mean(center_vals), _mean(horizontal_edges), _mean(vertical_edges), rg_gap, gb_gap]
        )
        _IMAGE_FEATURE_CACHE[cache_key] = result
        return result
    except Exception:
        seed = abs(hash(str(path))) % 1000 / 1000.0
        result = [seed, 1 - seed, 0.5, seed * 0.5] + [0.0] * 15
        _IMAGE_FEATURE_CACHE[cache_key] = result
        return result


def featurize_sample(sample: dict[str, Any]) -> list[float]:
    paths = [Path(path) for path in sample.get("history_frame_paths") or [sample.get("frame_path", "")]]
    all_features = [_image_features(path) for path in paths if str(path)]
    if not all_features:
        all_features = [[0.0] * 8]
    dim = len(all_features[0])
    means = [sum(feature[i] for feature in all_features) / len(all_features) for i in range(dim)]
    last = all_features[-1]
    first = all_features[0]
    delta = [last[i] - first[i] for i in range(dim)]
    temporal_std = [_std([feature[i] for feature in all_features]) for i in range(dim)]
    return means + last + delta + temporal_std


def _weighted_centroid(features: list[list[float]], weights: list[float]) -> list[float]:
    total = sum(weights)
    if total <= 0:
        return [0.0] * len(features[0])
    return [sum(feature[i] * weight for feature, weight in zip(features, weights)) / total for i in range(len(features[0]))]


def _distance(a: list[float], b: list[float]) -> float:
    return math.sqrt(sum((x - y) ** 2 for x, y in zip(a, b)))


def _softmax(scores: dict[str, float]) -> dict[str, float]:
    max_score = max(scores.values())
    exps = {key: math.exp(value - max_score) for key, value in scores.items()}
    total = sum(exps.values()) or 1.0
    return {key: value / total for key, value in exps.items()}


class D2EActionPriorModel:
    def __init__(self, centroids: dict[str, list[float]] | None = None, base_prior: dict[str, float] | None = None, temperature: float = 4.0) -> None:
        self.centroids = centroids or {}
        self.base_prior = base_prior or {action: 1.0 / len(ATOMIC_ACTIONS) for action in ATOMIC_ACTIONS}
        self.temperature = temperature

    def fit(self, samples: list[dict[str, Any]]) -> None:
        if not samples:
            raise ValueError("No D2E samples available for training")
        features = [featurize_sample(sample) for sample in samples]
        base = {}
        for action in ATOMIC_ACTIONS:
            weights = [safe_float(sample.get("atomic_distribution", {}).get(action), 0.0) for sample in samples]
            self.centroids[action] = _weighted_centroid(features, weights)
            base[action] = statistics.mean(weights) if weights else 0.0
        total = sum(base.values()) or 1.0
        self.base_prior = {action: max(1e-6, base[action] / total) for action in ATOMIC_ACTIONS}

    def predict(self, sample: dict[str, Any]) -> dict[str, float]:
        feature = featurize_sample(sample)
        if not self.centroids:
            return dict(self.base_prior)
        scores = {}
        for action in ATOMIC_ACTIONS:
            distance = _distance(feature, self.centroids[action])
            scores[action] = -self.temperature * distance + math.log(max(1e-6, self.base_prior.get(action, 1e-6)))
        return _softmax(scores)

    def to_dict(self) -> dict[str, Any]:
        return {"centroids": self.centroids, "base_prior": self.base_prior, "temperature": self.temperature, "actions": list(ATOMIC_ACTIONS)}

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "D2EActionPriorModel":
        return cls(centroids={k: list(v) for k, v in data.get("centroids", {}).items()}, base_prior=data.get("base_prior"), temperature=safe_float(data.get("temperature"), 4.0))


def evaluate_predictions(model: D2EActionPriorModel, samples: list[dict[str, Any]]) -> dict[str, Any]:
    top1 = 0
    kl_total = 0.0
    rows = []
    for sample in samples:
        pred = model.predict(sample)
        target = sample.get("atomic_distribution", {})
        pred_top = max(ATOMIC_ACTIONS, key=lambda action: pred.get(action, 0.0))
        true_top = max(ATOMIC_ACTIONS, key=lambda action: safe_float(target.get(action), 0.0))
        top1 += int(pred_top == true_top)
        kl = 0.0
        for action in ATOMIC_ACTIONS:
            y = max(1e-6, safe_float(target.get(action), 0.0))
            p = max(1e-6, pred.get(action, 0.0))
            kl += y * math.log(y / p)
        kl_total += kl
        rows.append({"sample_id": sample.get("sample_id"), "game_id": sample.get("game_id"), "true_top": true_top, "pred_top": pred_top, "kl": kl})
    return {
        "top1_action_accuracy": top1 / max(1, len(samples)),
        "mean_target_prediction_kl": kl_total / max(1, len(samples)),
        "rows": rows,
    }


def per_game_metrics(model: D2EActionPriorModel, samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        grouped[str(sample.get("game_id", "unknown"))].append(sample)
    rows = []
    for game_id, group in sorted(grouped.items()):
        result = evaluate_predictions(model, group)
        rows.append(
            {
                "game_id": game_id,
                "sample_count": len(group),
                "top1_action_accuracy": result["top1_action_accuracy"],
                "mean_target_prediction_kl": result["mean_target_prediction_kl"],
            }
        )
    return rows


def heldout_game_metrics(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    games = sorted({str(sample.get("game_id", "unknown")) for sample in samples})
    rows = []
    for game_id in games:
        train = [sample for sample in samples if str(sample.get("game_id", "unknown")) != game_id]
        test = [sample for sample in samples if str(sample.get("game_id", "unknown")) == game_id]
        if not train or not test:
            continue
        model = D2EActionPriorModel()
        model.fit(train)
        result = evaluate_predictions(model, test)
        rows.append(
            {
                "heldout_game_id": game_id,
                "train_count": len(train),
                "test_count": len(test),
                "top1_action_accuracy": result["top1_action_accuracy"],
                "mean_target_prediction_kl": result["mean_target_prediction_kl"],
            }
        )
    return rows


def heldout_episode_metrics(samples: list[dict[str, Any]]) -> list[dict[str, Any]]:
    groups: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for sample in samples:
        game_id = str(sample.get("game_id", "unknown"))
        episode_id = str(sample.get("episode_id") or sample.get("source_manifest") or "unknown")
        groups[(game_id, episode_id)].append(sample)
    rows = []
    for (game_id, episode_id), test in sorted(groups.items()):
        train = [sample for sample in samples if not (str(sample.get("game_id", "unknown")) == game_id and str(sample.get("episode_id") or sample.get("source_manifest") or "unknown") == episode_id)]
        if not train or not test:
            continue
        model = D2EActionPriorModel()
        model.fit(train)
        result = evaluate_predictions(model, test)
        rows.append(
            {
                "heldout_game_id": game_id,
                "heldout_episode_id": episode_id,
                "train_count": len(train),
                "test_count": len(test),
                "top1_action_accuracy": result["top1_action_accuracy"],
                "mean_target_prediction_kl": result["mean_target_prediction_kl"],
            }
        )
    return rows


def train_model(samples_path: Path | None = None, min_label_confidence: float = 0.0) -> dict[str, Any]:
    ensure_dirs()
    samples_path = samples_path or (PROCESSED_DIR / "d2e_action_prior_samples.jsonl")
    all_samples = read_jsonl(samples_path)
    samples = [sample for sample in all_samples if safe_float(sample.get("action_label_confidence"), 0.0) >= min_label_confidence]
    if not samples:
        row = {
            "status": "no_d2e_samples",
            "samples_path": str(samples_path),
            "raw_sample_count": len(all_samples),
            "min_label_confidence": min_label_confidence,
        }
        write_csv(REPORTS_DIR / "d2e_action_prior_training_metrics.csv", [row])
        write_json(MODELS_DIR / "d2e_action_prior_model.json", {"status": "no_d2e_samples"})
        return row
    train = [sample for idx, sample in enumerate(samples) if idx % 5 != 0]
    test = [sample for idx, sample in enumerate(samples) if idx % 5 == 0] or samples[-max(1, len(samples) // 5) :]
    model = D2EActionPriorModel()
    model.fit(train)
    result = evaluate_predictions(model, test)
    metrics = {
        "status": "trained",
        "feature_policy": FEATURE_POLICY,
        "raw_sample_count": len(all_samples),
        "sample_count": len(samples),
        "filtered_low_confidence_count": len(all_samples) - len(samples),
        "min_label_confidence": min_label_confidence,
        "train_count": len(train),
        "test_count": len(test),
        "top1_action_accuracy": result["top1_action_accuracy"],
        "mean_target_prediction_kl": result["mean_target_prediction_kl"],
        "primary_subset_barony_rows": sum(1 for sample in samples if sample.get("is_primary_subset")),
        "mean_history_frame_count": statistics.mean(len(sample.get("history_frame_paths") or [sample.get("frame_path")]) for sample in samples),
    }
    write_csv(REPORTS_DIR / "d2e_action_prior_predictions.csv", result["rows"])
    write_csv(REPORTS_DIR / "d2e_action_prior_per_game_metrics.csv", per_game_metrics(model, test))
    write_csv(REPORTS_DIR / "d2e_action_prior_heldout_game_metrics.csv", heldout_game_metrics(samples))
    write_csv(REPORTS_DIR / "d2e_action_prior_heldout_episode_metrics.csv", heldout_episode_metrics(samples))
    write_csv(REPORTS_DIR / "d2e_action_prior_training_metrics.csv", [metrics])
    write_csv(
        REPORTS_DIR / "d2e_feature_audit.csv",
        [
            {
                "feature_policy": FEATURE_POLICY,
                "uses_frame_history": True,
                "uses_raw_input_as_feature": False,
                "uses_phase_as_feature": False,
                "raw_input_role": "training_target_proxy_only",
                "phase_role": "analysis_grouping_only_not_model_input",
            }
        ],
    )
    write_json(MODELS_DIR / "d2e_action_prior_model.json", {"status": "trained", **model.to_dict()})
    return metrics


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", type=Path, default=None)
    parser.add_argument("--min-label-confidence", type=float, default=0.0)
    args = parser.parse_args()
    print(json.dumps(train_model(args.samples, args.min_label_confidence), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
