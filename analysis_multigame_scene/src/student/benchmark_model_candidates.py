from __future__ import annotations

import csv
import json
import math
import random
import statistics
import time
from collections import Counter
from pathlib import Path
from typing import Any

from analysis_multigame_scene.src.common import (
    ACTION_WINDOWS,
    DEMAND_KEYS,
    DOMINANT_MODES,
    MODELS_DIR,
    REPORTS_DIR,
    REPO_ROOT,
    THREAT_LEVELS,
    URGENCY_LEVELS,
    clamp01,
    load_scene_samples,
    load_student_training_rows,
    prior_kl,
    recall_for_class,
    safe_float,
    student_image_tensor,
    write_csv,
    write_json,
)


CANDIDATE_DIR = MODELS_DIR / "student_candidates"
IMAGE_SIZE = 160
TARGET_CLASSES = {
    "dominant_mode": DOMINANT_MODES,
    "threat_level": THREAT_LEVELS,
    "action_window": ACTION_WINDOWS,
    "urgency_level": URGENCY_LEVELS,
}


MODEL_RESEARCH_ROWS = [
    {
        "model": "MobileNetV3-Small",
        "paper": "Searching for MobileNetV3",
        "source": "https://arxiv.org/abs/1905.02244",
        "why": "hardware-aware NAS/NetAdapt 기반 모바일 CPU 지연 최적화. 현재 Unity prior update 후보 1순위.",
        "trained": True,
    },
    {
        "model": "MobileNetV4",
        "paper": "MobileNetV4: Universal Models for the Mobile Ecosystem",
        "source": "https://arxiv.org/abs/2404.10518",
        "why": "2024년 최신 MobileNet 계열. Pixel 8 EdgeTPU 등 모바일 생태계 전체를 겨냥하지만 torchvision 기본 모델이 아니라 별도 구현/ONNX 검증 필요.",
        "trained": False,
    },
    {
        "model": "MobileNetV2",
        "paper": "MobileNets: Efficient Convolutional Neural Networks for Mobile Vision Applications",
        "source": "https://arxiv.org/abs/1704.04861",
        "why": "depthwise separable conv 계열의 표준 모바일 baseline. V3와 비교 가치가 큼.",
        "trained": True,
    },
    {
        "model": "ShuffleNetV2",
        "paper": "ShuffleNet V2: Practical Guidelines for Efficient CNN Architecture Design",
        "source": "https://arxiv.org/abs/1807.11164",
        "why": "FLOPs보다 실제 속도 측정을 강조한 경량 CNN. 지연 baseline으로 적합.",
        "trained": True,
    },
    {
        "model": "MnasNet",
        "paper": "MnasNet: Platform-Aware Neural Architecture Search for Mobile",
        "source": "https://research.google/pubs/mnasnet-platform-aware-neural-architecture-search-for-mobile/",
        "why": "모바일 기기 실제 latency를 search objective에 넣은 모델. MobileNet 계열과 비교 가능.",
        "trained": True,
    },
    {
        "model": "SqueezeNet",
        "paper": "SqueezeNet: AlexNet-level accuracy with 50x fewer parameters",
        "source": "https://arxiv.org/abs/1602.07360",
        "why": "매우 작은 parameter footprint. 정확도보다 크기/배포 비용 baseline.",
        "trained": True,
    },
    {
        "model": "MobileOne",
        "paper": "MobileOne: An Improved One millisecond Mobile Backbone",
        "source": "https://arxiv.org/abs/2206.04040",
        "why": "iPhone12에서 1ms급 mobile backbone을 목표로 한 최신 실시간 후보. timm/Apple 구현 경로와 Unity export 검증이 필요.",
        "trained": False,
    },
    {
        "model": "PP-LCNet",
        "paper": "PP-LCNet: A Lightweight CPU Convolutional Neural Network",
        "source": "https://arxiv.org/abs/2109.15099",
        "why": "CPU inference에 초점을 둔 경량 모델. Paddle/PPLCNet 계열이라 현재 torchvision-only benchmark에서는 제외.",
        "trained": False,
    },
    {
        "model": "EdgeNeXt",
        "paper": "EdgeNeXt: Efficiently Amalgamated CNN-Transformer Architecture for Mobile Vision Applications",
        "source": "https://arxiv.org/abs/2206.10589",
        "why": "CNN-Transformer hybrid edge model. 최신성은 좋지만 추가 구현/weight/export 검증 필요.",
        "trained": False,
    },
    {
        "model": "EfficientNet-B0",
        "paper": "EfficientNet: Rethinking Model Scaling for Convolutional Neural Networks",
        "source": "https://research.google/pubs/efficientnet-rethinking-model-scaling-for-convolutional-neural-networks/",
        "why": "정확도/효율 compound scaling baseline. 실제 지연이 모바일 전용 모델보다 불리한지 확인.",
        "trained": True,
    },
    {
        "model": "ResNet18",
        "paper": "Deep Residual Learning for Image Recognition",
        "source": "https://arxiv.org/abs/1512.03385",
        "why": "모바일 최적화 모델은 아니지만 transfer-learning sanity baseline으로 사용.",
        "trained": True,
    },
    {
        "model": "AlexNet",
        "paper": "ImageNet Classification with Deep Convolutional Neural Networks",
        "source": "https://papers.nips.cc/paper_files/paper/2012/hash/c399862d3b9d6b76c8436e924a68c45b-Abstract.html",
        "why": "현대 모델과 비교하기 위한 고전 CNN baseline. 실사용 후보라기보다는 기준선.",
        "trained": True,
    },
    {
        "model": "RegNetY-400MF",
        "paper": "Designing Network Design Spaces",
        "source": "https://arxiv.org/abs/2003.13678",
        "why": "Meta RegNet 계열의 경량 baseline. 모바일 전용은 아니지만 연산/정확도 균형 비교용.",
        "trained": True,
    },
    {
        "model": "ConvNeXt-Tiny",
        "paper": "A ConvNet for the 2020s",
        "source": "https://arxiv.org/abs/2201.03545",
        "why": "현대 ConvNet baseline. latency가 클 가능성이 높아 Unity runtime 후보보다는 정확도 상한 비교용.",
        "trained": True,
    },
    {
        "model": "EfficientFormer / RepViT / FastViT",
        "paper": "EfficientFormer, RepViT, FastViT",
        "source": "https://arxiv.org/abs/2206.01191 ; https://arxiv.org/abs/2307.09283 ; https://arxiv.org/abs/2303.14189",
        "why": "논문상 mobile latency-accuracy가 좋지만 현재 repo의 안정적 torchvision 경로가 아니므로 별도 timm/ONNX/Sentis 검증 후 도입.",
        "trained": False,
    },
]


