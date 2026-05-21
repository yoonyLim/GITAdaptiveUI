# 실제 MOBA 화면/HoK 데이터 확보 기록

## 1. DOTA2 Event Extraction Gameplay Video Dataset

원 논문:

- Zijin Luo, Matthew Guzdial, Mark Riedl, *Making CNNs for Video Parsing Accessible*
- 논문 소스의 footnote 링크: `https://github.com/icpm/dota2-dataset`

확보 결과:

| 항목 | 값 |
|---|---:|
| 원본 ZIP | `analysis_multigame_scene/data/raw/dota2_event_extraction_dataset/icpm_dota2_dataset_master.zip` |
| 원본 크기 | 642,251,085 bytes |
| MP4 클립 수 | 100 |
| 이벤트 클래스 수 | 10 |
| 추출 프레임 수 | 9,000 |
| 추출 프레임 위치 | `analysis_multigame_scene/data/frames/dota2_event_extraction/` |
| 처리 테이블 | `analysis_multigame_scene/data/processed/dota2_event_frames.parquet` |
| 요약 리포트 | `analysis_multigame_scene/outputs/reports/dota2_event_extraction_summary.csv` |

이벤트 클래스:

- `bkb`
- `eul`
- `glyph`
- `lose`
- `roshan`
- `shiva`
- `shrine`
- `teamFight`
- `teleport`
- `towerDestory`

각 이벤트 클래스당 10개 클립, 900개 프레임을 추출했다.

역할:

- 실제 DOTA2 gameplay video frame 기반 상황 인식 teacher/student 학습 데이터
- action_intensity, temporal_urgency, information_priority 등 interaction-demand weak label의 근거
- MOBA 실제 화면 도메인 보강

한계:

- 이벤트 라벨은 DOTA2 이벤트 클래스이며 Unity Attack/Dodge 라벨이 아니다.
- 버튼 레이아웃, 터치 좌표, Unity 전투 상태는 없다.
- 따라서 Attack/Dodge Bayesian correction의 직접 검증에는 쓰지 않는다.

## 2. Hokoff / HoK Env

확보한 파일:

| 항목 | 위치 | 크기 |
|---|---|---:|
| Hokoff code | `analysis_multigame_scene/data/raw/hokoff/hokoff_main.zip` | 978,238 bytes |
| HoK Env code | `analysis_multigame_scene/data/raw/hok_env/hok_env_master.zip` | 231,244,219 bytes |
| Hokoff 1v1 norm_medium dataset | `analysis_multigame_scene/data/raw/hokoff/datasets/1v1_norm_medium.zip` | 195,048,247 bytes |

Hokoff 1v1 데이터 내부:

| HDF5 dataset | shape | dtype |
|---|---:|---|
| `datas` | `1776809x911` | `float32` |

처리 결과:

- `analysis_multigame_scene/data/processed/hokoff_1v1_state_samples.parquet`
- `analysis_multigame_scene/outputs/reports/hokoff_1v1_norm_medium_schema.csv`
- `analysis_multigame_scene/outputs/reports/hokoff_1v1_norm_medium_summary.csv`

역할:

- Honor of Kings 기반 실제 MOBA offline RL 상태/행동 구조 근거
- MOBA 상황의 상태 공간 복잡성, 연속 조작, 전술적 정보량을 논의하는 외부 근거
- 화면 프레임 모델의 직접 학습 데이터가 아니라 state-vector branch 또는 해석 보조 데이터

한계:

- 다운로드한 Hokoff 1v1 `norm_medium`는 실제 스크린샷 프레임이 아니라 HDF5 상태 벡터다.
- HoK Env는 실행 환경 코드이며 gamecore/license가 필요하다.
- 현재 Codex 환경에서는 HoK gamecore를 실행해 실제 화면을 렌더링하지 않았다.
- 따라서 HoK/Hokoff는 실제 화면 teacher 라벨링 데이터가 아니라 MOBA state/action evidence로 분리한다.

## 현재 전체 장면 데이터셋 반영

`analysis_multigame_scene/data/processed/multigame_scene_samples.parquet`에 반영된 프레임 데이터:

- ViZDoom generated frames: 25,000
- DOTA2 event extraction actual gameplay frames: 9,000
- Betty Dota2 symbolic MOBA frames: 406
- League of Legends symbolic MOBA frames: 77
- OpenDota symbolic MOBA frames: 93

총 34,576개 장면 row가 있다.

## 해석 주의

이 데이터는 상황 인식 pretraining 및 teacher/student 라벨링을 위한 외부 데이터다. 최종 Attack/Dodge correction 성능 주장은 Unity telemetry에서만 해야 한다.
