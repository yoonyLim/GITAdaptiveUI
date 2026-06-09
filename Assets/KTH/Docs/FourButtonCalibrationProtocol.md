# Four-Button Adaptive Touch Calibration Protocol

This protocol is designed for the current top-down prototype: the player moves with a left virtual joystick and triggers four combat actions with a right-side 2x2 button cluster (Attack, Dodge, Heal, Whirlwind). The calibration target is not "what action should the player choose from a combat state." It is "how the player's touch endpoint shifts and spreads when they try to press one of these four buttons, especially in situations where accidental adjacent-button activation is likely."

## Design claim

The calibration should represent input uncertainty in our actual game conditions:

- Small, adjacent targets: four combat buttons are close enough that an inward-biased touch can cross into a neighboring action.
- Thumb/visual occlusion: the finger covers the target and the perceived input point can be systematically offset.
- Speed pressure: combat actions are often quick reactions, so endpoint variance can grow.
- Bimanual posture: the left finger may be holding the joystick while the right finger taps actions.
- Context stress: the user may tap while enemies/projectiles create urgency. Touch endpoint uncertainty is calibrated as a Gaussian layer, while combat-scenario action preference is calibrated as a separate Bayesian count layer.

## Implemented calibration blocks

1. Warm-up block
   - Purpose: reduce first-trial noise.
   - Model usage: not used for fitting.

2. Center block
   - Prompt: tap the center of each action button.
   - Model usage: estimates per-button mean offset and covariance.
   - Rationale: captures the user's perceived input point and stable personal bias.

3. Inner-edge block
   - Prompt: tap the side of the target closest to the other buttons.
   - Model usage: updates covariance only, not the mean center.
   - Rationale: directly measures the ambiguous cluster area where adjacent-button mistakes occur.

4. Rapid-switch block
   - Prompt: quickly switch to a target action under combat pressure.
   - Model usage: updates covariance only with higher weight.
   - Rationale: speed-accuracy tradeoff and temporal pointing work both predict larger error when response timing is constrained.

5. Joystick-hold block
   - Prompt: hold the left joystick, then tap the target action.
   - Model usage: updates covariance only.
   - Rationale: this matches the real bimanual play posture of moving while using skills.

6. Combat-scenario block
   - Prompt: fight a temporary live combat setup and use the action the player would actually take.
   - Model usage: executes the tapped action, updates the user-specific context-action prior, and updates touch covariance for direct/high-confidence combat taps without moving the center bias.
   - Rationale: captures touch spread and action preference while enemies, projectiles, low HP, crowding, and joystick movement are active in the same scene.

7. Validation block
   - Prompt: tap target actions under pressure.
   - Model usage: not used for fitting.
   - Rationale: checks whether the fitted Gaussian profile, including combat-scenario spread samples, predicts the intended button in stress-like samples. Validation reports correct/total and mean target distance.

## Model

For each action button, the calibration stores touch offsets:

`offset = touch_position - visual_button_center`

Center trials estimate the mean offset. All model-fitting trials estimate a weighted 2D covariance matrix. The covariance is shrunk toward a conservative public prior so a small number of noisy samples cannot produce an overconfident hitbox. Runtime likelihood uses a per-button bivariate Gaussian:

`L(action | touch) = exp(-0.5 * d^T Sigma^-1 d) * peak_penalty`

where `d` is the touch position relative to the calibrated button center. `peak_penalty` prevents a very wide covariance from always winning simply because it is forgiving. Direct taps inside the visual button rectangle still execute directly; the Gaussian model mainly affects near-miss and ambiguous inputs.

Online adaptation is deliberately conservative: after calibration, high-confidence direct/adaptive taps can update covariance with small weight, but they do not move the calibrated center bias. This avoids reinforcing a wrong inferred intent.

## Context-response prior

The combat-scenario stage does not claim to infer the true optimal player action from video. It creates temporary live combat scenes, executes the user's chosen action, and estimates a user-specific preference prior:

`P_user(action | scenario) = (count(scenario, action) + alpha) / (count(scenario) + alpha * 4)`

The current implementation classifies combat into coarse scenarios: attack opportunity, dodge threat, low HP, enemy crowd, moving threat, low HP plus threat, and crowd plus low HP. During calibration, `RoguelikeGameManager` constructs matching temporary scenes with spawned enemies, projectiles, HP changes, joystick motion, and live enemy behavior where appropriate. `FourButtonCalibrationFlow` records the chosen action, executes it through `CombatManager`, and adds direct/high-confidence combat taps to the touch covariance model as spread-only samples. At runtime, `UserContextPriorModel` blends the public combat prior from `CombatManager` with the learned user prior. The blend is weak when only a few calibration answers exist and grows as the count matures, so it behaves like a Bayesian backoff model rather than a hard rule override.