def write_research_summary() -> None:
    write_csv(REPORTS_DIR / "student_model_candidate_research.csv", MODEL_RESEARCH_ROWS)
    lines = [
        "# Student 모델 후보 조사",
        "",
        "선정 기준은 Unity situation-prior update 지연, pretrained transfer 가능성, ONNX/Sentis 이전 가능성, 현재 200개 teacher label에서의 과적합 위험이다.",
        "",
        "| model | source | 이번 학습 여부 | 선택 이유 |",
        "|---|---|---:|---|",
    ]
    for row in MODEL_RESEARCH_ROWS:
        lines.append(f"| {row['model']} | {row['source']} | {row['trained']} | {row['why']} |")
    lines.extend(
        [
            "",
            "주의: EfficientFormer/RepViT/FastViT는 논문상 좋은 후보지만, 현재 단계에서는 torchvision 경로의 안정적인 모델을 먼저 학습했다. 이후 timm 기반 pretrained weight와 Unity Sentis/ONNX 호환성을 따로 확인해야 한다.",
        ]
    )
    (REPORTS_DIR / "student_model_candidate_research_ko.md").write_text("\n".join(lines), encoding="utf-8")


def import_stack() -> tuple[Any, Any, Any, Any, Any]:
    import torch
    import torch.nn as nn
    import torch.nn.functional as functional
    import torchvision.models as models
    from PIL import Image

    return torch, nn, functional, models, Image


class MultiTaskHead:
    pass


