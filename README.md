# GITAdaptiveUI

2-button action-first adaptive UI prototype with Unity telemetry, public dataset evidence, Bayesian decoding, safety-gated correction, and analysis reports.

## Why Public Datasets Are Included

Public datasets are used actively as external evidence and sanity-check benchmarks:

- Touch-Dynamics-Research and MC-Snake-Results: touch dynamics and user variation.
- Google TSI: target-labeled touch decoding and hitbox baseline sanity checks.
- Henze Hit It / 100M taps: optional circular target hit/miss benchmark when manually available.
- Rico and Screen Annotation: UI grounding and button-like component support.

Important limits:

- Public game touch logs validate touch dynamics, not Attack/Dodge correction.
- Public target-selection datasets validate touch-target modeling and hitbox behavior, not game-state prior.
- Public UI datasets support UI grounding, not game-specific combat context.
- Unity controlled prototype validates the actual Attack/Dodge Bayesian pipeline.
- This is not yet commercial mobile game validation.

## Unity Frontend

Existing frontend scripts are preserved under `Assets/00Scripts/`. New model and logging scripts are under:

- `Assets/ADUI/Scripts/Models/`
- `Assets/ADUI/Scripts/DataLogging/`
- `Assets/ADUI/Scripts/Adaptation/`
- `Assets/ADUI/Scripts/Feedback/`
- `Assets/ADUI/Scripts/Survey/`
- `Assets/ADUI/Editor/`

Inventory is documented in `Assets/ADUI/Docs/frontend_inventory.md`.

The Unity frontend now includes all four report-level interaction modes:

- `ActionFirst`
- `CognitiveFirst`
- `GuidanceProcedure`
- `LearningReview`

The implemented policy controls include visibility, emphasis, density, position constraint, interaction error tolerance, correction strength, hitbox expansion, ambiguity margin, haptic feedback, guidance visibility, and review visibility. See `docs/full_framework_implementation_ko.md`.

## Public Pipeline

Place real public files under `analysis_public/data/<dataset>/raw` or `manual`, then run:

```bash
uv run --with pyarrow --with matplotlib python -m analysis_public.src.data.download_all_required
uv run --with pyarrow --with matplotlib python -m analysis_public.src.data.inspect_public_datasets
uv run --with pyarrow --with matplotlib python -m analysis_public.src.preprocess.build_all_public
uv run --with pyarrow --with matplotlib python -m analysis_public.src.evaluation.evaluate_public_touch_dynamics
uv run --with pyarrow --with matplotlib python -m analysis_public.src.evaluation.evaluate_public_target_selection
uv run --with pyarrow --with matplotlib python -m analysis_public.src.evaluation.build_public_to_unity_config
uv run --with pyarrow --with matplotlib python -m analysis_public.src.evaluation.public_report_builder
```

If public files are unavailable, reports explicitly record `unavailable`; no public data is fabricated.

## Vision Grounding Pipeline

Vision grounding is the layer that learns screen UI elements before passing detected layout/state into the Bayesian decoder. It is separate from the direct Unity Attack/Dodge validation.

```bash
uv run python -m analysis_vision.src.download_vision_datasets
uv run python -m analysis_vision.src.build_vision_grounding_dataset
uv run python -m analysis_vision.src.inspect_vision_datasets
```

Current supported sources:

- Screen Annotation: UI element type/location/text grounding.
- Rico: screenshots and view hierarchies when manually placed.
- Widget Caption: widget semantic captions when downloadable or manually placed.
- ScreenQA/UICrit/VINS: optional grounding/region/component datasets.

Unity can also create game-specific vision labels with `VisionFrameLogger`, which saves screenshots plus Attack/Dodge boxes and enemy state into `vision_frames.jsonl`.

## Public Multi-game Situation Pretraining

The situation recognizer is not a Unity-only classifier. Unity-only scene classification can overfit to Unity colors, text, enemy model, UI layout, and camera style. The active multi-game layer uses ViZDoom state/reward weak labels and predicts abstract context only:

- `ui_phase`
- `threat_level`
- `action_window`
- `urgency_level`
- interaction-demand scores
- confidence

It does not directly predict Attack or Dodge. `VisionPriorBuilder` maps abstract context into `P(Attack | s_t), P(Dodge | s_t)`, then the existing Bayesian decoder and safety gate make the final decision.

