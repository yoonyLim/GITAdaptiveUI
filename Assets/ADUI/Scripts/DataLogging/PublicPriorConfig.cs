using System;
using System.IO;
using UnityEngine;

public class PublicPriorConfig : MonoBehaviour
{
    public float recommendedDefaultVariance = 180f * 180f;
    public float recommendedExpandedHitboxMargin = 1.25f;
    public float recommendedAmbiguityMarginPx = 60f;
    public string source = "unity_default";
    [TextArea]
    public string priorWarning = "Public datasets provide priors only; Unity telemetry validates Attack/Dodge.";

    public string streamingAssetsRelativePath = "ADUI/public_touch_prior_config.json";

    public bool LoadPublicDefaults()
    {
        var persistentPath = Path.Combine(Application.persistentDataPath, "adui_public_defaults", "public_touch_prior_config.json");
        var streamingPath = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath);
        var path = File.Exists(persistentPath) ? persistentPath : streamingPath;
        if (!File.Exists(path)) return false;
        var json = File.ReadAllText(path);
        var config = JsonUtility.FromJson<PublicTouchPriorConfigJson>(json);
        if (config == null) return false;
        source = string.IsNullOrEmpty(config.source) ? source : config.source;
        recommendedDefaultVariance = config.recommended_default_variance > 0f ? config.recommended_default_variance : recommendedDefaultVariance;
        recommendedExpandedHitboxMargin = config.recommended_expanded_hitbox_margin > 0f ? config.recommended_expanded_hitbox_margin : recommendedExpandedHitboxMargin;
        recommendedAmbiguityMarginPx = config.recommended_ambiguity_margin_px > 0f ? config.recommended_ambiguity_margin_px : recommendedAmbiguityMarginPx;
        priorWarning = string.IsNullOrEmpty(config.prior_warning) ? priorWarning : config.prior_warning;
        return true;
    }

    public void ApplyTo(UserTouchModel userTouchModel, BayesianInputDecoder decoder)
    {
        if (userTouchModel) userTouchModel.ConfigureFromPublicDefault(recommendedDefaultVariance);
        if (decoder)
        {
            decoder.publicPriorSource = source;
            decoder.publicVarianceSource = source;
        }
    }

    [Serializable]
    private class PublicTouchPriorConfigJson
    {
        public string source;
        public float recommended_default_variance;
        public float recommended_default_std_px;
        public float recommended_expanded_hitbox_margin;
        public float recommended_ambiguity_margin_px;
        public string prior_warning;
    }
}

