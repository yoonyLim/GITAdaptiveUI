#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class ADUIExperimentPanel : EditorWindow
{
    private string participantId = "test_user";

    [MenuItem("ADUI/Experiment Panel")]
    public static void ShowWindow()
    {
        GetWindow<ADUIExperimentPanel>("ADUI Experiment");
    }

    private void OnGUI()
    {
        GUILayout.Label("ADUI Data Collection", EditorStyles.boldLabel);
        participantId = EditorGUILayout.TextField("Participant ID", participantId);

        var participant = FindObjectOfType<ParticipantConfig>();
        var session = FindObjectOfType<ExperimentSessionManager>();
        var trial = FindObjectOfType<TrialScenarioManager>();
        var condition = FindObjectOfType<ConditionManager>();
        var publicConfig = FindObjectOfType<PublicPriorConfig>();
        var decoder = FindObjectOfType<BayesianInputDecoder>();
        var attackManager = FindObjectOfType<AdaptiveTouchManager>();
        var demandModel = FindObjectOfType<InteractionDemandModel>();

        if (GUILayout.Button("Set Participant ID"))
        {
            if (participant) participant.participantId = participantId;
            else Debug.LogWarning("[ADUI] ParticipantConfig not found.");
        }

        if (GUILayout.Button("Start Session"))
        {
            if (session) session.StartSession();
            else Debug.LogWarning("[ADUI] ExperimentSessionManager not found.");
        }

        if (GUILayout.Button("Run Calibration"))
        {
            if (trial) trial.StartCalibration();
            else Debug.LogWarning("[ADUI] TrialScenarioManager not found.");
        }

        if (GUILayout.Button("Run Main Trials"))
        {
            if (trial) trial.StartMainTrials();
            else Debug.LogWarning("[ADUI] TrialScenarioManager not found.");
        }

        if (GUILayout.Button("Toggle Condition"))
        {
            if (condition) Debug.Log("[ADUI] Condition: " + condition.NextCondition());
            else Debug.LogWarning("[ADUI] ConditionManager not found.");
        }

        if (GUILayout.Button("Load Public Defaults"))
        {
            if (publicConfig)
            {
                var loaded = publicConfig.LoadPublicDefaults();
                publicConfig.ApplyTo(FindObjectOfType<UserTouchModel>(), decoder);
                Debug.Log("[ADUI] Public defaults loaded: " + loaded);
            }
            else Debug.LogWarning("[ADUI] PublicPriorConfig not found.");
        }

        if (GUILayout.Button("Show Current Priors"))
        {
            if (decoder)
            {
                var state = trial ? trial.CurrentEnemyState() : ADUIEnemyState.Neutral;
                var priors = decoder.PriorForState(state);
                Debug.Log($"[ADUI] Priors for {state}: Attack={priors.x:F3}, Dodge={priors.y:F3}");
            }
        }

        if (GUILayout.Button("Show Current Posterior"))
        {
            if (attackManager && attackManager.LastDecodeResult != null)
            {
                var result = attackManager.LastDecodeResult;
                Debug.Log(
                    $"[ADUI] Posterior: Attack={result.posteriorAttack:F3}, Dodge={result.posteriorDodge:F3}, " +
                    $"gap={result.posteriorGap:F3}, final={result.finalExecutedAction}, reason={result.safetyGateReason}"
                );
            }
            else Debug.Log("[ADUI] No decode result yet. Tap a button area first.");
        }

        if (GUILayout.Button("Show Hitbox Visualization"))
        {
            if (attackManager)
            {
                if (attackManager.attackHitboxVisualizer) attackManager.attackHitboxVisualizer.gameObject.SetActive(true);
                if (attackManager.dodgeHitboxVisualizer) attackManager.dodgeHitboxVisualizer.gameObject.SetActive(true);
                Debug.Log("[ADUI] Hitbox visualization enabled.");
            }
        }

        if (GUILayout.Button("Export Logs"))
        {
            if (session) Debug.Log("[ADUI] Logs are written incrementally to: " + session.EnsureSession());
            else Debug.LogWarning("[ADUI] ExperimentSessionManager not found.");
        }

        EditorGUILayout.Space();
        GUILayout.Label("Interaction Mode", EditorStyles.boldLabel);
        if (demandModel)
        {
            demandModel.useManualMode = EditorGUILayout.Toggle("Manual Mode", demandModel.useManualMode);
            demandModel.manualMode = (ADUIInteractionMode)EditorGUILayout.EnumPopup("Mode", demandModel.manualMode);
            demandModel.externalInformationPriority = EditorGUILayout.Slider("Info Priority", demandModel.externalInformationPriority, 0f, 1f);
            demandModel.externalOcclusionRisk = EditorGUILayout.Slider("Occlusion Risk", demandModel.externalOcclusionRisk, 0f, 1f);
        }
        if (attackManager && attackManager.LastPolicy != null)
        {
            EditorGUILayout.LabelField("Current Mode", attackManager.LastPolicy.mode.ToString());
            EditorGUILayout.LabelField("Policy Reason", attackManager.LastPolicy.policyReason);
            EditorGUILayout.LabelField("Correction Strength", attackManager.LastPolicy.correctionStrength.ToString("F2"));
            EditorGUILayout.LabelField("Hitbox Expansion", attackManager.LastPolicy.hitboxExpansionRatio.ToString("F2"));
        }
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dataset Save Path", session ? session.sessionDirectory : "(session not started)");
        if (decoder)
        {
            EditorGUILayout.LabelField("Last Prior Source", decoder.publicPriorSource);
        }
    }
}
#endif
