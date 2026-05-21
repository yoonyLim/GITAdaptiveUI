from __future__ import annotations

import argparse
import statistics
import time
from collections import Counter
from pathlib import Path
from typing import Any

from analysis_multigame_scene.src.common import (
    MODELS_DIR,
    REPORTS_DIR,
    REPO_ROOT,
    clamp01,
    load_student_training_rows,
    safe_float,
    write_csv,
    write_json,
)
from analysis_multigame_scene.src.paths import FEATURES_DIR
from analysis_multigame_scene.src.student.train_focused_student import (
    FOCUSED_MODEL_DIR,
    FOCUSED_TARGET_CLASSES,
    focused_action_window,
    focused_prior_direction,
    focused_risk_state,
)


def _safe_name(value: str) -> str:
    return value.replace("/", "__").replace("\\", "__").replace(":", "_")


def import_stack() -> tuple[Any, Any, Any, Any, Any, Any]:
    import torch
    import torch.nn as nn
    import torch.nn.functional as functional
    from PIL import Image
    from transformers import AutoImageProcessor, AutoModel

    return torch, nn, functional, Image, AutoImageProcessor, AutoModel


def load_dino(model_name: str, local_files_only: bool = False) -> tuple[Any, Any, str]:
    torch, _nn, _functional, _Image, AutoImageProcessor, AutoModel = import_stack()
    processor = AutoImageProcessor.from_pretrained(model_name, local_files_only=local_files_only)
    model = AutoModel.from_pretrained(model_name, local_files_only=local_files_only)
    model.eval()
    return processor, model, str(model.config.hidden_size)


def build_dino_inputs(paths: list[Path], processor: Any, Image: Any, torch: Any) -> Any:
    images = []
    for path in paths:
        try:
            images.append(Image.open(path).convert("RGB"))
        except Exception:
            images.append(Image.new("RGB", (224, 224), color=(0, 0, 0)))
    return processor(images=images, return_tensors="pt")


def extract_features(
    rows: list[dict[str, Any]],
    model_name: str,
    batch_size: int = 8,
    local_files_only: bool = False,
) -> tuple[Any, list[str], int, str]:
    torch, _nn, _functional, Image, _AutoImageProcessor, _AutoModel = import_stack()
    FEATURES_DIR.mkdir(parents=True, exist_ok=True)
    cache_path = FEATURES_DIR / f"{_safe_name(model_name)}_focused_features.pt"
    sample_ids = [str(row.get("sample_id")) for row in rows]
    if cache_path.exists():
        cached = torch.load(cache_path, map_location="cpu")
        if cached.get("sample_ids") == sample_ids:
            return cached["features"], sample_ids, int(cached["feature_dim"]), str(cached.get("model_name", model_name))

    processor, model, hidden_size = load_dino(model_name, local_files_only=local_files_only)
    features = []
    with torch.no_grad():
        for start in range(0, len(rows), batch_size):
            batch = rows[start : start + batch_size]
            paths = [REPO_ROOT / str(row.get("frame_path", "")) for row in batch]
            inputs = build_dino_inputs(paths, processor, Image, torch)
            outputs = model(**inputs)
            if hasattr(outputs, "pooler_output") and outputs.pooler_output is not None:
                feat = outputs.pooler_output
            else:
                feat = outputs.last_hidden_state[:, 0]
            features.append(feat.detach().cpu())
    feature_tensor = torch.cat(features, dim=0) if features else torch.empty((0, int(hidden_size)))
    torch.save(
        {
            "model_name": model_name,
            "sample_ids": sample_ids,
            "features": feature_tensor,
            "feature_dim": int(feature_tensor.shape[1]) if feature_tensor.ndim == 2 else int(hidden_size),
        },
        cache_path,
    )
    return feature_tensor, sample_ids, int(feature_tensor.shape[1]), model_name


class FeatureDataset:
    def __init__(self, rows: list[dict[str, Any]], by_id: dict[str, dict[str, Any]], features_by_id: dict[str, Any]) -> None:
        self.rows = rows
        self.by_id = by_id
        self.features_by_id = features_by_id

    def tensors(self, torch: Any) -> tuple[Any, dict[str, Any], Any, list[str]]:
        class_to_idx = {target: {cls: idx for idx, cls in enumerate(classes)} for target, classes in FOCUSED_TARGET_CLASSES.items()}
        xs = []
        ys = {target: [] for target in FOCUSED_TARGET_CLASSES}
        urgency = []
        ids = []
        for row in self.rows:
            sample_id = str(row.get("sample_id"))
            label = self.by_id.get(sample_id)
            feature = self.features_by_id.get(sample_id)
            if label is None or feature is None:
                continue
            ids.append(sample_id)
            xs.append(feature)
            target_values = {
                "risk_state": focused_risk_state(label),
                "action_window_prior": focused_action_window(label),
            }
            for target, value in target_values.items():
                ys[target].append(class_to_idx[target].get(value, class_to_idx[target][FOCUSED_TARGET_CLASSES[target][-1]]))
            urgency.append(clamp01(label.get("interaction_demand", {}).get("temporal_urgency", 0.0)))
        return (
            torch.stack(xs, dim=0) if xs else torch.empty((0, 1)),
            {target: torch.tensor(values, dtype=torch.long) for target, values in ys.items()},
            torch.tensor(urgency, dtype=torch.float32),
            ids,
        )


