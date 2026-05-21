from __future__ import annotations

import random
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
    load_scene_samples,
    load_student_training_rows,
    safe_float,
    student_image_tensor,
    write_csv,
    write_json,
)


IMAGE_SIZE = 160
FOCUSED_TARGET_CLASSES = {
    "risk_state": ["safe_or_warning", "active", "critical", "unknown"],
    "action_window_prior": ["engage", "avoid", "neutral"],
}
FOCUSED_MODEL_DIR = MODELS_DIR / "focused_student"


def focused_risk_state(label: dict[str, Any]) -> str:
    threat = str(label.get("threat_level", "unknown"))
    if threat in {"none", "warning"}:
        return "safe_or_warning"
    if threat in {"active", "critical"}:
        return threat
    return "unknown"


def focused_action_window(label: dict[str, Any]) -> str:
    action_window = str(label.get("action_window", "unknown"))
    if action_window in {"engage", "avoid"}:
        return action_window
    return "neutral"


def focused_prior_direction(risk_state: str, action_window: str, urgency: float) -> str:
    if risk_state == "critical" or (risk_state == "active" and (action_window == "avoid" or urgency >= 0.65)):
        return "dodge_prior"
    if action_window == "engage" and risk_state in {"safe_or_warning", "active"} and urgency < 0.85:
        return "attack_prior"
    return "neutral_prior"


def import_stack() -> tuple[Any, Any, Any, Any, Any]:
    import torch
    import torch.nn as nn
    import torch.nn.functional as functional
    import torchvision.models as models
    from PIL import Image

    return torch, nn, functional, models, Image


def _load_with_optional_weights(factory: Any, weights: Any) -> tuple[Any, str]:
    try:
        return factory(weights=weights), "torchvision_imagenet_pretrained_frozen_backbone"
    except Exception as exc:
        return factory(weights=None), f"torchvision_no_pretrained:{type(exc).__name__}"


def build_backbone(name: str, nn: Any, models: Any) -> tuple[Any, int, str]:
    def classifier_backbone(factory: Any, weights: Any, attr: str) -> tuple[Any, int, str]:
        model, status = _load_with_optional_weights(factory, weights)
        if attr == "classifier":
            feature_dim = None
            for module in model.classifier.modules():
                if isinstance(module, nn.Linear):
                    feature_dim = module.in_features
                    break
            if feature_dim is None:
                raise ValueError("classifier has no Linear layer")
            model.classifier = nn.Identity()
        elif attr == "fc":
            feature_dim = model.fc.in_features
            model.fc = nn.Identity()
        else:
            raise ValueError(attr)
        return model, int(feature_dim), status

    registry: dict[str, Any] = {
        "mobilenet_v3_small": lambda: classifier_backbone(
            models.mobilenet_v3_small,
            models.MobileNet_V3_Small_Weights.DEFAULT,
            "classifier",
        ),
        "shufflenet_v2_x1_0": lambda: classifier_backbone(
            models.shufflenet_v2_x1_0,
            models.ShuffleNet_V2_X1_0_Weights.DEFAULT,
            "fc",
        ),
        "resnet18": lambda: classifier_backbone(models.resnet18, models.ResNet18_Weights.DEFAULT, "fc"),
    }
    return registry[name]()


def build_focused_model(name: str, nn: Any, models: Any) -> tuple[Any, str]:
    class FocusedSceneHead(nn.Module):
        def __init__(self, backbone: Any, feature_dim: int, pretrained_status: str) -> None:
            super().__init__()
            self.backbone = backbone
            freeze_backbone = "pretrained" in pretrained_status
            for parameter in self.backbone.parameters():
                parameter.requires_grad = not freeze_backbone
            self.shared = nn.Sequential(nn.Linear(feature_dim, 192), nn.Hardswish(inplace=True), nn.Dropout(0.20))
            self.class_heads = nn.ModuleDict(
                {target: nn.Linear(192, len(classes)) for target, classes in FOCUSED_TARGET_CLASSES.items()}
            )
            self.temporal_urgency_head = nn.Linear(192, 1)

        def forward(self, x: Any) -> dict[str, Any]:
            h = self.backbone(x)
            if h.ndim > 2:
                h = h.flatten(1)
            h = self.shared(h)
            return {
                "class_logits": {target: head(h) for target, head in self.class_heads.items()},
                "temporal_urgency": self.temporal_urgency_head(h).sigmoid().squeeze(1),
            }

    backbone, feature_dim, pretrained_status = build_backbone(name, nn, models)
    return FocusedSceneHead(backbone, feature_dim, pretrained_status), pretrained_status


