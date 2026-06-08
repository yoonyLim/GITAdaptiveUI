from analysis_public.src.common import compute_touch_dynamics_features


def summarize_user_variation(events: list[dict], dataset_id: str) -> list[dict]:
    rows = compute_touch_dynamics_features(events, dataset_id)
    return [
        {
            "dataset_id": row.get("dataset_id"),
            "user_id": row.get("user_id"),
            "within_user_variance": row.get("within_user_variance"),
            "between_user_variance": row.get("between_user_variance"),
        }
        for row in rows
    ]

