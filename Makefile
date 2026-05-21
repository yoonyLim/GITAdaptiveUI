PY=uv run --with pyarrow --with matplotlib
MULTIGAME_PY=uv run --with pyarrow --with matplotlib --with vizdoom
PYTEST=uv run --with pytest --with matplotlib --with pyarrow

.PHONY: multigame-generate multigame-train multigame-evaluate discover-scenes acquire-scenes acquire-moba-scenes acquire-dota2-event acquire-hokoff acquire-extra-screen-scenes teacher-label teacher-label-real teacher-label-real-screen-100 teacher-label-real-screen-200 teacher-latency student-train student-latency async-runtime unity-teacher-student-eval public-download public-inspect public-preprocess public-evaluate public-config vision-download vision-build vision-inspect unity-ingest unity-train unity-evaluate compare report test all

multigame-generate:
	$(MULTIGAME_PY) python -m analysis_multigame.src.data.generate_vizdoom_dataset --mode small
	$(MULTIGAME_PY) python -m analysis_multigame.src.data.download_atari_head_subset --mode small
	$(MULTIGAME_PY) python -m analysis_multigame.src.data.download_minerl_subset --mode minimal
	$(MULTIGAME_PY) python -m analysis_multigame.src.data.download_dqn_replay_subset --mode small
	$(MULTIGAME_PY) python -m analysis_multigame.src.data.inspect_multigame_datasets

multigame-train:
	$(MULTIGAME_PY) python -m analysis_multigame.src.preprocess.build_multigame_dataset
	$(MULTIGAME_PY) python -m analysis_multigame.src.train.train_multigame_scene_head
	$(MULTIGAME_PY) python -m analysis_multigame.src.train.train_clip_zero_shot_baseline

multigame-evaluate:
	$(MULTIGAME_PY) python -m analysis_multigame.src.evaluation.evaluate_scene_recognition
	$(MULTIGAME_PY) python -m analysis_multigame.src.evaluation.evaluate_cross_game_generalization
	$(MULTIGAME_PY) python -m analysis_multigame.src.models.prior_builder
	$(MULTIGAME_PY) python -m analysis_multigame.src.evaluation.report_builder

discover-scenes:
	$(PY) python -m analysis_multigame_scene.src.data.discover_game_scene_datasets
	$(PY) python -m analysis_multigame_scene.src.data.dataset_selection

acquire-scenes:
	$(PY) --with imageio --with imageio-ffmpeg --with pillow python -m analysis_multigame_scene.src.data.acquire_dota2_event_extraction
	$(PY) --with imageio --with imageio-ffmpeg --with pillow python -m analysis_multigame_scene.src.data.acquire_bleeding_edge_gameplay
	$(PY) --with pillow --with huggingface_hub python -m analysis_multigame_scene.src.data.acquire_gameplay_captions
	$(PY) --with pillow --with huggingface_hub python -m analysis_multigame_scene.src.data.acquire_gameplay_images
	$(PY) --with pillow --with requests python -m analysis_multigame_scene.src.data.acquire_atari_head_subset
	$(PY) --with h5py python -m analysis_multigame_scene.src.data.acquire_hokoff
	$(PY) python -m analysis_multigame_scene.src.data.acquire_moba_scene_datasets
	$(PY) python -m analysis_multigame_scene.src.data.build_all_sources
	$(PY) python -m analysis_multigame_scene.src.data.inspect_all_sources

acquire-moba-scenes:
	$(PY) python -m analysis_multigame_scene.src.data.acquire_moba_scene_datasets

acquire-dota2-event:
	$(PY) --with imageio --with imageio-ffmpeg --with pillow python -m analysis_multigame_scene.src.data.acquire_dota2_event_extraction

acquire-hokoff:
	$(PY) --with h5py python -m analysis_multigame_scene.src.data.acquire_hokoff

acquire-extra-screen-scenes:
	$(PY) --with imageio --with imageio-ffmpeg --with pillow python -m analysis_multigame_scene.src.data.acquire_bleeding_edge_gameplay
	$(PY) --with pillow --with huggingface_hub python -m analysis_multigame_scene.src.data.acquire_gameplay_captions
	$(PY) --with pillow --with huggingface_hub python -m analysis_multigame_scene.src.data.acquire_gameplay_images
	$(PY) --with pillow --with requests python -m analysis_multigame_scene.src.data.acquire_atari_head_subset

