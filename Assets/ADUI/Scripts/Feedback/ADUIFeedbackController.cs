using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ADUIFeedbackController : MonoBehaviour
{
    public Text feedbackText;
    public Image attackButtonImage;
    public Image dodgeButtonImage;
    public Color correctionColor = new Color(1f, 0.86f, 0.3f, 1f);
    public Color invalidColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    public float pulseSeconds = 0.12f;

    private Coroutine activePulse;
    public string LastFeedbackMessage { get; private set; } = "";
    public bool LastHapticTriggered { get; private set; }

    public void ShowDecisionFeedback(ADUIDecodeResult result, ADUIAdjustmentPolicy policy)
    {
        if (result == null) return;
        LastHapticTriggered = false;
        var level = FeedbackLevel(policy);
        var message = BuildMessage(result);
        LastFeedbackMessage = message;
        if (feedbackText && level != ADUIFeedbackLevel.None)
        {
            feedbackText.text = message;
            feedbackText.color = result.invalidTouch ? invalidColor : correctionColor;
        }

        if (level != ADUIFeedbackLevel.None)
        {
            var target = result.finalExecutedAction == ADUIAction.Attack
                ? attackButtonImage
                : result.finalExecutedAction == ADUIAction.Dodge
                    ? dodgeButtonImage
                    : null;
            if (target)
            {
                if (activePulse != null) StopCoroutine(activePulse);
                activePulse = StartCoroutine(Pulse(target, result.invalidTouch ? invalidColor : correctionColor));
            }
        }

        if (policy != null && policy.hapticEnabled && level != ADUIFeedbackLevel.None)
        {
            Handheld.Vibrate();
            LastHapticTriggered = true;
        }
    }

    private ADUIFeedbackLevel FeedbackLevel(ADUIAdjustmentPolicy policy)
    {
        if (policy == null) return ADUIFeedbackLevel.Subtle;
        if (policy.feedbackIntensity <= 0.05f) return ADUIFeedbackLevel.None;
        if (policy.feedbackIntensity < 0.4f) return ADUIFeedbackLevel.Subtle;
        if (policy.feedbackIntensity < 0.75f) return ADUIFeedbackLevel.Standard;
        return ADUIFeedbackLevel.Strong;
    }

    private string BuildMessage(ADUIDecodeResult result)
    {
        if (result.invalidTouch) return "Invalid touch";
        if (result.safetyGatePassed) return $"Corrected to {result.finalExecutedAction}";
        if (result.safetyGateReason == "preserve_clear_visual_input") return $"Clear {result.finalExecutedAction}";
        return result.finalExecutedAction.ToString();
    }

    private IEnumerator Pulse(Image image, Color color)
    {
        var original = image.color;
        image.color = color;
        yield return new WaitForSeconds(pulseSeconds);
        image.color = original;
    }
}
