from analysis_multigame_scene.src.common import (
    ACTION_WINDOWS,
    DEMAND_KEYS,
    DOMINANT_MODES,
    MODE_KEYS,
    POLICIES,
    THREAT_LEVELS,
    UI_PHASES,
    validate_teacher_label,
)


INTERACTION_DEMAND_SCHEMA = {
    "variables": {
        "action_intensity": "행동집약도: 현재 장면에서 동시에 처리해야 하는 행동 수와 입력 밀도",
        "temporal_urgency": "순간긴급도: 입력 지연이 실패로 이어지는 시간 압박",
        "information_priority": "정보우선도: 의사결정에 필요한 정보의 중요도",
        "occlusion_risk": "가려짐위험도: 손가락/버튼/HUD가 중요한 정보를 가릴 위험",
        "control_continuity": "조작연속성: 조작이 단발성인지 연속 흐름인지",
        "ui_skill_proxy": "사용자숙련도 proxy: touch/user history 없으면 null",
    },
    "modes": {
        "action_first": "행동 우선",
        "cognition_first": "인지 우선",
        "guidance_procedure": "안내/절차",
        "learning_review": "학습/복기",
    },
    "allowed": {
        "ui_phase": UI_PHASES,
        "threat_level": THREAT_LEVELS,
        "action_window": ACTION_WINDOWS,
        "dominant_mode": DOMINANT_MODES,
        "policies": POLICIES,
        "demand_keys": DEMAND_KEYS,
        "mode_keys": MODE_KEYS,
    },
}


__all__ = ["INTERACTION_DEMAND_SCHEMA", "validate_teacher_label"]

