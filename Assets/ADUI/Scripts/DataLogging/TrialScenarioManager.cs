using System.Collections.Generic;
using UnityEngine;

public class TrialScenarioManager : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;
    public ConditionManager conditionManager;

    [Header("Trial Counts")]
    public int calibrationTapsPerButton = 30;
    public int mainTrialCount = 240;

    public int currentTrialId;
    public int currentBlockId;
    public string currentPhase = DatasetSchema.PhaseFreeplay;
    public string currentRequiredAction = "";
    public string currentIntendedAction = "";
    public string currentLabelSource = "unavailable";
    public string currentTrialType = "";
    public long currentTrialStartMs;

    private readonly List<string> calibrationQueue = new List<string>();
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
        currentPhase = DatasetSchema.PhaseCalibration;
        calibrationQueue.Clear();
        for (var i = 0; i < calibrationTapsPerButton; i++)
        {
            calibrationQueue.Add("Attack");
            calibrationQueue.Add("Dodge");
        }
        Shuffle(calibrationQueue, 13);
        currentTrialId = 0;
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
            return;
        }
        currentTrialId += 1;
        currentTrialStartMs = NowMs();
        currentIntendedAction = calibrationQueue[0];
        calibrationQueue.RemoveAt(0);
        currentRequiredAction = currentIntendedAction;
        currentLabelSource = "calibration_instruction";
        currentTrialType = "calibration_tap";
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

    private void Shuffle(List<string> values, int seed)
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
}