def make_focused_batch(rows: list[dict[str, Any]], by_id: dict[str, dict[str, Any]], torch: Any, Image: Any) -> tuple[Any, dict[str, Any], Any]:
    class_to_idx = {target: {cls: idx for idx, cls in enumerate(classes)} for target, classes in FOCUSED_TARGET_CLASSES.items()}
    xs = []
    ys: dict[str, list[int]] = {target: [] for target in FOCUSED_TARGET_CLASSES}
    urgency_values = []
    for sample in rows:
        sample_id = str(sample.get("sample_id"))
        label = by_id[sample_id]
        xs.append(student_image_tensor(REPO_ROOT / str(sample.get("frame_path", "")), IMAGE_SIZE, torch, Image, normalization="imagenet"))
        targets = {
            "risk_state": focused_risk_state(label),
            "action_window_prior": focused_action_window(label),
        }
        for target, value in targets.items():
            ys[target].append(class_to_idx[target].get(value, class_to_idx[target][FOCUSED_TARGET_CLASSES[target][-1]]))
        urgency_values.append(clamp01(label.get("interaction_demand", {}).get("temporal_urgency", 0.0)))
    return (
        torch.stack(xs, dim=0),
        {target: torch.tensor(values, dtype=torch.long) for target, values in ys.items()},
        torch.tensor(urgency_values, dtype=torch.float32),
    )


def train_one_focused_candidate(name: str, split_rows: dict[str, list[dict[str, Any]]], by_id: dict[str, dict[str, Any]]) -> dict[str, Any]:
    torch, nn, functional, models, Image = import_stack()
    random.seed(1337)
    torch.manual_seed(1337)
    try:
        net, pretrained_status = build_focused_model(name, nn, models)
    except Exception as exc:
        return {"model": name, "status": "failed_to_build", "error": f"{type(exc).__name__}: {str(exc)[:240]}"}

    train_rows = list(split_rows.get("train", []))
    valid_rows = list(split_rows.get("valid", []))
    test_rows = list(split_rows.get("test", []))
    batch_size = min(16, max(4, len(train_rows)))
    epochs = 45 if len(train_rows) < 300 else 30
    optimizer = torch.optim.AdamW([p for p in net.parameters() if p.requires_grad], lr=0.0015, weight_decay=0.01)

    class_weights: dict[str, Any] = {}
    for target, classes in FOCUSED_TARGET_CLASSES.items():
        counts = Counter()
        for sample in train_rows:
            label = by_id[str(sample.get("sample_id"))]
            counts[focused_risk_state(label) if target == "risk_state" else focused_action_window(label)] += 1
        total = sum(counts.values()) or 1
        weights = [total / max(1, counts.get(cls, 0)) for cls in classes]
        mean_weight = statistics.mean(weights) if weights else 1.0
        class_weights[target] = torch.tensor([w / max(1e-6, mean_weight) for w in weights], dtype=torch.float32)

    best_state = None
    best_loss = float("inf")
    train_log = []
    for epoch in range(epochs):
        random.shuffle(train_rows)
        net.train()
        losses = []
        for start in range(0, len(train_rows), batch_size):
            x, y, temporal_urgency = make_focused_batch(train_rows[start : start + batch_size], by_id, torch, Image)
            out = net(x)
            loss = torch.tensor(0.0)
            for target in FOCUSED_TARGET_CLASSES:
                loss = loss + functional.cross_entropy(out["class_logits"][target], y[target], weight=class_weights[target])
            loss = loss + 1.6 * functional.mse_loss(out["temporal_urgency"], temporal_urgency)
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            losses.append(float(loss.detach().cpu()))

        eval_rows = valid_rows or train_rows
        net.eval()
        with torch.no_grad():
            x_val, y_val, temporal_val = make_focused_batch(eval_rows, by_id, torch, Image)
            out_val = net(x_val)
            valid_loss = torch.tensor(0.0)
            for target in FOCUSED_TARGET_CLASSES:
                valid_loss = valid_loss + functional.cross_entropy(out_val["class_logits"][target], y_val[target], weight=class_weights[target])
            valid_loss = valid_loss + 1.6 * functional.mse_loss(out_val["temporal_urgency"], temporal_val)
        score = float(valid_loss.detach().cpu())
        if score < best_loss:
            best_loss = score
            best_state = {key: value.detach().cpu().clone() for key, value in net.state_dict().items()}
        if epoch in {0, epochs - 1} or (epoch + 1) % 10 == 0:
            train_log.append({"model": name, "epoch": epoch + 1, "train_loss": statistics.mean(losses), "valid_loss": score})

    if best_state is not None:
        try:
            net.load_state_dict(best_state)
        except Exception:
            pass
    FOCUSED_MODEL_DIR.mkdir(parents=True, exist_ok=True)
    checkpoint_path = FOCUSED_MODEL_DIR / f"{name}.pt"
    torch.save(
        {
            "state_dict": net.state_dict(),
            "model_name": name,
            "image_size": IMAGE_SIZE,
            "target_classes": FOCUSED_TARGET_CLASSES,
            "pretrained_status": pretrained_status,
            "outputs": ["risk_state", "action_window_prior", "temporal_urgency"],
        },
        checkpoint_path,
    )
    write_csv(REPORTS_DIR / f"focused_student_{name}_training_log.csv", train_log)
    metrics = evaluate_focused_candidate(name, net, test_rows, by_id, torch, functional, Image)
    latency = benchmark_focused_latency(net, test_rows or train_rows, torch, functional, Image)
    return {
        "model": name,
        "status": "trained",
        "pretrained_status": pretrained_status,
        "checkpoint_path": str(checkpoint_path),
        "train_rows": len(train_rows),
        "valid_rows": len(valid_rows),
        "test_rows": len(test_rows),
        "best_valid_loss": best_loss,
        **metrics,
        **latency,
    }


