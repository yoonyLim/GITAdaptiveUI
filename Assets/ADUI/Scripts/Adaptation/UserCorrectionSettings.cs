using UnityEngine;
using UnityEngine.UI;

public class UserCorrectionSettings : MonoBehaviour
{
    [Header("Runtime Controls")]
    public Slider correctionStrengthSlider;
    public Toggle correctionEnabledToggle;
    public Toggle hapticEnabledToggle;
    public Text correctionStrengthLabel;

    [Header("Targets")]
    public BayesianInputDecoder decoder;
    public SafetyGate safetyGate;

    [Range(0f, 1f)] public float correctionStrength = 0.5f;
    public bool correctionEnabled = true;
    public bool hapticEnabled = false;

    private void Start()
    {
        if (correctionStrengthSlider)
        {
            correctionStrengthSlider.value = correctionStrength;
            correctionStrengthSlider.onValueChanged.AddListener(SetCorrectionStrength);
        }
        if (correctionEnabledToggle)
        {
            correctionEnabledToggle.isOn = correctionEnabled;
            correctionEnabledToggle.onValueChanged.AddListener(SetCorrectionEnabled);
        }
        if (hapticEnabledToggle)
        {
            hapticEnabledToggle.isOn = hapticEnabled;
            hapticEnabledToggle.onValueChanged.AddListener(SetHapticEnabled);
        }
        Apply();
    }

    public void ApplyPolicy(ADUIAdjustmentPolicy policy)
    {
        if (policy == null) return;
        if (!correctionEnabled)
        {
            policy.correctionStrength = 0f;
            policy.hapticEnabled = false;
            return;
        }
        policy.correctionStrength = Mathf.Clamp01(policy.correctionStrength * Mathf.Lerp(0.25f, 1.25f, correctionStrength));
        policy.hapticEnabled = policy.hapticEnabled && hapticEnabled;
    }

    public void SetCorrectionStrength(float value)
    {
        correctionStrength = Mathf.Clamp01(value);
        Apply();
    }

    public void SetCorrectionEnabled(bool enabled)
    {
        correctionEnabled = enabled;
        Apply();
    }

    public void SetHapticEnabled(bool enabled)
    {
        hapticEnabled = enabled;
        Apply();
    }

    private void Apply()
    {
        if (decoder)
        {
            decoder.priorStrength = Mathf.Lerp(0.5f, 1.5f, correctionStrength);
        }
        if (safetyGate)
        {
            safetyGate.enabled = correctionEnabled;
        }
        if (correctionStrengthLabel)
        {
            correctionStrengthLabel.text = $"Correction {Mathf.RoundToInt(correctionStrength * 100f)}%";
        }
    }
}

