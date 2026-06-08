from analysis_multigame_scene.src.teacher.batch_contact_sheet import create_contact_sheet


def test_contact_sheet_placeholder_created():
    path = create_contact_sheet(["a", "b"])
    assert path.exists()