def make_head(nn: Any, feature_dim: int) -> Any:
    class DinoFocusedHead(nn.Module):
        def __init__(self) -> None:
            super().__init__()
            self.shared = nn.Sequential(
                nn.LayerNorm(feature_dim),
                nn.Linear(feature_dim, 256),
                nn.GELU(),
                nn.Dropout(0.25),
                nn.Linear(256, 128),
                nn.GELU(),
                nn.Dropout(0.15),
            )
            self.class_heads = nn.ModuleDict(
                {target: nn.Linear(128, len(classes)) for target, classes in FOCUSED_TARGET_CLASSES.items()}
            )
            self.temporal_urgency_head = nn.Linear(128, 1)

        def forward(self, x: Any) -> dict[str, Any]:
            h = self.shared(x)
            return {
                "class_logits": {target: head(h) for target, head in self.class_heads.items()},
                "temporal_urgency": self.temporal_urgency_head(h).sigmoid().squeeze(1),
            }

    return DinoFocusedHead()


def train_head(
    model_name: str,
    split_rows: dict[str, list[dict[str, Any]]],
    by_id: dict[str, dict[str, Any]],
    features_by_id: dict[str, Any],
    feature_dim: int,
) -> tuple[dict[str, Any], Any]:
    torch, nn, functional, _Image, _AutoImageProcessor, _AutoModel = import_stack()
    torch.manual_seed(1337)
    net = make_head(nn, feature_dim)
    train_x, train_y, train_urgency, _ids = FeatureDataset(split_rows["train"], by_id, features_by_id).tensors(torch)
    valid_x, valid_y, valid_urgency, _valid_ids = FeatureDataset(split_rows.get("valid", []), by_id, features_by_id).tensors(torch)
    if valid_x.numel() == 0:
        valid_x, valid_y, valid_urgency = train_x, train_y, train_urgency

    class_weights = {}
    for target, classes in FOCUSED_TARGET_CLASSES.items():
        counts = Counter()
        for row in split_rows["train"]:
            label = by_id[str(row.get("sample_id"))]
            counts[focused_risk_state(label) if target == "risk_state" else focused_action_window(label)] += 1
        total = sum(counts.values()) or 1
        weights = [total / max(1, counts.get(cls, 0)) for cls in classes]
        mean_weight = statistics.mean(weights) if weights else 1.0
        class_weights[target] = torch.tensor([w / max(1e-6, mean_weight) for w in weights], dtype=torch.float32)

    optimizer = torch.optim.AdamW(net.parameters(), lr=0.0015, weight_decay=0.02)
    epochs = 260
    batch_size = min(32, max(8, int(train_x.shape[0])))
    best_state = None
    best_valid = float("inf")
    train_log = []
    indices = list(range(int(train_x.shape[0])))
    for epoch in range(epochs):
        net.train()
        random_order = indices[:]
        import random

        random.shuffle(random_order)
        losses = []
        for start in range(0, len(random_order), batch_size):
            idx = torch.tensor(random_order[start : start + batch_size], dtype=torch.long)
            out = net(train_x[idx])
            loss = torch.tensor(0.0)
            for target in FOCUSED_TARGET_CLASSES:
                loss = loss + functional.cross_entropy(out["class_logits"][target], train_y[target][idx], weight=class_weights[target])
            loss = loss + 1.6 * functional.mse_loss(out["temporal_urgency"], train_urgency[idx])
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            losses.append(float(loss.detach().cpu()))

        net.eval()
        with torch.no_grad():
            out_valid = net(valid_x)
            valid_loss = torch.tensor(0.0)
            for target in FOCUSED_TARGET_CLASSES:
                valid_loss = valid_loss + functional.cross_entropy(
                    out_valid["class_logits"][target],
                    valid_y[target],
                    weight=class_weights[target],
                )
            valid_loss = valid_loss + 1.6 * functional.mse_loss(out_valid["temporal_urgency"], valid_urgency)
        valid_score = float(valid_loss.detach().cpu())
        if valid_score < best_valid:
            best_valid = valid_score
            best_state = {key: value.detach().cpu().clone() for key, value in net.state_dict().items()}
        if epoch in {0, epochs - 1} or (epoch + 1) % 50 == 0:
            train_log.append(
                {
                    "model": model_name,
                    "epoch": epoch + 1,
                    "train_loss": statistics.mean(losses) if losses else 0.0,
                    "valid_loss": valid_score,
                }
            )
    if best_state is not None:
        net.load_state_dict(best_state)
    write_csv(REPORTS_DIR / "dino_focused_student_training_log.csv", train_log)
    checkpoint_path = FOCUSED_MODEL_DIR / f"{_safe_name(model_name)}_head.pt"
    torch.save(
        {
            "state_dict": net.state_dict(),
            "model_name": model_name,
            "feature_dim": feature_dim,
            "target_classes": FOCUSED_TARGET_CLASSES,
            "outputs": ["risk_state", "action_window_prior", "temporal_urgency"],
        },
        checkpoint_path,
    )
    return {
        "checkpoint_path": str(checkpoint_path),
        "best_valid_loss": best_valid,
        "epochs": epochs,
        "train_rows": len(split_rows["train"]),
        "valid_rows": len(split_rows.get("valid", [])),
        "test_rows": len(split_rows.get("test", [])),
    }, net


