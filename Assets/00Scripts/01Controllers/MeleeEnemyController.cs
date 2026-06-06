using System.Collections;
using UnityEngine;

public class MeleeEnemyController : EnemyControllerBase
{
    [Header("Melee")]
    public float swordArcDegrees = 95f;
    public float swordWarningWidth = 0.75f;

    private bool swordVisualCreated;

    protected override void Awake()
    {
        enemyKind = EnemyKind.Melee;
        displayName = "Sword Enemy";
        base.Awake();
    }

    protected override void TickBehavior()
    {
        if (IsTelegraphing || IsAttacking)
        {
            return;
        }

        if (DistanceToPlayer > attackRange)
        {
            MoveToward(playerTransform.position);
        }
        else if (CanStartAttack())
        {
            StartCoroutine(SwordAttack());
        }
    }

    protected override void EnsureBaseVisual()
    {
        bodyRenderer = PrototypeVisualFactory.EnsureSpriteRenderer(
            gameObject,
            PrototypeVisualFactory.CircleSprite,
            normalColor,
            Vector2.one * 0.9f,
            2);

        CreateSwordVisual();
    }

    private IEnumerator SwordAttack()
    {
        IsTelegraphing = true;
        SetBodyColor(telegraphColor);

        Vector2 origin = transform.position;
        Vector2 attackDirection = DirectionToPlayer();
        GameObject warning = TrackTelegraph(PrototypeVisualFactory.CreateTelegraphLine(
            "Sword Swing Warning",
            origin,
            attackDirection,
            attackRange + 0.55f,
            swordWarningWidth,
            new Color(1f, 0f, 0f, 0.24f)));

        yield return new WaitForSeconds(telegraphDuration);
        DestroyTrackedTelegraph(warning);

        IsTelegraphing = false;
        IsAttacking = true;
        SetBodyColor(attackColor);

        if (playerTransform != null && playerController != null)
        {
            Vector2 toPlayer = playerTransform.position - transform.position;
            float distance = toPlayer.magnitude;
            float angle = Vector2.Angle(attackDirection, toPlayer.normalized);

            if (distance <= attackRange + 0.35f && angle <= swordArcDegrees * 0.5f)
            {
                playerController.TakeDamage(attackDamage);
            }
        }

        yield return new WaitForSeconds(attackFrameDuration);

        IsAttacking = false;
        SetBodyColor(normalColor);
        nextAttackTime = Time.time + attackCooldown;
    }

    private void CreateSwordVisual()
    {
        if (swordVisualCreated || transform.Find("SwordBlade") != null)
        {
            swordVisualCreated = true;
            return;
        }

        PrototypeVisualFactory.CreateChildSprite(
            "SwordBlade",
            transform,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.78f, 0.85f, 0.95f, 1f),
            new Vector2(0.95f, 0.13f),
            new Vector2(0.66f, 0.08f),
            0f,
            5);

        PrototypeVisualFactory.CreateChildSprite(
            "SwordHandle",
            transform,
            PrototypeVisualFactory.SquareSprite,
            new Color(0.35f, 0.22f, 0.12f, 1f),
            new Vector2(0.28f, 0.18f),
            new Vector2(0.2f, -0.02f),
            0f,
            6);

        swordVisualCreated = true;
    }

    private void Reset()
    {
        enemyKind = EnemyKind.Melee;
        displayName = "Sword Enemy";
        maxHP = 35;
        moveSpeed = 2.9f;
        attackRange = 1.55f;
        attackCooldown = 1.55f;
        telegraphDuration = 0.42f;
        attackFrameDuration = 0.16f;
        attackDamage = 9;
        normalColor = new Color(0.95f, 0.38f, 0.28f, 1f);
        telegraphColor = new Color(1f, 0.75f, 0.25f, 1f);
        attackColor = new Color(1f, 0.08f, 0.08f, 1f);
    }
}
