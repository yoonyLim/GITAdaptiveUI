using UnityEngine;

public class HPOutcomeLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    public void Log(int trialId, int playerHpBefore, int playerHpAfter, int enemyHpBefore, int enemyHpAfter, bool actionSuccess)
    {
        if (!sessionManager) return;
        var record = new ADUIHPOutcomeRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            player_hp_before = playerHpBefore,
            player_hp_after = playerHpAfter,
            enemy_hp_before = enemyHpBefore,
            enemy_hp_after = enemyHpAfter,
            damage_taken = Mathf.Max(0, playerHpBefore - playerHpAfter),
            damage_dealt = Mathf.Max(0, enemyHpBefore - enemyHpAfter),
            survived = playerHpAfter > 0,
            action_success = actionSuccess
        };
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "hp_outcomes.jsonl", record);
    }
}