def predict_head(net: Any, feature: Any, torch: Any, functional: Any) -> dict[str, Any]:
    with torch.no_grad():
        out = net(feature.unsqueeze(0))
        pred = {}
        confidences = []
        for target, classes in FOCUSED_TARGET_CLASSES.items():
            probs = functional.softmax(out["class_logits"][target][0], dim=0)
            idx = int(torch.argmax(probs).detach().cpu())
            pred[target] = classes[idx]
            confidences.append(float(probs[idx].detach().cpu()))
        pred["temporal_urgency"] = clamp01(float(out["temporal_urgency"][0].detach().cpu()))
        pred["confidence"] = statistics.mean(confidences) if confidences else 0.0
        pred["prior_direction"] = focused_prior_direction(
            str(pred["risk_state"]),
            str(pred["action_window_prior"]),
            safe_float(pred["temporal_urgency"]),
        )
        return pred


def evaluate_head(
    model_name: str,
    net: Any,
    split_rows: dict[str, list[dict[str, Any]]],
    by_id: dict[str, dict[str, Any]],
    features_by_id: dict[str, Any],
) -> dict[str, Any]:
    torch, _nn, functional, _Image, _AutoImageProcessor, _AutoModel = import_stack()
    eval_rows = []
    correct = Counter()
    total = Counter()
    urgency_error = 0.0
    for row in split_rows.get("test", []):
        sample_id = str(row.get("sample_id"))
        label = by_id.get(sample_id)
        feature = features_by_id.get(sample_id)
        if label is None or feature is None:
            continue
        pred = predict_head(net, feature, torch, functional)
        true_risk = focused_risk_state(label)
        true_action = focused_action_window(label)
        true_urgency = clamp01(label.get("interaction_demand", {}).get("temporal_urgency", 0.0))
        true_prior = focused_prior_direction(true_risk, true_action, true_urgency)
        for target, y, p in [
            ("risk_state", true_risk, str(pred["risk_state"])),
            ("action_window_prior", true_action, str(pred["action_window_prior"])),
            ("prior_direction", true_prior, str(pred["prior_direction"])),
        ]:
            total[target] += 1
            correct[target] += int(y == p)
        urgency_error += abs(true_urgency - safe_float(pred["temporal_urgency"]))
        eval_rows.append(
            {
                "model": model_name,
                "sample_id": sample_id,
                "scenario_name": row.get("scenario_name"),
                "true_risk_state": true_risk,
                "pred_risk_state": pred["risk_state"],
                "true_action_window_prior": true_action,
                "pred_action_window_prior": pred["action_window_prior"],
                "true_temporal_urgency": true_urgency,
                "pred_temporal_urgency": pred["temporal_urgency"],
                "true_prior_direction": true_prior,
                "pred_prior_direction": pred["prior_direction"],
                "confidence": pred["confidence"],
            }
        )
    write_csv(REPORTS_DIR / "dino_focused_student_predictions.csv", eval_rows)
    return {
        "risk_state_accuracy": correct["risk_state"] / max(1, total["risk_state"]),
        "action_window_prior_accuracy": correct["action_window_prior"] / max(1, total["action_window_prior"]),
        "prior_direction_accuracy": correct["prior_direction"] / max(1, total["prior_direction"]),
        "temporal_urgency_mae": urgency_error / max(1, len(eval_rows)),
        "eval_rows": len(eval_rows),
    }


