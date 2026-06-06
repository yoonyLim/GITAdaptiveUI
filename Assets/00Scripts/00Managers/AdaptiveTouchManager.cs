using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class AdaptiveTouchManager : MonoBehaviour
{
    private enum AdaptiveAction
    {
        Attack,
        Dodge,
        Heal,
        Whirlwind
    }

    private struct ActionCandidate
    {
        public AdaptiveAction action;
        public string label;
        public Image image;
        public float prior;
        public float likelihood;
        public float posterior;
    }

    [Header("Visual Buttons (UI Images)")]
    public Canvas mainCanvas;
    public Image visualAttackButton;
    public Image visualDodgeButton;
    public Image visualHealButton;
    public Image visualWhirlwindButton;

    [Header("Skill Cooldown Labels")]
    public TextMeshProUGUI healButtonLabel;
    public TextMeshProUGUI whirlwindButtonLabel;

    [Header("Button Feedback Colors")]
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Header("Gaussian Hitbox Visualizers")]
    public RectTransform attackHitboxVisualizer;
    public RectTransform dodgeHitboxVisualizer;
    public RectTransform healHitboxVisualizer;
    public RectTransform whirlwindHitboxVisualizer;

    [Header("Touch Tuning")]
    [Tooltip("Represents the player's fat-finger spread in screen pixels. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f;

    [Tooltip("Minimum posterior required before a touch is accepted.")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    private Color attackBaseColor;
    private Color dodgeBaseColor;
    private Color healBaseColor;
    private Color whirlwindBaseColor;
    private bool capturedBaseColors;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
    }

    private void Update()
    {
        CaptureBaseColorsIfNeeded();

        CombatManager combatManager = CombatManager.Instance;
        float attackPrior = combatManager != null ? combatManager.priorAttack : 0.5f;
        float dodgePrior = combatManager != null ? combatManager.priorDodge : 0.5f;
        float healPrior = combatManager != null ? combatManager.priorHeal : 0.05f;
        float whirlwindPrior = combatManager != null ? combatManager.priorWhirlwind : 0.05f;

        UpdateHitboxVisualizer(attackHitboxVisualizer, CalculateDynamicRadius(attackPrior));
        UpdateHitboxVisualizer(dodgeHitboxVisualizer, CalculateDynamicRadius(dodgePrior));
        UpdateHitboxVisualizer(healHitboxVisualizer, CalculateDynamicRadius(healPrior));
        UpdateHitboxVisualizer(whirlwindHitboxVisualizer, CalculateDynamicRadius(whirlwindPrior));
        UpdateSkillCooldownLabels(combatManager);

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
        CaptureBaseColorsIfNeeded();

        CombatManager combatManager = CombatManager.Instance;
        List<ActionCandidate> candidates = new List<ActionCandidate>(4);

        AddCandidate(candidates, AdaptiveAction.Attack, "ATTACK", visualAttackButton, combatManager != null ? combatManager.priorAttack : 0.5f, inputPos);
        AddCandidate(candidates, AdaptiveAction.Dodge, "DODGE", visualDodgeButton, combatManager != null ? combatManager.priorDodge : 0.5f, inputPos);
        AddCandidate(candidates, AdaptiveAction.Heal, "HEAL", visualHealButton, combatManager != null ? combatManager.priorHeal : 0.05f, inputPos);
        AddCandidate(candidates, AdaptiveAction.Whirlwind, "WHIRLWIND", visualWhirlwindButton, combatManager != null ? combatManager.priorWhirlwind : 0.05f, inputPos);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("AdaptiveTouchManager needs at least one visual button image assigned.");
            return;
        }

        ActionCandidate best = candidates[0];
        for (int i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].posterior > best.posterior)
            {
                best = candidates[i];
            }
        }

        float threshold = Mathf.Max(0.0001f, minLikelihoodThreshold * 0.5f);
        if (best.posterior < threshold)
        {
            Debug.Log($"[Adaptive Touch] Rejected. Best posterior {best.posterior:F4} below {threshold:F4}. {FormatCandidatePosteriors(candidates)}");
            return;
        }

        best.image.color = Color.Lerp(GetBaseColor(best.action), pressedColor, 0.55f);
        Debug.Log(
            $"[Adaptive Touch] {best.label} accepted. Prior={best.prior:F2}, Likelihood={best.likelihood:F2}, Posterior={best.posterior:F3}. {FormatCandidatePosteriors(candidates)}");

        ExecuteAction(best.action, combatManager);
    }

    private void ProcessInputEnded()
    {
        ResetButtonColor(visualAttackButton, attackBaseColor);
        ResetButtonColor(visualDodgeButton, dodgeBaseColor);
        ResetButtonColor(visualHealButton, healBaseColor);
        ResetButtonColor(visualWhirlwindButton, whirlwindBaseColor);
    }

    private void AddCandidate(
        List<ActionCandidate> candidates,
        AdaptiveAction action,
        string label,
        Image image,
        float prior,
        Vector2 inputPos)
    {
        if (image == null)
        {
            return;
        }

        float likelihood = CalculateGaussianLikelihood(Vector2.Distance(inputPos, image.rectTransform.position), userTouchVariance);
        candidates.Add(new ActionCandidate
        {
            action = action,
            label = label,
            image = image,
            prior = Mathf.Clamp01(prior),
            likelihood = likelihood,
            posterior = likelihood * Mathf.Clamp01(prior)
        });
    }

    private void ExecuteAction(AdaptiveAction action, CombatManager combatManager)
    {
        if (combatManager == null)
        {
            Debug.LogWarning("AdaptiveTouchManager accepted an action, but no CombatManager is available.");
            return;
        }

        switch (action)
        {
            case AdaptiveAction.Attack:
                combatManager.OnPlayerAttack();
                break;
            case AdaptiveAction.Dodge:
                combatManager.OnPlayerDodge();
                break;
            case AdaptiveAction.Heal:
                combatManager.OnPlayerHeal();
                break;
            case AdaptiveAction.Whirlwind:
                combatManager.OnPlayerWhirlwind();
                break;
        }
    }

    private string FormatCandidatePosteriors(List<ActionCandidate> candidates)
    {
        string result = "Posteriors:";
        for (int i = 0; i < candidates.Count; i++)
        {
            result += $" {candidates[i].label}={candidates[i].posterior:F3}";
        }

        return result;
    }

    private void CaptureBaseColorsIfNeeded()
    {
        if (capturedBaseColors)
        {
            return;
        }

        attackBaseColor = visualAttackButton != null ? visualAttackButton.color : normalColor;
        dodgeBaseColor = visualDodgeButton != null ? visualDodgeButton.color : normalColor;
        healBaseColor = visualHealButton != null ? visualHealButton.color : normalColor;
        whirlwindBaseColor = visualWhirlwindButton != null ? visualWhirlwindButton.color : normalColor;
        capturedBaseColors = true;
    }

    private Color GetBaseColor(AdaptiveAction action)
    {
        switch (action)
        {
            case AdaptiveAction.Dodge:
                return dodgeBaseColor;
            case AdaptiveAction.Heal:
                return healBaseColor;
            case AdaptiveAction.Whirlwind:
                return whirlwindBaseColor;
            default:
                return attackBaseColor;
        }
    }

    private void ResetButtonColor(Image image, Color color)
    {
        if (image != null)
        {
            image.color = color;
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

    private void UpdateSkillCooldownLabels(CombatManager combatManager)
    {
        PlayerController player = combatManager != null ? combatManager.playerController : null;

        if (healButtonLabel != null)
        {
            healButtonLabel.text = player != null && player.HealCooldownRemaining > 0f
                ? $"{player.HealCooldownRemaining:F1}s"
                : "Heal";
            healButtonLabel.fontSize = player != null && player.HealCooldownRemaining > 0f ? 28f : 24f;
        }

        if (whirlwindButtonLabel != null)
        {
            whirlwindButtonLabel.text = player != null && player.WhirlwindCooldownRemaining > 0f
                ? $"{player.WhirlwindCooldownRemaining:F1}s"
                : "Whirlwind";
            whirlwindButtonLabel.fontSize = player != null && player.WhirlwindCooldownRemaining > 0f ? 28f : 17f;
        }
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
