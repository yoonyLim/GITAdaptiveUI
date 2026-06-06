using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class AdaptiveTouchManager : MonoBehaviour
{
    [Header("Visual Buttons (UI Images)")]
    public Canvas mainCanvas;
    public Image visualAttackButton;
    public Image visualDodgeButton;

    [Header("Button Feedback Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Gaussian Hitbox Visualizers")]
    public RectTransform attackHitboxVisualizer;
    public RectTransform dodgeHitboxVisualizer;

    [Header("Touch Tuning")]
    [Tooltip("Represents the player's fat-finger spread in screen pixels. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f;

    [Tooltip("Minimum posterior required before a touch is accepted.")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Update()
    {
        CombatManager combatManager = CombatManager.Instance;
        float attackPrior = combatManager != null ? combatManager.priorAttack : 0.5f;
        float dodgePrior = combatManager != null ? combatManager.priorDodge : 0.5f;

        UpdateHitboxVisualizer(attackHitboxVisualizer, CalculateDynamicRadius(attackPrior));
        UpdateHitboxVisualizer(dodgeHitboxVisualizer, CalculateDynamicRadius(dodgePrior));

#if UNITY_EDITOR
        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                ProcessInputBegan(Mouse.current.position.ReadValue());
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame)
            {
                ProcessInputEnded();
            }
        }
#endif

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                ProcessInputBegan(touch.screenPosition);
            }
            else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                     touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                ProcessInputEnded();
            }
        }
    }

    private void ProcessInputBegan(Vector2 inputPos)
    {
        if (visualAttackButton == null || visualDodgeButton == null)
        {
            Debug.LogWarning("AdaptiveTouchManager needs both attack and dodge button images assigned.");
            return;
        }

        CombatManager combatManager = CombatManager.Instance;
        float priorAttack = combatManager != null ? combatManager.priorAttack : 0.5f;
        float priorDodge = combatManager != null ? combatManager.priorDodge : 0.5f;

        Vector2 attackCenter = visualAttackButton.rectTransform.position;
        Vector2 dodgeCenter = visualDodgeButton.rectTransform.position;

        float likelihoodAttack = CalculateGaussianLikelihood(Vector2.Distance(inputPos, attackCenter), userTouchVariance);
        float likelihoodDodge = CalculateGaussianLikelihood(Vector2.Distance(inputPos, dodgeCenter), userTouchVariance);

        float posteriorAttack = likelihoodAttack * priorAttack;
        float posteriorDodge = likelihoodDodge * priorDodge;
        float threshold = Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f);

        if (posteriorAttack < threshold && posteriorDodge < threshold)
        {
            Debug.Log(
                $"[Adaptive Touch] Rejected. Posterior below {threshold:F4}. Attack={posteriorAttack:F4}, Dodge={posteriorDodge:F4}");
            return;
        }

        if (posteriorAttack >= posteriorDodge)
        {
            if (visualAttackButton != null)
            {
                visualAttackButton.color = pressedColor;
            }

            Debug.Log(
                $"[Adaptive Touch] ATTACK accepted. Prior={priorAttack:F2}, Likelihood={likelihoodAttack:F2}, Posterior={posteriorAttack:F3}");
            combatManager?.OnPlayerAttack();
        }
        else
        {
            if (visualDodgeButton != null)
            {
                visualDodgeButton.color = pressedColor;
            }

            Debug.Log(
                $"[Adaptive Touch] DODGE accepted. Prior={priorDodge:F2}, Likelihood={likelihoodDodge:F2}, Posterior={posteriorDodge:F3}");
            combatManager?.OnPlayerDodge();
        }
    }

    private void ProcessInputEnded()
    {
        if (visualAttackButton != null)
        {
            visualAttackButton.color = normalColor;
        }

        if (visualDodgeButton != null)
        {
            visualDodgeButton.color = normalColor;
        }
    }

    private void UpdateHitboxVisualizer(RectTransform visualizer, float screenRadius)
    {
        if (visualizer == null)
        {
            return;
        }

        float scaleFactor = mainCanvas != null ? Mathf.Max(0.01f, mainCanvas.scaleFactor) : 1f;
        float uiSize = (screenRadius * 2f) / scaleFactor;
        visualizer.sizeDelta = new Vector2(uiSize, uiSize);
    }

    private float CalculateGaussianLikelihood(float distance, float variance)
    {
        float safeVariance = Mathf.Max(variance, 0.1f);
        return Mathf.Exp(-(distance * distance) / (2f * safeVariance * safeVariance));
    }

    private float CalculateDynamicRadius(float prior)
    {
        float safePrior = Mathf.Clamp(prior, 0.01f, 0.99f);
        float thresholdRatio = (Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f)) / safePrior;
        thresholdRatio = Mathf.Clamp(thresholdRatio, 0.0001f, 0.999f);
        return userTouchVariance * Mathf.Sqrt(-2f * Mathf.Log(thresholdRatio));
    }
}
