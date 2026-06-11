using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoguelikeGameManager : MonoBehaviour
{
    public static RoguelikeGameManager Instance;
    public const float ArenaHalfWidth = 10f;
    public const float ArenaHalfHeight = 10f;
    public const float PlayerArenaPadding = 0.72f;
    public const float EnemyArenaPadding = 0.52f;

    [Header("Prefabs")]
    public GameObject meleeEnemyPrefab;
    public GameObject rangedEnemyPrefab;
    public GameObject bossEnemyPrefab;

    [Header("SPUM Visuals")]
    public bool useSpumVisuals = true;
    public string meleeSpumResourcePath = "Addons/BasicPack/2_Prefab/Skelton/SPUM_20240911215639833";
    public string rangedSpumResourcePath = "Addons/BasicPack/2_Prefab/Skelton/SPUM_20240911215639920";
    public string bossSpumResourcePath = "Addons/BasicPack/2_Prefab/Devil/SPUM_20240911215640719";
    public Vector3 enemySpumVisualLocalPosition = new Vector3(0f, -0.1f, 0f);
    public Vector3 meleeSpumVisualLocalScale = new Vector3(1.25f, 1.25f, 1f);
    public Vector3 rangedSpumVisualLocalScale = new Vector3(1.25f, 1.25f, 1f);
    public Vector3 bossSpumVisualLocalScale = new Vector3(2.05f, 2.05f, 1f);

    [Header("Player & Spawning")]
    public Transform playerTransform;
    public PlayerController playerController;
    public Transform enemyRoot;
    public float spawnRadius = 7.5f;
    public float spawnRadiusJitter = 1.2f;
    public bool startStageOnPlay;
    public int startingStage = 1;

    [Header("Evaluation Demo")]
    public bool useEvaluationScenarioStages = false;
    public float evaluationStage1Seconds = 20f;
    public float evaluationStage2Seconds = 30f;
    public float evaluationStage3Seconds = 30f;

    [Header("UI")]
    public Button skipButton;
    public Button startButton;
    public Button restartButton;
    public GameObject startScreenRoot;
    public GameObject resultScreenRoot;
    public TextMeshProUGUI stageText;
    public TextMeshProUGUI resultText;
    public UserEvaluationLogger evaluationLogger;

    public int CurrentStage => currentStage;
    public bool IsStageRunning => stageRunning;
    public bool PrototypeComplete => prototypeComplete;
    public bool PrototypeFailed => prototypeFailed;
    public bool PrototypeEnded => prototypeComplete || prototypeFailed;
    public IReadOnlyList<EnemyControllerBase> ActiveEnemies => activeEnemies;
    public Transform PlayerTransform => playerTransform;
    public PlayerController PlayerController => playerController;
    public static Rect ArenaBounds => Rect.MinMaxRect(-ArenaHalfWidth, -ArenaHalfHeight, ArenaHalfWidth, ArenaHalfHeight);

    private readonly List<EnemyControllerBase> activeEnemies = new List<EnemyControllerBase>();
    private readonly List<StageTelemetry> completedStageData = new List<StageTelemetry>();
    private int currentStage = 1;
    private bool stageRunning;
    private bool isClearingStage;
    private bool calibrationScenarioActive;
    private bool prototypeComplete;
    private bool prototypeFailed;
    private Coroutine autoAdvanceRoutine;
    private Coroutine evaluationStageRoutine;
    private GameObject runtimePrefabRoot;
    private StageTelemetry currentStageData;
    private float stageStartTime;
    private float nextStageUiUpdateTime;

    private class StageTelemetry
    {
        public int stageNumber;
        public int buttonPresses;
        public int damageTaken;
        public int healingDone;
        public int touchDistanceSamples;
        public float totalTouchDistance;
        public float duration;
        public bool skipped;
        public bool failed;

        public float AverageTouchDistance => touchDistanceSamples <= 0 ? 0f : totalTouchDistance / touchDistanceSamples;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        ResolveReferences();
        EnsureRuntimeArena();
        EnsureRuntimePrefabs();

        if (skipButton != null)
        {
            skipButton.onClick.RemoveListener(SkipStage);
            skipButton.onClick.AddListener(SkipStage);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(BeginPrototype);
            startButton.onClick.AddListener(BeginPrototype);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(BeginPrototype);
            restartButton.onClick.AddListener(BeginPrototype);
        }

        if (startStageOnPlay)
        {
            BeginPrototype();
        }
        else
        {
            ShowStartScreen();
        }
    }

    private void Update()
    {
        if (!useEvaluationScenarioStages || !stageRunning || calibrationScenarioActive || prototypeComplete || prototypeFailed)
        {
            return;
        }

        if (Time.unscaledTime >= nextStageUiUpdateTime)
        {
            nextStageUiUpdateTime = Time.unscaledTime + 0.25f;
            UpdateStageUI();
        }
    }

    public void BeginPrototype()
    {
        completedStageData.Clear();
        prototypeComplete = false;
        prototypeFailed = false;
        stageRunning = false;
        currentStageData = null;
        calibrationScenarioActive = false;

        SetScreenActive(startScreenRoot, false);
        SetScreenActive(resultScreenRoot, false);

        StartStage(startingStage);
    }

    public static Vector2 ClampToArena(Vector2 position, float padding = 0f)
    {
        float safePadding = Mathf.Max(0f, padding);
        float minX = -ArenaHalfWidth + safePadding;
        float maxX = ArenaHalfWidth - safePadding;
        float minY = -ArenaHalfHeight + safePadding;
        float maxY = ArenaHalfHeight - safePadding;
        return new Vector2(
            Mathf.Clamp(position.x, minX, maxX),
            Mathf.Clamp(position.y, minY, maxY));
    }

    public void StartStage(int stageNumber)
    {
        if (stageNumber > 3)
        {
            CompletePrototype();
            return;
        }

        prototypeComplete = false;
        prototypeFailed = false;
        ClearEnemies();
        currentStage = stageNumber;
        ResolveReferences();

        if (playerController != null)
        {
            playerController.ResetStats();
        }

        Debug.Log($"Starting Stage {currentStage}");
        StartStageTelemetry(currentStage);

        if (useEvaluationScenarioStages && SpawnEvaluationScenarioStage(currentStage))
        {
            StartEvaluationStageTimer(currentStage);
            UpdateStageUI();
            return;
        }

        switch (currentStage)
        {
            case 1:
                SpawnEnemies(meleeEnemyPrefab, Random.Range(5, 11), EnemyKind.Melee);
                break;
            case 2:
                SpawnEnemies(meleeEnemyPrefab, 5, EnemyKind.Melee);
                SpawnEnemies(rangedEnemyPrefab, 2, EnemyKind.Ranged);
                break;
            case 3:
                SpawnEnemies(bossEnemyPrefab, 1, EnemyKind.Boss);
                SpawnEnemies(meleeEnemyPrefab, 2, EnemyKind.Melee);
                SpawnEnemies(rangedEnemyPrefab, 2, EnemyKind.Ranged);
                break;
        }

        UpdateStageUI();
    }

    public void SkipStage()
    {
        Debug.Log("Stage Skipped!");
        FinishCurrentStage(true);
        StartStage(currentStage + 1);
    }

    public void NotifyPlayerDied()
    {
        if (prototypeComplete || prototypeFailed || calibrationScenarioActive || !stageRunning)
        {
            return;
        }

        prototypeFailed = true;
        FinishCurrentStage(false, true);
        ClearEnemies();

        if (playerController != null)
        {
            playerController.ClearVirtualMoveInput();
        }

        UpdateStageUI();
        ShowResultScreen();
        Debug.Log("Game Over!");
    }

    public void BeginCalibrationScenario(ADUIContextScenario scenario, bool liveCombat = false)
    {
        ResolveReferences();
        EnsureRuntimeArena();
        EnsureRuntimePrefabs();
        ClearEnemiesImmediate();

        calibrationScenarioActive = true;
        stageRunning = false;
        prototypeComplete = false;
        prototypeFailed = false;
        currentStageData = null;

        if (playerController != null)
        {
            playerController.ResetStats(true);
            playerController.ClearVirtualMoveInput();
        }

        Vector2 playerPosition = playerTransform != null ? playerTransform.position : Vector2.zero;
        switch (scenario)
        {
            case ADUIContextScenario.AttackCommitWindow:
            case ADUIContextScenario.SafeAttackOpportunity:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.95f, 0.12f), false, false, true);
                break;
            case ADUIContextScenario.AttackOpportunity:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.75f, 0.15f), false, false, !liveCombat);
                break;
            case ADUIContextScenario.PreDodgeWindow:
                EnemyControllerBase preDodgeEnemy = SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.72f, 0.24f), false, false, true);
                preDodgeEnemy?.SetCalibrationAttackReadyIn(0.45f);
                break;
            case ADUIContextScenario.RiskyCloseEnemy:
                EnemyControllerBase riskyEnemy = SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.2f, 0.3f), false, false, true);
                riskyEnemy?.SetCalibrationAttackReadyIn(0.35f);
                break;
            case ADUIContextScenario.ImmediateDodgeThreat:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.28f, 0.2f), true, false, true);
                break;
            case ADUIContextScenario.ProjectileDodgeThreat:
                SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(5.2f, 1.6f), false, false, !liveCombat);
                SpawnCalibrationProjectile(playerPosition + new Vector2(2.6f, -0.45f), new Vector2(-1f, 0.08f), 0, true);
                break;
            case ADUIContextScenario.MovingUnderPressure:
                if (playerController != null)
                {
                    playerController.SetVirtualMoveInput(new Vector2(0.8f, 0.25f));
                }

                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.16f, 0.58f), false, false, true);
                break;
            case ADUIContextScenario.DodgeThreat:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.45f, 0.25f), !liveCombat, false, !liveCombat);
                SpawnCalibrationProjectile(playerPosition + new Vector2(3.8f, -0.85f), new Vector2(-1f, 0.18f), liveCombat ? 1 : 0);
                break;
            case ADUIContextScenario.LowHpHeal:
                SetCalibrationHp(30);
                SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(5.6f, 2.1f), false, false, !liveCombat);
                break;
            case ADUIContextScenario.CrowdWhirlwind:
                SpawnCalibrationCrowd(playerPosition, false, !liveCombat);
                break;
            case ADUIContextScenario.MovementThreat:
                if (playerController != null)
                {
                    playerController.SetVirtualMoveInput(new Vector2(0.8f, 0.25f));
                }

                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.65f, 0.75f), false, false, !liveCombat);
                break;
            case ADUIContextScenario.LowHpThreat:
                SetCalibrationHp(25);
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.55f, -0.2f), !liveCombat, false, !liveCombat);
                SpawnCalibrationProjectile(playerPosition + new Vector2(-2.8f, 0.7f), new Vector2(1f, -0.12f), liveCombat ? 1 : 0, true);
                break;
            case ADUIContextScenario.CrowdLowHp:
                SetCalibrationHp(30);
                SpawnCalibrationCrowd(playerPosition, !liveCombat, !liveCombat);
                break;
            default:
                break;
        }

        UpdateStageUI();
        CombatManager.Instance?.ForceRefreshCombatContext();
    }

    public void BeginModeShowcaseScenario(ADUIInteractionMode mode)
    {
        if (mode == ADUIInteractionMode.ActionFirst || mode == ADUIInteractionMode.GuidanceProcedure)
        {
            BeginCalibrationScenario(ADUIContextScenario.ImmediateDodgeThreat, false);
            stageRunning = true;
            CombatManager.Instance?.ForceRefreshCombatContext();
            return;
        }

        ResolveReferences();
        EnsureRuntimeArena();
        EnsureRuntimePrefabs();
        ClearEnemiesImmediate();

        calibrationScenarioActive = true;
        stageRunning = true;
        prototypeComplete = false;
        currentStageData = null;

        if (playerController != null)
        {
            playerController.ResetStats(true);
            playerController.ClearVirtualMoveInput();
            SetCalibrationHp(Mathf.CeilToInt(playerController.maxHP * 0.34f));
        }

        Vector2 playerPosition = playerTransform != null ? playerTransform.position : Vector2.zero;
        SpawnCalibrationEnemy(bossEnemyPrefab, EnemyKind.Boss, playerPosition + new Vector2(4.8f, 2.1f), false, false, true);
        SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(-4.4f, 1.8f), false, false, true);
        SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(4.2f, -2.2f), false, false, true);

        UpdateStageUI();
        CombatManager.Instance?.ForceRefreshCombatContext();
    }

    public void ClearCalibrationScenario()
    {
        if (playerController != null)
        {
            playerController.ClearVirtualMoveInput();
        }

        ClearEnemiesImmediate();
        calibrationScenarioActive = false;
        stageRunning = false;
        currentStageData = null;
        UpdateStageUI();
        CombatManager.Instance?.ForceRefreshCombatContext();
    }

    public void NotifyEnemyDefeated(EnemyControllerBase enemy)
    {
        if (enemy == null)
        {
            return;
        }

        activeEnemies.Remove(enemy);
        UpdateStageUI();

        if (!calibrationScenarioActive && !isClearingStage && activeEnemies.Count == 0 && !prototypeComplete)
        {
            FinishCurrentStage(false);

            if (autoAdvanceRoutine != null)
            {
                StopCoroutine(autoAdvanceRoutine);
            }

            autoAdvanceRoutine = StartCoroutine(AutoAdvanceAfterDelay());
        }
    }

    public void NotifyEnemyDestroyed(EnemyControllerBase enemy)
    {
        if (enemy == null)
        {
            return;
        }

        activeEnemies.Remove(enemy);
        UpdateStageUI();
    }

    public void RecordButtonPress(float targetDistancePixels)
    {
        if (!stageRunning || currentStageData == null)
        {
            return;
        }

        currentStageData.buttonPresses++;
        currentStageData.totalTouchDistance += Mathf.Max(0f, targetDistancePixels);
        currentStageData.touchDistanceSamples++;
    }

    public void RecordPlayerDamage(int amount)
    {
        if (!stageRunning || currentStageData == null || amount <= 0)
        {
            return;
        }

        currentStageData.damageTaken += amount;
    }

    public void RecordPlayerHealing(int amount)
    {
        if (!stageRunning || currentStageData == null || amount <= 0)
        {
            return;
        }

        currentStageData.healingDone += amount;
    }

    private bool SpawnEvaluationScenarioStage(int stageNumber)
    {
        Vector2 playerPosition = playerTransform != null ? playerTransform.position : Vector2.zero;

        switch (stageNumber)
        {
            case 1:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.88f, 0.12f), false, false, true);
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(2.15f, -0.82f), false, false, true);
                break;
            case 2:
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.18f, 0.58f), false, false, false);
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(-1.1f, -0.92f), false, false, false);
                SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(4.8f, 1.55f), false, false, true);
                SpawnCalibrationProjectile(playerPosition + new Vector2(2.9f, -0.55f), new Vector2(-1f, 0.1f), 0, true);
                break;
            case 3:
                SetCalibrationHp(Mathf.CeilToInt((playerController != null ? playerController.maxHP : 100) * 0.28f));
                SpawnCalibrationEnemy(bossEnemyPrefab, EnemyKind.Boss, playerPosition + new Vector2(3.2f, 0.95f), false, false, false);
                SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + new Vector2(1.32f, -0.18f), true, false, true);
                SpawnCalibrationEnemy(rangedEnemyPrefab, EnemyKind.Ranged, playerPosition + new Vector2(-4.1f, 1.45f), false, false, true);
                SpawnCalibrationProjectile(playerPosition + new Vector2(-2.8f, 0.72f), new Vector2(1f, -0.12f), 0, true);
                break;
            default:
                return false;
        }

        Debug.Log($"Evaluation scenario stage {stageNumber} spawned.");
        return true;
    }

    private void SpawnEnemies(GameObject prefab, int count, EnemyKind fallbackKind)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"Cannot spawn {fallbackKind}: prefab is missing.");
            return;
        }

        if (enemyRoot == null)
        {
            enemyRoot = new GameObject("Enemies").transform;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPos = GetSpawnPosition(i, count);
            GameObject enemyObject = Instantiate(prefab, spawnPos, Quaternion.identity, enemyRoot);
            enemyObject.SetActive(true);

            EnemyControllerBase enemy = enemyObject.GetComponent<EnemyControllerBase>();
            if (enemy == null)
            {
                enemy = AddControllerForKind(enemyObject, fallbackKind);
            }

            enemy.Initialize(playerTransform, this, CombatManager.Instance);
            activeEnemies.Add(enemy);
        }
    }

    private EnemyControllerBase SpawnCalibrationEnemy(
        GameObject prefab,
        EnemyKind fallbackKind,
        Vector2 position,
        bool telegraphing,
        bool attacking,
        bool freezeAi)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"Cannot spawn calibration {fallbackKind}: prefab is missing.");
            return null;
        }

        if (enemyRoot == null)
        {
            enemyRoot = new GameObject("Enemies").transform;
        }

        GameObject enemyObject = Instantiate(prefab, position, Quaternion.identity, enemyRoot);
        enemyObject.name = $"Calibration {fallbackKind} Scenario Enemy";
        enemyObject.SetActive(true);

        EnemyControllerBase enemy = enemyObject.GetComponent<EnemyControllerBase>();
        if (enemy == null)
        {
            enemy = AddControllerForKind(enemyObject, fallbackKind);
        }

        enemy.Initialize(playerTransform, this, CombatManager.Instance);
        enemy.ConfigureCalibrationPose(telegraphing, attacking, freezeAi);
        if (!freezeAi)
        {
            enemy.attackDamage = Mathf.Min(enemy.attackDamage, 1);
            enemy.attackCooldown = Mathf.Max(enemy.attackCooldown, 2.2f);
        }

        activeEnemies.Add(enemy);
        return enemy;
    }

    private void SpawnCalibrationCrowd(Vector2 playerPosition, bool includeThreat, bool freezeAi)
    {
        Vector2[] offsets =
        {
            new Vector2(1.65f, 0.2f),
            new Vector2(-1.3f, 0.85f),
            new Vector2(-0.8f, -1.35f),
            new Vector2(0.85f, -1.2f)
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            bool telegraphing = includeThreat && i == 0;
            SpawnCalibrationEnemy(meleeEnemyPrefab, EnemyKind.Melee, playerPosition + offsets[i], telegraphing, false, freezeAi);
        }
    }

    private void SpawnCalibrationProjectile(Vector2 position, Vector2 direction, int damage, bool holdThreat = false)
    {
        GameObject arrow = new GameObject("Calibration Incoming Projectile");
        arrow.transform.position = position;

        Vector2 safeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.left;
        arrow.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(safeDirection.y, safeDirection.x) * Mathf.Rad2Deg);

        PrototypeVisualFactory.EnsureSpriteRenderer(
            arrow,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.95f, 0.85f, 0.48f, 1f),
            new Vector2(0.68f, 0.12f),
            3);

        Rigidbody2D body = arrow.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        BoxCollider2D collider = arrow.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(0.8f, 0.2f);

        EnemyProjectile projectile = arrow.AddComponent<EnemyProjectile>();
        projectile.speed = holdThreat ? 0.35f : 3.4f;
        projectile.damage = Mathf.Max(0, damage);
        projectile.lifetime = holdThreat ? 4.8f : 3.5f;
        projectile.alwaysThreatening = holdThreat;
        projectile.SetDirection(safeDirection);
    }

    private void SetCalibrationHp(int hp)
    {
        if (playerController == null)
        {
            return;
        }

        playerController.SetScenarioHP(hp);
    }

    private void ClearEnemies()
    {
        isClearingStage = true;

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        if (evaluationStageRoutine != null)
        {
            StopCoroutine(evaluationStageRoutine);
            evaluationStageRoutine = null;
        }

        foreach (EnemyControllerBase enemy in activeEnemies)
        {
            if (enemy != null)
            {
                enemy.gameObject.SetActive(false);
                Destroy(enemy.gameObject);
            }
        }

        activeEnemies.Clear();
        EnemyProjectile.DestroyAllProjectiles();
        UpdateStageUI();
        StartCoroutine(ReleaseClearingFlag());
    }

    private void ClearEnemiesImmediate()
    {
        isClearingStage = true;

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        if (evaluationStageRoutine != null)
        {
            StopCoroutine(evaluationStageRoutine);
            evaluationStageRoutine = null;
        }

        foreach (EnemyControllerBase enemy in activeEnemies)
        {
            if (enemy != null)
            {
                enemy.gameObject.SetActive(false);
                Destroy(enemy.gameObject);
            }
        }

        activeEnemies.Clear();
        EnemyProjectile.DestroyAllProjectiles();
        isClearingStage = false;
        UpdateStageUI();
    }

    private Vector3 GetSpawnPosition(int index, int count)
    {
        Vector2 playerPosition = playerTransform != null ? playerTransform.position : Vector2.zero;
        float angle = (Mathf.PI * 2f * index / Mathf.Max(1, count)) + Random.Range(-0.35f, 0.35f);
        float radius = spawnRadius + Random.Range(-spawnRadiusJitter, spawnRadiusJitter);
        Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Max(2.5f, radius);
        return new Vector3(playerPosition.x + offset.x, playerPosition.y + offset.y, 0f);
    }

    private EnemyControllerBase AddControllerForKind(GameObject enemyObject, EnemyKind kind)
    {
        switch (kind)
        {
            case EnemyKind.Ranged:
                return enemyObject.AddComponent<RangedEnemyController>();
            case EnemyKind.Boss:
                return enemyObject.AddComponent<BossController>();
            default:
                return enemyObject.AddComponent<MeleeEnemyController>();
        }
    }

    private void ResolveReferences()
    {
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }

        if (playerTransform == null && playerController != null)
        {
            playerTransform = playerController.transform;
        }

        if (enemyRoot == null)
        {
            GameObject existingRoot = GameObject.Find("Enemies");
            enemyRoot = existingRoot != null ? existingRoot.transform : new GameObject("Enemies").transform;
        }
    }

    private void EnsureRuntimePrefabs()
    {
        if (runtimePrefabRoot == null)
        {
            runtimePrefabRoot = new GameObject("Runtime Prototype Prefabs");
            runtimePrefabRoot.SetActive(false);
        }

        if (meleeEnemyPrefab == null)
        {
            meleeEnemyPrefab = CreateMeleePrototype();
        }

        if (rangedEnemyPrefab == null)
        {
            rangedEnemyPrefab = CreateRangedPrototype();
        }

        if (bossEnemyPrefab == null)
        {
            bossEnemyPrefab = CreateBossPrototype();
        }
    }

    private GameObject CreateMeleePrototype()
    {
        GameObject enemy = CreateEnemyPrototypeObject(
            "Prototype Sword Enemy",
            new Color(0.95f, 0.38f, 0.28f, 1f),
            Vector2.one * 0.9f,
            meleeSpumResourcePath,
            meleeSpumVisualLocalScale,
            10);
        MeleeEnemyController controller = enemy.AddComponent<MeleeEnemyController>();
        controller.maxHP = 35;
        controller.moveSpeed = 2.9f;
        controller.attackRange = 1.55f;
        controller.attackCooldown = 1.55f;
        controller.telegraphDuration = 1.1f;
        controller.attackFrameDuration = 0.16f;
        controller.attackDamage = 9;
        controller.normalColor = new Color(0.95f, 0.38f, 0.28f, 1f);
        controller.telegraphColor = new Color(1f, 0.75f, 0.25f, 1f);
        controller.attackColor = new Color(1f, 0.08f, 0.08f, 1f);
        return enemy;
    }

    private GameObject CreateRangedPrototype()
    {
        GameObject enemy = CreateEnemyPrototypeObject(
            "Prototype Bow Enemy",
            new Color(0.45f, 0.64f, 1f, 1f),
            Vector2.one * 0.86f,
            rangedSpumResourcePath,
            rangedSpumVisualLocalScale,
            10);
        RangedEnemyController controller = enemy.AddComponent<RangedEnemyController>();
        controller.maxHP = 28;
        controller.moveSpeed = 2.05f;
        controller.attackRange = 6.8f;
        controller.shootingRange = 7f;
        controller.keepAwayDistance = 3.2f;
        controller.attackCooldown = 2.4f;
        controller.telegraphDuration = 1.25f;
        controller.attackFrameDuration = 0.12f;
        controller.attackDamage = 8;
        controller.normalColor = new Color(0.45f, 0.64f, 1f, 1f);
        controller.telegraphColor = new Color(1f, 0.66f, 0.2f, 1f);
        controller.attackColor = new Color(1f, 0.1f, 0.1f, 1f);
        return enemy;
    }

    private GameObject CreateBossPrototype()
    {
        GameObject enemy = CreateEnemyPrototypeObject(
            "Prototype Boss Enemy",
            new Color(0.55f, 0.18f, 0.72f, 1f),
            Vector2.one * 1.75f,
            bossSpumResourcePath,
            bossSpumVisualLocalScale,
            12);
        CircleCollider2D collider = enemy.GetComponent<CircleCollider2D>();
        if (collider != null)
        {
            collider.radius = 0.7f;
        }

        BossController controller = enemy.AddComponent<BossController>();
        controller.maxHP = 360;
        controller.moveSpeed = 1.65f;
        controller.attackRange = 3f;
        controller.bossKeepDistance = 3.1f;
        controller.bossRetreatDistance = 1.55f;
        controller.bossApproachSpeedMultiplier = 0.95f;
        controller.bossRetreatSpeedMultiplier = 0.45f;
        controller.attackLeewayAfterApproach = 0.65f;
        controller.bossPositionDeadZone = 0.18f;
        controller.attackCooldown = 2.65f;
        controller.telegraphDuration = 1.25f;
        controller.attackFrameDuration = 0.22f;
        controller.attackDamage = 24;
        controller.normalColor = new Color(0.55f, 0.18f, 0.72f, 1f);
        controller.telegraphColor = new Color(1f, 0.55f, 0.1f, 1f);
        controller.attackColor = new Color(1f, 0.05f, 0.05f, 1f);
        return enemy;
    }

    private GameObject CreateEnemyPrototypeObject(
        string name,
        Color color,
        Vector2 size,
        string spumResourcePath,
        Vector3 spumLocalScale,
        int spumSortingOrderOffset)
    {
        GameObject enemy = new GameObject(name);
        enemy.transform.SetParent(runtimePrefabRoot.transform, false);
        enemy.SetActive(false);

        SpumVisualController visual = null;
        if (useSpumVisuals && !string.IsNullOrEmpty(spumResourcePath))
        {
            visual = enemy.AddComponent<SpumVisualController>();
            visual.Configure(spumResourcePath, enemySpumVisualLocalPosition, spumLocalScale, spumSortingOrderOffset);
        }

        if (visual == null || !visual.HasVisual)
        {
            PrototypeVisualFactory.EnsureSpriteRenderer(enemy, PrototypeVisualFactory.CircleSprite, color, size, 2);
        }

        Rigidbody2D body = enemy.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        CircleCollider2D collider = enemy.AddComponent<CircleCollider2D>();
        collider.radius = 0.45f;

        return enemy;
    }

    private void EnsureRuntimeArena()
    {
        if (GameObject.Find("Prototype Arena") != null)
        {
            return;
        }

        GameObject arena = new GameObject("Prototype Arena");
        GameObject floor = new GameObject("Arena Floor");
        floor.transform.SetParent(arena.transform, false);
        PrototypeVisualFactory.EnsureSpriteRenderer(
            floor,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.1f, 0.13f, 0.12f, 1f),
            new Vector2(20f, 20f),
            -10);

        CreateArenaLine(arena.transform, "North Border", new Vector2(0f, ArenaHalfHeight), new Vector2(ArenaHalfWidth * 2f, 0.14f));
        CreateArenaLine(arena.transform, "South Border", new Vector2(0f, -ArenaHalfHeight), new Vector2(ArenaHalfWidth * 2f, 0.14f));
        CreateArenaLine(arena.transform, "East Border", new Vector2(ArenaHalfWidth, 0f), new Vector2(0.14f, ArenaHalfHeight * 2f));
        CreateArenaLine(arena.transform, "West Border", new Vector2(-ArenaHalfWidth, 0f), new Vector2(0.14f, ArenaHalfHeight * 2f));
    }

    private void CreateArenaLine(Transform parent, string name, Vector2 position, Vector2 size)
    {
        GameObject line = new GameObject(name);
        line.transform.SetParent(parent, false);
        line.transform.localPosition = position;
        PrototypeVisualFactory.EnsureSpriteRenderer(
            line,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.26f, 0.32f, 0.28f, 1f),
            size,
            -8);

        BoxCollider2D collider = line.AddComponent<BoxCollider2D>();
        collider.size = size;
    }

    private IEnumerator AutoAdvanceAfterDelay()
    {
        yield return new WaitForSeconds(1.25f);
        StartStage(currentStage + 1);
    }

    private void StartEvaluationStageTimer(int stageNumber)
    {
        if (evaluationStageRoutine != null)
        {
            StopCoroutine(evaluationStageRoutine);
        }

        evaluationStageRoutine = StartCoroutine(AdvanceEvaluationStageAfterDuration(stageNumber, EvaluationStageDuration(stageNumber)));
    }

    private IEnumerator AdvanceEvaluationStageAfterDuration(int stageNumber, float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, duration));
        evaluationStageRoutine = null;

        if (prototypeComplete ||
            prototypeFailed ||
            calibrationScenarioActive ||
            !stageRunning ||
            currentStage != stageNumber)
        {
            yield break;
        }

        FinishCurrentStage(false);
        StartStage(stageNumber + 1);
    }

    private bool IsEvaluationScenarioStageActive()
    {
        return useEvaluationScenarioStages &&
               stageRunning &&
               !calibrationScenarioActive &&
               currentStage >= 1 &&
               currentStage <= 3;
    }

    private float EvaluationStageDuration(int stageNumber)
    {
        switch (stageNumber)
        {
            case 1:
                return Mathf.Max(1f, evaluationStage1Seconds);
            case 2:
                return Mathf.Max(1f, evaluationStage2Seconds);
            case 3:
                return Mathf.Max(1f, evaluationStage3Seconds);
            default:
                return 0f;
        }
    }

    private IEnumerator ReleaseClearingFlag()
    {
        yield return null;
        isClearingStage = false;
    }

    private void CompletePrototype()
    {
        FinishCurrentStage(false);
        prototypeComplete = true;
        prototypeFailed = false;
        ClearEnemies();
        ResolveReferences();

        if (playerController != null)
        {
            playerController.ResetStats();
        }

        currentStage = 4;
        UpdateStageUI();
        ShowResultScreen();
        Debug.Log("All stages complete!");
    }

    private void StartStageTelemetry(int stageNumber)
    {
        stageRunning = true;
        stageStartTime = Time.time;
        currentStageData = new StageTelemetry
        {
            stageNumber = stageNumber
        };

        if (skipButton != null)
        {
            skipButton.interactable = true;
        }

        evaluationLogger?.BeginStage(stageNumber, EvaluationStageLabel(stageNumber));
    }

    private void FinishCurrentStage(bool skipped)
    {
        FinishCurrentStage(skipped, false);
    }

    private void FinishCurrentStage(bool skipped, bool failed)
    {
        if (!stageRunning || currentStageData == null)
        {
            return;
        }

        currentStageData.duration = Mathf.Max(0f, Time.time - stageStartTime);
        currentStageData.skipped = skipped;
        currentStageData.failed = failed;
        evaluationLogger?.EndStage(
            currentStageData.stageNumber,
            EvaluationStageLabel(currentStageData.stageNumber),
            skipped,
            failed,
            currentStageData.duration,
            currentStageData.buttonPresses,
            currentStageData.damageTaken,
            currentStageData.healingDone,
            currentStageData.AverageTouchDistance,
            playerController != null ? playerController.CurrentHP : 0,
            activeEnemies.Count);
        completedStageData.Add(currentStageData);
        currentStageData = null;
        stageRunning = false;

        if (skipButton != null)
        {
            skipButton.interactable = false;
        }

        UpdateStageUI();
    }

    private void ShowStartScreen()
    {
        prototypeComplete = false;
        prototypeFailed = false;
        stageRunning = false;
        currentStageData = null;
        SetScreenActive(resultScreenRoot, false);
        SetScreenActive(startScreenRoot, true);

        if (skipButton != null)
        {
            skipButton.interactable = false;
        }

        UpdateStageUI();
    }

    private void ShowResultScreen()
    {
        SetScreenActive(startScreenRoot, false);
        SetScreenActive(resultScreenRoot, true);

        if (skipButton != null)
        {
            skipButton.interactable = false;
        }

        if (resultText != null)
        {
            resultText.text = BuildResultsText();
        }
    }

    private void SetScreenActive(GameObject screenRoot, bool active)
    {
        if (screenRoot != null)
        {
            screenRoot.SetActive(active);
        }
    }

    private string BuildResultsText()
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(prototypeFailed ? "Game Over" : "Final Results");
        if (prototypeFailed)
        {
            builder.AppendLine($"Defeated on Stage {currentStage}");
        }

        int totalButtons = 0;
        int totalDamage = 0;
        int totalHealing = 0;
        float totalTime = 0f;
        float totalDistance = 0f;
        int totalDistanceSamples = 0;

        for (int i = 0; i < completedStageData.Count; i++)
        {
            StageTelemetry data = completedStageData[i];
            totalButtons += data.buttonPresses;
            totalDamage += data.damageTaken;
            totalHealing += data.healingDone;
            totalTime += data.duration;
            totalDistance += data.totalTouchDistance;
            totalDistanceSamples += data.touchDistanceSamples;

            string statusText = data.failed ? " (failed)" : data.skipped ? " (skipped)" : string.Empty;
            builder.AppendLine();
            builder.AppendLine($"Stage {data.stageNumber}{statusText}");
            builder.AppendLine($"Button presses: {data.buttonPresses}");
            builder.AppendLine($"Damage taken: {data.damageTaken}");
            builder.AppendLine($"Healed: {data.healingDone}");
            builder.AppendLine($"Stage time: {data.duration:F1}s");
            builder.AppendLine($"Avg touch error: {data.AverageTouchDistance:F1}px");
        }

        float totalAverageDistance = totalDistanceSamples <= 0 ? 0f : totalDistance / totalDistanceSamples;
        builder.AppendLine();
        builder.AppendLine("Total");
        builder.AppendLine($"Button presses: {totalButtons}");
        builder.AppendLine($"Damage taken: {totalDamage}");
        builder.AppendLine($"Healed: {totalHealing}");
        builder.AppendLine($"Time: {totalTime:F1}s");
        builder.AppendLine($"Avg touch error: {totalAverageDistance:F1}px");
        return builder.ToString();
    }

    private void UpdateStageUI()
    {
        if (stageText == null)
        {
            return;
        }

        if (prototypeComplete || currentStage > 3)
        {
            stageText.text = "Prototype Clear";
            return;
        }

        if (prototypeFailed)
        {
            stageText.text = "Game Over";
            return;
        }

        if (!stageRunning)
        {
            stageText.text = "Ready";
            return;
        }

        string evaluationLabel = useEvaluationScenarioStages ? $" - {EvaluationStageLabel(currentStage)}" : string.Empty;
        string timerLabel = IsEvaluationScenarioStageActive()
            ? $" - Max {Mathf.CeilToInt(Mathf.Max(0f, EvaluationStageDuration(currentStage) - (Time.time - stageStartTime)))}s"
            : string.Empty;
        stageText.text = $"Stage {currentStage} / 3{evaluationLabel}{timerLabel} - Enemies {activeEnemies.Count}";
    }

    private static string EvaluationStageLabel(int stageNumber)
    {
        switch (stageNumber)
        {
            case 1:
                return "Attack Window";
            case 2:
                return "Move Pressure";
            case 3:
                return "Boss Low HP Threat";
            default:
                return "Evaluation";
        }
    }
}