def build_candidate_model(name: str, nn: Any, models: Any) -> tuple[Any, str]:
    class SceneHead(nn.Module):
        def __init__(self, backbone: Any, feature_dim: int) -> None:
            super().__init__()
            self.backbone = backbone
            for parameter in self.backbone.parameters():
                parameter.requires_grad = False
            self.shared = nn.Sequential(nn.Linear(feature_dim, 256), nn.Hardswish(inplace=True), nn.Dropout(0.25))
            self.class_heads = nn.ModuleDict({target: nn.Linear(256, len(classes)) for target, classes in TARGET_CLASSES.items()})
            self.demand_head = nn.Linear(256, len(DEMAND_KEYS))

        def forward(self, x: Any) -> dict[str, Any]:
            h = self.backbone(x)
            if h.ndim > 2:
                h = h.flatten(1)
            h = self.shared(h)
            return {
                "class_logits": {target: head(h) for target, head in self.class_heads.items()},
                "demand": self.demand_head(h).sigmoid(),
            }

    def classifier_backbone(factory: Any, weights: Any, attr: str = "classifier") -> tuple[Any, int]:
        model = factory(weights=weights)
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
        return model, feature_dim

    registry: dict[str, Any] = {
        "squeezenet1_1": lambda: build_squeezenet(models.squeezenet1_1(weights=models.SqueezeNet1_1_Weights.DEFAULT), nn),
        "shufflenet_v2_x0_5": lambda: classifier_backbone(models.shufflenet_v2_x0_5, models.ShuffleNet_V2_X0_5_Weights.DEFAULT, "fc"),
        "shufflenet_v2_x1_0": lambda: classifier_backbone(models.shufflenet_v2_x1_0, models.ShuffleNet_V2_X1_0_Weights.DEFAULT, "fc"),
        "mobilenet_v2": lambda: classifier_backbone(models.mobilenet_v2, models.MobileNet_V2_Weights.DEFAULT, "classifier"),
        "mobilenet_v3_small": lambda: classifier_backbone(models.mobilenet_v3_small, models.MobileNet_V3_Small_Weights.DEFAULT, "classifier"),
        "mobilenet_v3_large": lambda: classifier_backbone(models.mobilenet_v3_large, models.MobileNet_V3_Large_Weights.DEFAULT, "classifier"),
        "mnasnet0_5": lambda: classifier_backbone(models.mnasnet0_5, models.MNASNet0_5_Weights.DEFAULT, "classifier"),
        "mnasnet1_0": lambda: classifier_backbone(models.mnasnet1_0, models.MNASNet1_0_Weights.DEFAULT, "classifier"),
        "efficientnet_b0": lambda: classifier_backbone(models.efficientnet_b0, models.EfficientNet_B0_Weights.DEFAULT, "classifier"),
        "resnet18": lambda: classifier_backbone(models.resnet18, models.ResNet18_Weights.DEFAULT, "fc"),
        "alexnet": lambda: classifier_backbone(models.alexnet, models.AlexNet_Weights.DEFAULT, "classifier"),
        "regnet_y_400mf": lambda: classifier_backbone(models.regnet_y_400mf, models.RegNet_Y_400MF_Weights.DEFAULT, "fc"),
        "convnext_tiny": lambda: classifier_backbone(models.convnext_tiny, models.ConvNeXt_Tiny_Weights.DEFAULT, "classifier"),
    }
    backbone, feature_dim = registry[name]()
    return SceneHead(backbone, feature_dim), "torchvision_imagenet_pretrained_frozen_backbone"


def build_squeezenet(model: Any, nn: Any) -> tuple[Any, int]:
    class SqueezeBackbone(nn.Module):
        def __init__(self) -> None:
            super().__init__()
            self.features = model.features
            self.pool = nn.AdaptiveAvgPool2d((1, 1))

        def forward(self, x: Any) -> Any:
            return self.pool(self.features(x)).flatten(1)

    return SqueezeBackbone(), 512


def make_batch(rows: list[dict[str, Any]], by_id: dict[str, dict[str, Any]], torch: Any, Image: Any) -> tuple[Any, dict[str, Any], Any]:
    class_to_idx = {target: {cls: idx for idx, cls in enumerate(classes)} for target, classes in TARGET_CLASSES.items()}
    xs = []
    ys: dict[str, list[int]] = {target: [] for target in TARGET_CLASSES}
    demand_values = []
    for sample in rows:
        label = by_id[str(sample.get("sample_id"))]
        xs.append(student_image_tensor(REPO_ROOT / str(sample.get("frame_path", "")), IMAGE_SIZE, torch, Image, normalization="imagenet"))
        for target in TARGET_CLASSES:
            ys[target].append(class_to_idx[target].get(str(label.get(target, "unknown")), class_to_idx[target]["unknown"]))
        demand_values.append([clamp01(label.get("interaction_demand", {}).get(key, 0.0)) for key in DEMAND_KEYS])
    return (
        torch.stack(xs, dim=0),
        {target: torch.tensor(values, dtype=torch.long) for target, values in ys.items()},
        torch.tensor(demand_values, dtype=torch.float32),
    )


