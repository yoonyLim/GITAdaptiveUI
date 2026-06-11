using System;
using System.IO;
using System.Text;
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

    public string WriteFinalLogBundle(string completionState, int currentStage, string fileName = "final_session_logs.json")
    {
        string directory = EnsureSession();
        string outputPath = Path.Combine(directory, fileName);
        string outputFileName = Path.GetFileName(outputPath);
        WriteFinalSummary(directory, completionState, currentStage, outputFileName);
        string[] files = Directory.GetFiles(directory);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder(32 * 1024);
        builder.AppendLine("{");
        AppendStringProperty(builder, "session_id", sessionId, true);
        AppendStringProperty(builder, "participant_id", ParticipantId(), true);
        AppendStringProperty(builder, "generated_at_utc", DateTime.UtcNow.ToString("o"), true);
        AppendStringProperty(builder, "completion_state", completionState ?? "", true);
        builder.Append("  \"current_stage\": ").Append(currentStage).AppendLine(",");
        AppendStringProperty(builder, "source_directory", directory.Replace("\\", "/"), true);

        builder.AppendLine("  \"json_logs\": {");
        bool wroteJsonFile = false;
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            string extension = Path.GetExtension(path);
            if (string.Equals(name, outputFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (wroteJsonFile)
            {
                builder.AppendLine(",");
            }

            builder.Append("    \"").Append(EscapeJson(name)).Append("\": ");
            if (string.Equals(extension, ".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                AppendJsonlArray(builder, path);
            }
            else
            {
                AppendRawJson(builder, path);
            }

            wroteJsonFile = true;
        }

        builder.AppendLine();
        builder.AppendLine("  },");

        builder.AppendLine("  \"text_logs\": {");
        bool wroteTextFile = false;
        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            string name = Path.GetFileName(path);
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (wroteTextFile)
            {
                builder.AppendLine(",");
            }

            builder.Append("    \"").Append(EscapeJson(name)).Append("\": \"");
            builder.Append(EscapeJson(File.ReadAllText(path, Encoding.UTF8)));
            builder.Append("\"");
            wroteTextFile = true;
        }

        builder.AppendLine();
        builder.AppendLine("  }");
        builder.AppendLine("}");

        File.WriteAllText(outputPath, builder.ToString(), Encoding.UTF8);
        Debug.Log($"[ADUI] Final session log bundle written: {outputPath}");
        return outputPath;
    }

    private void WriteFinalSummary(string directory, string completionState, int currentStage, string bundleFileName)
    {
        ConditionManager conditionManager = FindAnyObjectByType<ConditionManager>();
        RoguelikeGameManager gameManager = RoguelikeGameManager.Instance != null
            ? RoguelikeGameManager.Instance
            : FindAnyObjectByType<RoguelikeGameManager>();
        var summary = new ADUISessionFinalSummary
        {
            session_id = sessionId,
            participant_id = ParticipantId(),
            condition = conditionManager != null ? conditionManager.currentCondition : "",
            completion_state = completionState ?? "",
            current_stage = currentStage,
            prototype_complete = gameManager != null && gameManager.PrototypeComplete,
            prototype_failed = gameManager != null && gameManager.PrototypeFailed,
            timestamp_end = DateTime.UtcNow.ToString("o"),
            final_bundle_file = bundleFileName
        };

        exporter.WriteJson(directory, "session_final_summary.json", summary, true);
    }

    private static void AppendStringProperty(StringBuilder builder, string name, string value, bool trailingComma)
    {
        builder.Append("  \"")
            .Append(EscapeJson(name))
            .Append("\": \"")
            .Append(EscapeJson(value ?? ""))
            .Append("\"");

        if (trailingComma)
        {
            builder.Append(",");
        }

        builder.AppendLine();
    }

    private static void AppendRawJson(StringBuilder builder, string path)
    {
        string raw = File.ReadAllText(path, Encoding.UTF8).Trim();
        builder.Append(string.IsNullOrEmpty(raw) ? "null" : raw);
    }

    private static void AppendJsonlArray(StringBuilder builder, string path)
    {
        builder.AppendLine("[");
        bool wroteLine = false;
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (wroteLine)
            {
                builder.AppendLine(",");
            }

            builder.Append("      ").Append(line);
            wroteLine = true;
        }

        if (wroteLine)
        {
            builder.AppendLine();
        }

        builder.Append("    ]");
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        var builder = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 32)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }
                    break;
            }
        }

        return builder.ToString();
    }
}