teacher-label:
	$(PY) python -m analysis_multigame_scene.src.teacher.run_teacher_labeling --provider dryrun --mode dryrun --max-samples 120
	$(PY) python -m analysis_multigame_scene.src.teacher.validate_teacher_outputs

teacher-label-real:
	$(PY) python -m analysis_multigame_scene.src.teacher.run_teacher_labeling --provider codex_cli --mode single_frame --max-samples 50 --model gpt-5.5 --force-retry
	$(PY) python -m analysis_multigame_scene.src.teacher.validate_teacher_outputs

teacher-label-real-screen-100:
	$(PY) python -m analysis_multigame_scene.src.teacher.run_teacher_labeling --provider codex_cli --mode single_frame --actual-screen-only --per-source vizdoom_generated=50 --per-source dota2_event_extraction_video=50 --model gpt-5.5
	$(PY) python -m analysis_multigame_scene.src.teacher.validate_teacher_outputs

teacher-label-real-screen-200:
	$(PY) python -m analysis_multigame_scene.src.teacher.run_teacher_labeling --provider codex_cli --mode single_frame --actual-screen-only --per-source vizdoom_generated=50 --per-source dota2_event_extraction_video=50 --per-source bleeding_edge_gameplay_sample=50 --per-source gameplay_captions=50 --model gpt-5.5
	$(PY) python -m analysis_multigame_scene.src.teacher.validate_teacher_outputs

teacher-latency:
	$(PY) python -m analysis_multigame_scene.src.latency.benchmark_codex_teacher_latency --provider dryrun --max-samples 30 --model gpt-5.5

student-train:
	$(PY) python -m analysis_multigame_scene.src.student.train_student
	$(PY) python -m analysis_multigame_scene.src.student.evaluate_student
	$(PY) python -m analysis_multigame_scene.src.student.export_student
	$(PY) python -m analysis_multigame_scene.src.decoder.prior_builder

student-latency:
	$(PY) python -m analysis_multigame_scene.src.latency.benchmark_student_latency

async-runtime:
	$(PY) python -m analysis_multigame_scene.src.latency.benchmark_async_runtime

unity-teacher-student-eval:
	$(PY) python -m analysis_unity.src.evaluate_teacher_student_prior
	$(PY) python -m analysis_unity.src.compare_prior_sources
	$(PY) python -m analysis_unity.src.ablation_teacher_student

public-download:
	$(PY) python -m analysis_public.src.data.download_all_required

public-inspect:
	$(PY) python -m analysis_public.src.data.inspect_public_datasets

public-preprocess:
	$(PY) python -m analysis_public.src.preprocess.build_all_public

public-evaluate:
	$(PY) python -m analysis_public.src.evaluation.evaluate_public_touch_dynamics
	$(PY) python -m analysis_public.src.evaluation.evaluate_public_target_selection
	$(PY) python -m analysis_public.src.evaluation.public_report_builder

public-config:
	$(PY) python -m analysis_public.src.evaluation.build_public_to_unity_config

vision-download:
	uv run python -m analysis_vision.src.download_vision_datasets

vision-build:
	uv run python -m analysis_vision.src.build_vision_grounding_dataset

vision-inspect:
	uv run python -m analysis_vision.src.inspect_vision_datasets

unity-ingest:
	$(PY) python -m analysis_unity.src.ingest_unity_logs
	$(PY) python -m analysis_unity.src.build_unity_dataset

unity-train:
	$(PY) python -m analysis_unity.src.train_user_touch_model

unity-evaluate:
	$(PY) python -m analysis_unity.src.evaluate_decoders
	$(PY) python -m analysis_unity.src.evaluate_hp_outcomes
	$(PY) python -m analysis_unity.src.ablation
	$(PY) python -m analysis_unity.src.evaluate_modes
	$(PY) python -m analysis_unity.src.evaluate_multigame_vision_prior
	$(PY) python -m analysis_unity.src.compare_unity_only_vs_multigame_vision
	$(PY) python -m analysis_unity.src.evaluate_unity_vision_leakage

compare:
	$(PY) python -m analysis_unity.src.compare_public_and_unity

report:
	$(PY) python -m analysis_unity.src.report_builder
	$(PY) python -m analysis_multigame_scene.src.reports.build_report

test:
	$(PYTEST) pytest -p no:cacheprovider

all: multigame-generate multigame-train multigame-evaluate discover-scenes acquire-scenes teacher-label teacher-latency student-train student-latency async-runtime public-download public-preprocess public-evaluate public-config vision-download vision-build vision-inspect unity-ingest unity-train unity-evaluate unity-teacher-student-eval compare report test
