using System.Collections.Generic;
using UnityEngine;

public class TrialScenarioManager : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;
    public ConditionManager conditionManager;

    [Header("Trial Counts")]
    public int calibrationTapsPerButton = 30;
    public int mainTrialCount = 240;

    [Header("Calibration Protocol")]
    public int centerTapsPerButton = 8;
    public int reciprocalAlternationPairs = 10;
    public int boundaryTapsPerButton = 4;
    public int ambiguousTapsPerButton = 4;
    public int contextTapsPerState = 4;
    public bool shuffleDiscreteCenterTaps = true;

    public int currentTrialId;
    public int currentBlockId;
    public string currentPhase = DatasetSchema.PhaseFreeplay;
    public string currentRequiredAction = "";
    public string currentIntendedAction = "";
    public string currentLabelSource = "unavailable";
    public string currentTrialType = "";
    public long currentTrialStartMs;
    public string currentInstruction = "";
    public bool currentCalibrationUsedForTouchModel;
    public ADUIEnemyState currentCalibrationEnemyState = ADUIEnemyState.Safe;
    public int calibrationTotalCount;

    public int CalibrationRemainingCount => calibrationQueue.Count;

    private readonly List<CalibrationTrial> calibrationQueue = new List<CalibrationTrial>();
    private readonly string[] scenarioCycle = { "Safe", "Telegraph", "Attacking", "Neutral" };
    private readonly string[] trialTypeCycle =
    {
        "clear_attack",
        "clear_dodge",
        "near_boundary_attack",
        "near_boundary_dodge",
        "ambiguous_between_attack_dodge",
        "outside_but_recoverable",
        "invalid_far_touch"
    };

    public void StartCalibration()
    {
        UserTouchModel touchModel = FindAnyObjectByType<UserTouchModel>();
        if (touchModel != null)
        {
            touchModel.ResetCalibration();
        }

        if (conditionManager != null)
        {
            conditionManager.randomizeConditionOrder = false;
            conditionManager.SetCondition(DatasetSchema.ConditionContextBayesianSafety);
        }

        currentPhase = DatasetSchema.PhaseCalibration;
        calibrationQueue.Clear();
        BuildCalibrationQueue();
        calibrationTotalCount = calibrationQueue.Count;
        currentTrialId = 0;
        currentBlockId = 0;
        BeginNextCalibrationTrial();
    }

    public void StartMainTrials()
    {
        currentPhase = DatasetSchema.PhaseTest;
        currentTrialId = 0;
        currentBlockId = 0;
        if (conditionManager)
        {
            conditionManager.BuildConditionOrder();
            conditionManager.SaveConditionOrder(sessionManager);
        }
        BeginNextMainTrial();
    }

    public void BeginNextCalibrationTrial()
    {
        if (calibrationQueue.Count == 0)
        {
            currentPhase = DatasetSchema.PhaseFreeplay;
            currentInstruction = "Calibration complete.";
            currentCalibrationUsedForTouchModel = false;
            return;
        }
        currentTrialId += 1;
        currentTrialStartMs = NowMs();
        CalibrationTrial trial = calibrationQueue[0];
        calibrationQueue.RemoveAt(0);
        currentIntendedAction = trial.intendedAction;
        currentRequiredAction = currentIntendedAction;
        currentLabelSource = trial.labelSource;
        currentTrialType = trial.trialType;
        currentInstruction = trial.instruction;
        currentBlockId = trial.blockId;
        currentCalibrationUsedForTouchModel = trial.useForTouchModel;
        currentCalibrationEnemyState = trial.enemyState;
    }

    public void BeginNextMainTrial()
    {
        currentTrialId += 1;
        currentTrialStartMs = NowMs();
        currentBlockId = Mathf.Max(0, (currentTrialId - 1) / Mathf.Max(DatasetSchema.Conditions.Length, 1));
        var scenario = scenarioCycle[(currentTrialId - 1) % scenarioCycle.Length];
        currentRequiredAction = scenario == "Safe" || scenario == "Neutral" && currentTrialId % 2 == 0 ? "Attack" : "Dodge";
        currentIntendedAction = currentRequiredAction;
        currentLabelSource = "scenario_rule";
        currentTrialType = trialTypeCycle[(currentTrialId - 1) % trialTypeCycle.Length];
        if (conditionManager && currentTrialId % 4 == 1) conditionManager.NextCondition();
    }

    public ADUIEnemyState CurrentEnemyState()
    {
        if (currentPhase == DatasetSchema.PhaseCalibration)
        {
            return currentCalibrationEnemyState;
        }

        if (CombatManager.Instance)
        {
            switch (CombatManager.Instance.currentState)
            {
                case CombatManager.CombatState.Safe:
                    return ADUIEnemyState.Safe;
                case CombatManager.CombatState.Telegraph:
                    return ADUIEnemyState.Telegraph;
                case CombatManager.CombatState.Attacking:
                    return ADUIEnemyState.Attacking;
            }
        }
        if (currentRequiredAction == "Dodge") return ADUIEnemyState.Telegraph;
        return ADUIEnemyState.Safe;
    }

    public string CurrentCondition()
    {
        return conditionManager ? conditionManager.currentCondition : DatasetSchema.ConditionContextBayesianSafety;
    }

    public void CompleteTrial()
    {
        if (currentPhase == DatasetSchema.PhaseCalibration) BeginNextCalibrationTrial();
        else if (currentPhase == DatasetSchema.PhaseTest && currentTrialId < mainTrialCount) BeginNextMainTrial();
    }

    public bool ShouldUseCurrentCalibrationTrialForTouchModel()
    {
        return currentPhase == DatasetSchema.PhaseCalibration && currentCalibrationUsedForTouchModel;
    }

    private void BuildCalibrationQueue()
    {
        var centerTrials = new List<CalibrationTrial>();
        for (int i = 0; i < Mathf.Max(1, centerTapsPerButton); i++)
        {
            centerTrials.Add(NewCalibrationTrial(
                "Attack",
                "discrete_center_attack",
                ADUIEnemyState.Safe,
                "Tap the center of Attack.",
                true,
                0,
                "calibration_fit_discrete"));
            centerTrials.Add(NewCalibrationTrial(
                "Dodge",
                "discrete_center_dodge",
                ADUIEnemyState.Safe,
                "Tap the center of Dodge.",
                true,
                0,
                "calibration_fit_discrete"));
        }

        if (shuffleDiscreteCenterTaps)
        {
            Shuffle(centerTrials, 31);
        }

        calibrationQueue.AddRange(centerTrials);

        for (int i = 0; i < Mathf.Max(1, reciprocalAlternationPairs); i++)
        {
            calibrationQueue.Add(NewCalibrationTrial(
                "Attack",
                "reciprocal_alternating_attack",
                ADUIEnemyState.Safe,
                "Alternate quickly: tap Attack.",
                true,
                1,
                "calibration_fit_reciprocal"));
            calibrationQueue.Add(NewCalibrationTrial(
                "Dodge",
                "reciprocal_alternating_dodge",
                ADUIEnemyState.Safe,
                "Alternate quickly: tap Dodge.",
                true,
                1,
                "calibration_fit_reciprocal"));
        }

        for (int i = 0; i < Mathf.Max(1, boundaryTapsPerButton); i++)
        {
            calibrationQueue.Add(NewCalibrationTrial(
                "Attack",
                "near_boundary_attack",
                ADUIEnemyState.Safe,
                "Aim for Attack near the inner edge between the buttons.",
                false,
                2,
                "calibration_diagnostic_boundary"));
            calibrationQueue.Add(NewCalibrationTrial(
                "Dodge",
                "near_boundary_dodge",
                ADUIEnemyState.Safe,
                "Aim for Dodge near the inner edge between the buttons.",
                false,
                2,
                "calibration_diagnostic_boundary"));
        }

        for (int i = 0; i < Mathf.Max(1, ambiguousTapsPerButton); i++)
        {
            calibrationQueue.Add(NewCalibrationTrial(
                "Attack",
                "ambiguous_gap_attack",
                ADUIEnemyState.Safe,
                "Aim for Attack from the gap between Attack and Dodge.",
                false,
                3,
                "calibration_diagnostic_ambiguity"));
            calibrationQueue.Add(NewCalibrationTrial(
                "Dodge",
                "ambiguous_gap_dodge",
                ADUIEnemyState.Telegraph,
                "Aim for Dodge from the gap between Attack and Dodge.",
                false,
                3,
                "calibration_diagnostic_ambiguity"));
        }

        for (int i = 0; i < Mathf.Max(1, contextTapsPerState); i++)
        {
            calibrationQueue.Add(NewCalibrationTrial(
                "Attack",
                "context_safe_attack",
                ADUIEnemyState.Safe,
                "Safe context: tap Attack.",
                true,
                4,
                "calibration_fit_context"));
            calibrationQueue.Add(NewCalibrationTrial(
                "Dodge",
                "context_telegraph_dodge",
                ADUIEnemyState.Telegraph,
                "Telegraph context: tap Dodge.",
                true,
                4,
                "calibration_fit_context"));
            calibrationQueue.Add(NewCalibrationTrial(
                "Dodge",
                "context_attacking_dodge",
                ADUIEnemyState.Attacking,
                "Incoming attack context: tap Dodge.",
                true,
                4,
                "calibration_fit_context"));
        }
    }

    private CalibrationTrial NewCalibrationTrial(
        string intendedAction,
        string trialType,
        ADUIEnemyState enemyState,
        string instruction,
        bool useForTouchModel,
        int blockId,
        string labelSource)
    {
        return new CalibrationTrial
        {
            intendedAction = intendedAction,
            trialType = trialType,
            enemyState = enemyState,
            instruction = instruction,
            useForTouchModel = useForTouchModel,
            blockId = blockId,
            labelSource = labelSource
        };
    }

    private void Shuffle(List<CalibrationTrial> values, int seed)
    {
        var rng = new System.Random(seed);
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            var tmp = values[i];
            values[i] = values[j];
            values[j] = tmp;
        }
    }

    private long NowMs()
    {
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private struct CalibrationTrial
    {
        public string intendedAction;
        public string trialType;
        public ADUIEnemyState enemyState;
        public string instruction;
        public bool useForTouchModel;
        public int blockId;
        public string labelSource;
    }
}

