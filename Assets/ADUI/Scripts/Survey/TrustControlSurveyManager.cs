using System;
using UnityEngine;
using UnityEngine.UI;

public class TrustControlSurveyManager : MonoBehaviour
{
    public ExperimentSessionManager sessionManager;

    [Header("Optional UI Controls")]
    public Slider trustSlider;
    public Slider controlSlider;
    public Slider predictabilitySlider;
    public InputField freeTextInput;

    public void SubmitCurrentSurvey()
    {
        var record = new ADUISurveyRecord
        {
            session_id = sessionManager ? sessionManager.sessionId : "",
            participant_id = sessionManager ? sessionManager.ParticipantId() : "",
            timestamp_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            trust_score = trustSlider ? Mathf.RoundToInt(trustSlider.value) : -1,
            control_score = controlSlider ? Mathf.RoundToInt(controlSlider.value) : -1,
            predictability_score = predictabilitySlider ? Mathf.RoundToInt(predictabilitySlider.value) : -1,
            free_text = freeTextInput ? freeTextInput.text : "",
            source = "in_app_optional_survey"
        };
        SubmitSurvey(record);
    }

    public void SubmitSurvey(ADUISurveyRecord record)
    {
        if (!sessionManager || !sessionManager.exporter || record == null) return;
        sessionManager.exporter.AppendJsonl(sessionManager.EnsureSession(), "trust_control_survey.jsonl", record);
    }
}

