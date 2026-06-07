# D2E 행동 prior + 모바일 Bayesian 입력 디코딩 파이프라인

## 현재 데이터 상태

- 현재 저장소에서 실제 `D2E-480p` 원본 manifest/frame/input log는 확인되지 않았다.
- 따라서 현재 실행 결과는 `analysis_d2e/data/fixtures/d2e_smoke`에 생성한 synthetic fixture 기반 smoke 검증이다.
- 이 fixture는 코드 경로와 metric 계산을 검증하기 위한 것이며, 실제 D2E 성능으로 주장하면 안 된다.
- 실제 D2E-480p는 Hugging Face `open-world-agents/D2E-480p`에 있으며, 게임별 `.mcap` 이벤트 파일과 `.mkv` 480p 영상이 paired로 제공된다.

## 실제 D2E 배치 위치

기본 위치:

```text
analysis_d2e/data/raw/d2e_480p/
```

또는 환경 변수:

```text
D2E_480P_ROOT=/path/to/D2E-480p
```

전처리기는 `csv`, `jsonl`, `json` manifest를 찾고 다음 필드를 유연하게 읽는다.

- frame path: `frame_path`, `image_path`, `image`, `path`, `file`, `screenshot`
- game id: `game_id`, `game`, `game_name`, `title`
- raw input: `raw_input`, `input`, `inputs`, `keys`, `keyboard`, `mouse`, `mouse_buttons`, `action`
- optional situation fields: `phase`, `player_hp_norm`, `melee_enemy_count`, `ranged_enemy_count`, `boss_visible`, `boss_telegraph`

## 실행 순서

부분 다운로드:

```powershell
uv run --with huggingface_hub python -m analysis_d2e.src.download_d2e_subset --games Barony Brotato Skul Core_Keeper Vampire_Survivors --max-recordings-per-game 1 --dry-run
uv run --with huggingface_hub python -m analysis_d2e.src.download_d2e_subset --games Barony Brotato Core_Keeper --max-recordings-per-game 1 --max-file-mb 500
```

실제 D2E:

```powershell
uv run --with pillow --with mcap-owa-support --with owa-msgs python -m analysis_d2e.src.d2e_preprocess
uv run --with pillow python -m analysis_d2e.src.d2e_action_prior_model --min-label-confidence 0.2
uv run --with pillow python -m analysis_d2e.src.evaluate
uv run python -m analysis_d2e.src.report_builder
```

smoke fixture:

```powershell
uv run --with pillow python -m analysis_d2e.src.generate_smoke_fixture
uv run --with pillow python -m analysis_d2e.src.d2e_preprocess --root .\analysis_d2e\data\fixtures\d2e_smoke
uv run --with pillow python -m analysis_d2e.src.d2e_action_prior_model
uv run --with pillow python -m analysis_d2e.src.evaluate
uv run python -m analysis_d2e.src.report_builder
```

## 모델 역할

`D2EActionPriorModel`은 최근 N프레임 화면에서 `P(action | environment)`를 예측한다.

실제 D2E `.mcap` 처리 시 `screen`, `keyboard`, `keyboard/state`, `mouse`, `mouse/state`, `mouse/raw` topic을 읽는다. `screen` frame은 paired `.mkv`에서 디코딩되고, 최근 key/mouse 상태는 atomic action label proxy로 변환된다.

WASD 이동만 있는 frame은 Attack/Dodge/Skill/Heal/Escape 같은 버튼 행동 label로 보기 어렵다. 그래서 전처리 결과에는 `action_label_confidence`와 `has_action_input`을 기록하고, 학습 시 `--min-label-confidence`로 낮은 신호 샘플을 제외할 수 있다.

D2E에는 이 연구에서 정의한 Phase 1/2/3 라벨이 직접 포함되어 있지 않다. 전처리 결과의 `phase_source`가 `provided`가 아니면 phase는 metadata 또는 action-input 기반 weak proxy다. 따라서 phase별 결과는 직접 전투 시나리오 정답 검증이 아니라 제한적인 proxy 분석으로 해석해야 한다.

Atomic actions:

```text
attack, defense, dodge, skill, heal, escape
```

이 분포는 `ActionToNButtonProjector`에서 N-button prior로 변환된다.

- N=2: `attack`, `defense`
- N=3: `attack`, `defense`, `skill`
- N=4: `attack`, `defense`, `skill`, `context`

`context`는 고정 정답 버튼이 아니라 동적 보조 슬롯이다. `ContextButtonPolicy`가 `context_action`과 `visible_label`을 함께 산출한다.

## Bayesian decoder

점수식:

```text
score(button_i)
= log P(touch | button_i, user)
+ lambda(skill_profile) * log P(button_i | situation, N)
```

보정 조건:

1. 터치가 ambiguous region에 있어야 한다.
2. max posterior >= tau
3. posterior gap >= delta
4. 선택 버튼이 cooldown/executable 조건을 만족해야 한다.
5. 명확한 입력은 절대 다른 버튼으로 바꾸지 않는다.

## 평가 산출물

```text
analysis_d2e/outputs/reports/baseline_comparison.csv
analysis_d2e/outputs/reports/phase_n_skill_results.csv
analysis_d2e/outputs/reports/button_confusion_matrix.csv
analysis_d2e/outputs/reports/derived_decoder_metrics.csv
analysis_d2e/outputs/reports/final_d2e_bayesian_decoder_report_ko.md
```

현재 smoke 결과는 실제 데이터 검증이 아니라 다음을 확인한다.

- 전처리 가능 여부
- raw input to atomic action mapping
- N-button prior 변환
- context visible label 생성
- skill-aware threshold 적용
- clear input preservation
- baseline metric 계산
