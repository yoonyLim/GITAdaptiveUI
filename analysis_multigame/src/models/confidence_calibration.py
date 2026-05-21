from __future__ import annotations


def mix_with_neutral(base_attack: float, base_dodge: float, confidence: float) -> tuple[float, float]:
    c = max(0.0, min(1.0, confidence))
    attack = c * base_attack + (1.0 - c) * 0.5
    dodge = c * base_dodge + (1.0 - c) * 0.5
    total = attack + dodge
    return attack / total, dodge / total

