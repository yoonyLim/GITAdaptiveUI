from analysis_unity.src.common import report_builder
from analysis_unity.src.multigame_vision_common import build_final_multigame_vision_unity_report
from analysis_unity.src.teacher_student_common import build_teacher_student_unity_report


def main() -> None:
    report_builder()
    build_final_multigame_vision_unity_report()
    build_teacher_student_unity_report()


if __name__ == "__main__":
    main()
