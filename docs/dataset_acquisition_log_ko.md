# 공개 데이터 확보 로그

## Required datasets

### Touch-Dynamics-Research

- Source: `https://github.com/Brprb08/Touch-Dynamics-Research`
- Downloaded files:
  - `diep_raw_data.zip`
  - `mc_raw_data.zip`
  - `pubg_raw_data.zip`
  - `snake_raw_data.zip`
- Local path: `analysis_public/data/touch_dynamics/raw/`
- Processed rows: 4,095,268
- Games: `diep`, `minecraft`, `pubg`, `snake`
- Users: 25

### MC-Snake-Results

- Source: `https://github.com/zderidder/MC-Snake-Results`
- Downloaded file: GitHub `main.zip`
- Local path: `analysis_public/data/mc_snake/raw/main.zip`
- Processed rows: 2,369,009
- Games: `minecraft`, `snake`
- Users: 25

### Google TSI

- Source: `https://github.com/google-research-datasets/tap-typing-with-touch-sensing-images`
- Downloaded files:
  - `touch_data.csv`
  - `keyboard_data.json`
  - `prompt_data.csv`
- Local path: `analysis_public/data/tsi/raw/`
- Processed rows: 43,735
- Participants: 16
- Targets: 28

## Optional datasets

### Henze / Hit It

- Source page checked: `https://nhenze.net/data/touch-events-on-mobile-phones/`
- Page states the full dataset is over 10GB and the Galaxy Tab subset link is broken in comments.
- No parseable local file found under `analysis_public/data/henze/manual/` or `raw/`.
- Status: unavailable, manual placement required.

### Rico

- Source page checked: `https://www.interactionmining.org/rico`
- No local view hierarchy/screenshot archive found.
- Status: unavailable, manual placement required.

### Screen Annotation

- Source: `https://github.com/google-research-datasets/screen_annotation`
- Downloaded files:
  - `train.csv`
  - `valid.csv`
  - `test.csv`
- Processed UI element rows: 373,127
- Role: UI grounding support only.

