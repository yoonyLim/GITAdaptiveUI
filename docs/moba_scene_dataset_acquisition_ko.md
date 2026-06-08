# MOBA 장면 데이터 확보 기록

## 확보한 원본

| 데이터셋 | 저장 위치 | 원본 파일 수 | 원본 크기 | 처리 결과 |
|---|---:|---:|---:|---:|
| Betty Dota 2 Decision Context | `analysis_multigame_scene/data/raw/betty_dota2/` | 6 | 8,993,361 bytes | 406 symbolic scene rows |
| League of Legends decoded replay packets | `analysis_multigame_scene/data/raw/lol_replay_packets/` | 1 | 86,557,239 bytes | 77 symbolic scene rows |
| OpenDota parsed match subset | `analysis_multigame_scene/data/raw/opendota/` | 16 | 3,169,214 bytes | 93 symbolic scene rows |

총 23개 원본 파일, 98,719,814 bytes를 확보했다.

## 왜 이 데이터를 추가했는가

기존 장면 데이터는 ViZDoom 중심이라 FPS 전투/위협 장면에 치우쳐 있었다. 상황 인식 pretraining에는 여러 게임 장르의 장면 구조가 필요하므로 MOBA 데이터를 추가했다.

MOBA 데이터는 다음 상황을 보강한다.

- 다중 유닛/다중 목표가 동시에 존재하는 장면
- 교전, 후퇴, 오브젝트, 저체력, 사망 이벤트가 섞인 장면
- action_intensity, temporal_urgency, information_priority가 동시에 높은 장면
- FPS와 다른 탑다운 전술/목표 중심 상황

## 처리 방식

다운로드한 원본은 대부분 실제 스크린샷이 아니라 상태/이벤트/리플레이 로그다. 따라서 원본 이벤트를 그대로 이미지 모델에 넣지 않고, 영웅 위치, 교전 이벤트, 사망 이벤트, 오브젝트 이벤트를 160x160 symbolic scene frame으로 렌더링했다.

생성 위치:

- `analysis_multigame_scene/data/frames/moba_symbolic/betty_dota2/`
- `analysis_multigame_scene/data/frames/moba_symbolic/lol_replay_packets/`
- `analysis_multigame_scene/data/frames/moba_symbolic/opendota/`

처리 결과:

- `analysis_multigame_scene/data/processed/moba_scene_samples.parquet`
- `analysis_multigame_scene/data/processed/multigame_scene_samples.parquet`
- `analysis_multigame_scene/outputs/reports/moba_scene_dataset_summary.csv`
- `analysis_multigame_scene/outputs/reports/dataset_size_summary.csv`

## 라벨의 의미

MOBA weak label은 정답 행동을 의미하지 않는다. 다음 proxy만 만든다.

- `weak_threat_level`
- `weak_action_window`
- `weak_urgency_level`
- `weak_action_intensity`
- `weak_temporal_urgency`
- `weak_information_priority`
- `weak_occlusion_risk`
- `weak_control_continuity`

라벨 출처는 `moba_state_event_proxy`다.

## 한계

- 실제 렌더링된 게임 화면 스크린샷이 아니라 symbolic render다.
- 유저 의도나 Attack/Dodge 정답을 제공하지 않는다.
- Unity의 Attack/Dodge Bayesian correction을 직접 검증하지 않는다.
- 역할은 상황 인식 teacher/student pretraining의 장면 다양성 보강이다.
