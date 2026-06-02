using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem; 
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
    [Tooltip("Represents the player's fat finger spread/variance. Higher = wider forgiving area.")]
    [Range(50f, 400f)]
    public float userTouchVariance = 180f; 
    
    [Tooltip("Spatial Likelihood: less than this is ignored (e.g.: 0.05 = 5%)")]
    [Range(0.01f, 0.5f)]
    public float minLikelihoodThreshold = 0.05f;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();
    }
    
    private void Update()
    {
        float attackScreenRadius = CalculateDynamicRadius(CombatManager.Instance.priorAttack);
        float dodgeScreenRadius = CalculateDynamicRadius(CombatManager.Instance.priorDodge);
        
        float scaleFactor = mainCanvas ? mainCanvas.scaleFactor : 1f;
        
        float attackUiSize = (attackScreenRadius * 2f) / scaleFactor;
        float dodgeUiSize = (dodgeScreenRadius * 2f) / scaleFactor;
        
        if (attackHitboxVisualizer) attackHitboxVisualizer.sizeDelta = new Vector2(attackUiSize, attackUiSize);
        if (dodgeHitboxVisualizer) dodgeHitboxVisualizer.sizeDelta = new Vector2(dodgeUiSize, dodgeUiSize);

        // for unity editor testing
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
        
        // for mobile touch
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
        // get the screen-space center positions directly
        Vector2 attackCenter = visualAttackButton.rectTransform.position;
        Vector2 dodgeCenter = visualDodgeButton.rectTransform.position;

        // calculate Spatial Likelihood P(I|A)
        float distToAttack = Vector2.Distance(inputPos, attackCenter);
        float distToDodge = Vector2.Distance(inputPos, dodgeCenter);

        // Gaussian spatial likelihood P(I|A) calculation
        float likelihoodAttack = CalculateGaussianLikelihood(distToAttack, userTouchVariance);
        float likelihoodDodge = CalculateGaussianLikelihood(distToDodge, userTouchVariance);
        
        // contextual prior P(A) from Game State
        float priorAttack = CombatManager.Instance.priorAttack;
        float priorDodge = CombatManager.Instance.priorDodge;
        
        // augmented Bayesian Decoder: P(A|I) ∝ P(I|A) * P(A)
        float posteriorAttack = likelihoodAttack * priorAttack;
        float posteriorDodge = likelihoodDodge * priorDodge;
        
        // dynamic threshold
        float effectiveThreshold = minLikelihoodThreshold * 0.5f;
        
        if (posteriorAttack < effectiveThreshold && posteriorDodge < effectiveThreshold)
        {
            Debug.Log($"<color=grey>[Invalid Touch]</color> Posterior likelihood out of threshold({effectiveThreshold}). (Attack posterior: {posteriorAttack:F4}, Dodge posterior: {posteriorDodge:F4})");
            return;
        }
        
        string logContext = CombatManager.Instance.currentState == CombatManager.CombatState.Safe ? "Safe" : "Urgent";

        // 5. Execute Action and Apply Visual Feedback
        if (posteriorAttack > posteriorDodge)
        {
            Debug.Log($"<color=red>[Action Executed] ATTACK!</color> (Enemy State: {logContext})");
            visualAttackButton.color = pressedColor;
            CombatManager.Instance.OnPlayerAttack();
        }
        else
        {
            Debug.Log($"<color=blue>[Action Executed] DODGE!</color> (Enemy State: {logContext})");
            visualDodgeButton.color = pressedColor;
            CombatManager.Instance.OnPlayerDodge();
        }
    }

    private void ProcessInputEnded()
    {
        visualAttackButton.color = normalColor;
        visualDodgeButton.color = normalColor;
    }

    private float CalculateGaussianLikelihood(float distance, float variance)
    {
        float safeVariance = Mathf.Max(variance, 0.1f); 
        return Mathf.Exp(-(distance * distance) / (2 * safeVariance * safeVariance));
    }
    
    private float CalculateDynamicRadius(float prior)
    {
        // adjust threshold according to the current prior probability; neutral: 0.5
        float effectiveThresholdRatio = (minLikelihoodThreshold * 0.5f) / prior;
        
        // clamp to prevent log NAN
        effectiveThresholdRatio = Mathf.Clamp(effectiveThresholdRatio, 0.0001f, 0.999f);
        
        // Inverse Gaussian
        return userTouchVariance * Mathf.Sqrt(-2f * Mathf.Log(effectiveThresholdRatio));
    }
}