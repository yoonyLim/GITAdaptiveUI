from analysis_multigame.src.common import prior_from_abstract


def test_prior_builder_maps_avoid_to_dodge() -> None:
    prior = prior_from_abstract("active", "avoid", 0.9)
    assert prior["prior_dodge"] > prior["prior_attack"]


def test_prior_builder_low_confidence_neutral() -> None:
    prior = prior_from_abstract("critical", "avoid", 0.1)
    assert abs(prior["prior_attack"] - 0.5) < 1e-6
    assert abs(prior["prior_dodge"] - 0.5) < 1e-6

