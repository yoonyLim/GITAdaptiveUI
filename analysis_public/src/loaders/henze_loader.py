from analysis_public.src.common import parse_henze_lines


def load_henze_text(path) -> list[dict]:
    return parse_henze_lines(path.read_text(encoding="utf-8", errors="ignore").splitlines())

