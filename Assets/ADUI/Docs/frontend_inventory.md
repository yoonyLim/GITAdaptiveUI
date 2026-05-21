# ADUI Frontend Inventory

- Unity project root: `C:\Users\kth00\Documents\GitHub\GITAdaptiveUI`
- Unity version: `6000.4.4f1` from `ProjectSettings/ProjectVersion.txt`
- Build scene: `Assets/Scenes/SampleScene.unity`
- Additional imported scene: `Assets/FST/Gladiators Low Poly Arena/_DemoScene.unity`

## Existing Frontend Objects

- Attack button object: `Attack Button` in `Assets/Scenes/SampleScene.unity`
- Dodge button object: `Dodge Button` in `Assets/Scenes/SampleScene.unity`
- Attack hitbox visualization: `Attack Button Hitbox`
- Dodge hitbox visualization: `Dodge Button Hitbox`
- Current touch input handler: `Assets/00Scripts/AdaptiveTouchManager.cs`
- Current Bayesian decoder code before refactor: inline in `AdaptiveTouchManager.ProcessInputBegan`
- Current model folder added for separated model logic: `Assets/ADUI/Scripts/Models/`
- Current enemy/combat state manager: `Assets/00Scripts/CombatManager.cs`
- Current HP manager: `CombatManager` private HP fields with public getters added
- Current feedback system: `CombatManager.ShowFeedback`, `feedbackLogText`, button color feedback in `AdaptiveTouchManager`
- Current enemy visual state sync: `Assets/00Scripts/SimulatedEnemy.cs`

## Confirmed Existing Prototype Behavior

- Two action buttons are present: Attack and Dodge.
- Raw touch interception exists through Unity Input System EnhancedTouch and editor mouse fallback.
- Existing spatial Gaussian likelihood uses distance to button center and `userTouchVariance`.
- Existing game-context prior comes from `CombatManager.priorAttack` and `CombatManager.priorDodge`.
- Existing posterior comparison computes `P(I|A) * P(A)` for Attack and Dodge.
- Existing invalid touch handling uses a likelihood threshold.
- Existing dynamic invisible hitbox visualization resizes `attackHitboxVisualizer` and `dodgeHitboxVisualizer`.
- Existing HP validation idea is implemented through player/enemy HP changes in `CombatManager`.

## Added Components

- `UserTouchModel`: calibration-based user-specific spatial likelihood.
- `BayesianInputDecoder`: action-first Bayesian decoding with configurable priors.
- `SafetyGate`: ambiguity-gated correction policy that preserves clear inside-button inputs.
- `ExperimentSessionManager`, loggers, schema, condition manager, trial scenario manager.
- `PublicPriorConfig`: optional loading of public-data-derived variance, hitbox margin, and ambiguity defaults.
- `ADUIExperimentPanel`: Unity editor panel for session, calibration, main trials, public defaults, and log path inspection.

## Missing Or Manual Integration Items

- The new ADUI scripts are added to the repository but must be attached to scene GameObjects in Unity if not auto-added.
- Public datasets are not bundled in the repository; place real files under `analysis_public/data/**/raw` or `manual`.
- Henze full data is not auto-downloaded because the public page describes a very large/manual dataset and an unreliable subset link.
- Real participant validation still requires Unity telemetry collection from actual devices or editor sessions.

