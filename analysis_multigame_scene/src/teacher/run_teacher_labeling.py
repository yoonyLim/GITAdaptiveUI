from pathlib import Path

from analysis_multigame_scene.src.common import parse_args_max_samples, parse_per_source_args, run_teacher_labeling


def main() -> None:
    args = parse_args_max_samples(default=50)
    sample_ids = []
    if args.sample_id_file:
        sample_ids = [line.strip() for line in Path(args.sample_id_file).read_text(encoding="utf-8").splitlines() if line.strip()]
    run_teacher_labeling(
        provider=args.provider,
        mode=args.mode,
        max_samples=args.max_samples,
        model=args.model,
        force_retry=args.force_retry,
        source_datasets=args.source_dataset,
        per_source=parse_per_source_args(args.per_source),
        actual_screen_only=args.actual_screen_only,
        sample_ids=sample_ids,
    )


if __name__ == "__main__":
    main()
