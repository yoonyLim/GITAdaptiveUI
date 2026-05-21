# Public Scene Pretraining용 프로젝트 인벤토리

## Unity frontend

- Unity project root: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI`
- Unity version: `6000.4.4f1`
- main scene: `Assets/Scenes/SampleScene.unity`
- existing combat/demo scene asset: `Assets/FST/Gladiators Low Poly Arena/_DemoScene.unity`
- current touch manager: `Assets/00Scripts/AdaptiveTouchManager.cs`
- current combat state manager: `Assets/00Scripts/CombatManager.cs`
- current simulated enemy script: `Assets/00Scripts/SimulatedEnemy.cs`

## Existing ADUI scripts

- Models:
  - `Assets/ADUI/Scripts/Models/UserTouchModel.cs`
  - `Assets/ADUI/Scripts/Models/BayesianInputDecoder.cs`
  - `Assets/ADUI/Scripts/Models/SafetyGate.cs`
  - `Assets/ADUI/Scripts/Models/ADUIModelTypes.cs`
- Data logging:
  - `ExperimentSessionManager`, `TrialScenarioManager`, `RawTouchLogger`, `ButtonLayoutLogger`, `BayesianDecisionLogger`, `HPOutcomeLogger`, `ModePolicyLogger`, `CsvJsonlExporter`
- Public touch defaults:
  - `Assets/ADUI/Scripts/DataLogging/PublicPriorConfig.cs`
  - `Assets/StreamingAssets/ADUI/public_touch_prior_config.json`
- Vision logging already present:
  - `Assets/ADUI/Scripts/Vision/VisionFrameLogger.cs`
- New multigame prior runtime hooks:
  - `Assets/ADUI/Scripts/Vision/MultigameVisionPriorConfig.cs`
  - `Assets/ADUI/Scripts/Vision/VisionPriorBuilder.cs`
  - `Assets/ADUI/Scripts/Vision/VisionPriorReplayLoader.cs`
  - `Assets/ADUI/Scripts/Vision/VisionInferenceStub.cs`

## Existing analysis folders

- `analysis_public`: public touch / target-selection / UI grounding evidence
- `analysis_unity`: Unity telemetry ingest, user touch model training, decoder evaluation
- `analysis_vision`: public mobile UI grounding datasets
- `analysis_multigame`: added for public multi-game abstract situation pretraining
  - ViZDoom runtime generation support
  - DINO active pipeline removed after poor held-out situation generalization

## Existing public touch data

Current public-touch reports show:

- Touch-Dynamics-Research: available, 4 raw files, 44,955,805 bytes, 4,095,268 processed events, 25 users, games `diep/minecraft/pubg/snake`
- MC-Snake-Results: available, 1 raw archive/folder source, 26,643,923 bytes, 2,369,009 processed events, 25 users, games `minecraft/snake`
- Google TSI: available, 3 raw files, 28,427,273 bytes, 43,735 target-labeled touch rows, 16 users
- Henze: unavailable locally
- Rico: unavailable locally
- Screen Annotation: available, 3 raw files, 25,221,819 bytes, over 370k UI annotation rows

## Existing vision/UI grounding data

- Screen Annotation: available; UI element location/type/text-description support
- Widget Caption: available; widget semantic captions
- ScreenQA: available; answer UI element boxes
- UICrit: available; UI critique regions
- Rico/VINS: manual or large external download required

These datasets support UI grounding, but they do not provide game combat situation labels.

## Missing before this change

- public multi-game situation pretraining package
- abstract labels for `ui_phase`, `threat_level`, `action_window`, `urgency_level`
- prior builder that maps abstract situation to Attack/Dodge prior
- Unity Bayesian evaluation conditions for multigame vision prior
- leakage/overfitting report for Unity-only vision classifier

## Added scope boundary

The new situation layer predicts only abstract context and confidence. It does not directly predict Attack or Dodge. Final action selection remains:

`P(a_i | x_t, F_t, u, B_t) ∝ P(x_t | a_i, u, B_t) * P(a_i | g_theta(F_t))`

with the existing safety gate preserving clear inputs and rejecting uncertain/far touches.
