using System.Collections;
using TMPro;
using UnityEngine;

public class KTHCalibrationGameFlow : MonoBehaviour
{
    [Header("References")]
    public RoguelikeGameManager gameManager;
    public TrialScenarioManager trialScenarioManager;
    public ExperimentSessionManager sessionManager;
    public UserTouchModel userTouchModel;
    public TextMeshProUGUI stageText;

    [Header("Flow")]
    public bool autoStartCalibration = true;
    public bool autoStartGameAfterCalibration = true;
    public int gameStartStage = 1;
    public float gameStartDelaySeconds = 0.8f;

    public bool CalibrationStarted { get; private set; }
    public bool CalibrationComplete { get; private set; }
    public bool GameStarted { get; private set; }

    private Coroutine gameStartRoutine;

    private void Start()
    {
        ResolveReferences();
        if (autoStartCalibration)
        {
            StartCalibrationThenGame();
        }
    }

    private void Update()
    {
        ResolveReferences();
        UpdateStageLabel();

        if (CalibrationStarted &&
            !CalibrationComplete &&
            trialScenarioManager != null &&
            trialScenarioManager.currentPhase != DatasetSchema.PhaseCalibration)
        {
            CalibrationComplete = true;
            if (autoStartGameAfterCalibration && gameStartRoutine == null)
            {
                gameStartRoutine = StartCoroutine(StartGameAfterDelay());
            }
        }
    }

    public void StartCalibrationThenGame()
    {
        ResolveReferences();
        if (trialScenarioManager == null)
        {
            Debug.LogWarning("KTHCalibrationGameFlow requires TrialScenarioManager.");
            return;
        }

        sessionManager?.EnsureSession();
        gameManager.startStageOnPlay = false;
        trialScenarioManager.StartCalibration();
        CalibrationStarted = true;
        CalibrationComplete = false;
        GameStarted = false;
    }

    private IEnumerator StartGameAfterDelay()
    {
        yield return new WaitForSeconds(gameStartDelaySeconds);
        ResolveReferences();
        if (gameManager != null)
        {
            gameManager.StartStage(gameStartStage);
            GameStarted = true;
        }
    }

    private void ResolveReferences()
    {
        if (gameManager == null)
        {
            gameManager = RoguelikeGameManager.Instance != null ? RoguelikeGameManager.Instance : FindAnyObjectByType<RoguelikeGameManager>();
        }

        if (trialScenarioManager == null)
        {
            trialScenarioManager = FindAnyObjectByType<TrialScenarioManager>();
        }

        if (sessionManager == null)
        {
            sessionManager = FindAnyObjectByType<ExperimentSessionManager>();
        }

        if (userTouchModel == null)
        {
            userTouchModel = FindAnyObjectByType<UserTouchModel>();
        }

        if (stageText == null && gameManager != null)
        {
            stageText = gameManager.stageText;
        }
    }

    private void UpdateStageLabel()
    {
        if (stageText == null || trialScenarioManager == null || GameStarted)
        {
            return;
        }

        if (trialScenarioManager.currentPhase == DatasetSchema.PhaseCalibration)
        {
            int total = Mathf.Max(trialScenarioManager.calibrationTotalCount, 1);
            stageText.text = $"Calibration {trialScenarioManager.currentTrialId} / {total}";
        }
        else if (CalibrationComplete)
        {
            stageText.text = "Calibration complete - starting game";
        }
    }
}