The four action controls are circular visual targets. Direct button hits are therefore resolved with a circular hit test, while near-miss/ambiguous touches continue through the calibrated Gaussian model.

Online context adaptation is also conservative. Direct successful taps and high-confidence Bayesian-decoded actions add small observations to the active scenario. Failed actions, cooldown-blocked skills, and low-confidence ambiguous decodes do not update the context prior. This keeps the model from learning from obvious misfires.

## Runtime model movement

The visual button rectangles do not move. Calibration moves the probabilistic touch model attached to each button:

- Center samples update the per-button mean offset. On screen, the small white Gaussian-center marker moves from the visual button center to the calibrated model center.
- Inner-edge, rapid-switch, joystick-hold, and combat-scenario samples update covariance only. On screen, the colored Gaussian hitbox can widen or reshape, but the mean marker is not pulled toward those stress samples.
- Runtime decoding compares the touch against the calibrated model center and covariance, not just the original visual rectangle.
- The calibration feedback line reports the current model movement as `dx`, `dy`, `sx`, and `sy` after each fitted sample.

## Evaluation metrics

For the assignment/demo, the relevant metrics are:

- Calibration validation accuracy: intended button vs Gaussian-predicted button.
- Confusion pattern: which neighboring button is selected when a validation sample misses.
- Mean distance from intended center.
- In-game accidental activation rate.
- Cancel/correction rate, if a correction UI exists.
- Time-to-correct.
- Combat outcome metrics already logged by the prototype.

Action-prediction accuracy from screen context is not the main metric. The main claim is whether context-aware Gaussian touch decoding reduces accidental or unintended skill activation under realistic combat input conditions.

## Literature basis

The protocol is grounded in the following papers and primary publication pages. The key design implication is summarized for each.

