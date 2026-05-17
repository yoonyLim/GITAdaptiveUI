using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class AdaptiveTouchManager : MonoBehaviour
{
    [Header("Visual Button Transforms (Read-Only)")]
    public RectTransform visualAttackButton;
    public RectTransform visualDodgeButton;

    [Header("Touch Tuning")]
    // The assumed variance (spread) of the player's thumb. 
    public float userTouchVariance = 150f; 

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += HandleRawTouch;
    }

    private void OnDisable()
    {
        Touch.onFingerDown -= HandleRawTouch;
        EnhancedTouchSupport.Disable();
    }

    private void HandleRawTouch(Finger finger)
    {
        Vector2 touchPos = finger.screenPosition;

        // 1. Get Visual Centers of the static UI buttons
        Vector2 attackCenter = RectTransformUtility.PixelAdjustPoint(visualAttackButton.position, transform, null);
        Vector2 dodgeCenter = RectTransformUtility.PixelAdjustPoint(visualDodgeButton.position, transform, null);

        // 2. Calculate Spatial Likelihood P(I|A) using a simplified Gaussian distance
        float distToAttack = Vector2.Distance(touchPos, attackCenter);
        float distToDodge = Vector2.Distance(touchPos, dodgeCenter);

        float likelihoodAttack = CalculateGaussianLikelihood(distToAttack, userTouchVariance);
        float likelihoodDodge = CalculateGaussianLikelihood(distToDodge, userTouchVariance);
        
        float priorAttack = GameStateManager.Instance.priorAttack;
        float priorDodge = GameStateManager.Instance.priorDodge;
        
        float posteriorAttack = likelihoodAttack * priorAttack;
        float posteriorDodge = likelihoodDodge * priorDodge;

        // 5. Execute Intended Action
        if (posteriorAttack > posteriorDodge)
        {
            Debug.Log($"[Action Executed] ATTACK! (Raw Touch: {touchPos})");
        }
        else
        {
            Debug.Log($"[Action Executed] DODGE! (Raw Touch: {touchPos})");
        }
    }

    private float CalculateGaussianLikelihood(float distance, float variance)
    {
        // A simplified 2D Gaussian probability density function for real-time calculation
        return Mathf.Exp(-(distance * distance) / (2 * variance * variance));
    }
}