using System.Collections;
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
    public float baseHealScore = 0.08f;
    public float baseWhirlwindScore = 0.12f;
    public float closeEnemyRadius = 3f;
    public float projectileNearRadius = 0.95f;
    public float projectileLookAheadSeconds = 1.15f;
    public float preDodgeAttackReadyWindow = 0.65f;
    [Range(-1f, 1f)] public float movementDangerDotThreshold = 0.35f;

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
    [HideInInspector] public float priorHeal = 0.05f;
    [HideInInspector] public float priorWhirlwind = 0.05f;

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
        public int projectileThreats;
        public int enemiesInAttackRange;
        public int safeAttackTargets;
        public int attackCommitTargets;
        public int preDodgeEnemies;
        public int movingTowardDangerEnemies;
        public int dangerousCloseEnemies;
        public int immediateThreats;
        public int whirlwindTargets;
        public EnemyKind closestEnemyKind;
        public string closestEnemyName;
        public float closestEnemyDistance;
        public float attackOpportunityScore;
        public float dodgeUrgencyScore;
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

    public void ForceRefreshCombatContext()
    {
        ResolveReferences();
        EvaluateCombatContext();
        UpdateCombatUI();
    }

    public bool OnPlayerAttack()
    {
        ResolveReferences();

        if (playerController == null)
        {
            if (IsLegacyStatePriorActive())
            {
                ReportFeedback("Attack accepted (SampleScene state prototype).", Color.green);
                return true;
            }

            ReportFeedback("No player found.", Color.red);
            return false;
        }

        EnemyControllerBase target = FindBestAttackTarget(playerController.attackRange + 0.45f);
        if (target == null)
        {
            playerController.PlayAttackVisual();
            ReportFeedback("Attack missed: no enemy in reach.", Color.gray);
            return false;
        }

        playerController.PlayAttackVisual(target.transform.position);
        bool assistedRange = target.DistanceToPlayer > playerController.attackRange;
        int damage = playerController.attackDamage;
        target.TakeDamage(damage);

        string assistText = assistedRange ? " (range assist)" : string.Empty;
        ReportFeedback($"Attack hit {target.displayName}{assistText}.", Color.green);
        return true;
    }

    public bool OnPlayerDodge()
    {
        ResolveReferences();

        if (playerController == null)
        {
            if (IsLegacyStatePriorActive())
            {
                ReportFeedback("Dodge accepted (SampleScene state prototype).", Color.cyan);
                return true;
            }

            ReportFeedback("No player found.", Color.red);
            return false;
        }

        Vector2 direction = CalculateDodgeDirection();
        playerController.PerformDodge(direction);
        ReportFeedback("Dodge accepted.", Color.cyan);
        return true;
    }

    public bool OnPlayerHeal()
    {
        ResolveReferences();

        if (playerController == null)
        {
            ReportFeedback("No player found.", Color.red);
            return false;
        }

        if (playerController.HealCooldownRemaining > 0f)
        {
            ReportFeedback($"Heal cooldown: {playerController.HealCooldownRemaining:F1}s.", Color.gray);
            return false;
        }

        if (playerController.CurrentHP >= playerController.maxHP)
        {
            ReportFeedback("Heal skipped: HP is already full.", Color.gray);
            return false;
        }

        if (playerController.TryHeal(out int amountHealed))
        {
            ReportFeedback($"Healed {amountHealed} HP.", Color.green);
            return true;
        }

        return false;
    }

    public bool OnPlayerWhirlwind()
    {
        ResolveReferences();

        if (playerController == null)
        {
            ReportFeedback("No player found.", Color.red);
            return false;
        }

        if (playerController.WhirlwindCooldownRemaining > 0f)
        {
            ReportFeedback($"Whirlwind cooldown: {playerController.WhirlwindCooldownRemaining:F1}s.", Color.gray);
            return false;
        }

        List<EnemyControllerBase> targets = FindWhirlwindTargets();
        if (targets.Count == 0)
        {
            ReportFeedback("Whirlwind missed: no nearby enemies.", Color.gray);
            return false;
        }

        if (!playerController.TryStartWhirlwindCooldown())
        {
            ReportFeedback($"Whirlwind cooldown: {playerController.WhirlwindCooldownRemaining:F1}s.", Color.gray);
            return false;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null)
            {
                targets[i].TakeDamage(playerController.whirlwindDamage);
            }
        }

        StartCoroutine(ShowWhirlwindVisual(playerController.transform.position, playerController.whirlwindRange));
        ReportFeedback($"Whirlwind hit {targets.Count} enemies.", new Color(1f, 0.82f, 0.18f, 1f));
        return true;
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

        float attackScore = baseAttackScore;
        float dodgeScore = baseDodgeScore;
        float healScore = baseHealScore;
        float whirlwindScore = baseWhirlwindScore;
        float playerAttackRange = playerController != null ? playerController.attackRange : 2f;
        float playerWhirlwindRange = playerController != null ? playerController.whirlwindRange : 3f;

        int managerEnemyCount = 0;
        if (gameManager != null && gameManager.ActiveEnemies != null && gameManager.ActiveEnemies.Count > 0)
        {
            IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (!CanUseRegisteredEnemyForContext(enemy))
                {
                    continue;
                }

                AddEnemyToContext(enemy, ref context);
                managerEnemyCount++;

                if (playerController != null && enemy.DistanceToPlayer <= playerWhirlwindRange)
                {
                    context.whirlwindTargets++;
                }
            }
        }

        if (managerEnemyCount == 0)
        {
            EnemyControllerBase[] enemies = FindObjectsByType<EnemyControllerBase>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (!CanUseEnemyForContext(enemy))
                {
                    continue;
                }

                AddEnemyToContext(enemy, ref context);

                if (playerController != null && enemy.DistanceToPlayer <= playerWhirlwindRange)
                {
                    context.whirlwindTargets++;
                }
            }
        }

        context.incomingProjectiles = CountIncomingProjectiles();
        context.projectileThreats = context.incomingProjectiles;
        context.immediateThreats += context.projectileThreats;
        context.dodgeUrgencyScore += context.projectileThreats * 3f;

        if (playerController != null && playerController.CurrentHP <= playerController.maxHP * 0.35f)
        {
            dodgeScore += 0.75f;
        }

        bool useStateFallback = ShouldUseLegacyStatePriorFallback(context);
        if (useStateFallback)
        {
            CurrentPriorResult = ResolvePriorBuilder().BuildStatePrior(currentState);
        }
        else
        {
            CurrentPriorResult = ResolvePriorBuilder().Build(context, playerController, playerAttackRange);
        }

        attackScore = CurrentPriorResult.attackScore;
        dodgeScore = CurrentPriorResult.dodgeScore;

        ApplySkillScores(context, ref healScore, ref whirlwindScore);

        if (context.totalEnemies == 0)
        {
            attackScore = 0.5f;
            dodgeScore = 0.5f;
            whirlwindScore = 0.01f;
        }

        NormalizePriors(attackScore, dodgeScore, healScore, whirlwindScore);

        if (!useStateFallback)
        {
            currentState = MapEnemyState(CurrentPriorResult.enemyState);
        }

        CurrentContext = context;
    }

    private void AddEnemyToContext(EnemyControllerBase enemy, ref CombatContext context)
    {
        enemy.RefreshDistanceToPlayer();
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

        float playerAttackRange = playerController != null ? playerController.attackRange : 2f;
        bool inPlayerAttackReach = enemy.DistanceToPlayer <= playerAttackRange + 0.45f;
        bool enemyThreateningNow = enemy.IsTelegraphing || enemy.IsAttacking;
        bool enemyCanAttackSoon = enemy.CanStartAttackSoon(preDodgeAttackReadyWindow);
        bool meleeBodyPressure = enemy.enemyKind != EnemyKind.Ranged &&
                                  enemy.DistanceToPlayer <= enemy.attackRange + 0.25f;
        bool preDodgeDistance = enemy.enemyKind != EnemyKind.Ranged &&
                                enemy.DistanceToPlayer <= enemy.attackRange + 0.85f;
        bool movingTowardDanger = IsPlayerMovingTowardEnemy(enemy, playerAttackRange + 1.25f);
        bool preDodgeCandidate = !enemyThreateningNow &&
                                 preDodgeDistance &&
                                 (enemyCanAttackSoon || movingTowardDanger || meleeBodyPressure);
        bool attackCommitCandidate = inPlayerAttackReach &&
                                     !enemyThreateningNow &&
                                     !preDodgeCandidate &&
                                     !meleeBodyPressure;
        bool dangerouslyClose = meleeBodyPressure || enemyThreateningNow;

        if (inPlayerAttackReach)
        {
            context.enemiesInAttackRange++;
        }

        if (dangerouslyClose)
        {
            context.dangerousCloseEnemies++;
        }

        if (movingTowardDanger)
        {
            context.movingTowardDangerEnemies++;
        }

        if (preDodgeCandidate)
        {
            context.preDodgeEnemies++;
        }

        if (enemyThreateningNow)
        {
            context.immediateThreats++;
        }

        if (attackCommitCandidate)
        {
            context.safeAttackTargets++;
            context.attackCommitTargets++;
        }

        context.attackOpportunityScore += enemy.GetAttackOpportunityScore(playerAttackRange);
        context.dodgeUrgencyScore += enemy.GetDodgeThreatScore();
        if (preDodgeCandidate)
        {
            context.dodgeUrgencyScore += enemyCanAttackSoon ? 0.85f : 0.45f;
        }

        if (movingTowardDanger)
        {
            context.dodgeUrgencyScore += 0.35f;
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

    private bool IsPlayerMovingTowardEnemy(EnemyControllerBase enemy, float checkRange)
    {
        if (enemy == null || playerController == null || !playerController.IsMoving)
        {
            return false;
        }

        if (enemy.DistanceToPlayer > Mathf.Max(0.1f, checkRange))
        {
            return false;
        }

        Vector2 moveInput = playerController.MoveInput;
        if (moveInput.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        Vector2 toEnemy = (Vector2)enemy.transform.position - (Vector2)playerController.transform.position;
        if (toEnemy.sqrMagnitude <= 0.001f)
        {
            return true;
        }

        return Vector2.Dot(moveInput.normalized, toEnemy.normalized) >= movementDangerDotThreshold;
    }

    private static bool CanUseEnemyForContext(EnemyControllerBase enemy)
    {
        return enemy != null && enemy.gameObject.activeInHierarchy && (enemy.IsAlive || enemy.IsCalibrationScenarioEnemy);
    }

    private static bool CanUseRegisteredEnemyForContext(EnemyControllerBase enemy)
    {
        return enemy != null && enemy.gameObject.activeInHierarchy;
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

    private void ApplySkillScores(CombatContext context, ref float healScore, ref float whirlwindScore)
    {
        if (playerController == null)
        {
            healScore = 0.01f;
            whirlwindScore = 0.01f;
            return;
        }

        if (playerController.CanHeal)
        {
            float missingRatio = playerController.MissingHpRatio;
            healScore += Mathf.Lerp(0f, 4.2f, missingRatio);

            if (playerController.CurrentHP <= playerController.maxHP * 0.35f)
            {
                healScore += 1.5f;
            }

            if (currentState == CombatState.Attacking)
            {
                healScore += 0.35f;
            }
        }
        else
        {
            healScore = 0.01f;
        }

        if (playerController.CanWhirlwind && context.whirlwindTargets > 0)
        {
            whirlwindScore += context.whirlwindTargets * 1.15f;

            if (context.whirlwindTargets >= 3)
            {
                whirlwindScore += 1.4f;
            }

            if (context.closeEnemies >= 5)
            {
                whirlwindScore += 0.8f;
            }
        }
        else
        {
            whirlwindScore = 0.01f;
        }
    }

    private void NormalizePriors(float attackScore, float dodgeScore, float healScore, float whirlwindScore)
    {
        attackScore = Mathf.Max(0.001f, attackScore);
        dodgeScore = Mathf.Max(0.001f, dodgeScore);
        healScore = Mathf.Max(0.001f, healScore);
        whirlwindScore = Mathf.Max(0.001f, whirlwindScore);

        float totalScore = Mathf.Max(0.001f, attackScore + dodgeScore + healScore + whirlwindScore);
        priorAttack = Mathf.Clamp01(attackScore / totalScore);
        priorDodge = Mathf.Clamp01(dodgeScore / totalScore);
        priorHeal = Mathf.Clamp01(healScore / totalScore);
        priorWhirlwind = Mathf.Clamp01(whirlwindScore / totalScore);
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

    private List<EnemyControllerBase> FindWhirlwindTargets()
    {
        List<EnemyControllerBase> targets = new List<EnemyControllerBase>();

        if (gameManager == null || playerController == null)
        {
            return targets;
        }

        IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyControllerBase enemy = enemies[i];
            if (enemy == null || !enemy.IsAlive)
            {
                continue;
            }

            if (enemy.DistanceToPlayer <= playerController.whirlwindRange)
            {
                targets.Add(enemy);
            }
        }

        return targets;
    }

    private IEnumerator ShowWhirlwindVisual(Vector2 position, float radius)
    {
        GameObject visual = PrototypeVisualFactory.CreateTelegraphCircle(
            "Whirlwind Slash",
            position,
            radius,
            new Color(1f, 0.82f, 0.18f, 0.34f));

        yield return new WaitForSeconds(0.22f);

        if (visual != null)
        {
            Destroy(visual);
        }
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
            priorText.text =
                $"P(A) {priorAttack:P0}   P(D) {priorDodge:P0}   P(H) {priorHeal:P0}   P(W) {priorWhirlwind:P0}";
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

                string skillText = playerController != null
                    ? $"HealCD {playerController.HealCooldownRemaining:F1}s   WhirlCD {playerController.WhirlwindCooldownRemaining:F1}s"
                    : "Skills unavailable";

                contextText.text =
                    $"Closest: {closestText}   Close: {CurrentContext.closeEnemies}   Commit: {CurrentContext.attackCommitTargets}   PreDodge: {CurrentContext.preDodgeEnemies}   MoveDanger: {CurrentContext.movingTowardDangerEnemies}   Danger: {CurrentContext.dangerousCloseEnemies}   Threat: {CurrentContext.immediateThreats}   Arrows: {CurrentContext.projectileThreats}   Whirl targets: {CurrentContext.whirlwindTargets}   {skillText}";
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
