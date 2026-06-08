using UnityEngine;

public class VisionInferenceStub : MonoBehaviour
{
    [TextArea]
    public string note = "Runtime DINO/Sentis inference is intentionally not forced. Use offline replay first; low confidence falls back to neutral prior.";

    public ADUIVisionSituation LastSituation { get; private set; }

    public ADUIVisionSituation PredictNeutral()
    {
        LastSituation = new ADUIVisionSituation
        {
            threatLevel = ADUIThreatLevel.Unknown,
            actionWindow = ADUIActionWindow.Unknown,
            confidence = 0f,
            source = "runtime_stub_neutral"
        };
        return LastSituation;
    }
}