def train_one_candidate(name: str, split_rows: dict[str, list[dict[str, Any]]], by_id: dict[str, dict[str, Any]]) -> dict[str, Any]:
    torch, nn, functional, models, Image = import_stack()
    random.seed(1337)
    torch.manual_seed(1337)
    try:
        net, pretrained_status = build_candidate_model(name, nn, models)
    except Exception as exc:
        return {"model": name, "status": "failed_to_build_or_download", "error": f"{type(exc).__name__}: {str(exc)[:200]}"}

    train_rows = list(split_rows["train"])
    valid_rows = list(split_rows.get("valid", []))
    test_rows = list(split_rows.get("test", []))
    batch_size = min(16, max(4, len(train_rows)))
    epochs = 25
    optimizer = torch.optim.AdamW([p for p in net.parameters() if p.requires_grad], lr=0.002, weight_decay=0.01)

    class_weights: dict[str, Any] = {}
    for target, classes in TARGET_CLASSES.items():
        counts = Counter(str(by_id[str(sample.get("sample_id"))].get(target, "unknown")) for sample in train_rows)
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
            x, y, demand = make_batch(train_rows[start : start + batch_size], by_id, torch, Image)
            out = net(x)
            loss = torch.tensor(0.0)
            for target in TARGET_CLASSES:
                loss = loss + functional.cross_entropy(out["class_logits"][target], y[target], weight=class_weights[target])
            loss = loss + 1.2 * functional.mse_loss(out["demand"], demand)
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            losses.append(float(loss.detach().cpu()))
        eval_rows = valid_rows or train_rows
        net.eval()
        with torch.no_grad():
            x_val, y_val, demand_val = make_batch(eval_rows, by_id, torch, Image)
            out_val = net(x_val)
            val_loss = torch.tensor(0.0)
            for target in TARGET_CLASSES:
                val_loss = val_loss + functional.cross_entropy(out_val["class_logits"][target], y_val[target], weight=class_weights[target])
            val_loss = val_loss + 1.2 * functional.mse_loss(out_val["demand"], demand_val)
        score = float(val_loss.detach().cpu())
        if score < best_loss:
            best_loss = score
            best_state = {key: value.detach().cpu().clone() for key, value in net.state_dict().items()}
        if epoch in {0, epochs - 1} or (epoch + 1) % 10 == 0:
            train_log.append({"model": name, "epoch": epoch + 1, "train_loss": statistics.mean(losses), "valid_loss": score})

    if best_state is not None:
        try:
            net.load_state_dict(best_state)
        except Exception:
            # Some torchvision modules, notably MNASNet, keep private version
            # metadata in state_dict. If cloning drops it, keep the final epoch
            # weights rather than aborting the whole benchmark.
            pass
    CANDIDATE_DIR.mkdir(parents=True, exist_ok=True)
    checkpoint_path = CANDIDATE_DIR / f"{name}.pt"
    torch.save({"state_dict": net.state_dict(), "model_name": name, "image_size": IMAGE_SIZE, "target_classes": TARGET_CLASSES}, checkpoint_path)
    write_csv(REPORTS_DIR / f"student_candidate_{name}_training_log.csv", train_log)
    metrics = evaluate_candidate(name, net, test_rows, by_id, torch, functional, Image)
    latency = benchmark_candidate_latency(name, net, torch, functional, Image)
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


def predict_candidate(net: Any, sample: dict[str, Any], torch: Any, functional: Any, Image: Any) -> dict[str, Any]:
    idx_to_class = TARGET_CLASSES
    with torch.no_grad():
        x = student_image_tensor(REPO_ROOT / str(sample.get("frame_path", "")), IMAGE_SIZE, torch, Image, normalization="imagenet").unsqueeze(0)
        out = net(x)
        pred: dict[str, Any] = {}
        confidences = []
        for target, classes in idx_to_class.items():
            probs = functional.softmax(out["class_logits"][target][0], dim=0)
            idx = int(torch.argmax(probs).detach().cpu())
            pred[target] = classes[idx]
            confidences.append(float(probs[idx].detach().cpu()))
        pred["interaction_demand"] = {key: clamp01(float(out["demand"][0][idx].detach().cpu())) for idx, key in enumerate(DEMAND_KEYS)}
        pred["confidence"] = statistics.mean(confidences) if confidences else 0.0
    return pred


