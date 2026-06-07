using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class UserTouchModel : MonoBehaviour
{
    [Header("Defaults")]
    public float publicDefaultVariance = 180f * 180f;
    public float minVariance = 35f * 35f;
    public float maxVariance = 420f * 420f;

    [Header("Learned Attack Profile")]
    public Vector2 attackMean = Vector2.zero;
    public float attackVariance = 180f * 180f;
    public int attackSampleCount;

    [Header("Learned Dodge Profile")]
    public Vector2 dodgeMean = Vector2.zero;
    public float dodgeVariance = 180f * 180f;
    public int dodgeSampleCount;

    [Header("Online Adaptation")]
    public bool enableOnlineAdaptation = true;
    [Range(0.001f, 0.2f)] public float onlineLearningRate = 0.035f;
    public int minimumSamplesBeforeOnlineAdaptation = 8;

    private readonly List<Vector2> attackSamples = new List<Vector2>();
    private readonly List<Vector2> dodgeSamples = new List<Vector2>();
    private float attackReferenceRadius = 180f;
    private float dodgeReferenceRadius = 180f;

    public bool HasCalibration => attackSampleCount > 0 && dodgeSampleCount > 0;

    public void ConfigureFromPublicDefault(float defaultVariance)
    {
        publicDefaultVariance = ClampVariance(defaultVariance);
        if (attackSampleCount == 0) attackVariance = publicDefaultVariance;
        if (dodgeSampleCount == 0) dodgeVariance = publicDefaultVariance;
    }

    public Vector2 ToRelative(Vector2 touchPosition, ADUIButtonGeometry button)
    {
        var radius = Mathf.Max(button.visualRadius, 1f);
        return new Vector2((touchPosition.x - button.centerX) / radius, (touchPosition.y - button.centerY) / radius);
    }

    public void AddCalibrationSample(ADUIAction action, Vector2 touchPosition, ADUIButtonGeometry button)
    {
        if (action == ADUIAction.Attack)
        {
            attackReferenceRadius = Mathf.Max(button.visualRadius, 1f);
            attackSamples.Add(ToRelative(touchPosition, button));
        }
        else if (action == ADUIAction.Dodge)
        {
            dodgeReferenceRadius = Mathf.Max(button.visualRadius, 1f);
            dodgeSamples.Add(ToRelative(touchPosition, button));
        }

        Recompute();
    }

    public void AddOnlineAdaptationSample(ADUIAction action, Vector2 touchPosition, ADUIButtonGeometry button)
    {
        if (!enableOnlineAdaptation)
        {
            return;
        }

        if (action == ADUIAction.Attack && attackSampleCount >= minimumSamplesBeforeOnlineAdaptation)
        {
            BlendProfileSample(action, ToRelative(touchPosition, button), Mathf.Max(button.visualRadius, 1f));
        }
        else if (action == ADUIAction.Dodge && dodgeSampleCount >= minimumSamplesBeforeOnlineAdaptation)
        {
            BlendProfileSample(action, ToRelative(touchPosition, button), Mathf.Max(button.visualRadius, 1f));
        }
    }

    public void ResetCalibration()
    {
        attackSamples.Clear();
        dodgeSamples.Clear();
        attackMean = Vector2.zero;
        dodgeMean = Vector2.zero;
        attackVariance = publicDefaultVariance;
        dodgeVariance = publicDefaultVariance;
        attackSampleCount = 0;
        dodgeSampleCount = 0;
        attackReferenceRadius = 180f;
        dodgeReferenceRadius = 180f;
    }

    public float SpatialLikelihood(ADUIAction action, Vector2 touchPosition, ADUIButtonGeometry button)
    {
        var relative = ToRelative(touchPosition, button);
        var mean = action == ADUIAction.Attack ? attackMean : dodgeMean;
        var variance = action == ADUIAction.Attack ? attackVariance : dodgeVariance;
        var normalizedDistanceSq = (relative - mean).sqrMagnitude;
        return Mathf.Exp(-normalizedDistanceSq / (2f * Mathf.Max(variance / (button.visualRadius * button.visualRadius), 0.0001f)));
    }

    public float GetVariance(ADUIAction action)
    {
        return action == ADUIAction.Attack ? attackVariance : dodgeVariance;
    }

    public void Recompute()
    {
        attackSampleCount = attackSamples.Count;
        dodgeSampleCount = dodgeSamples.Count;
        if (attackSamples.Count > 0)
        {
            attackMean = Mean(attackSamples);
            attackVariance = RelativeVarianceToScreenVariance(attackSamples, attackMean, attackReferenceRadius);
        }
        if (dodgeSamples.Count > 0)
        {
            dodgeMean = Mean(dodgeSamples);
            dodgeVariance = RelativeVarianceToScreenVariance(dodgeSamples, dodgeMean, dodgeReferenceRadius);
        }
    }

    public void SaveProfile(string path)
    {
        var profile = new UserTouchProfile
        {
            attackMeanX = attackMean.x,
            attackMeanY = attackMean.y,
            attackVariance = attackVariance,
            attackSampleCount = attackSampleCount,
            dodgeMeanX = dodgeMean.x,
            dodgeMeanY = dodgeMean.y,
            dodgeVariance = dodgeVariance,
            dodgeSampleCount = dodgeSampleCount,
            publicDefaultVariance = publicDefaultVariance,
            attackReferenceRadius = attackReferenceRadius,
            dodgeReferenceRadius = dodgeReferenceRadius
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(profile, true));
    }

    public void LoadProfile(string path)
    {
        if (!File.Exists(path)) return;
        var profile = JsonUtility.FromJson<UserTouchProfile>(File.ReadAllText(path));
        attackMean = new Vector2(profile.attackMeanX, profile.attackMeanY);
        dodgeMean = new Vector2(profile.dodgeMeanX, profile.dodgeMeanY);
        attackVariance = ClampVariance(profile.attackVariance);
        dodgeVariance = ClampVariance(profile.dodgeVariance);
        attackSampleCount = profile.attackSampleCount;
        dodgeSampleCount = profile.dodgeSampleCount;
        publicDefaultVariance = ClampVariance(profile.publicDefaultVariance);
        attackReferenceRadius = profile.attackReferenceRadius > 0f ? profile.attackReferenceRadius : attackReferenceRadius;
        dodgeReferenceRadius = profile.dodgeReferenceRadius > 0f ? profile.dodgeReferenceRadius : dodgeReferenceRadius;
    }

    private Vector2 Mean(List<Vector2> samples)
    {
        var sum = Vector2.zero;
        foreach (var sample in samples) sum += sample;
        return sum / Mathf.Max(samples.Count, 1);
    }

    private void BlendProfileSample(ADUIAction action, Vector2 relativeTouch, float referenceRadius)
    {
        float rate = Mathf.Clamp01(onlineLearningRate);
        if (action == ADUIAction.Attack)
        {
            Vector2 nextMean = Vector2.Lerp(attackMean, relativeTouch, rate);
            float sampleVariance = (relativeTouch - nextMean).sqrMagnitude * referenceRadius * referenceRadius;
            attackMean = nextMean;
            attackVariance = ClampVariance(Mathf.Lerp(attackVariance, Mathf.Max(sampleVariance, minVariance), rate));
            attackSampleCount++;
            attackReferenceRadius = referenceRadius;
        }
        else if (action == ADUIAction.Dodge)
        {
            Vector2 nextMean = Vector2.Lerp(dodgeMean, relativeTouch, rate);
            float sampleVariance = (relativeTouch - nextMean).sqrMagnitude * referenceRadius * referenceRadius;
            dodgeMean = nextMean;
            dodgeVariance = ClampVariance(Mathf.Lerp(dodgeVariance, Mathf.Max(sampleVariance, minVariance), rate));
            dodgeSampleCount++;
            dodgeReferenceRadius = referenceRadius;
        }
    }

    private float RelativeVarianceToScreenVariance(List<Vector2> samples, Vector2 mean, float referenceRadius)
    {
        if (samples.Count < 2) return publicDefaultVariance;
        var total = 0f;
        foreach (var sample in samples) total += (sample - mean).sqrMagnitude;
        var relativeVariance = total / samples.Count;
        return ClampVariance(relativeVariance * Mathf.Max(referenceRadius, 1f) * Mathf.Max(referenceRadius, 1f));
    }

    private float ClampVariance(float variance)
    {
        return Mathf.Clamp(variance, minVariance, maxVariance);
    }

    [Serializable]
    private class UserTouchProfile
    {
        public float attackMeanX;
        public float attackMeanY;
        public float attackVariance;
        public int attackSampleCount;
        public float dodgeMeanX;
        public float dodgeMeanY;
        public float dodgeVariance;
        public int dodgeSampleCount;
        public float publicDefaultVariance;
        public float attackReferenceRadius;
        public float dodgeReferenceRadius;
    }
}

