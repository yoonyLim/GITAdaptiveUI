using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyKind
{
    Melee,
    Ranged,
    Boss
}

public abstract class EnemyControllerBase : MonoBehaviour
{
    [Header("Identity")]
    public EnemyKind enemyKind = EnemyKind.Melee;
    public string displayName = "Enemy";

    [Header("Stats")]
    public int maxHP = 35;
    public float moveSpeed = 2.5f;
    public float attackRange = 1.5f;
    public float attackCooldown = 1.8f;
    public float telegraphDuration = 0.55f;
    public float attackFrameDuration = 0.18f;
    public int attackDamage = 10;

    [Header("Runtime Colors")]
    public Color normalColor = Color.white;
    public Color telegraphColor = new Color(1f, 0.55f, 0.1f, 1f);
    public Color attackColor = Color.red;
    public Color damageFlashColor = Color.white;

    public int CurrentHP => currentHP;
    public bool IsAlive => currentHP > 0;
    public bool IsCalibrationScenarioEnemy { get; private set; }
    public bool IsTelegraphing { get; protected set; }
    public bool IsAttacking { get; protected set; }
    public float DistanceToPlayer { get; protected set; } = float.MaxValue;
    public float TimeUntilAttackReady => Mathf.Max(0f, nextAttackTime - Time.time);
    public Transform PlayerTransform => playerTransform;

    protected Transform playerTransform;
    protected PlayerController playerController;
    protected RoguelikeGameManager gameManager;
    protected CombatManager combatManager;
    protected Rigidbody2D rb;
    protected SpriteRenderer bodyRenderer;
    protected float nextAttackTime;
    protected int currentHP;

