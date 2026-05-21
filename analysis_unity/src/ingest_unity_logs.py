from analysis_unity.src.common import ingest_unity_logs, parse_sessions_arg


def main() -> None:
    ingest_unity_logs(parse_sessions_arg())


if __name__ == "__main__":
    main()

