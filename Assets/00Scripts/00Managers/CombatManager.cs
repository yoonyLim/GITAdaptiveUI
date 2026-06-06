using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;

    [Header("Prototype References")]
    public RoguelikeGameManager gameManager;
    public PlayerController playerController;

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

    private float feedbackClearTime;

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

        float attackScore = baseAttackScore;
        float dodgeScore = baseDodgeScore;
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

                AddEnemyToContext(enemy, ref context, ref attackScore, ref dodgeScore, playerAttackRange);
            }
        }
        else
        {
            EnemyControllerBase[] enemies = FindObjectsByType<EnemyControllerBase>();
            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                AddEnemyToContext(enemy, ref context, ref attackScore, ref dodgeScore, playerAttackRange);
            }
        }

        context.incomingProjectiles = CountIncomingProjectiles();
        dodgeScore += context.incomingProjectiles * 2.45f;

        if (context.closeEnemies >= 4)
        {
            dodgeScore += 1.25f;
        }

        if (context.closeEnemies >= 8)
        {
            dodgeScore += 0.85f;
        }

        if (playerController != null && playerController.CurrentHP <= playerController.maxHP * 0.35f)
        {
            dodgeScore += 0.75f;
        }

        if (context.totalEnemies == 0)
        {
            attackScore = 0.5f;
            dodgeScore = 0.5f;
        }

        float totalScore = Mathf.Max(0.001f, attackScore + dodgeScore);
        priorAttack = Mathf.Clamp(attackScore / totalScore, 0.05f, 0.95f);
        priorDodge = 1f - priorAttack;

        if (context.attackingEnemies > 0 || context.incomingProjectiles > 0)
        {
            currentState = CombatState.Attacking;
        }
        else if (context.telegraphingEnemies > 0 || priorDodge >= 0.62f)
        {
            currentState = CombatState.Telegraph;
        }
        else
        {
            currentState = CombatState.Safe;
        }

        CurrentContext = context;
    }

    private void AddEnemyToContext(
        EnemyControllerBase enemy,
        ref CombatContext context,
        ref float attackScore,
        ref float dodgeScore,
        float playerAttackRange)
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

        attackScore += enemy.GetAttackOpportunityScore(playerAttackRange);
        dodgeScore += enemy.GetDodgeThreatScore();

        if (enemy.enemyKind == EnemyKind.Ranged)
        {
            attackScore += 0.15f;
        }

        if (enemy.enemyKind == EnemyKind.Boss)
        {
            attackScore += 0.45f;
        }
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
            enemyHpText.text =
                $"Enemies {CurrentContext.totalEnemies}  M:{CurrentContext.meleeEnemies} R:{CurrentContext.rangedEnemies} B:{CurrentContext.bossEnemies}";
        }

        if (priorText != null)
        {
            priorText.text = $"P(Attack) {priorAttack:P0}   P(Dodge) {priorDodge:P0}";
        }

        if (contextText != null)
        {
            string closestText = CurrentContext.totalEnemies > 0
                ? $"{CurrentContext.closestEnemyName} {CurrentContext.closestEnemyDistance:F1}m"
                : "None";

            contextText.text =
                $"Closest: {closestText}   Close: {CurrentContext.closeEnemies}   Telegraphs: {CurrentContext.telegraphingEnemies}   Arrows: {CurrentContext.incomingProjectiles}";
        }

        if (feedbackLogText != null && feedbackLogText.text.Length > 0 && Time.time > feedbackClearTime)
        {
            feedbackLogText.text = string.Empty;
        }
    }

    private void UpdatePlayerHpUI()
    {
        if (playerHpText != null && playerController != null)
        {
            playerHpText.text = $"Player HP: {playerController.CurrentHP} / {playerController.maxHP}";
        }
    }
}
