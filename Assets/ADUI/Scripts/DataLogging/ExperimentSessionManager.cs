using System;
using System.IO;
using UnityEngine;

public class ExperimentSessionManager : MonoBehaviour
{
    public ParticipantConfig participantConfig;
    public CsvJsonlExporter exporter;

    [Header("Session")]
    public string sessionId = "";
    public string sessionDirectory = "";
    public string appVersion = "0.1.0";
    public bool autoStartSession = false;

    public bool HasActiveSession => !string.IsNullOrEmpty(sessionDirectory);

    private void Awake()
    {
        if (!exporter) exporter = GetComponent<CsvJsonlExporter>();
        if (!exporter) exporter = gameObject.AddComponent<CsvJsonlExporter>();
        if (autoStartSession) StartSession();
    }

    public void StartSession()
    {
        var participantId = participantConfig ? participantConfig.participantId : "test_user";
        if (string.IsNullOrWhiteSpace(participantId)) participantId = "unknown_participant";
        sessionId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + participantId;
        sessionDirectory = Path.Combine(Application.persistentDataPath, "adui_sessions", sessionId);
        Directory.CreateDirectory(sessionDirectory);
        exporter.WriteJson(sessionDirectory, "session_meta.json", BuildMeta());
    }

    public ADUISessionMeta BuildMeta()
    {
        return new ADUISessionMeta
        {
            session_id = sessionId,
            participant_id = participantConfig ? participantConfig.participantId : "test_user",
            device_model = SystemInfo.deviceModel,
            platform = Application.platform.ToString(),
            screen_width = Screen.width,
            screen_height = Screen.height,
            dpi = Screen.dpi,
            unity_version = Application.unityVersion,
            app_version = appVersion,
            timestamp_start = DateTime.UtcNow.ToString("o"),
            handedness = participantConfig ? participantConfig.handedness : "unavailable",
            notes = participantConfig ? participantConfig.notes : ""
        };
    }

    public string EnsureSession()
    {
        if (!HasActiveSession) StartSession();
        return sessionDirectory;
    }

    public string ParticipantId()
    {
        return participantConfig ? participantConfig.participantId : "test_user";
    }
}

