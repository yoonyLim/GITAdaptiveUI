using System.Collections;
using UnityEngine;

public class RangedEnemyController : EnemyControllerBase
{
    [Header("Ranged")]
    public float shootingRange = 7f;
    public float keepAwayDistance = 3.2f;
    public float arrowSpeed = 8.5f;
    public float aimLineWidth = 0.18f;
    public GameObject arrowPrefab;
    public Transform firePoint;

    private bool bowVisualCreated;

    protected override void Awake()
    {
        enemyKind = EnemyKind.Ranged;
        displayName = "Bow Enemy";
        base.Awake();
    }

    protected override void TickBehavior()
    {
        if (IsTelegraphing || IsAttacking)
        {
            return;
        }

        if (DistanceToPlayer > shootingRange)
        {
            MoveToward(playerTransform.position, 0.85f);
        }
        else if (DistanceToPlayer < keepAwayDistance)
        {
            MoveAwayFrom(playerTransform.position, 1.05f);
        }
        else if (CanStartAttack())
        {
            StartCoroutine(ShootArrow());
        }
    }

    protected override void EnsureBaseVisual()
    {
        bodyRenderer = PrototypeVisualFactory.EnsureSpriteRenderer(
            gameObject,
            PrototypeVisualFactory.CircleSprite,
            normalColor,
            Vector2.one * 0.86f,
            2);

        CreateBowVisual();
    }

    private IEnumerator ShootArrow()
    {
        IsTelegraphing = true;
        SetBodyColor(telegraphColor);

        Vector2 origin = firePoint != null ? firePoint.position : transform.position;
        Vector2 direction = DirectionToPlayer();
        GameObject warning = TrackTelegraph(PrototypeVisualFactory.CreateTelegraphLine(
            "Arrow Aim Warning",
            origin,
            direction,
            shootingRange,
            aimLineWidth,
            new Color(1f, 0f, 0f, 0.28f)));

        yield return new WaitForSeconds(telegraphDuration);
        DestroyTrackedTelegraph(warning);

        IsTelegraphing = false;
        IsAttacking = true;
        SetBodyColor(attackColor);
        SpawnArrow(origin, direction);

        yield return new WaitForSeconds(attackFrameDuration);

        IsAttacking = false;
        SetBodyColor(normalColor);
        nextAttackTime = Time.time + attackCooldown;
    }

    private void SpawnArrow(Vector2 origin, Vector2 direction)
    {
        Vector2 safeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
        Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(safeDirection.y, safeDirection.x) * Mathf.Rad2Deg);
        GameObject arrow = arrowPrefab != null ? Instantiate(arrowPrefab, origin, rotation) : new GameObject("Arrow");

        arrow.transform.position = origin;
        arrow.transform.rotation = rotation;

        PrototypeVisualFactory.EnsureSpriteRenderer(
            arrow,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.95f, 0.85f, 0.48f, 1f),
            new Vector2(0.68f, 0.12f),
            3);

        Rigidbody2D arrowBody = arrow.GetComponent<Rigidbody2D>();
        if (arrowBody == null)
        {
            arrowBody = arrow.AddComponent<Rigidbody2D>();
        }

        arrowBody.bodyType = RigidbodyType2D.Kinematic;
        arrowBody.gravityScale = 0f;

        BoxCollider2D collider = arrow.GetComponent<BoxCollider2D>();
        if (collider == null)
        {
            collider = arrow.AddComponent<BoxCollider2D>();
        }

        collider.isTrigger = true;
        collider.size = new Vector2(0.8f, 0.2f);

        EnemyProjectile projectile = arrow.GetComponent<EnemyProjectile>();
        if (projectile == null)
        {
            projectile = arrow.AddComponent<EnemyProjectile>();
        }

        projectile.speed = arrowSpeed;
        projectile.damage = attackDamage;
        projectile.SetDirection(safeDirection);
    }

    private void CreateBowVisual()
    {
        if (bowVisualCreated || transform.Find("BowUpper") != null)
        {
            bowVisualCreated = true;
            return;
        }

        PrototypeVisualFactory.CreateChildSprite(
            "BowUpper",
            transform,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.42f, 0.22f, 0.08f, 1f),
            new Vector2(0.52f, 0.08f),
            new Vector2(0.48f, 0.23f),
            38f,
            5);

        PrototypeVisualFactory.CreateChildSprite(
            "BowLower",
            transform,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.42f, 0.22f, 0.08f, 1f),
            new Vector2(0.52f, 0.08f),
            new Vector2(0.48f, -0.23f),
            -38f,
            5);

        PrototypeVisualFactory.CreateChildSprite(
            "BowString",
            transform,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.92f, 0.86f, 0.7f, 1f),
            new Vector2(0.06f, 0.62f),
            new Vector2(0.24f, 0f),
            0f,
            6);

        GameObject firePointObject = new GameObject("FirePoint");
        firePointObject.transform.SetParent(transform, false);
        firePointObject.transform.localPosition = new Vector3(0.74f, 0f, 0f);
        firePoint = firePointObject.transform;
        bowVisualCreated = true;
    }

    private void Reset()
    {
        enemyKind = EnemyKind.Ranged;
        displayName = "Bow Enemy";
        maxHP = 28;
        moveSpeed = 2.05f;
        attackRange = 6.8f;
        shootingRange = 7f;
        keepAwayDistance = 3.2f;
        attackCooldown = 2.4f;
        telegraphDuration = 0.62f;
        attackFrameDuration = 0.12f;
        attackDamage = 8;
        normalColor = new Color(0.45f, 0.64f, 1f, 1f);
        telegraphColor = new Color(1f, 0.66f, 0.2f, 1f);
        attackColor = new Color(1f, 0.1f, 0.1f, 1f);
    }
}
