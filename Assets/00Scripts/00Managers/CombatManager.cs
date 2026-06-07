using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;

    [Header("Prototype References")]
    public RoguelikeGameManager gameManager;
    public PlayerController playerController;
    public CombatActionPriorBuilder priorBuilder;

    [Header("UI Text Elements")]
    public TextMeshProUGUI stateText;
    public TextMeshProUGUI playerHpText;
    public TextMeshProUGUI enemyHpText;
    public TextMeshProUGUI feedbackLogText;
    public TextMeshProUGUI priorText;
    public TextMeshProUGUI contextText;

    [Header("Bayesian Prior Tuning")]
    public float baseAttackScore = 0.85f;
    public float baseDodgeScore = 0.45f;
    public float closeEnemyRadius = 3f;
    public float projectileNearRadius = 0.95f;
    public float projectileLookAheadSeconds = 0.75f;

    [Header("SampleScene Compatibility")]
    public bool useLegacyStatePriorFallback = true;

    public enum CombatState
    {
        Safe,
        Telegraph,
        Attacking
    }

    public CombatState currentState = CombatState.Safe;

    [HideInInspector] public float priorAttack = 0.5f;
    [HideInInspector] public float priorDodge = 0.5f;

    public CombatContext CurrentContext { get; private set; }
    public CombatActionPriorResult CurrentPriorResult { get; private set; }

    private float feedbackClearTime;
    private SimulatedEnemy legacyStateEnemy;

    public struct CombatContext
    {
        public int totalEnemies;
        public int meleeEnemies;
        public int rangedEnemies;
        public int bossEnemies;
        public int closeEnemies;
        public int telegraphingEnemies;
        public int attackingEnemies;
        public int incomingProjectiles;
        public EnemyKind closestEnemyKind;
        public string closestEnemyName;
        public float closestEnemyDistance;
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
        UpdatePlayerHpUI();
    }

    private void Update()
    {
        ResolveReferences();
        EvaluateCombatContext();
        UpdateCombatUI();
    }

    public void OnPlayerAttack()
    {
        ResolveReferences();

        if (playerController == null)
        {
            if (IsLegacyStatePriorActive())
            {
                ReportFeedback("Attack accepted (SampleScene state prototype).", Color.green);
                return;
            }

            ReportFeedback("No player found.", Color.red);
            return;
        }

        EnemyControllerBase target = FindBestAttackTarget(playerController.attackRange + 0.45f);
        if (target == null)
        {
            ReportFeedback("Attack missed: no enemy in reach.", Color.gray);
            return;
        }

        bool assistedRange = target.DistanceToPlayer > playerController.attackRange;
        int damage = playerController.attackDamage;
        target.TakeDamage(damage);

        string assistText = assistedRange ? " (range assist)" : string.Empty;
        ReportFeedback($"Attack hit {target.displayName}{assistText}.", Color.green);
    }

    public void OnPlayerDodge()
    {
        ResolveReferences();

        if (playerController == null)
        {
            if (IsLegacyStatePriorActive())
            {
                ReportFeedback("Dodge accepted (SampleScene state prototype).", Color.cyan);
                return;
            }

            ReportFeedback("No player found.", Color.red);
            return;
        }

        Vector2 direction = CalculateDodgeDirection();
        playerController.PerformDodge(direction);
        ReportFeedback("Dodge accepted.", Color.cyan);
    }

    public void ReportFeedback(string message, Color color)
    {
        if (feedbackLogText != null)
        {
            feedbackLogText.text = message;
            feedbackLogText.color = color;
            feedbackClearTime = Time.time + 2f;
        }

        Debug.Log(message);
    }

    private void ResolveReferences()
    {
        if (gameManager == null)
        {
            gameManager = RoguelikeGameManager.Instance;
        }

        if (playerController == null)
        {
            playerController = gameManager != null ? gameManager.PlayerController : FindAnyObjectByType<PlayerController>();
        }
    }

    private void EvaluateCombatContext()
    {
        CombatContext context = new CombatContext
        {
            closestEnemyDistance = float.MaxValue,
            closestEnemyName = "None"
        };

        float playerAttackRange = playerController != null ? playerController.attackRange : 2f;

        if (gameManager != null)
        {
            IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                AddEnemyToContext(enemy, ref context);
            }
        }
        else
        {
            EnemyControllerBase[] enemies = FindObjectsByType<EnemyControllerBase>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                AddEnemyToContext(enemy, ref context);
            }
        }

        context.incomingProjectiles = CountIncomingProjectiles();

        CurrentContext = context;
        bool useStateFallback = ShouldUseLegacyStatePriorFallback(context);
        CurrentPriorResult = useStateFallback
            ? ResolvePriorBuilder().BuildStatePrior(currentState)
            : ResolvePriorBuilder().Build(context, playerController, playerAttackRange);
        priorAttack = CurrentPriorResult.attackPrior;
        priorDodge = CurrentPriorResult.dodgePrior;

        if (!useStateFallback)
        {
            currentState = MapEnemyState(CurrentPriorResult.enemyState);
        }
    }

    private void AddEnemyToContext(EnemyControllerBase enemy, ref CombatContext context)
    {
        context.totalEnemies++;

        switch (enemy.enemyKind)
        {
            case EnemyKind.Melee:
                context.meleeEnemies++;
                break;
            case EnemyKind.Ranged:
                context.rangedEnemies++;
                break;
            case EnemyKind.Boss:
                context.bossEnemies++;
                break;
        }

        if (enemy.DistanceToPlayer <= closeEnemyRadius)
        {
            context.closeEnemies++;
        }

        if (enemy.IsTelegraphing)
        {
            context.telegraphingEnemies++;
        }

        if (enemy.IsAttacking)
        {
            context.attackingEnemies++;
        }

        if (enemy.DistanceToPlayer < context.closestEnemyDistance)
        {
            context.closestEnemyDistance = enemy.DistanceToPlayer;
            context.closestEnemyKind = enemy.enemyKind;
            context.closestEnemyName = enemy.displayName;
        }
    }

    private CombatActionPriorBuilder ResolvePriorBuilder()
    {
        if (priorBuilder == null)
        {
            priorBuilder = GetComponent<CombatActionPriorBuilder>();
        }

        if (priorBuilder == null)
        {
            priorBuilder = gameObject.AddComponent<CombatActionPriorBuilder>();
            priorBuilder.baseAttackScore = baseAttackScore;
            priorBuilder.baseDodgeScore = baseDodgeScore;
        }

        return priorBuilder;
    }

    private CombatState MapEnemyState(ADUIEnemyState state)
    {
        switch (state)
        {
            case ADUIEnemyState.Attacking:
            case ADUIEnemyState.Urgent:
                return CombatState.Attacking;
            case ADUIEnemyState.Telegraph:
                return CombatState.Telegraph;
            default:
                return CombatState.Safe;
        }
    }

    private bool ShouldUseLegacyStatePriorFallback(CombatContext context)
    {
        return useLegacyStatePriorFallback &&
               context.totalEnemies == 0 &&
               context.incomingProjectiles == 0 &&
               HasLegacyStateEnemy();
    }

    private bool HasLegacyStateEnemy()
    {
        if (legacyStateEnemy == null)
        {
            legacyStateEnemy = FindAnyObjectByType<SimulatedEnemy>();
        }

        return legacyStateEnemy != null;
    }

    private bool IsLegacyStatePriorActive()
    {
        return CurrentPriorResult.source == "sample_scene_state_prior" && HasLegacyStateEnemy();
    }

    private int CountIncomingProjectiles()
    {
        if (playerController == null)
        {
            return 0;
        }

        int count = 0;
        Vector2 playerPosition = playerController.transform.position;
        IReadOnlyList<EnemyProjectile> projectiles = EnemyProjectile.ActiveProjectiles;

        for (int i = 0; i < projectiles.Count; i++)
        {
            EnemyProjectile projectile = projectiles[i];
            if (projectile != null && projectile.IsThreatening(playerPosition, projectileNearRadius, projectileLookAheadSeconds))
            {
                count++;
            }
        }

        return count;
    }

    private EnemyControllerBase FindBestAttackTarget(float maxRange)
    {
        if (gameManager == null)
        {
            return null;
        }

        EnemyControllerBase best = null;
        float bestDistance = float.MaxValue;
        IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyControllerBase enemy = enemies[i];
            if (enemy == null || !enemy.IsAlive || enemy.DistanceToPlayer > maxRange)
            {
                continue;
            }

            if (enemy.DistanceToPlayer < bestDistance)
            {
                best = enemy;
                bestDistance = enemy.DistanceToPlayer;
            }
        }

        return best;
    }

    private Vector2 CalculateDodgeDirection()
    {
        Vector2 result = Vector2.zero;

        if (playerController == null)
        {
            return Vector2.right;
        }

        Vector2 playerPosition = playerController.transform.position;
        EnemyControllerBase mostDangerousEnemy = FindMostDangerousEnemy();
        if (mostDangerousEnemy != null)
        {
            Vector2 awayFromEnemy = playerPosition - (Vector2)mostDangerousEnemy.transform.position;
            result += awayFromEnemy.sqrMagnitude > 0.001f ? awayFromEnemy.normalized * 2f : Vector2.right;
        }

        IReadOnlyList<EnemyProjectile> projectiles = EnemyProjectile.ActiveProjectiles;
        for (int i = 0; i < projectiles.Count; i++)
        {
            EnemyProjectile projectile = projectiles[i];
            if (projectile == null || !projectile.IsThreatening(playerPosition, projectileNearRadius * 1.4f, projectileLookAheadSeconds))
            {
                continue;
            }

            Vector2 awayFromProjectile = playerPosition - (Vector2)projectile.transform.position;
            if (awayFromProjectile.sqrMagnitude > 0.001f)
            {
                result += awayFromProjectile.normalized * 1.5f;
            }
        }

        if (result.sqrMagnitude < 0.001f && CurrentContext.totalEnemies > 0)
        {
            EnemyControllerBase closest = FindClosestEnemy();
            if (closest != null)
            {
                result = playerPosition - (Vector2)closest.transform.position;
            }
        }

        return result.sqrMagnitude > 0.001f ? result.normalized : Vector2.right;
    }

    private EnemyControllerBase FindMostDangerousEnemy()
    {
        if (gameManager == null)
        {
            return null;
        }

        EnemyControllerBase best = null;
        float bestScore = 0f;
        IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyControllerBase enemy = enemies[i];
            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            float score = enemy.GetDodgeThreatScore();
            if (score > bestScore)
            {
                bestScore = score;
                best = enemy;
            }
        }

        return best;
    }

    private EnemyControllerBase FindClosestEnemy()
    {
        if (gameManager == null)
        {
            return null;
        }

        EnemyControllerBase best = null;
        float bestDistance = float.MaxValue;
        IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;

        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyControllerBase enemy = enemies[i];
            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            if (enemy.DistanceToPlayer < bestDistance)
            {
                best = enemy;
                bestDistance = enemy.DistanceToPlayer;
            }
        }

        return best;
    }

    private void UpdateCombatUI()
    {
        UpdatePlayerHpUI();

        if (stateText != null)
        {
            switch (currentState)
            {
                case CombatState.Attacking:
                    stateText.text = "Context: Incoming Attack";
                    stateText.color = Color.red;
                    break;
                case CombatState.Telegraph:
                    stateText.text = "Context: Telegraph / High Dodge Prior";
                    stateText.color = Color.yellow;
                    break;
                default:
                    stateText.text = "Context: Attack Opportunity";
                    stateText.color = Color.white;
                    break;
            }
        }

        if (enemyHpText != null)
        {
            enemyHpText.text = IsLegacyStatePriorActive()
                ? $"Enemy: SimulatedEnemy   State: {currentState}"
                : $"Enemies {CurrentContext.totalEnemies}  M:{CurrentContext.meleeEnemies} R:{CurrentContext.rangedEnemies} B:{CurrentContext.bossEnemies}";
        }

        if (priorText != null)
        {
            priorText.text = $"P(Attack) {priorAttack:P0}   P(Dodge) {priorDodge:P0}";
        }

        if (contextText != null)
        {
            if (IsLegacyStatePriorActive())
            {
                contextText.text = "Source: sample_scene_state_prior   CombatContext: unavailable in SampleScene";
            }
            else
            {
                string closestText = CurrentContext.totalEnemies > 0
                    ? $"{CurrentContext.closestEnemyName} {CurrentContext.closestEnemyDistance:F1}m"
                    : "None";

                contextText.text =
                    $"Closest: {closestText}   Close: {CurrentContext.closeEnemies}   Telegraphs: {CurrentContext.telegraphingEnemies}   Arrows: {CurrentContext.incomingProjectiles}";
            }
        }

        if (feedbackLogText != null && feedbackLogText.text.Length > 0 && Time.time > feedbackClearTime)
        {
            feedbackLogText.text = string.Empty;
        }
    }

    private void UpdatePlayerHpUI()
    {
        if (playerHpText != null && IsLegacyStatePriorActive())
        {
            playerHpText.text = "Player HP: SampleScene prototype";
        }
        else if (playerHpText != null && playerController != null)
        {
            playerHpText.text = $"Player HP: {playerController.CurrentHP} / {playerController.maxHP}";
        }
    }
}