def evaluate_candidate(name: str, net: Any, rows: list[dict[str, Any]], by_id: dict[str, dict[str, Any]], torch: Any, functional: Any, Image: Any) -> dict[str, Any]:
    eval_rows = []
    correct = Counter()
    total = Counter()
    demand_error = 0.0
    kl_values = []
    for sample in rows:
        label = by_id.get(str(sample.get("sample_id")))
        if not label:
            continue
        pred = predict_candidate(net, sample, torch, functional, Image)
        out = {"model": name, "sample_id": sample.get("sample_id"), "scenario_name": sample.get("scenario_name")}
        for target in TARGET_CLASSES:
            y = str(label.get(target, "unknown"))
            p = str(pred.get(target, "unknown"))
            total[target] += 1
            correct[target] += int(y == p)
            out[f"true_{target}"] = y
            out[f"pred_{target}"] = p
        for key in DEMAND_KEYS:
            demand_error += abs(safe_float(label.get("interaction_demand", {}).get(key), 0.0) - safe_float(pred["interaction_demand"].get(key), 0.0))
        kl = prior_kl(
            [safe_float(label.get("prior_attack"), 0.5), safe_float(label.get("prior_dodge"), 0.5)],
            [0.5, 0.5],
        )
        kl_values.append(kl)
        eval_rows.append(out)
    write_csv(REPORTS_DIR / f"student_candidate_{name}_predictions.csv", eval_rows)
    return {
        "dominant_mode_accuracy": correct["dominant_mode"] / max(1, total["dominant_mode"]),
        "action_first_recall": recall_for_class(eval_rows, "dominant_mode", "action_first"),
        "cognition_first_recall": recall_for_class(eval_rows, "dominant_mode", "cognition_first"),
        "guidance_procedure_recall": recall_for_class(eval_rows, "dominant_mode", "guidance_procedure"),
        "threat_level_accuracy": correct["threat_level"] / max(1, total["threat_level"]),
        "action_window_accuracy": correct["action_window"] / max(1, total["action_window"]),
        "urgency_level_accuracy": correct["urgency_level"] / max(1, total["urgency_level"]),
        "interaction_demand_MAE": demand_error / max(1, len(eval_rows) * len(DEMAND_KEYS)),
        "prior_KL_to_neutral": statistics.mean(kl_values) if kl_values else 0.0,
        "eval_rows": len(eval_rows),
    }


def benchmark_candidate_latency(name: str, net: Any, torch: Any, functional: Any, Image: Any, max_samples: int = 120) -> dict[str, Any]:
    rows = load_scene_samples(max_samples)
    totals = []
    net.eval()
    for idx, sample in enumerate(rows):
        start = time.perf_counter()
        _ = predict_candidate(net, sample, torch, functional, Image)
        total = (time.perf_counter() - start) * 1000.0
        if idx >= 10:
            totals.append(total)
    if not totals:
        totals = [0.0]
    totals_sorted = sorted(totals)

    def pct(q: float) -> float:
        pos = min(len(totals_sorted) - 1, max(0, int(round((len(totals_sorted) - 1) * q))))
        return totals_sorted[pos]

    return {
        "latency_mean_ms": statistics.mean(totals),
        "latency_p50_ms": pct(0.50),
        "latency_p90_ms": pct(0.90),
        "latency_p95_ms": pct(0.95),
        "latency_p99_ms": pct(0.99),
        "deadline_miss_rate_16ms": sum(1 for value in totals if value > 16) / len(totals),
        "deadline_miss_rate_33ms": sum(1 for value in totals if value > 33) / len(totals),
    }


def rank_candidates(rows: list[dict[str, Any]]) -> list[dict[str, Any]]:
    ranked = []
    for row in rows:
        if row.get("status") != "trained":
            ranked.append({**row, "recommendation_score": -999.0})
            continue
        accuracy_score = (
            safe_float(row.get("dominant_mode_accuracy"))
            + safe_float(row.get("threat_level_accuracy"))
            + safe_float(row.get("action_window_accuracy"))
            - safe_float(row.get("interaction_demand_MAE"))
        )
        latency_penalty = max(0.0, safe_float(row.get("latency_p95_ms")) - 16.0) / 16.0
        ranked.append({**row, "recommendation_score": accuracy_score - latency_penalty})
    return sorted(ranked, key=lambda item: safe_float(item.get("recommendation_score"), -999), reverse=True)


