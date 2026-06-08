# 데이터셋 추가 수집 및 Codex teacher 라벨링 라운드 2

## 1. 이번 라운드 목표

기존 데이터가 ViZDoom, DOTA2, 일부 gameplay screenshot에 치우쳐 있었기 때문에 실제 게임 화면 다양성을 늘리고, 새 화면에 대해 Codex CLI gpt-5.5 teacher 라벨을 추가했다. 특히 전투 중심 `action_first`뿐 아니라 2D arcade/reward, open-world, racing, social/cognition-heavy 화면을 더 넣는 것을 목표로 했다.

## 2. 추가/확장한 실제 화면 데이터

| 데이터셋 | 이전 frame | 현재 frame | 확보 방식 | 역할 |
|---|---:|---:|---|---|
| Bingsu Gameplay Images | 500 | 1,000 | Hugging Face parquet shard, 10개 게임 균형 추출 | 다양한 실제 gameplay screenshot |
| GameplayCaptions | 500 | 1,000 | Hugging Face parquet shard 확장 | caption 기반 gameplay screenshot |
| Atari-HEAD | 0 | 300 | Zenodo `breakout.zip` 다운로드 후 tar.bz2 내부 PNG 추출 | 2D arcade, action/reward/gaze 기반 상황 다양성 |

통합 scene table 기준 현재 frame 수:

| source | frames | games/scenarios |
|---|---:|---:|
| ViZDoom generated | 25,000 | 6 scenarios |
| DOTA2 event extraction video | 9,000 | 10 event classes |
| Bleeding Edge sample | 100 | 4 clips |
| GameplayCaptions | 1,000 | 1 local shard group |
| Bingsu Gameplay Images | 1,000 | 10 games |
| Atari-HEAD subset | 300 | Breakout |

총 실제/생성 화면 row는 36,400개다.

## 3. 수집하지 못한 후보

| 후보 | 상태 | 이유 |
|---|---|---|
| taesiri/Gameplay_frames | 미확보 | Hugging Face API가 401 Unauthorized를 반환해 토큰 없이 접근 불가 |
| elefantai/p2p-full-data | metadata만 확인 | frame batch tar가 최소 수 GB~수십 GB라 이번 라운드 다운로드 대상에서 제외 |
| AGAIN | 미확보 | 공개 페이지는 확인됐지만 다운로드가 form/access 절차 기반이라 자동 수집 불가 |
| Procgen | 미확보 | 현재 Python 3.11 환경에서 wheel 호환 문제가 있어 별도 Python 3.10 환경 필요 |

## 4. Codex CLI teacher 라벨링 결과

Codex CLI OAuth 상태에서 `gpt-5.5` teacher를 사용했다.

| 항목 | 값 |
|---|---:|
| 전체 teacher label | 332 |
| 실제 Codex teacher label | 332 |
| fallback/dryrun label | 0 |
| valid JSON rate | 1.0 |
| 마지막 실행 요청 | Gameplay Images 20, GameplayCaptions 20 |
| 마지막 실행 실제 호출 | 16 |
| 마지막 실행 cache hit | 24 |
| 마지막 실행 p50 latency | 18,622 ms |
| 마지막 실행 p95 latency | 21,384 ms |

라벨 source 분포:

| source | labels |
|---|---:|
| GameplayCaptions | 110 |
| Gameplay Images | 62 |
| ViZDoom | 50 |
| DOTA2 | 50 |
| Bleeding Edge | 50 |
| Atari-HEAD | 10 |

mode 분포:

| dominant_mode | labels |
|---|---:|
| action_first | 303 |
| cognition_first | 16 |
| guidance_procedure | 13 |

## 5. 해석

데이터셋 다양성은 늘었지만, teacher label 분포는 여전히 `action_first`에 크게 치우쳐 있다. Atari-HEAD Breakout을 추가했어도 teacher는 많은 frame을 즉각적인 조작 요구가 있는 장면으로 판단했다. 따라서 다음 라운드는 단순히 게임 종류를 늘리는 것만으로는 부족하고, menu, map, inventory, objective, dialogue, tutorial, result 화면을 의도적으로 더 샘플링해야 한다.

## 6. 다음 추천 수집

1. Bingsu에서 Among Us, Minecraft, Genshin, Roblox, Terraria 중심으로 cognition/guidance 후보를 추가 샘플링한다.
2. GameplayCaptions caption에 `menu`, `map`, `inventory`, `dialogue`, `quest`, `tutorial`, `score`, `result`가 포함된 frame을 우선 라벨링한다.
3. Atari-HEAD는 Breakout 외 `freeway`, `ms_pacman`, `montezuma_revenge` 중 작은 subset을 추가한다.
4. Codex teacher 라벨 목표는 다음처럼 맞춘다.
   - action_first: 300 내외로 유지
   - cognition_first: 200 이상
   - guidance_procedure: 150 이상
   - learning_review/result/tutorial: 100 이상

현재 단계의 결론은 “데이터 확보와 실제 라벨링은 성공했지만, 일반 상황 인식기로 만들려면 라벨 균형을 더 강하게 제어해야 한다”이다.
