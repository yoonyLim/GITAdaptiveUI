from analysis_multigame_scene.src.common import benchmark_codex_teacher_latency, parse_args_max_samples


def main() -> None:
    args = parse_args_max_samples(default=20)
    benchmark_codex_teacher_latency(max_samples=args.max_samples, provider=args.provider, model=args.model)


if __name__ == "__main__":
    main()
