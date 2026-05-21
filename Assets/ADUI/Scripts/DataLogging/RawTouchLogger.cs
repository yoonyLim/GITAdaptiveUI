using UnityEngine;

public class RawTouchLogger : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    public void Log(int trialId, Vector2 position, string phase, float pressure = 0f, float radius = 0f, int fingerId = 0)
    {
        if (!sessionManager) return;
        var record = new ADUIRawTouchEvent
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            trial_id = trialId,
            timestamp_touch_ms = UnixMs(),
            touch_x = position.x,
            touch_y = position.y,
            touch_phase = phase,
            touch_pressure = pressure,
            touch_radius = radius,
            finger_id = fingerId
        };
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "raw_touch_events.jsonl", record);
    }

    private long UnixMs()
    {
        return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}

