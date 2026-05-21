using UnityEngine;

public class ButtonLayoutLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    public void Log(int trialId, ADUIButtonGeometry attack, ADUIButtonGeometry dodge, float dynamicAttackRadius, float dynamicDodgeRadius)
    {
        if (!sessionManager) return;
        var record = new ADUILayoutSnapshotRecord
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            screen_width = Screen.width,
            screen_height = Screen.height,
            attack_center_x = attack.centerX,
            attack_center_y = attack.centerY,
            attack_visual_radius = attack.visualRadius,
            attack_hitbox_radius = attack.hitboxRadius,
            dodge_center_x = dodge.centerX,
            dodge_center_y = dodge.centerY,
            dodge_visual_radius = dodge.visualRadius,
            dodge_hitbox_radius = dodge.hitboxRadius,
            dynamic_attack_radius = dynamicAttackRadius,
            dynamic_dodge_radius = dynamicDodgeRadius
        };
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "ui_layout_snapshots.jsonl", record);
    }
}