def write_candidate_report(rows: list[dict[str, Any]]) -> None:
    ranked = rank_candidates(rows)
    write_csv(REPORTS_DIR / "student_model_candidate_metrics_ranked.csv", ranked)
    lines = [
        "# Student 모델 후보 학습/지연 벤치마크",
        "",
        "모든 후보는 현재 확보된 Codex teacher label에서 scenario holdout split을 사용했다. Backbone은 ImageNet pretrained를 사용하고, 현재 라벨 수가 작기 때문에 backbone은 고정한 뒤 multi-head 상황 head만 학습했다.",
        "",
        "| rank | model | status | mode acc | threat acc | action_window acc | demand MAE | p95 ms | score |",
        "|---:|---|---|---:|---:|---:|---:|---:|---:|",
    ]
    for idx, row in enumerate(ranked, start=1):
        lines.append(
            f"| {idx} | {row.get('model')} | {row.get('status')} | {safe_float(row.get('dominant_mode_accuracy')):.3f} | {safe_float(row.get('threat_level_accuracy')):.3f} | {safe_float(row.get('action_window_accuracy')):.3f} | {safe_float(row.get('interaction_demand_MAE')):.3f} | {safe_float(row.get('latency_p95_ms')):.2f} | {safe_float(row.get('recommendation_score')):.3f} |"
        )
    if ranked:
        best = ranked[0]
        trained = [row for row in ranked if row.get("status") == "trained"]
        best_accuracy = max(
            trained,
            key=lambda row: (
                safe_float(row.get("dominant_mode_accuracy"))
                + safe_float(row.get("threat_level_accuracy"))
                + safe_float(row.get("action_window_accuracy"))
                - safe_float(row.get("interaction_demand_MAE"))
            ),
            default=best,
        )
        fastest = min(trained, key=lambda row: safe_float(row.get("latency_p95_ms"), 999.0), default=best)
        mobile_practical = next((row for row in trained if row.get("model") == "mobilenet_v3_small"), fastest)
        write_json(
            MODELS_DIR / "student_model_candidate_recommendation.json",
            {
                "best_accuracy": best_accuracy.get("model"),
                "best_latency_balanced": best.get("model"),
                "fastest_measured": fastest.get("model"),
                "practical_low_latency_mobile_baseline": mobile_practical.get("model"),
                "note": "Ranking is based on current expanded teacher labels. p95 latency is CPU-side local measurement, not a phone benchmark.",
            },
        )
        lines.extend(
            [
                "",
                f"추천: `{best.get('model')}`. 단, 현재 라벨은 action_first에 강하게 치우쳐 있으므로 이 추천은 현재 데이터셋 기준이다.",
                "",
                "해석: p95가 16ms 이하면 60Hz frame budget 안에서도 situation update가 가능하지만, 이 시스템에서는 touch decoding을 막지 않는 비동기 prior update가 목적이므로 30Hz 이하도 충분히 실용적이다.",
            ]
        )
    (REPORTS_DIR / "student_model_candidate_benchmark_ko.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    write_research_summary()
    _train_rows, by_id, split_rows = load_student_training_rows()
    candidates = [
        "squeezenet1_1",
        "shufflenet_v2_x0_5",
        "shufflenet_v2_x1_0",
        "mobilenet_v2",
        "mobilenet_v3_small",
        "mobilenet_v3_large",
        "mnasnet0_5",
        "mnasnet1_0",
        "efficientnet_b0",
        "resnet18",
        "alexnet",
        "regnet_y_400mf",
        "convnext_tiny",
    ]
    results = []
    for name in candidates:
        try:
            results.append(train_one_candidate(name, split_rows, by_id))
        except Exception as exc:
            results.append({"model": name, "status": "failed_during_training", "error": f"{type(exc).__name__}: {str(exc)[:240]}"})
    write_csv(REPORTS_DIR / "student_model_candidate_metrics.csv", results)
    write_candidate_report(results)
    write_json(CANDIDATE_DIR / "candidate_benchmark_config.json", {"image_size": IMAGE_SIZE, "candidates": candidates})


if __name__ == "__main__":
    main()
