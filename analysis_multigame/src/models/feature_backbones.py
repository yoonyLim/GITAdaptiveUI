from __future__ import annotations

from analysis_multigame.src.common import FEATURE_FIELDS, normalize_feature_vector


class HandcraftedFeatureBackbone:
    name = "handcrafted_env_features"
    feature_fields = FEATURE_FIELDS

    def encode(self, row: dict) -> list[float]:
        return normalize_feature_vector(row)

