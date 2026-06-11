using System;
using System.Collections.Generic;
using UnityEngine;

public class UserEvaluationLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;
    public ConditionManager conditionManager;

    private readonly List<ADUIEvaluationStageSummary> summaries = new List<ADUIEvaluationStageSummary>();
    private StageCounters currentStage;

    public void BeginStage(int stageNumber, string stageLabel)
    {
        EnsureDependencies();
        sessionManager?.EnsureSession();

        currentStage = new StageCounters
        {
            active = true,
            stageNumber = stageNumber,
            stageLabel = stageLabel,
            startMs = NowMs()
        };
    }

    public void LogTouchDecision(
        int trialId,
        ADUIDecodeInput input,
        ADUIDecodeResult result,
        ADUIInteractionDemand demand,
        ADUIAdjustmentPolicy policy,
        ADUIContextScenario scenario,
        int playerHpBefore,
        int playerHpAfter,
        int enemyHpBefore,
        int enemyHpAfter)
    {
        if (input == null || result == null)
        {
            return;
        }

        EnsureDependencies();
        string expectedAction = ExpectedActionForScenario(scenario);
        bool expectedMatch = MatchesExpected(result.finalExecutedAction.ToString(), expectedAction);
        bool rejected = result.invalidTouch ||
                        result.finalExecutedAction == ADUIAction.None ||
                        StartsWith(result.safetyGateReason, "rejected") ||
                        StartsWith(result.safetyGateReason, "cooldown") ||
                        StartsWith(result.safetyGateReason, "action_failed");
        bool corrected = StartsWith(result.safetyGateReason, "correction_allowed");
        bool preserved = result.safetyGateReason == "preserve_clear_button";
        bool cooldownWasted = result.finalExecutedAction == ADUIAction.None ||
                              StartsWith(result.safetyGateReason, "cooldown");
        bool misTouch = !expectedMatch && !rejected && result.finalExecutedAction != ADUIAction.None;

        if (currentStage.active)
        {
            currentStage.touchEvents++;
            currentStage.expectedMatchCount += expectedMatch ? 1 : 0;
            currentStage.misTouchCount += misTouch ? 1 : 0;
            currentStage.invalidTouchCount += result.invalidTouch ? 1 : 0;
            currentStage.rejectedCount += rejected ? 1 : 0;
            currentStage.preservedCount += preserved ? 1 : 0;
            currentStage.correctedCount += corrected ? 1 : 0;
            currentStage.ambiguousCount += result.isAmbiguous ? 1 : 0;
            currentStage.cooldownWastedCount += cooldownWasted ? 1 : 0;
            currentStage.actionFirstCount += policy != null && policy.mode == ADUIInteractionMode.ActionFirst ? 1 : 0;
            currentStage.cognitiveFirstCount += policy != null && policy.mode == ADUIInteractionMode.CognitiveFirst ? 1 : 0;
            currentStage.posteriorGapSum += result.posteriorGap;
            currentStage.errorToleranceSum += policy != null ? policy.interactionErrorTolerance : 0f;
            currentStage.correctionStrengthSum += policy != null ? policy.correctionStrength : 0f;
        }

        if (sessionManager == null || sessionManager.exporter == null)
        {
            return;
        }

        var record = new ADUIEvaluationTouchRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            condition = CurrentCondition(input),
            stage_number = currentStage.active ? currentStage.stageNumber : 0,
            stage_label = currentStage.active ? currentStage.stageLabel : "",
            trial_id = trialId,
            timestamp_ms = NowMs(),
            scenario = UserContextPriorModel.ScenarioLabel(scenario),
            interaction_mode = policy != null ? policy.mode.ToString() : "",
            expected_action = expectedAction,
            final_action = result.finalExecutedAction.ToString(),
            expected_match = expectedMatch,
            invalid_touch = result.invalidTouch,
            rejected = rejected,
            preserved_clear_input = preserved,
            corrected = corrected,
            ambiguous = result.isAmbiguous,
            cooldown_wasted = cooldownWasted,
            safety_gate_passed = result.safetyGatePassed,
            safety_gate_reason = result.safetyGateReason,
            posterior_attack = result.posteriorAttack,
            posterior_dodge = result.posteriorDodge,
            posterior_gap = result.posteriorGap,
            max_posterior = result.maxPosterior,
            policy_error_tolerance = policy != null ? policy.interactionErrorTolerance : 0f,
            policy_correction_strength = policy != null ? policy.correctionStrength : 0f,
            touch_x = input.touchPosition.x,
            touch_y = input.touchPosition.y,
            player_hp_before = playerHpBefore,
            player_hp_after = playerHpAfter,
            damage_taken = Mathf.Max(0, playerHpBefore - playerHpAfter),
            enemy_hp_before = enemyHpBefore,
            enemy_hp_after = enemyHpAfter,
            damage_dealt = Mathf.Max(0, enemyHpBefore - enemyHpAfter)
        };

        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "evaluation_touch_events.jsonl", record);
    }

    public void EndStage(
        int stageNumber,
        string stageLabel,
        bool skipped,
        bool failed,
        float durationSeconds,
        int buttonPresses,
        int damageTaken,
        int healingDone,
        float averageTouchError,
        int finalHp,
        int enemiesRemaining)
    {
        EnsureDependencies();
        if (!currentStage.active || currentStage.stageNumber != stageNumber)
        {
            BeginStage(stageNumber, stageLabel);
        }

        var summary = new ADUIEvaluationStageSummary
        {
            session_id = sessionManager != null ? sessionManager.sessionId : "",
            participant_id = sessionManager != null ? sessionManager.ParticipantId() : "",
            condition = CurrentCondition(null),
            stage_number = stageNumber,
            stage_label = stageLabel,
            skipped = skipped,
            failed = failed,
            duration_seconds = durationSeconds,
            button_presses = buttonPresses,
            touch_events = currentStage.touchEvents,
            expected_match_count = currentStage.expectedMatchCount,
            mis_touch_count = currentStage.misTouchCount,
            invalid_touch_count = currentStage.invalidTouchCount,
            rejected_count = currentStage.rejectedCount,
            preserved_count = currentStage.preservedCount,
            corrected_count = currentStage.correctedCount,
            ambiguous_count = currentStage.ambiguousCount,
            cooldown_wasted_count = currentStage.cooldownWastedCount,
            action_first_count = currentStage.actionFirstCount,
            cognitive_first_count = currentStage.cognitiveFirstCount,
            damage_taken = damageTaken,
            healing_done = healingDone,
            final_hp = finalHp,
            enemies_remaining = enemiesRemaining,
            avg_touch_error_px = averageTouchError,
            avg_posterior_gap = Average(currentStage.posteriorGapSum, currentStage.touchEvents),
            avg_policy_error_tolerance = Average(currentStage.errorToleranceSum, currentStage.touchEvents),
            avg_policy_correction_strength = Average(currentStage.correctionStrengthSum, currentStage.touchEvents)
        };

        summaries.Add(summary);
        currentStage = default;

        if (sessionManager == null || sessionManager.exporter == null)
        {
            return;
        }

        string sessionDir = sessionManager.EnsureSession();
        sessionManager.exporter.AppendJsonl(sessionDir, "evaluation_stage_summary.jsonl", summary);
        sessionManager.exporter.WriteCsv(sessionDir, "evaluation_stage_summary.csv", summaries);
    }

    private void EnsureDependencies()
    {
        if (sessionManager == null)
        {
            sessionManager = FindAnyObjectByType<ExperimentSessionManager>();
        }

        if (conditionManager == null)
        {
            conditionManager = FindAnyObjectByType<ConditionManager>();
        }
    }

    private string CurrentCondition(ADUIDecodeInput input)
    {
        if (conditionManager != null && !string.IsNullOrEmpty(conditionManager.currentCondition))
        {
            return conditionManager.currentCondition;
        }

        return input != null ? input.condition : "";
    }

    private static float Average(float sum, int count)
    {
        return count <= 0 ? 0f : sum / count;
    }

    private static long NowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static bool StartsWith(string value, string prefix)
    {
        return !string.IsNullOrEmpty(value) &&
               value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExpectedActionForScenario(ADUIContextScenario scenario)
    {
        switch (scenario)
        {
            case ADUIContextScenario.AttackCommitWindow:
            case ADUIContextScenario.SafeAttackOpportunity:
            case ADUIContextScenario.AttackOpportunity:
                return "Attack";
            case ADUIContextScenario.PreDodgeWindow:
            case ADUIContextScenario.ImmediateDodgeThreat:
            case ADUIContextScenario.ProjectileDodgeThreat:
            case ADUIContextScenario.MovingUnderPressure:
            case ADUIContextScenario.RiskyCloseEnemy:
            case ADUIContextScenario.DodgeThreat:
            case ADUIContextScenario.MovementThreat:
                return "Dodge";
            case ADUIContextScenario.LowHpHeal:
                return "Heal";
            case ADUIContextScenario.LowHpThreat:
                return "Dodge|Heal";
            case ADUIContextScenario.CrowdWhirlwind:
                return "Whirlwind";
            case ADUIContextScenario.CrowdLowHp:
                return "Whirlwind|Heal";
            default:
                return "";
        }
    }

    private static bool MatchesExpected(string finalAction, string expected)
    {
        if (string.IsNullOrEmpty(expected))
        {
            return true;
        }

        string[] candidates = expected.Split('|');
        for (int i = 0; i < candidates.Length; i++)
        {
            if (string.Equals(finalAction, candidates[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private struct StageCounters
    {
        public bool active;
        public int stageNumber;
        public string stageLabel;
        public long startMs;
        public int touchEvents;
        public int expectedMatchCount;
        public int misTouchCount;
        public int invalidTouchCount;
        public int rejectedCount;
        public int preservedCount;
        public int correctedCount;
        public int ambiguousCount;
        public int cooldownWastedCount;
        public int actionFirstCount;
        public int cognitiveFirstCount;
        public float posteriorGapSum;
        public float errorToleranceSum;
        public float correctionStrengthSum;
    }
}
