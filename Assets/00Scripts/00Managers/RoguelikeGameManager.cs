using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RoguelikeGameManager : MonoBehaviour
{
    public static RoguelikeGameManager Instance;

    [Header("Prefabs")]
    public GameObject meleeEnemyPrefab;
    public GameObject rangedEnemyPrefab;
    public GameObject bossEnemyPrefab;

    [Header("Player & Spawning")]
    public Transform playerTransform;
    public PlayerController playerController;
    public Transform enemyRoot;
    public float spawnRadius = 7.5f;
    public float spawnRadiusJitter = 1.2f;
    public bool startStageOnPlay = true;
    public int startingStage = 1;

    [Header("UI")]
    public Button skipButton;
    public TextMeshProUGUI stageText;

    public int CurrentStage => currentStage;
    public IReadOnlyList<EnemyControllerBase> ActiveEnemies => activeEnemies;
    public Transform PlayerTransform => playerTransform;
    public PlayerController PlayerController => playerController;

    private readonly List<EnemyControllerBase> activeEnemies = new List<EnemyControllerBase>();
    private int currentStage = 1;
    private bool isClearingStage;
    private bool prototypeComplete;
    private Coroutine autoAdvanceRoutine;
    private GameObject runtimePrefabRoot;

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

        if (startStageOnPlay)
        {
            StartStage(startingStage);
        }
    }

    public void StartStage(int stageNumber)
    {
        if (stageNumber > 3)
        {
            CompletePrototype();
            return;
        }

        prototypeComplete = false;
        ClearEnemies();
        currentStage = stageNumber;
        ResolveReferences();

        if (playerController != null)
        {
            playerController.ResetStats();
        }

        Debug.Log($"Starting Stage {currentStage}");

        switch (currentStage)
        {
            case 1:
                SpawnEnemies(meleeEnemyPrefab, Random.Range(10, 21), EnemyKind.Melee);
                break;
            case 2:
                SpawnEnemies(meleeEnemyPrefab, 10, EnemyKind.Melee);
                SpawnEnemies(rangedEnemyPrefab, 5, EnemyKind.Ranged);
                break;
            case 3:
                SpawnEnemies(bossEnemyPrefab, 1, EnemyKind.Boss);
                SpawnEnemies(meleeEnemyPrefab, 3, EnemyKind.Melee);
                SpawnEnemies(rangedEnemyPrefab, 3, EnemyKind.Ranged);
                break;
        }

        UpdateStageUI();
    }

    public void SkipStage()
    {
        Debug.Log("Stage Skipped!");
        StartStage(currentStage + 1);
    }

    public void NotifyEnemyDefeated(EnemyControllerBase enemy)
    {
        if (enemy == null)
        {
            return;
        }

        activeEnemies.Remove(enemy);
        UpdateStageUI();

        if (!isClearingStage && activeEnemies.Count == 0 && !prototypeComplete)
        {
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

    private void ClearEnemies()
    {
        isClearingStage = true;

        if (autoAdvanceRoutine != null)
        {
            StopCoroutine(autoAdvanceRoutine);
            autoAdvanceRoutine = null;
        }

        foreach (EnemyControllerBase enemy in activeEnemies)
        {
            if (enemy != null)
            {
                Destroy(enemy.gameObject);
            }
        }

        activeEnemies.Clear();
        EnemyProjectile.DestroyAllProjectiles();
        UpdateStageUI();
        StartCoroutine(ReleaseClearingFlag());
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
        GameObject enemy = CreateEnemyPrototypeObject("Prototype Sword Enemy", new Color(0.95f, 0.38f, 0.28f, 1f), Vector2.one * 0.9f);
        MeleeEnemyController controller = enemy.AddComponent<MeleeEnemyController>();
        controller.maxHP = 35;
        controller.moveSpeed = 2.9f;
        controller.attackRange = 1.55f;
        controller.attackCooldown = 1.55f;
        controller.telegraphDuration = 0.42f;
        controller.attackFrameDuration = 0.16f;
        controller.attackDamage = 9;
        controller.normalColor = new Color(0.95f, 0.38f, 0.28f, 1f);
        controller.telegraphColor = new Color(1f, 0.75f, 0.25f, 1f);
        controller.attackColor = new Color(1f, 0.08f, 0.08f, 1f);
        return enemy;
    }

    private GameObject CreateRangedPrototype()
    {
        GameObject enemy = CreateEnemyPrototypeObject("Prototype Bow Enemy", new Color(0.45f, 0.64f, 1f, 1f), Vector2.one * 0.86f);
        RangedEnemyController controller = enemy.AddComponent<RangedEnemyController>();
        controller.maxHP = 28;
        controller.moveSpeed = 2.05f;
        controller.attackRange = 6.8f;
        controller.shootingRange = 7f;
        controller.keepAwayDistance = 3.2f;
        controller.attackCooldown = 2.4f;
        controller.telegraphDuration = 0.62f;
        controller.attackFrameDuration = 0.12f;
        controller.attackDamage = 8;
        controller.normalColor = new Color(0.45f, 0.64f, 1f, 1f);
        controller.telegraphColor = new Color(1f, 0.66f, 0.2f, 1f);
        controller.attackColor = new Color(1f, 0.1f, 0.1f, 1f);
        return enemy;
    }

    private GameObject CreateBossPrototype()
    {
        GameObject enemy = CreateEnemyPrototypeObject("Prototype Boss Enemy", new Color(0.55f, 0.18f, 0.72f, 1f), Vector2.one * 1.75f);
        CircleCollider2D collider = enemy.GetComponent<CircleCollider2D>();
        if (collider != null)
        {
            collider.radius = 0.7f;
        }

        BossController controller = enemy.AddComponent<BossController>();
        controller.maxHP = 360;
        controller.moveSpeed = 1.35f;
        controller.attackRange = 3f;
        controller.attackCooldown = 2.65f;
        controller.telegraphDuration = 1.25f;
        controller.attackFrameDuration = 0.22f;
        controller.attackDamage = 24;
        controller.normalColor = new Color(0.55f, 0.18f, 0.72f, 1f);
        controller.telegraphColor = new Color(1f, 0.55f, 0.1f, 1f);
        controller.attackColor = new Color(1f, 0.05f, 0.05f, 1f);
        return enemy;
    }

    private GameObject CreateEnemyPrototypeObject(string name, Color color, Vector2 size)
    {
        GameObject enemy = new GameObject(name);
        enemy.transform.SetParent(runtimePrefabRoot.transform, false);
        enemy.SetActive(false);

        PrototypeVisualFactory.EnsureSpriteRenderer(enemy, PrototypeVisualFactory.CircleSprite, color, size, 2);

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

        CreateArenaLine(arena.transform, "North Border", new Vector2(0f, 10f), new Vector2(20f, 0.14f));
        CreateArenaLine(arena.transform, "South Border", new Vector2(0f, -10f), new Vector2(20f, 0.14f));
        CreateArenaLine(arena.transform, "East Border", new Vector2(10f, 0f), new Vector2(0.14f, 20f));
        CreateArenaLine(arena.transform, "West Border", new Vector2(-10f, 0f), new Vector2(0.14f, 20f));
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
    }

    private IEnumerator AutoAdvanceAfterDelay()
    {
        yield return new WaitForSeconds(1.25f);
        StartStage(currentStage + 1);
    }

    private IEnumerator ReleaseClearingFlag()
    {
        yield return null;
        isClearingStage = false;
    }

    private void CompletePrototype()
    {
        prototypeComplete = true;
        ClearEnemies();
        ResolveReferences();

        if (playerController != null)
        {
            playerController.ResetStats();
        }

        currentStage = 4;
        UpdateStageUI();
        Debug.Log("All stages complete!");
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

        stageText.text = $"Stage {currentStage} / 3 - Enemies {activeEnemies.Count}";
    }
}
