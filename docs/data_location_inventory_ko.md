# 데이터 위치 인벤토리

작성일: 2026-05-18

## 프로젝트 경로 확인

- 요청 경로 `D:\PROJECT\ADUI`: 현재 실행 세션에 `D:` 드라이브가 없어 접근 불가.
- 실제 작업 경로: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI`
- Unity project root: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI`
- Unity scene: `Assets/Scenes/SampleScene.unity`
- ADUI scripts: `Assets/ADUI/Scripts/`

## 탐색한 기존 데이터 위치

- `analysis_public/data/`: 존재, public data 저장 위치로 사용.
- `analysis_public/outputs/`: 존재, report/figure/model 저장 위치로 사용.
- `analysis_unity/fixtures/adui_sessions/`: Unity-like smoke fixture 저장 위치.
- `D:\PROJECT\ADUI`: 드라이브 없음.
- `mobile-game-adaptive-ui-a2z/data/`: 현재 repo 하위에서 발견되지 않음.
- `D:\PROJECT\ADUI\data\`: 드라이브 없음.

## 확보된 공개 데이터

| dataset | raw path | raw files | raw size | processed path | rows/events | parseable |
|---|---|---:|---:|---|---:|---|
| Touch-Dynamics-Research | `analysis_public/data/touch_dynamics/raw/` | 4 | 44,955,805 bytes | `analysis_public/data/touch_dynamics/processed/touch_dynamics_events.parquet` | 4,095,268 | yes |
| MC-Snake-Results | `analysis_public/data/mc_snake/raw/main.zip` | 1 | 26,643,923 bytes | `analysis_public/data/mc_snake/processed/mc_snake_events.parquet` | 2,369,009 | yes |
| Google TSI | `analysis_public/data/tsi/raw/` | 3 | 28,427,273 bytes | `analysis_public/data/tsi/processed/tsi_touch_targets.parquet` | 43,735 | yes |
| Screen Annotation | `analysis_public/data/screen_annotation/raw/` | 3 | 25,221,819 bytes | `analysis_public/data/screen_annotation/processed/screen_annotation_elements.parquet` | 373,127 | yes |
| Henze | `analysis_public/data/henze/manual/` | 0 | 0 | none | 0 | no local file |
| Rico | `analysis_public/data/rico/manual/` | 0 | 0 | none | 0 | no local file |

## 필드 확인

- Touch-Dynamics/MC-Snake: `Timestamp`, `X`, `Y`, `BTN_TOUCH`, `TOUCH_MAJOR`/`WIDTH_MAJOR`, `TOUCH_MINOR`, `FINGER`, `CLASS` 계열. action label, button layout, game-state label 없음.
- TSI: `participant_id`, `task_id`, `trial_id`, `timestamp_ms`, `ref_char`, `first_frame_touch_x/y`, touch ellipse fields. keyboard layout 있음.
- Screen Annotation: screen id와 UI element annotation text. touch coordinate/action label 없음.

