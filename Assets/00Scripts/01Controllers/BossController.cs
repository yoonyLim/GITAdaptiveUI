using System.Collections;
using UnityEngine;

public class BossController : EnemyControllerBase
{
    [Header("Boss Patterns")]
    public float smashRadius = 2.65f;
    public float shockwaveLength = 12f;
    public float shockwaveWidth = 1.15f;
    public float bossKeepDistance = 4.5f;

    [Header("Optional Telegraph Prefabs")]
    public GameObject smashWarningPrefab;
    public GameObject shockwaveWarningPrefab;

    protected override void Awake()
    {
        enemyKind = EnemyKind.Boss;
        displayName = "Boss";
        base.Awake();
    }

    protected override void TickBehavior()
    {
        if (IsTelegraphing || IsAttacking)
        {
            return;
        }

        if (DistanceToPlayer > bossKeepDistance + 1.75f)
        {
            MoveToward(playerTransform.position, 0.55f);
        }
        else if (DistanceToPlayer < bossKeepDistance - 1.25f)
        {
            MoveAwayFrom(playerTransform.position, 0.45f);
        }

        if (!CanStartAttack())
        {
            return;
        }

        if (Random.value < 0.5f)
        {
            StartCoroutine(SmashAttack());
        }
        else
        {
            StartCoroutine(ShockwaveAttack());
        }
    }

    protected override void EnsureBaseVisual()
    {
        bodyRenderer = PrototypeVisualFactory.EnsureSpriteRenderer(
            gameObject,
            PrototypeVisualFactory.CircleSprite,
            normalColor,
            Vector2.one * 1.75f,
            3);

        if (transform.Find("BossCrown") == null)
        {
            PrototypeVisualFactory.CreateChildSprite(
                "BossCrown",
                transform,
                PrototypeVisualFactory.SquareSprite,
                new Color(1f, 0.82f, 0.18f, 1f),
                new Vector2(0.64f, 0.28f),
                new Vector2(-0.05f, 0.62f),
                0f,
                6);
        }
    }

    private IEnumerator SmashAttack()
    {
        IsTelegraphing = true;
        SetBodyColor(telegraphColor);

        Vector2 targetPosition = playerTransform != null ? playerTransform.position : transform.position;
        GameObject warning = CreateSmashWarning(targetPosition);

        yield return new WaitForSeconds(telegraphDuration);
        DestroyTrackedTelegraph(warning);

        IsTelegraphing = false;
        IsAttacking = true;
        SetBodyColor(attackColor);

        GameObject impact = TrackTelegraph(PrototypeVisualFactory.CreateTelegraphCircle(
            "Smash Impact",
            targetPosition,
            smashRadius,
            new Color(1f, 0.5f, 0.05f, 0.38f)));

        if (playerTransform != null && playerController != null &&
            Vector2.Distance(playerTransform.position, targetPosition) <= smashRadius)
        {
            playerController.TakeDamage(attackDamage);
        }

        yield return new WaitForSeconds(attackFrameDuration + 0.12f);
        DestroyTrackedTelegraph(impact);

        IsAttacking = false;
        SetBodyColor(normalColor);
        nextAttackTime = Time.time + attackCooldown;
    }

    private IEnumerator ShockwaveAttack()
    {
        IsTelegraphing = true;
        SetBodyColor(telegraphColor);

        Vector2 origin = transform.position;
        Vector2 direction = DirectionToPlayer();
        GameObject warning = CreateShockwaveWarning(origin, direction);

        yield return new WaitForSeconds(telegraphDuration);
        DestroyTrackedTelegraph(warning);

        IsTelegraphing = false;
        IsAttacking = true;
        SetBodyColor(attackColor);

        GameObject impact = TrackTelegraph(PrototypeVisualFactory.CreateTelegraphLine(
            "Shockwave Impact",
            origin,
            direction,
            shockwaveLength,
            shockwaveWidth,
            new Color(1f, 0.45f, 0f, 0.42f)));

        if (playerTransform != null && playerController != null &&
            PrototypeVisualFactory.PointInLineArea(playerTransform.position, origin, direction, shockwaveLength, shockwaveWidth))
        {
            playerController.TakeDamage(Mathf.Max(1, Mathf.RoundToInt(attackDamage * 0.75f)));
        }

        yield return new WaitForSeconds(attackFrameDuration + 0.16f);
        DestroyTrackedTelegraph(impact);

        IsAttacking = false;
        SetBodyColor(normalColor);
        nextAttackTime = Time.time + attackCooldown;
    }

    private GameObject CreateSmashWarning(Vector2 targetPosition)
    {
        if (smashWarningPrefab != null)
        {
            GameObject warning = Instantiate(smashWarningPrefab, targetPosition, Quaternion.identity);
            warning.transform.localScale = Vector3.one * smashRadius * 2f;
            return TrackTelegraph(warning);
        }

        return TrackTelegraph(PrototypeVisualFactory.CreateTelegraphCircle(
            "Smash Warning",
            targetPosition,
            smashRadius,
            new Color(1f, 0f, 0f, 0.25f)));
    }

    private GameObject CreateShockwaveWarning(Vector2 origin, Vector2 direction)
    {
        if (shockwaveWarningPrefab != null)
        {
            Vector2 safeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
            Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(safeDirection.y, safeDirection.x) * Mathf.Rad2Deg);
            GameObject warning = Instantiate(shockwaveWarningPrefab, origin + safeDirection * (shockwaveLength * 0.5f), rotation);
            warning.transform.localScale = new Vector3(shockwaveLength, shockwaveWidth, 1f);
            return TrackTelegraph(warning);
        }

        return TrackTelegraph(PrototypeVisualFactory.CreateTelegraphLine(
            "Shockwave Warning",
            origin,
            direction,
            shockwaveLength,
            shockwaveWidth,
            new Color(1f, 0f, 0f, 0.24f)));
    }

    private void Reset()
    {
        enemyKind = EnemyKind.Boss;
        displayName = "Boss";
        maxHP = 360;
        moveSpeed = 1.35f;
        attackRange = 3f;
        attackCooldown = 2.65f;
        telegraphDuration = 1.25f;
        attackFrameDuration = 0.22f;
        attackDamage = 24;
        normalColor = new Color(0.55f, 0.18f, 0.72f, 1f);
        telegraphColor = new Color(1f, 0.55f, 0.1f, 1f);
        attackColor = new Color(1f, 0.05f, 0.05f, 1f);
    }
}
