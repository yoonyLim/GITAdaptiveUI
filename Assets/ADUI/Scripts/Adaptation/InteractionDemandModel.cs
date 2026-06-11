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
        demand.mode = useManualMode ? RuntimeMode(manualMode) : SelectMode(demand);
        currentDemand = demand;
        return demand;
    }

    public ADUIInteractionDemand Evaluate(CombatManager.CombatContext context, PlayerController playerController, float recentActionRate)
    {
        var demand = new ADUIInteractionDemand();

        float normalizedHp = playerController != null && playerController.maxHP > 0
            ? Mathf.Clamp01((float)playerController.CurrentHP / playerController.maxHP)
            : 1f;
        bool joystickActive = playerController != null && playerController.IsVirtualMoveActive;
        bool moving = playerController != null && playerController.IsMoving;
        bool lowHp = normalizedHp > 0f && normalizedHp <= 0.38f;
        bool mediumHp = normalizedHp > 0f && normalizedHp <= 0.62f;
        bool healRelevant = playerController != null && (playerController.CanHeal || playerController.HealCooldownRemaining > 0f || mediumHp);
        bool whirlwindRelevant = playerController != null && (playerController.CanWhirlwind || playerController.WhirlwindCooldownRemaining > 0f);

        float crowdPressure = Mathf.Clamp01(context.closeEnemies / 5f);
        float targetPressure = Mathf.Clamp01((context.enemiesInAttackRange + context.attackCommitTargets) / 4f);
        demand.actionIntensity = Mathf.Clamp01(
            recentActionRate * 0.36f +
            crowdPressure * 0.26f +
            targetPressure * 0.2f +
            Mathf.Clamp01(context.totalEnemies / 8f) * 0.18f);

        float immediateThreat = Mathf.Clamp01(context.immediateThreats);
        float projectileThreat = Mathf.Clamp01(context.projectileThreats);
        float telegraphThreat = Mathf.Clamp01(context.telegraphingEnemies + context.attackingEnemies);
        float preDodge = Mathf.Clamp01(context.preDodgeEnemies + context.movingTowardDangerEnemies);
        demand.temporalUrgency = Mathf.Clamp01(
            immediateThreat * 0.48f +
            projectileThreat * 0.22f +
            telegraphThreat * 0.18f +
            preDodge * 0.32f +
            (lowHp ? 0.12f : 0f));

        float bossOrRangedInfo = Mathf.Clamp01((context.bossEnemies * 1.4f + context.rangedEnemies * 0.45f) / 2.2f);
        float cooldownInfo = Mathf.Clamp01((healRelevant ? 0.35f : 0f) + (whirlwindRelevant && context.closeEnemies >= 2 ? 0.25f : 0f));
        float stateInfo = Mathf.Clamp01(
            bossOrRangedInfo * 0.32f +
            projectileThreat * 0.24f +
            telegraphThreat * 0.18f +
            (lowHp ? 0.22f : mediumHp ? 0.12f : 0f) +
            cooldownInfo);
        demand.informationPriority = Mathf.Max(Mathf.Clamp01(stateInfo), externalInformationPriority);

        float rightSideThreatOverlap = Mathf.Clamp01(
            (context.projectileThreats > 0 ? 0.35f : 0f) +
            (context.rangedEnemies > 0 ? 0.18f : 0f) +
            (context.closeEnemies > 0 && demand.actionIntensity > 0.45f ? 0.18f : 0f));
        demand.occlusionRisk = Mathf.Max(
            Mathf.Clamp01(demand.informationPriority * 0.48f + demand.actionIntensity * 0.28f + rightSideThreatOverlap),
            externalOcclusionRisk);

        demand.controlContinuity = Mathf.Clamp01(
            (joystickActive ? 0.34f : 0f) +
            (moving ? 0.22f : 0f) +
            Mathf.Clamp01(context.movingTowardDangerEnemies / 2f) * 0.24f +
            recentActionRate * 0.2f);

        demand.uiSkill = Mathf.Clamp01(1f - (recentInvalidRate * 0.62f + recentOvercorrectionRisk * 0.38f));
        demand.mode = useManualMode ? RuntimeMode(manualMode) : SelectMode(demand);
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
        if (demand.temporalUrgency >= 0.62f ||
            demand.actionIntensity >= 0.7f ||
            (demand.controlContinuity >= 0.58f && demand.temporalUrgency >= 0.28f))
        {
            return ADUIInteractionMode.ActionFirst;
        }

        if (demand.uiSkill <= lowSkillThreshold || recentInvalidRate >= guidanceMistakeThreshold)
        {
            return ADUIInteractionMode.ActionFirst;
        }

        if (demand.informationPriority >= 0.65f || demand.occlusionRisk >= 0.7f)
        {
            return ADUIInteractionMode.CognitiveFirst;
        }

        return demand.CognitiveNeed > demand.ErrorToleranceNeed
            ? ADUIInteractionMode.CognitiveFirst
            : ADUIInteractionMode.ActionFirst;
    }

    private ADUIInteractionMode RuntimeMode(ADUIInteractionMode requestedMode)
    {
        return requestedMode == ADUIInteractionMode.CognitiveFirst ||
               requestedMode == ADUIInteractionMode.LearningReview
            ? ADUIInteractionMode.CognitiveFirst
            : ADUIInteractionMode.ActionFirst;
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