def benchmark_full_dino_latency(model_name: str, rows: list[dict[str, Any]], max_samples: int, local_files_only: bool) -> dict[str, Any]:
    torch, _nn, functional, Image, _AutoImageProcessor, _AutoModel = import_stack()
    processor, model, _hidden = load_dino(model_name, local_files_only=local_files_only)
    selected = rows[:max_samples]
    totals = []
    for idx, row in enumerate(selected):
        start = time.perf_counter()
        path = REPO_ROOT / str(row.get("frame_path", ""))
        inputs = build_dino_inputs([path], processor, Image, torch)
        with torch.no_grad():
            _ = model(**inputs)
        if idx >= 2:
            totals.append((time.perf_counter() - start) * 1000.0)
    totals = sorted(totals or [0.0])

    def pct(q: float) -> float:
        pos = min(len(totals) - 1, max(0, int(round((len(totals) - 1) * q))))
        return totals[pos]

    return {
        "dino_full_latency_mean_ms": statistics.mean(totals),
        "dino_full_latency_p50_ms": pct(0.50),
        "dino_full_latency_p95_ms": pct(0.95),
        "dino_full_latency_p99_ms": pct(0.99),
    }


def write_report(row: dict[str, Any]) -> None:
    write_csv(REPORTS_DIR / "dino_focused_student_metrics.csv", [row])
    focused_rows = []
    focused_path = REPORTS_DIR / "focused_student_metrics_ranked.csv"
    if focused_path.exists():
        import csv

        with focused_path.open("r", encoding="utf-8", newline="") as handle:
            focused_rows = list(csv.DictReader(handle))
    combined = focused_rows + [row]
    write_csv(REPORTS_DIR / "focused_vs_dino_student_comparison.csv", combined)
    lines = [
        "# DINO focused baseline 실험",
        "",
        "DINO는 Unity 런타임 후보가 아니라, 현재 392개 teacher weak label에서 범용 self-supervised feature가 일반화에 도움이 되는지 보는 비교 기준선으로 사용했다.",
        "",
        f"- DINO model: `{row.get('model')}`",
        f"- risk_state_accuracy: {safe_float(row.get('risk_state_accuracy')):.3f}",
        f"- action_window_prior_accuracy: {safe_float(row.get('action_window_prior_accuracy')):.3f}",
        f"- prior_direction_accuracy: {safe_float(row.get('prior_direction_accuracy')):.3f}",
        f"- temporal_urgency_mae: {safe_float(row.get('temporal_urgency_mae')):.3f}",
        f"- DINO full latency p95: {safe_float(row.get('dino_full_latency_p95_ms')):.2f} ms",
        "",
        "해석: DINO가 정확도에서 이기더라도 현재 CPU full inference latency가 크면 런타임에는 직접 쓰기 어렵다. 이 경우 DINO는 teacher/feature upper-bound로 두고 ShuffleNet/MobileNet student distillation을 하는 방향이 맞다.",
    ]
    (REPORTS_DIR / "dino_focused_student_report_ko.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-name", default="facebook/dinov2-small")
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--latency-samples", type=int, default=20)
    parser.add_argument("--local-files-only", action="store_true")
    args = parser.parse_args()

    _train_rows, by_id, split_rows = load_student_training_rows()
    all_rows = split_rows["train"] + split_rows.get("valid", []) + split_rows.get("test", [])
    try:
        features, sample_ids, feature_dim, model_name = extract_features(
            all_rows,
            model_name=args.model_name,
            batch_size=args.batch_size,
            local_files_only=args.local_files_only,
        )
    except Exception as exc:
        row = {
            "model": args.model_name,
            "status": "failed_to_load_or_extract_dino",
            "error": f"{type(exc).__name__}: {str(exc)[:400]}",
        }
        write_csv(REPORTS_DIR / "dino_focused_student_metrics.csv", [row])
        write_report(row)
        raise

    features_by_id = {sample_id: features[idx] for idx, sample_id in enumerate(sample_ids)}
    train_meta, net = train_head(model_name, split_rows, by_id, features_by_id, feature_dim)
    metrics = evaluate_head(model_name, net, split_rows, by_id, features_by_id)
    latency = benchmark_full_dino_latency(model_name, split_rows.get("test", []), args.latency_samples, args.local_files_only)
    row = {
        "model": model_name,
        "status": "trained",
        "feature_dim": feature_dim,
        **train_meta,
        **metrics,
        **latency,
    }
    write_json(
        FOCUSED_MODEL_DIR / "dino_focused_student_config.json",
        {
            "model": model_name,
            "feature_dim": feature_dim,
            "checkpoint_path": train_meta.get("checkpoint_path"),
            "outputs": ["risk_state", "action_window_prior", "temporal_urgency"],
            "runtime_role": "feature_baseline_not_mobile_runtime",
        },
    )
    write_report(row)


if __name__ == "__main__":
    main()
