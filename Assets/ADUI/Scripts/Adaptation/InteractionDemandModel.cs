using UnityEngine;

public class InteractionDemandModel : MonoBehaviour
{
    [Header("Manual Override")]
    public bool useManualMode;
    public ADUIInteractionMode manualMode = ADUIInteractionMode.ActionFirst;

    [Header("Demand Weights")]
    [Range(0f, 1f)] public float lowSkillThreshold = 0.35f;
    [Range(0f, 1f)] public float guidanceMistakeThreshold = 0.45f;
    [Range(0f, 1f)] public float externalInformationPriority;
    [Range(0f, 1f)] public float externalOcclusionRisk;

    [Header("Current Demand")]
    public ADUIInteractionDemand currentDemand = new ADUIInteractionDemand();

    private float recentInvalidRate;
    private float recentOvercorrectionRisk;

    public ADUIInteractionDemand Evaluate(ADUIEnemyState enemyState, bool dangerWarningVisible, float normalizedHp, float recentActionRate)
    {
        var demand = new ADUIInteractionDemand();
        demand.actionIntensity = Mathf.Clamp01(recentActionRate);
        demand.temporalUrgency = EnemyUrgency(enemyState, dangerWarningVisible, normalizedHp);
        var baseInformationPriority = enemyState == ADUIEnemyState.Telegraph || enemyState == ADUIEnemyState.Attacking ? 0.8f : 0.45f;
        demand.informationPriority = Mathf.Max(baseInformationPriority, externalInformationPriority);
        demand.occlusionRisk = Mathf.Max(
            Mathf.Clamp01(0.35f + demand.actionIntensity * 0.35f + demand.informationPriority * 0.2f),
            externalOcclusionRisk
        );
        demand.controlContinuity = Mathf.Clamp01(0.4f + demand.actionIntensity * 0.4f);
        demand.uiSkill = Mathf.Clamp01(1f - (recentInvalidRate * 0.6f + recentOvercorrectionRisk * 0.4f));
        demand.mode = useManualMode ? manualMode : SelectMode(demand);
        currentDemand = demand;
        return demand;
    }

    public void UpdateRecentErrorSignals(bool invalidTouch, bool overcorrectionRisk)
    {
        recentInvalidRate = Mathf.Lerp(recentInvalidRate, invalidTouch ? 1f : 0f, 0.15f);
        recentOvercorrectionRisk = Mathf.Lerp(recentOvercorrectionRisk, overcorrectionRisk ? 1f : 0f, 0.1f);
    }

    private ADUIInteractionMode SelectMode(ADUIInteractionDemand demand)
    {
        if (demand.temporalUrgency >= 0.65f || demand.actionIntensity >= 0.7f)
        {
            return ADUIInteractionMode.ActionFirst;
        }

        if (demand.uiSkill <= lowSkillThreshold || recentInvalidRate >= guidanceMistakeThreshold)
        {
            return ADUIInteractionMode.GuidanceProcedure;
        }

        if (demand.informationPriority >= 0.65f || demand.occlusionRisk >= 0.7f)
        {
            return ADUIInteractionMode.CognitiveFirst;
        }

        return ADUIInteractionMode.LearningReview;
    }

    private float EnemyUrgency(ADUIEnemyState enemyState, bool dangerWarningVisible, float normalizedHp)
    {
        var baseUrgency = 0.25f;
        switch (enemyState)
        {
            case ADUIEnemyState.Telegraph:
                baseUrgency = 0.75f;
                break;
            case ADUIEnemyState.Attacking:
            case ADUIEnemyState.Urgent:
                baseUrgency = 0.95f;
                break;
            case ADUIEnemyState.Safe:
            case ADUIEnemyState.Idle:
                baseUrgency = 0.25f;
                break;
            default:
                baseUrgency = 0.45f;
                break;
        }

        if (dangerWarningVisible) baseUrgency = Mathf.Max(baseUrgency, 0.8f);
        if (normalizedHp > 0f && normalizedHp < 0.35f) baseUrgency += 0.1f;
        return Mathf.Clamp01(baseUrgency);
    }
}