    private readonly List<GameObject> spawnedTelegraphs = new List<GameObject>();
    private Coroutine flashRoutine;
    private bool deathNotified;
    private bool calibrationAiFrozen;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyRenderer = GetComponent<SpriteRenderer>();
        currentHP = maxHP;
        EnsureBaseVisual();
    }

    protected virtual void Start()
    {
        ResolvePlayerIfNeeded();
    }

    public virtual void Initialize(Transform player, RoguelikeGameManager owner, CombatManager combat)
    {
        playerTransform = player;
        playerController = playerTransform ? playerTransform.GetComponent<PlayerController>() : null;
        gameManager = owner;
        combatManager = combat;
        IsCalibrationScenarioEnemy = false;
        calibrationAiFrozen = false;
        currentHP = maxHP;
        deathNotified = false;
        nextAttackTime = Time.time + Random.Range(0.15f, 0.75f);
        DistanceToPlayer = playerTransform ? Vector2.Distance(transform.position, playerTransform.position) : float.MaxValue;
        EnsureBaseVisual();
        SetBodyColor(normalColor);
    }

    public void ConfigureCalibrationPose(bool telegraphing, bool attacking, bool freezeAi = true)
    {
        IsCalibrationScenarioEnemy = true;
        currentHP = Mathf.Max(1, maxHP);
        deathNotified = false;

        IsTelegraphing = telegraphing;
        IsAttacking = attacking;
        calibrationAiFrozen = freezeAi;

        if (freezeAi)
        {
            moveSpeed = 0f;
            attackDamage = 0;
            nextAttackTime = Time.time + 999f;
        }

        if (IsAttacking)
        {
            SetBodyColor(attackColor);
        }
        else if (IsTelegraphing)
        {
            SetBodyColor(telegraphColor);
        }
        else
        {
            SetBodyColor(normalColor);
        }

        RefreshDistanceToPlayer();
    }

    public void SetCalibrationAttackReadyIn(float seconds)
    {
        IsCalibrationScenarioEnemy = true;
        nextAttackTime = Time.time + Mathf.Max(0f, seconds);
    }

    public void RefreshDistanceToPlayer()
    {
        ResolvePlayerIfNeeded();
        DistanceToPlayer = playerTransform ? Vector2.Distance(transform.position, playerTransform.position) : float.MaxValue;
    }

    protected virtual void Update()
    {
        if (!IsAlive)
        {
            return;
        }

        ResolvePlayerIfNeeded();
        if (playerTransform == null)
        {
            return;
        }

        DistanceToPlayer = Vector2.Distance(transform.position, playerTransform.position);
        FacePlayer();
        if (calibrationAiFrozen)
        {
            return;
        }

        TickBehavior();
    }

    protected abstract void TickBehavior();

    public virtual void TakeDamage(int amount)
    {
        if (!IsAlive)
        {
            return;
        }

        currentHP = Mathf.Max(0, currentHP - amount);

        if (currentHP <= 0)
        {
            Die();
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
        }

        flashRoutine = StartCoroutine(FlashDamage());
    }

    public virtual float GetAttackOpportunityScore(float playerAttackRange)
    {
        if (!IsAlive)
        {
            return 0f;
        }

        float rangeWindow = Mathf.Max(0.1f, playerAttackRange + 1.25f);
        float closeness = Mathf.Clamp01((rangeWindow - DistanceToPlayer) / rangeWindow);
        float kindBonus = enemyKind == EnemyKind.Boss ? 0.4f : 0f;
        return closeness * 2.2f + kindBonus;
    }

    public virtual float GetDodgeThreatScore()
    {
        if (!IsAlive)
        {
            return 0f;
        }

        float score = 0f;
        if (IsTelegraphing)
        {
            score += enemyKind == EnemyKind.Boss ? 4f : 2.2f;
        }

        if (IsAttacking)
        {
            score += enemyKind == EnemyKind.Boss ? 4.5f : 2.8f;
        }

        float dangerRange = Mathf.Max(attackRange + 0.75f, 0.1f);
        if (enemyKind != EnemyKind.Ranged && DistanceToPlayer <= dangerRange)
        {
            score += Mathf.Lerp(1.4f, 0.2f, DistanceToPlayer / dangerRange);
        }

        return score;
    }

    protected bool CanStartAttack()
    {
        return Time.time >= nextAttackTime && !IsTelegraphing && !IsAttacking;
    }

    public bool CanStartAttackSoon(float windowSeconds)
    {
        return IsAlive &&
               !IsTelegraphing &&
               !IsAttacking &&
               TimeUntilAttackReady <= Mathf.Max(0f, windowSeconds);
    }

    protected Vector2 DirectionToPlayer()
    {
        if (playerTransform == null)
        {
            return Vector2.right;
        }

        Vector2 direction = playerTransform.position - transform.position;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
    }

    protected void MoveToward(Vector2 target, float speedMultiplier = 1f)
    {
        Vector2 nextPosition = Vector2.MoveTowards(transform.position, target, moveSpeed * speedMultiplier * Time.deltaTime);
        MoveTo(nextPosition);
    }

    protected void MoveAwayFrom(Vector2 source, float speedMultiplier = 1f)
    {
        Vector2 direction = ((Vector2)transform.position - source).normalized;
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Random.insideUnitCircle.normalized;
        }

        MoveTo((Vector2)transform.position + direction * moveSpeed * speedMultiplier * Time.deltaTime);
    }

    protected GameObject TrackTelegraph(GameObject telegraph)
    {
        if (telegraph != null)
        {
            spawnedTelegraphs.Add(telegraph);
        }

        return telegraph;
    }

    protected void DestroyTrackedTelegraph(GameObject telegraph)
    {
        if (telegraph != null)
        {
            spawnedTelegraphs.Remove(telegraph);
            Destroy(telegraph);
        }
    }

    protected void SetBodyColor(Color color)
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.color = color;
        }
    }

    protected virtual void EnsureBaseVisual()
    {
        if (bodyRenderer == null)
        {
            bodyRenderer = PrototypeVisualFactory.EnsureSpriteRenderer(
                gameObject,
                PrototypeVisualFactory.CircleSprite,
                normalColor,
                Vector2.one,
                2);
        }
    }

    protected virtual void OnDestroy()
    {
        CleanupTelegraphs();

        if (!deathNotified && gameManager != null)
        {
            gameManager.NotifyEnemyDestroyed(this);
        }
    }

    private void MoveTo(Vector2 nextPosition)
    {
        nextPosition = RoguelikeGameManager.ClampToArena(nextPosition, RoguelikeGameManager.EnemyArenaPadding);

        if (rb != null)
        {
            rb.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    private void FacePlayer()
    {
        Vector2 direction = DirectionToPlayer();
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    private void ResolvePlayerIfNeeded()
    {
        if (playerTransform != null)
        {
            return;
        }

        if (RoguelikeGameManager.Instance != null && RoguelikeGameManager.Instance.PlayerTransform != null)
        {
            playerTransform = RoguelikeGameManager.Instance.PlayerTransform;
            playerController = RoguelikeGameManager.Instance.PlayerController;
            gameManager = RoguelikeGameManager.Instance;
        }
        else
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
                playerController = playerObject.GetComponent<PlayerController>();
            }
        }
    }

    private IEnumerator FlashDamage()
    {
        SetBodyColor(damageFlashColor);
        yield return new WaitForSeconds(0.08f);

        if (IsTelegraphing)
        {
            SetBodyColor(telegraphColor);
        }
        else if (IsAttacking)
        {
            SetBodyColor(attackColor);
        }
        else
        {
            SetBodyColor(normalColor);
        }
    }

    private void Die()
    {
        CleanupTelegraphs();
        deathNotified = true;

        if (gameManager != null)
        {
            gameManager.NotifyEnemyDefeated(this);
        }

        Destroy(gameObject);
    }

    private void CleanupTelegraphs()
    {
        for (int i = spawnedTelegraphs.Count - 1; i >= 0; i--)
        {
            if (spawnedTelegraphs[i] != null)
            {
                Destroy(spawnedTelegraphs[i]);
            }
        }

        spawnedTelegraphs.Clear();
    }
}