| # | Paper | Link | Calibration implication |
|---|---|---|---|
| 1 | Fitts, "The Information Capacity of the Human Motor System in Controlling the Amplitude of Movement" (1954) | https://doi.org/10.1037/h0055392 | Movement speed and target width jointly affect accuracy; rapid combat taps should be calibrated separately. |
| 2 | Card, English, Burr, "Evaluation of Mouse, Rate-Controlled Isometric Joystick, Step Keys, and Text Keys for Text Selection on a CRT" (1978) | https://doi.org/10.1080/00140137808931762 | Joystick and pointing performance differ by input device; movement and action controls should be treated as a compound input condition. |
| 3 | Guiard, "Asymmetric Division of Labor in Human Skilled Bimanual Action" (1987) | https://doi.org/10.1080/00222895.1987.10735426 | Left joystick plus right action taps is an asymmetric bimanual task, so joystick-hold calibration is justified. |
| 4 | Potter, Weldon, Shneiderman, "Improving the Accuracy of Touch Screens" (1988) | https://doi.org/10.1145/57167.57171 | Feedback/confirmation can reduce dense-target errors; validation should detect ambiguous target mistakes. |
| 5 | Sears, Shneiderman, "High Precision Touchscreens" (1991) | https://doi.org/10.1016/0020-7373(91)90037-8 | Stabilization can reduce touchscreen errors; calibrated covariance is a software stabilization analogue. |
| 6 | MacKenzie, "Fitts' Law as a Research and Design Tool in HCI" (1992) | https://doi.org/10.1207/s15327051hci0701_3 | Use speed-accuracy theory to justify rapid-switch trials. |
| 7 | MacKenzie, Buxton, "Extending Fitts' Law to Two-Dimensional Tasks" (1992) | https://doi.org/10.1145/142750.142794 | Four-button touch is 2D; scalar radius is weaker than a 2D covariance model. |
| 8 | Kabbash, Buxton, Sellen, "Two-Handed Input in a Compound Task" (1994) | https://doi.org/10.1145/259963.260425 | Some bimanual designs outperform one-handed input, but poor bimanual mapping can be worse; test joystick-hold explicitly. |
| 9 | Accot, Zhai, "Beyond Fitts' Law: Models for Trajectory-Based HCI Tasks" (1997) | https://doi.org/10.1145/258549.258760 | Continuous movement constraints matter; gameplay with held joystick is not equivalent to isolated tapping. |
| 10 | Leganchuk, Zhai, Buxton, "Manual and Cognitive Benefits of Two-Handed Input" (1998) | https://doi.org/10.1145/300520.300522 | Two-handed input can reduce some costs but changes task structure; calibrate the real hand assignment. |
| 11 | Accot, Zhai, "More than Dotting the i's" (2002) | https://doi.org/10.1145/503376.503390 | Pointing and crossing have different motor constraints; edge/transition samples should not shift the center bias. |
| 12 | Soukoreff, MacKenzie, "Towards a Standard for Pointing Device Evaluation" (2004) | https://doi.org/10.1016/j.ijhcs.2004.09.001 | Separate training and validation trials for robust evaluation. |
| 13 | Grossman, Balakrishnan, "The Bubble Cursor" (2005) | https://doi.org/10.1145/1054972.1055012 | Dynamic activation areas can improve target acquisition but must consider nearby distractors. |
| 14 | Parhi, Karlson, Bederson, "Target Size Study for One-Handed Thumb Use on Small Touchscreen Devices" (2006) | https://doi.org/10.1145/1152215.1152260 | Small thumb targets and serial taps need larger effective tolerance. |
| 15 | Benko, Wilson, Baudisch, "Precise Selection Techniques for Multi-Touch Screens" (2006) | https://doi.org/10.1145/1124772.1124963 | Occlusion/noise can be mitigated by assisted target selection; our method assists ambiguous taps. |
| 16 | Vogel, Baudisch, "Shift" (2007) | https://doi.org/10.1145/1240624.1240727 | Finger occlusion and ambiguous selection points are core causes of touch errors. |
| 17 | Karlson, Bederson, "ThumbSpace" (2007) | https://doi.org/10.1007/978-3-540-74796-3_30 | One-handed thumb reach changes accuracy; layout-relative calibration is needed. |
| 18 | Hoggan, Brewster, Johnston, "Investigating the Effectiveness of Tactile Feedback for Mobile Touchscreens" (2008) | https://doi.org/10.1145/1357054.1357300 | Touchscreens lack physical feedback; calibration should not assume physical-button precision. |
| 19 | Yatani et al., "Escape" (2008) | https://doi.org/10.1145/1357054.1357104 | Dense target clusters need disambiguation beyond simple bounding boxes. |
| 20 | Roudaut, Huot, Lecolinet, "TapTap and MagStick" (2008) | https://doi.org/10.1145/1385569.1385594 | One-handed small-screen target acquisition benefits from techniques designed for reach/occlusion/accuracy. |
| 21 | Park et al., "Touch Key Design for Target Selection on a Mobile Phone" (2008) | https://doi.org/10.1145/1409240.1409304 | Touch-key size and layout affect selection; four-button spacing should be evaluated as a cluster. |
| 22 | Wang, Ren, "Empirical Evaluation for Finger Input Properties in Multi-Touch Interaction" (2009) | https://doi.org/10.1145/1518701.1518864 | Finger contact area/orientation affects reported touch position; 2D covariance is justified. |
| 23 | Lee, Zhai, "The Performance of Touch Screen Soft Buttons" (2009) | https://doi.org/10.1145/1518701.1518750 | Soft buttons differ from hard buttons; feedback and size influence errors. |
| 24 | Gunawardana, Paek, Meek, "Usability Guided Key-Target Resizing for Soft Keyboards" (2010) | https://doi.org/10.1145/1719970.1719986 | Probabilistic target resizing must keep intuitive anchors; direct visual button taps remain direct in our system. |
| 25 | Holz, Baudisch, "The Generalized Perceived Input Point Model" (2010) | https://doi.org/10.1145/1753326.1753413 | Per-user/per-posture offset is real; center trials estimate it. |
| 26 | Henze, Rukzio, Boll, "100,000,000 Taps" (2011) | https://doi.org/10.1145/2037373.2037395 | Large-scale taps show systematic skew; compensation can reduce error. |
| 27 | Holz, Baudisch, "Understanding Touch" (2011) | https://doi.org/10.1145/1978942.1979308 | Users align visual finger features rather than the contact centroid; mean offset calibration is justified. |
| 28 | Findlater, Wobbrock, "Personalized Input" (2012) | https://doi.org/10.1145/2207676.2208520 | Automatic personalization of touch models can improve input. |
| 29 | Weir et al., "A User-Specific Machine Learning Approach for Improving Touch Accuracy on Mobile Devices" (2012) | https://doi.org/10.1145/2380116.2380175 | User-specific touch models can improve accuracy with calibration data. |
| 30 | Goel, Findlater, Wobbrock, "WalkType" (2012) | https://doi.org/10.1145/2207676.2208662 | Situational impairment changes touch behavior; stress-like calibration blocks are justified. |
| 31 | Goel, Wobbrock, Patel, "GripSense" (2012) | https://doi.org/10.1145/2380116.2380184 | Grip/posture matters; joystick-hold is a posture condition. |
| 32 | Azenkot, Zhai, "Touch Behavior with Different Postures on Soft Smartphone Keyboards" (2012) | https://doi.org/10.1145/2371574.2371612 | Different postures create consistent offsets; do not rely on one generic model. |
| 33 | Baldwin, Chai, "Towards Online Adaptation and Personalization of Key-Target Resizing for Mobile Devices" (2012) | https://doi.org/10.1145/2166966.2166969 | Online adaptation can reduce offline calibration burden, but must be conservative. |
| 34 | Bi, Li, Zhai, "FFitts Law" (2013) | https://doi.org/10.1145/2470654.2466180 | Finger touch endpoint distribution has absolute precision noise; Gaussian calibration is principled. |
| 35 | Bi et al., "Octopus" (2013) | https://research.google/pubs/pub41646 | Replaying logged touches is useful for evaluating algorithmic changes without re-running users. |
| 36 | Wang et al., "Understanding Performance of Eyes-Free, Absolute Position Control on Touchable Mobile Phones" (2013) | https://pi.cs.tsinghua.edu.cn/lab/people/YuntaoWang/en/publication/mobile-hci-2013-eyes-free/ | Eyes-free/low-attention touches have location-dependent offsets; combat pressure reduces visual checking. |
| 37 | Yu et al., "Rapid Selection of Hard-to-Access Targets by Thumb on Mobile Touch-Screens" (2013) | https://doi.org/10.1145/2493190.2493202 | Hard-to-reach target selection needs special support; target position matters. |
| 38 | Yin et al., "Making Touchscreen Keyboards Adaptive to Keys, Hand Postures, and Individuals" (2013) | https://doi.org/10.1145/2470654.2481384 | Backoff/shrinkage is needed when specific user/posture data is limited. |
| 39 | Weir, Rogers, Buschek, "Sparse Selection of Training Data for Touch Correction Systems" (2013) | https://doi.org/10.1145/2493190.2493241 | Calibration should be concise; a small but targeted sample set can still help. |
| 40 | Buschek, Alt, "TouchML" (2015) | https://doi.org/10.1145/2678025.2701381 | Touch offset models expose spatial targeting patterns and uncertainty. |
| 41 | Huang et al., "DigitSpace" (2016) | https://doi.org/10.1145/2858036.2858483 | Thumb anatomy and touch precision affect one-handed/eyes-free layouts. |
| 42 | Lee, Oulasvirta, "Modelling Error Rates in Temporal Pointing" (2016) | https://doi.org/10.1145/2858036.2858143 | Timing-critical touchscreen actions are noisy; rapid-switch trials are relevant to action games. |
| 43 | Noor, Rogers, Williamson, "Detecting Swipe Errors on Touchscreens Using Grip Modulation" (2016) | https://doi.org/10.1145/2858036.2858474 | User errors leave measurable physical traces; validation/confusion logging is meaningful. |
| 44 | Woodward et al., "Characterizing How Interface Complexity Affects Children's Touchscreen Interactions" (2016) | https://doi.org/10.1145/2858036.2858200 | Interface complexity affects touch behavior; combat visuals can change tapping reliability. |
| 45 | Buschek, Alt, "ProbUI" (2017) | https://doi.org/10.1145/3025453.3025502 | Probabilistic GUI target representations are a valid alternative to static rectangles. |
| 46 | Ko et al., "Modeling Two Dimensional Touch Pointing" (2020) | https://doi.org/10.1145/3379337.3415871 | 2D touch pointing needs 2D models; supports bivariate covariance. |
| 47 | Yamanaka, Usuba, "Computing Touch-Point Ambiguity on Mobile Touchscreens for Modeling Target Selection Times" (2021) | https://arxiv.org/abs/2101.05244 | Absolute touch ambiguity should be measured carefully rather than optimized blindly. |
| 48 | Jokinen et al., "Touchscreen Typing As Optimal Supervisory Control" (2021) | https://doi.org/10.1145/3411764.3445483 | Touch behavior adapts under visual/motor/cognitive constraints; validation should reflect task context. |
