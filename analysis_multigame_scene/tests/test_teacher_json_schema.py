from analysis_multigame_scene.src.common import write_teacher_schema


def test_teacher_json_schema_file_is_written():
    path = write_teacher_schema()
    assert path.exists()
    assert "teacher_label_schema" in path.name