def predict_focused(net: Any, sample: dict[str, Any], torch: Any, functional: Any, Image: Any) -> dict[str, Any]:
    with torch.no_grad():
        x = student_image_tensor(REPO_ROOT / str(sample.get("frame_path", "")), IMAGE_SIZE, torch, Image, normalization="imagenet").unsqueeze(0)
        out = net(x)
        pred: dict[str, Any] = {}
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


def evaluate_focused_candidate(
    name: str,
    net: Any,
    rows: list[dict[str, Any]],
    by_id: dict[str, dict[str, Any]],
    torch: Any,
    functional: Any,
    Image: Any,
) -> dict[str, Any]:
    eval_rows = []
    correct = Counter()
    total = Counter()
    urgency_abs_error = 0.0
    for sample in rows:
        label = by_id.get(str(sample.get("sample_id")))
        if not label:
            continue
        pred = predict_focused(net, sample, torch, functional, Image)
        true_risk = focused_risk_state(label)
        true_action = focused_action_window(label)
        true_urgency = clamp01(label.get("interaction_demand", {}).get("temporal_urgency", 0.0))
        true_prior_direction = focused_prior_direction(true_risk, true_action, true_urgency)
        for target, y, p in [
            ("risk_state", true_risk, str(pred["risk_state"])),
            ("action_window_prior", true_action, str(pred["action_window_prior"])),
            ("prior_direction", true_prior_direction, str(pred["prior_direction"])),
        ]:
            total[target] += 1
            correct[target] += int(y == p)
        urgency_abs_error += abs(true_urgency - safe_float(pred["temporal_urgency"]))
        eval_rows.append(
            {
                "model": name,
                "sample_id": sample.get("sample_id"),
                "scenario_name": sample.get("scenario_name"),
                "true_risk_state": true_risk,
                "pred_risk_state": pred["risk_state"],
                "true_action_window_prior": true_action,
                "pred_action_window_prior": pred["action_window_prior"],
                "true_temporal_urgency": true_urgency,
                "pred_temporal_urgency": pred["temporal_urgency"],
                "true_prior_direction": true_prior_direction,
                "pred_prior_direction": pred["prior_direction"],
                "confidence": pred["confidence"],
            }
        )
    write_csv(REPORTS_DIR / f"focused_student_{name}_predictions.csv", eval_rows)
    return {
        "risk_state_accuracy": correct["risk_state"] / max(1, total["risk_state"]),
        "action_window_prior_accuracy": correct["action_window_prior"] / max(1, total["action_window_prior"]),
        "prior_direction_accuracy": correct["prior_direction"] / max(1, total["prior_direction"]),
        "temporal_urgency_mae": urgency_abs_error / max(1, len(eval_rows)),
        "eval_rows": len(eval_rows),
    }


