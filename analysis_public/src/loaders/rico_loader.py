from analysis_public.src.common import walk_rico_node


def collect_rico_elements(root_node: dict) -> list[dict]:
    rows: list[dict] = []
    walk_rico_node(root_node, rows)
    return rows

