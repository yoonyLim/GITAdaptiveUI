using UnityEngine;
using UnityEngine.UI;

public class AdaptiveUIAdjustmentController : MonoBehaviour
{
    [Header("Targets")]
    public CanvasGroup gameplayHudGroup;
    public CanvasGroup guidanceGroup;
    public CanvasGroup reviewGroup;
    public Image attackButtonImage;
    public Image dodgeButtonImage;
    public Image healButtonImage;
    public Image whirlwindButtonImage;
    public RectTransform attackButtonRoot;
    public RectTransform dodgeButtonRoot;
    public RectTransform healButtonRoot;
    public RectTransform whirlwindButtonRoot;

    [Header("Colors")]
    public Color normalTint = Color.white;
    public Color emphasizedAttackTint = new Color(1f, 0.72f, 0.72f, 1f);
    public Color emphasizedDodgeTint = new Color(0.65f, 0.82f, 1f, 1f);
    public Color emphasizedUtilityTint = new Color(0.72f, 1f, 0.84f, 1f);
    public Color emphasizedSkillTint = new Color(1f, 0.82f, 0.48f, 1f);

    [Header("Position Bounds")]
    public RectTransform safeArea;
    public float maxPolicyShiftPx = 36f;

    public ADUIAdjustmentPolicy currentPolicy = new ADUIAdjustmentPolicy();

    private Vector2 attackInitialAnchoredPosition;
    private Vector2 dodgeInitialAnchoredPosition;
    private Vector2 healInitialAnchoredPosition;
    private Vector2 whirlwindInitialAnchoredPosition;
    private Color attackInitialColor;
    private Color dodgeInitialColor;
    private Color healInitialColor;
    private Color whirlwindInitialColor;
    private bool capturedInitialPositions;

    public void ApplyPolicy(ADUIAdjustmentPolicy policy)
    {
        if (policy == null) return;
        CaptureInitialPositions();
        currentPolicy = policy;

        if (gameplayHudGroup)
        {
            gameplayHudGroup.alpha = Mathf.Clamp(policy.visibility, 0.25f, 1f);
        }

        if (guidanceGroup)
        {
            guidanceGroup.alpha = policy.showGuidance ? 1f : 0f;
            guidanceGroup.interactable = policy.showGuidance;
            guidanceGroup.blocksRaycasts = policy.showGuidance;
        }

        if (reviewGroup)
        {
            reviewGroup.alpha = policy.showReview ? 1f : 0f;
            reviewGroup.interactable = policy.showReview;
            reviewGroup.blocksRaycasts = policy.showReview;
        }

        if (attackButtonImage)
        {
            attackButtonImage.color = Color.Lerp(attackInitialColor, emphasizedAttackTint, policy.emphasis * 0.55f);
        }
        if (dodgeButtonImage)
        {
            dodgeButtonImage.color = Color.Lerp(dodgeInitialColor, emphasizedDodgeTint, policy.emphasis * 0.55f);
        }
        if (healButtonImage)
        {
            healButtonImage.color = Color.Lerp(healInitialColor, emphasizedUtilityTint, policy.emphasis * 0.42f);
        }
        if (whirlwindButtonImage)
        {
            whirlwindButtonImage.color = Color.Lerp(whirlwindInitialColor, emphasizedSkillTint, policy.emphasis * 0.42f);
        }

        ApplyPositionConstraint(policy);
    }

    private void CaptureInitialPositions()
    {
        if (capturedInitialPositions) return;
        if (attackButtonRoot) attackInitialAnchoredPosition = attackButtonRoot.anchoredPosition;
        if (dodgeButtonRoot) dodgeInitialAnchoredPosition = dodgeButtonRoot.anchoredPosition;
        if (healButtonRoot) healInitialAnchoredPosition = healButtonRoot.anchoredPosition;
        if (whirlwindButtonRoot) whirlwindInitialAnchoredPosition = whirlwindButtonRoot.anchoredPosition;
        attackInitialColor = attackButtonImage ? attackButtonImage.color : normalTint;
        dodgeInitialColor = dodgeButtonImage ? dodgeButtonImage.color : normalTint;
        healInitialColor = healButtonImage ? healButtonImage.color : normalTint;
        whirlwindInitialColor = whirlwindButtonImage ? whirlwindButtonImage.color : normalTint;
        capturedInitialPositions = true;
    }

    private void ApplyPositionConstraint(ADUIAdjustmentPolicy policy)
    {
        var shift = Mathf.Lerp(maxPolicyShiftPx, 0f, policy.positionConstraint);
        if (attackButtonRoot)
        {
            attackButtonRoot.anchoredPosition = ClampToSafeArea(attackInitialAnchoredPosition + new Vector2(-shift, 0f));
        }
        if (dodgeButtonRoot)
        {
            dodgeButtonRoot.anchoredPosition = ClampToSafeArea(dodgeInitialAnchoredPosition + new Vector2(shift, 0f));
        }
        if (healButtonRoot)
        {
            healButtonRoot.anchoredPosition = ClampToSafeArea(healInitialAnchoredPosition + new Vector2(-shift * 0.35f, -shift * 0.25f));
        }
        if (whirlwindButtonRoot)
        {
            whirlwindButtonRoot.anchoredPosition = ClampToSafeArea(whirlwindInitialAnchoredPosition + new Vector2(shift * 0.35f, -shift * 0.25f));
        }
    }

    private Vector2 ClampToSafeArea(Vector2 position)
    {
        if (!safeArea) return position;
        var rect = safeArea.rect;
        return new Vector2(
            Mathf.Clamp(position.x, rect.xMin, rect.xMax),
            Mathf.Clamp(position.y, rect.yMin, rect.yMax)
        );
    }
}