```bash
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.data.generate_vizdoom_dataset --mode small
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.data.download_atari_head_subset --mode small
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.data.download_minerl_subset --mode minimal
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.data.download_dqn_replay_subset --mode small
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.data.inspect_multigame_datasets
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.preprocess.build_multigame_dataset
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.train.train_multigame_scene_head
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.models.prior_builder
uv run --with pyarrow --with matplotlib python -m analysis_multigame.src.evaluation.report_builder
```

This pipeline supports real ViZDoom runtime generation:

```bash
uv run --with pyarrow --with matplotlib --with vizdoom python -m analysis_multigame.src.data.generate_vizdoom_dataset --mode small
```

DINO was removed from the active pipeline after held-out scenario checks showed that it did not provide reliable situation understanding for this project. If ViZDoom is unavailable, generated data/model outputs are marked as fixture and must not be reported as real ViZDoom evidence.

## Multi-game Teacher/Student Situation Prior

`analysis_multigame_scene` is the current teacher/student layer. It catalogs diverse public game scene sources, reuses generated ViZDoom frames, optionally labels representative frames with Codex CLI OAuth, trains a lightweight student, benchmarks situation-update latency, and exports a mode-policy prior config.

Codex CLI teacher labeling uses ChatGPT OAuth, not an API key:

```bash
codex login status
codex login
# or
codex login --device-auth
```

The teacher is offline only. It is not a runtime touch-decoding dependency and it must not directly choose Attack/Dodge. If `codex exec` is blocked by local certificate or sandbox permissions, the pipeline records teacher labels as unavailable and uses dryrun/heuristic weak labels only for tests.

```bash
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.data.discover_game_scene_datasets
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.data.build_all_sources
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.teacher.run_teacher_labeling --provider codex_cli --mode single_frame --max-samples 50 --model gpt-5.5
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.teacher.validate_teacher_outputs
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.latency.benchmark_codex_teacher_latency --provider codex_cli --max-samples 30 --model gpt-5.5
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.student.train_student
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.student.evaluate_student
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.student.export_student
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.latency.benchmark_student_latency
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.decoder.prior_builder
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.latency.benchmark_async_runtime
uv run --with pyarrow --with matplotlib python -m analysis_multigame_scene.src.reports.build_report
```

The exported config is written to:

- `analysis_multigame_scene/outputs/models/mode_policy_prior_config.json`
- `Assets/StreamingAssets/ADUI/mode_policy_prior_config.json`

## Unity Analysis

For a smoke fixture:

```bash
uv run --with matplotlib python -m analysis_unity.src.generate_smoke_session
uv run --with matplotlib python -m analysis_unity.src.ingest_unity_logs
uv run --with matplotlib python -m analysis_unity.src.build_unity_dataset
uv run --with matplotlib python -m analysis_unity.src.train_user_touch_model
uv run --with matplotlib python -m analysis_unity.src.evaluate_decoders
uv run --with matplotlib python -m analysis_unity.src.evaluate_hp_outcomes
uv run --with matplotlib python -m analysis_unity.src.ablation
uv run --with matplotlib python -m analysis_unity.src.evaluate_modes
uv run --with matplotlib python -m analysis_unity.src.evaluate_multigame_vision_prior
uv run --with matplotlib python -m analysis_unity.src.compare_unity_only_vs_multigame_vision
uv run --with matplotlib python -m analysis_unity.src.evaluate_unity_vision_leakage
uv run --with matplotlib python -m analysis_unity.src.evaluate_teacher_student_prior
uv run --with matplotlib python -m analysis_unity.src.compare_prior_sources
uv run --with matplotlib python -m analysis_unity.src.ablation_teacher_student
uv run --with matplotlib python -m analysis_unity.src.compare_public_and_unity
uv run --with matplotlib python -m analysis_unity.src.report_builder
```

For real Unity sessions, pass the Unity `adui_sessions` path:

```bash
uv run python -m analysis_unity.src.ingest_unity_logs --sessions <path_to_adui_sessions>
```

## Tests

```bash
uv run --with pytest --with matplotlib --with pyarrow pytest -p no:cacheprovider
```

Makefile targets are also available:

```bash
make public-download public-preprocess public-evaluate public-config
make multigame-generate multigame-train multigame-evaluate
make discover-scenes acquire-scenes teacher-label teacher-latency student-train student-latency async-runtime unity-teacher-student-eval
make unity-ingest unity-train unity-evaluate compare report test
```
