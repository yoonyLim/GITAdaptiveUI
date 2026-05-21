from analysis_unity.src.common import generate_smoke_session


def main() -> None:
    path = generate_smoke_session()
    print(path)


if __name__ == "__main__":
    main()

