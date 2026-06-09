using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float movementSpeed = 5f;
    public float dodgeDistance = 2.35f;
    public float dodgeDuration = 0.22f;
    public float dodgeInvulnerability = 0.42f;
    public bool constrainToArena = true;

    [Header("Combat")]
    public int maxHP = 100;
    public int attackDamage = 18;
    public float attackRange = 2.05f;

    [Header("Skills")]
    [Range(0.05f, 1f)]
    public float healPercent = 0.2f;
    public float healCooldown = 5f;
    public int whirlwindDamage = 16;
    public float whirlwindRange = 3.15f;
    public float whirlwindCooldown = 7f;

    [Header("Visuals")]
    public Color bodyColor = new Color(0.2f, 0.75f, 1f, 1f);
    public Color invulnerableColor = new Color(0.65f, 0.95f, 1f, 0.75f);

    public int CurrentHP => currentHP;
    public bool IsDodging => isDodging;
    public bool IsInvulnerable => invulnerableUntil > Time.time;
    public bool IsMoving => inputVector.sqrMagnitude > 0.001f;
    public bool IsVirtualMoveActive => virtualMoveActive;
    public Vector2 MoveInput => inputVector;
    public float MissingHpRatio => maxHP <= 0 ? 0f : Mathf.Clamp01((maxHP - currentHP) / (float)maxHP);
    public float HealCooldownRemaining => Mathf.Max(0f, nextHealReadyTime - Time.time);
    public float WhirlwindCooldownRemaining => Mathf.Max(0f, nextWhirlwindReadyTime - Time.time);
    public bool CanHeal => currentHP < maxHP && HealCooldownRemaining <= 0f;
    public bool CanWhirlwind => WhirlwindCooldownRemaining <= 0f;
    public event Action<int, int> OnHpChanged;

    private Rigidbody2D rb;
    private SpriteRenderer bodyRenderer;
    private Vector2 inputVector;
    private Vector2 lastNonZeroDirection = Vector2.right;
    private int currentHP;
    private bool isDodging;
    private bool actionInputActive;
    private bool virtualMoveActive;
    private float invulnerableUntil;
    private float nextHealReadyTime;
    private float nextWhirlwindReadyTime;
    private Coroutine dodgeRoutine;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }

        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        if (GetComponent<Collider2D>() == null)
        {
            CircleCollider2D collider = gameObject.AddComponent<CircleCollider2D>();
            collider.radius = 0.42f;
        }

        try
        {
            gameObject.tag = "Player";
        }
        catch (UnityException)
        {
            Debug.LogWarning("The built-in Player tag is unavailable; enemy lookup will use manager references instead.");
        }

        bodyRenderer = PrototypeVisualFactory.EnsureSpriteRenderer(
            gameObject,
            PrototypeVisualFactory.CircleSprite,
            bodyColor,
            Vector2.one * 0.9f,
            4);

        ResetStats(false);
    }

    private void Update()
    {
        ReadKeyboardFallback();
        UpdateVisualState();
    }

    private void FixedUpdate()
    {
        if (isDodging)
        {
            return;
        }

        Vector2 nextPosition = rb.position + inputVector * movementSpeed * Time.fixedDeltaTime;
        Vector2 clampedPosition = ClampToArena(nextPosition);
        if ((clampedPosition - nextPosition).sqrMagnitude > 0.0001f)
        {
            rb.linearVelocity = Vector2.zero;
            rb.MovePosition(clampedPosition);
            return;
        }

        rb.linearVelocity = inputVector * movementSpeed;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            inputVector = Vector2.ClampMagnitude(context.ReadValue<Vector2>(), 1f);
            actionInputActive = inputVector.sqrMagnitude > 0.001f;
            RememberDirection(inputVector);
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            actionInputActive = false;
            if (!virtualMoveActive)
            {
                inputVector = Vector2.zero;
            }
        }
    }

    public void SetVirtualMoveInput(Vector2 moveInput)
    {
        inputVector = Vector2.ClampMagnitude(moveInput, 1f);
        virtualMoveActive = inputVector.sqrMagnitude > 0.001f;
        actionInputActive = false;
        RememberDirection(inputVector);
    }

    public void ClearVirtualMoveInput()
    {
        virtualMoveActive = false;
        if (!actionInputActive)
        {
            inputVector = Vector2.zero;
        }
    }

    public void ResetStats()
    {
        ResetStats(true);
    }

    public void ResetStats(bool resetPosition)
    {
        currentHP = maxHP;
        inputVector = Vector2.zero;
        actionInputActive = false;
        virtualMoveActive = false;
        invulnerableUntil = 0f;
        isDodging = false;
        nextHealReadyTime = 0f;
        nextWhirlwindReadyTime = 0f;

        if (dodgeRoutine != null)
        {
            StopCoroutine(dodgeRoutine);
            dodgeRoutine = null;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            if (resetPosition)
            {
                rb.position = ClampToArena(Vector2.zero);
            }
        }
        else if (resetPosition)
        {
            Vector2 clamped = ClampToArena(Vector2.zero);
            transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
        }

        OnHpChanged?.Invoke(currentHP, maxHP);
        Debug.Log("Player stats reset to full.");
    }

    public bool TakeDamage(int damage)
    {
        if (damage <= 0 || IsInvulnerable)
        {
            return false;
        }

        int previousHP = currentHP;
        currentHP = Mathf.Max(0, currentHP - damage);
        int actualDamage = previousHP - currentHP;
        OnHpChanged?.Invoke(currentHP, maxHP);
        RoguelikeGameManager.Instance?.RecordPlayerDamage(actualDamage);

        if (currentHP <= 0)
        {
            Debug.Log("Player Died!");
        }

        return true;
    }

    public bool TryHeal(out int amountHealed)
    {
        amountHealed = 0;

        if (!CanHeal)
        {
            return false;
        }

        int healAmount = Mathf.Max(1, Mathf.CeilToInt(maxHP * healPercent));
        int previousHP = currentHP;
        currentHP = Mathf.Min(maxHP, currentHP + healAmount);
        amountHealed = currentHP - previousHP;
        nextHealReadyTime = Time.time + healCooldown;
        OnHpChanged?.Invoke(currentHP, maxHP);
        RoguelikeGameManager.Instance?.RecordPlayerHealing(amountHealed);
        return amountHealed > 0;
    }

    public bool TryStartWhirlwindCooldown()
    {
        if (!CanWhirlwind)
        {
            return false;
        }

        nextWhirlwindReadyTime = Time.time + whirlwindCooldown;
        return true;
    }

    public void PerformDodge(Vector2 requestedDirection)
    {
        Vector2 dodgeDirection = requestedDirection.sqrMagnitude > 0.001f ? requestedDirection.normalized : lastNonZeroDirection;
        RememberDirection(dodgeDirection);

        if (dodgeRoutine != null)
        {
            StopCoroutine(dodgeRoutine);
        }

        dodgeRoutine = StartCoroutine(DodgeRoutine(dodgeDirection));
    }

    private void ReadKeyboardFallback()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        Vector2 keyboardInput = Vector2.zero;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
        {
            keyboardInput.x -= 1f;
        }

        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
        {
            keyboardInput.x += 1f;
        }

        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed)
        {
            keyboardInput.y -= 1f;
        }

        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed)
        {
            keyboardInput.y += 1f;
        }

        if (keyboardInput.sqrMagnitude > 0.001f)
        {
            inputVector = Vector2.ClampMagnitude(keyboardInput, 1f);
            actionInputActive = false;
            virtualMoveActive = false;
            RememberDirection(inputVector);
        }
        else if (!virtualMoveActive && !actionInputActive && inputVector.sqrMagnitude > 0.001f && NoKeyboardMovementKeysPressed())
        {
            inputVector = Vector2.zero;
        }
    }

    private bool NoKeyboardMovementKeysPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard == null ||
               (!keyboard.aKey.isPressed &&
                !keyboard.dKey.isPressed &&
                !keyboard.sKey.isPressed &&
                !keyboard.wKey.isPressed &&
                !keyboard.leftArrowKey.isPressed &&
                !keyboard.rightArrowKey.isPressed &&
                !keyboard.downArrowKey.isPressed &&
                !keyboard.upArrowKey.isPressed);
    }

    private void RememberDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0.001f)
        {
            lastNonZeroDirection = direction.normalized;
        }
    }

    private IEnumerator DodgeRoutine(Vector2 direction)
    {
        isDodging = true;
        invulnerableUntil = Time.time + dodgeInvulnerability;

        Vector2 start = rb.position;
        Vector2 end = ClampToArena(start + direction * dodgeDistance);
        float elapsed = 0f;

        while (elapsed < dodgeDuration)
        {
            float t = Mathf.Clamp01(elapsed / dodgeDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rb.MovePosition(ClampToArena(Vector2.Lerp(start, end, eased)));
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.MovePosition(ClampToArena(end));
        rb.linearVelocity = Vector2.zero;
        isDodging = false;
        dodgeRoutine = null;
    }

    private Vector2 ClampToArena(Vector2 position)
    {
        return constrainToArena
            ? RoguelikeGameManager.ClampToArena(position, RoguelikeGameManager.PlayerArenaPadding)
            : position;
    }

    private void UpdateVisualState()
    {
        if (bodyRenderer != null)
        {
            bodyRenderer.color = IsInvulnerable ? invulnerableColor : bodyColor;
        }
    }
}