def benchmark_focused_latency(net: Any, rows: list[dict[str, Any]], torch: Any, functional: Any, Image: Any, max_samples: int = 120) -> dict[str, Any]:
    selected = rows[:max_samples] if rows else load_scene_samples(max_samples)
    totals = []
    net.eval()
    for idx, sample in enumerate(selected):
        start = time.perf_counter()
        _ = predict_focused(net, sample, torch, functional, Image)
        if idx >= 5:
            totals.append((time.perf_counter() - start) * 1000.0)
    totals = totals or [0.0]
    sorted_totals = sorted(totals)

    def pct(q: float) -> float:
        pos = min(len(sorted_totals) - 1, max(0, int(round((len(sorted_totals) - 1) * q))))
        return sorted_totals[pos]

    return {
        "latency_mean_ms": statistics.mean(totals),
        "latency_p50_ms": pct(0.50),
        "latency_p90_ms": pct(0.90),
        "latency_p95_ms": pct(0.95),
        "latency_p99_ms": pct(0.99),
        "deadline_miss_rate_16ms": sum(1 for value in totals if value > 16) / len(totals),
        "deadline_miss_rate_33ms": sum(1 for value in totals if value > 33) / len(totals),
    }


def rank_focused(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ranked = []
    for row in rows:
        if row.get("status") != "trained":
            ranked.append({**row, "recommendation_score": -999.0})
            continue
        score = (
            safe_float(row.get("risk_state_accuracy"))
            + safe_float(row.get("action_window_prior_accuracy"))
            + safe_float(row.get("prior_direction_accuracy"))
            - safe_float(row.get("temporal_urgency_mae"))
            - max(0.0, safe_float(row.get("latency_p95_ms")) - 16.0) / 16.0
        )
        ranked.append({**row, "recommendation_score": score})
    return sorted(ranked, key=lambda item: safe_float(item.get("recommendation_score"), -999), reverse=True)


def write_focused_report(rows: list[dict[str, Any]]) -> None:
    ranked = rank_focused(rows)
    write_csv(REPORTS_DIR / "focused_student_metrics_ranked.csv", ranked)
    best = ranked[0] if ranked else {}
    write_json(
        FOCUSED_MODEL_DIR / "focused_student_recommendation.json",
        {
            "recommended_model": best.get("model"),
            "outputs": ["risk_state", "action_window_prior", "temporal_urgency"],
            "note": "Reduced-output model for Unity situation prior. It intentionally drops dominant_mode and the broad interaction-demand heads.",
        },
    )
    lines = [
        "# Focused student 학습 결과",
        "",
        "기존 multi-output student가 너무 많은 변수를 동시에 예측하던 문제를 줄이기 위해 Unity prior에 직접 필요한 세 출력만 학습했다.",
        "",
        "- `risk_state`: none/warning은 `safe_or_warning`으로 접고, `active`, `critical`, `unknown`만 유지",
        "- `action_window_prior`: `engage`, `avoid`, 나머지는 `neutral`로 접음",
        "- `temporal_urgency`: 0~1 회귀",
        "",
        "| rank | model | risk acc | action window acc | prior direction acc | urgency MAE | p95 ms | score |",
        "|---:|---|---:|---:|---:|---:|---:|---:|",
    ]
    for idx, row in enumerate(ranked, start=1):
        lines.append(
            f"| {idx} | {row.get('model')} | {safe_float(row.get('risk_state_accuracy')):.3f} | {safe_float(row.get('action_window_prior_accuracy')):.3f} | {safe_float(row.get('prior_direction_accuracy')):.3f} | {safe_float(row.get('temporal_urgency_mae')):.3f} | {safe_float(row.get('latency_p95_ms')):.2f} | {safe_float(row.get('recommendation_score')):.3f} |"
        )
    if best:
        lines.extend(
            [
                "",
                f"추천 후보: `{best.get('model')}`.",
                "",
                "해석: 이 결과는 teacher weak label 기준이다. 정확도가 올라가더라도 사람 검수 라벨이 아니므로 실제 상황 인식 성능으로 과장하면 안 된다.",
            ]
        )
    (REPORTS_DIR / "focused_student_report_ko.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    _train_rows, by_id, split_rows = load_student_training_rows()
    candidates = ["mobilenet_v3_small", "shufflenet_v2_x1_0", "resnet18"]
    rows = []
    for name in candidates:
        print(f"training focused candidate: {name}", flush=True)
        try:
            rows.append(train_one_focused_candidate(name, split_rows, by_id))
        except Exception as exc:
            rows.append({"model": name, "status": "failed_during_training", "error": f"{type(exc).__name__}: {str(exc)[:240]}"})
    write_csv(REPORTS_DIR / "focused_student_metrics.csv", rows)
    write_focused_report(rows)


if __name__ == "__main__":
    main()
