using UnityEngine;

public class BayesianDecisionLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    public void Log(int trialId, string condition, ADUIEnemyState enemyState, ADUIDecodeResult result)
    {
        if (!sessionManager || result == null) return;
        var record = new ADUIModelDecisionRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            condition = condition,
            enemy_state = enemyState.ToString(),
            likelihood_attack = result.likelihoodAttack,
            likelihood_dodge = result.likelihoodDodge,
            prior_attack = result.priorAttack,
            prior_dodge = result.priorDodge,
            posterior_attack = result.posteriorAttack,
            posterior_dodge = result.posteriorDodge,
            posterior_gap = result.posteriorGap,
            bayesian_prediction = result.bayesianPrediction.ToString(),
            final_executed_action = result.finalExecutedAction.ToString(),
            invalid_touch = result.invalidTouch,
            safety_gate_passed = result.safetyGatePassed,
            safety_gate_reason = result.safetyGateReason
        };
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "model_decisions.jsonl", record);
    }
}

