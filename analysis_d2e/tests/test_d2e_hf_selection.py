from __future__ import annotations

from analysis_d2e.src.d2e_hf import paired_video_path
from analysis_d2e.src.download_d2e_subset import download_subset, select_recordings, size_mb


def test_select_recordings_limits_per_target_game():
    files = [
        "Barony/0801_01.mcap",
        "Barony/0801_02.mcap",
        "Brotato/0802_01.mcap",
        "Core_Keeper/0803_01.mcap",
        "Apex_Legends/0804_01.mcap",
        "Barony/0801_01.mkv",
    ]

    selected = select_recordings(files, ("Barony", "Brotato", "Core_Keeper"), max_recordings_per_game=1)

    assert selected == ["Barony/0801_01.mcap", "Brotato/0802_01.mcap", "Core_Keeper/0803_01.mcap"]


def test_paired_video_path_maps_mcap_to_mkv():
    assert paired_video_path("Barony/0801_01.mcap") == "Barony/0801_01.mkv"


def test_size_mb_converts_bytes():
    assert size_mb(1048576) == 1.0
    assert size_mb(None) is None


def test_download_subset_can_skip_large_files(monkeypatch, tmp_path):
    class FakeApi:
        def list_repo_files(self, **_kwargs):
            return ["Barony/a.mcap", "Barony/a.mkv"]

        def get_paths_info(self, **_kwargs):
            class Info:
                def __init__(self, path, size):
                    self.path = path
                    self.size = size

            return [Info("Barony/a.mcap", 100), Info("Barony/a.mkv", 10 * 1024 * 1024)]

    def fake_import_hf():
        def _download(**_kwargs):
            raise AssertionError("download should be skipped")

        return lambda: FakeApi(), _download

    monkeypatch.setattr("analysis_d2e.src.download_d2e_subset.import_hf", fake_import_hf)
    rows = download_subset(output_root=tmp_path, games=("Barony",), max_file_mb=1.0, write_status=False)

    assert any(row["status"] == "downloaded" or row["status"] == "failed" for row in rows if row["repo_file"].endswith(".mcap"))
    assert any(row["status"] == "skipped_size_limit" for row in rows if row["repo_file"].endswith(".mkv"))
