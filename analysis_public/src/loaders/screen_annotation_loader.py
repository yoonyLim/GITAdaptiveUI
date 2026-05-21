from analysis_public.src.common import parse_screen_annotation_label


def parse_screen_annotation(label: str) -> list[dict]:
    return parse_screen_annotation_label(label)

