using System;
using UnityEngine;

[Serializable]
public struct CombatActionPriorResult
{
    public float attackScore;
    public float dodgeScore;
    public float attackPrior;
    public float dodgePrior;
    public ADUIEnemyState enemyState;
    public string source;
}

public class CombatActionPriorBuilder : MonoBehaviour
{
    [Header("Base Scores")]
    public float baseAttackScore = 0.85f;
    public float baseDodgeScore = 0.45f;

    [Header("Attack Opportunity Weights")]
    public float closestEnemyOpportunityWeight = 2.2f;
    public float closeEnemyAttackWeight = 0.12f;
    public float safeAttackTargetWeight = 1.65f;
    public float attackCommitTargetWeight = 1.35f;
    public float attackOpportunityScoreWeight = 0.35f;
    public float immediateThreatAttackPenalty = 0.45f;
    public float preDodgeAttackPenalty = 0.58f;
    public float riskyCloseAttackPenalty = 0.7f;
    public float rangedEnemyAttackBonus = 0.15f;
    public float bossEnemyAttackBonus = 0.45f;

    [Header("Dodge Pressure Weights")]
    public float telegraphThreatWeight = 2.2f;
    public float attackingThreatWeight = 2.8f;
    public float projectileThreatWeight = 2.45f;
    public float immediateThreatWeight = 2.65f;
    public float preDodgeEnemyWeight = 1.55f;
    public float movementTowardDangerWeight = 0.75f;
    public float dangerousCloseEnemyWeight = 0.9f;
    public float dodgeUrgencyScoreWeight = 0.42f;
    public float closeEnemyThreatWeight = 0.08f;
    public float closeEnemyCrowdBonus = 1.25f;
    public float heavyCrowdBonus = 0.85f;
    public float lowHpDodgeBonus = 0.75f;

    [Header("Thresholds")]
    public float attackRangeWindowBonus = 1.25f;
    public int closeCrowdThreshold = 4;
    public int heavyCrowdThreshold = 8;
    public float lowHpThreshold = 0.35f;
    public float telegraphDodgePriorThreshold = 0.62f;

    public CombatActionPriorResult Build(
        CombatManager.CombatContext context,
        PlayerController playerController,
        float playerAttackRange)
    {
        float attackScore = baseAttackScore;
        float dodgeScore = baseDodgeScore;

        if (context.totalEnemies <= 0)
        {
            return BuildResult(0.5f, 0.5f, ADUIEnemyState.Safe);
        }

        float rangeWindow = Mathf.Max(0.1f, playerAttackRange + attackRangeWindowBonus);
        if (context.closestEnemyDistance < float.MaxValue)
        {
            float closeness = Mathf.Clamp01((rangeWindow - context.closestEnemyDistance) / rangeWindow);
            attackScore += closeness * closestEnemyOpportunityWeight;
        }

        attackScore += context.closeEnemies * closeEnemyAttackWeight;
        attackScore += context.enemiesInAttackRange * closeEnemyAttackWeight;
        attackScore += context.safeAttackTargets * safeAttackTargetWeight;
        attackScore += context.attackCommitTargets * attackCommitTargetWeight;
        attackScore += context.attackOpportunityScore * attackOpportunityScoreWeight;
        attackScore += context.rangedEnemies * rangedEnemyAttackBonus;
        attackScore += context.bossEnemies * bossEnemyAttackBonus;

        int meleeImmediateThreats = Mathf.Max(0, context.immediateThreats - context.projectileThreats);
        dodgeScore += context.telegraphingEnemies * telegraphThreatWeight;
        dodgeScore += context.attackingEnemies * attackingThreatWeight;
        dodgeScore += meleeImmediateThreats * immediateThreatWeight;
        dodgeScore += context.projectileThreats * projectileThreatWeight;
        dodgeScore += context.preDodgeEnemies * preDodgeEnemyWeight;
        dodgeScore += context.movingTowardDangerEnemies * movementTowardDangerWeight;
        dodgeScore += context.dangerousCloseEnemies * dangerousCloseEnemyWeight;
        dodgeScore += context.dodgeUrgencyScore * dodgeUrgencyScoreWeight;
        dodgeScore += context.closeEnemies * closeEnemyThreatWeight;

        if (context.immediateThreats > 0 || context.projectileThreats > 0)
        {
            attackScore *= immediateThreatAttackPenalty;
        }
        else if (context.preDodgeEnemies > 0 || context.movingTowardDangerEnemies > 0)
        {
            attackScore *= preDodgeAttackPenalty;
        }
        else if (context.dangerousCloseEnemies > context.safeAttackTargets)
        {
            attackScore *= riskyCloseAttackPenalty;
        }

        if (context.closeEnemies >= closeCrowdThreshold)
        {
            dodgeScore += closeEnemyCrowdBonus;
        }

        if (context.closeEnemies >= heavyCrowdThreshold)
        {
            dodgeScore += heavyCrowdBonus;
        }

        if (playerController != null && playerController.maxHP > 0)
        {
            float normalizedHp = (float)playerController.CurrentHP / playerController.maxHP;
            if (normalizedHp <= lowHpThreshold)
            {
                dodgeScore += lowHpDodgeBonus;
            }
        }

        ADUIEnemyState state = InferEnemyState(context, attackScore, dodgeScore);
        return BuildResult(attackScore, dodgeScore, state);
    }

    public CombatActionPriorResult BuildStatePrior(
        CombatManager.CombatState state,
        string source = "sample_scene_state_prior")
    {
        switch (state)
        {
            case CombatManager.CombatState.Attacking:
                return BuildResult(0.05f, 0.95f, ADUIEnemyState.Attacking, source);
            case CombatManager.CombatState.Telegraph:
                return BuildResult(0.1f, 0.9f, ADUIEnemyState.Telegraph, source);
            case CombatManager.CombatState.Safe:
            default:
                return BuildResult(0.9f, 0.1f, ADUIEnemyState.Safe, source);
        }
    }

    private ADUIEnemyState InferEnemyState(CombatManager.CombatContext context, float attackScore, float dodgeScore)
    {
        if (context.immediateThreats > 0 || context.projectileThreats > 0)
        {
            return ADUIEnemyState.Attacking;
        }

        float total = Mathf.Max(0.001f, attackScore + dodgeScore);
        float dodgePrior = Mathf.Clamp(dodgeScore / total, 0.05f, 0.95f);
        if (context.telegraphingEnemies > 0 ||
            context.preDodgeEnemies > 0 ||
            context.movingTowardDangerEnemies > 0 ||
            context.dangerousCloseEnemies > context.safeAttackTargets ||
            dodgePrior >= telegraphDodgePriorThreshold)
        {
            return ADUIEnemyState.Telegraph;
        }

        return ADUIEnemyState.Safe;
    }

    private CombatActionPriorResult BuildResult(
        float attackScore,
        float dodgeScore,
        ADUIEnemyState state,
        string source = "combat_context_rule_prior")
    {
        float total = Mathf.Max(0.001f, attackScore + dodgeScore);
        float attackPrior = Mathf.Clamp(attackScore / total, 0.05f, 0.95f);
        float dodgePrior = 1f - attackPrior;

        return new CombatActionPriorResult
        {
            attackScore = attackScore,
            dodgeScore = dodgeScore,
            attackPrior = attackPrior,
            dodgePrior = dodgePrior,
            enemyState = state,
            source = source
        };
    }
}
