# 프로젝트 인벤토리: multi-game teacher/student

- Repository root: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI`
- Root items: .git, .gitignore, .uv-cache, analysis_multigame, analysis_multigame_scene, analysis_public, analysis_unity, analysis_vision, Assets, data, docs, Makefile, outputs, Packages, ProjectSettings, pytest.ini, README.md
- Unity ADUI script count: 27
- 주요 Unity 구성: AdaptiveTouchManager, CombatManager, HP/feedback, Attack/Dodge 버튼, BayesianInputDecoder, SafetyGate, DataLogging 스크립트가 존재한다.
- DINO 계열은 active pipeline에서 제거되어 있으며, 이번 작업은 Codex CLI teacher와 경량 student 중심으로 진행한다.

## 기존 데이터
- ViZDoom parquet: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI\analysis_multigame\data\processed\vizdoom_frames.parquet` / exists=True / size=814024
- Public reports dir: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI\analysis_public\outputs\reports` / exists=True
- Unity reports dir: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI\analysis_unity\outputs\reports` / exists=True

## 새로 필요한 모듈
- dataset discovery/catalog
- Codex CLI teacher schema/cache/labeling/latency
- lightweight student train/evaluate/export/latency
- mode-policy prior builder
- Unity teacher/student prior evaluation