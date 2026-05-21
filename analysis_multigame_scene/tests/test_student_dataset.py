from analysis_multigame_scene.src.common import load_scene_samples, student_features


def test_student_dataset_features_have_values():
    sample = load_scene_samples(1)[0]
    features = student_features(sample)
    assert len(features) >= 12
