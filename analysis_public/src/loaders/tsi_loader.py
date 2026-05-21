from analysis_public.src.common import load_tsi_records, target_rows_from_tsi
from analysis_public.src.data.dataset_registry import dataset_root


def load_tsi_target_rows() -> list[dict]:
    root = dataset_root("tsi") / "raw"
    touch_csv = root / "touch_data.csv"
    keyboard_json = root / "keyboard_data.json"
    if not touch_csv.exists() or not keyboard_json.exists():
        return []
    touches, keyboard = load_tsi_records(touch_csv, keyboard_json)
    return target_rows_from_tsi(touches, keyboard)

