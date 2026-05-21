from analysis_public.src.common import iter_touch_csv_rows


def load_touch_dynamics_events() -> list[dict]:
    return list(iter_touch_csv_rows("touch_dynamics"))

