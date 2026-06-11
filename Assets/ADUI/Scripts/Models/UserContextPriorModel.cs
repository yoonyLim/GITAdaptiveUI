using System;
using UnityEngine;

public enum ADUIContextScenario
{
    General,
    AttackOpportunity,
    DodgeThreat,
    LowHpHeal,
    CrowdWhirlwind,
    MovementThreat,
    LowHpThreat,
    CrowdLowHp,
    SafeAttackOpportunity,
    RiskyCloseEnemy,
    ImmediateDodgeThreat,
    ProjectileDodgeThreat,
    MovingUnderPressure,
    AttackCommitWindow,
    PreDodgeWindow
}

public class UserContextPriorModel : MonoBehaviour
{
    public const int ScenarioCount = 15;
    private const int ActionCount = 4;

    [Header("Bayesian Count Model")]
    [Range(0.1f, 5f)] public float alpha = 1f;
    [Range(1f, 24f)] public float matureSampleCount = 8f;
    [Range(0f, 1.5f)] public float userPriorStrength = 0.55f;
    [Range(0f, 1f)] public float onlineObservationWeight = 0.35f;
    [Range(0f, 3f)] public float calibrationObservationWeight = 1.4f;
    public bool enableOnlineContextAdaptation = true;

    private readonly float[,] scenarioActionCounts = new float[ScenarioCount, ActionCount];
    private readonly float[] scenarioTotals = new float[ScenarioCount];

    public void ResetUserPriors()
    {
        for (int scenario = 0; scenario < ScenarioCount; scenario++)
        {
            scenarioTotals[scenario] = 0f;
            for (int action = 0; action < ActionCount; action++)
            {
                scenarioActionCounts[scenario, action] = 0f;
            }
        }
    }

    public ADUIContextScenario Classify(CombatManager.CombatContext context, PlayerController playerController)
    {
        bool canHeal = playerController != null && playerController.CanHeal;
        bool canWhirlwind = playerController != null && playerController.CanWhirlwind;
        bool lowHp = playerController != null &&
                     playerController.maxHP > 0 &&
                     playerController.CurrentHP <= playerController.maxHP * 0.35f;
        bool moving = playerController != null && playerController.IsMoving;
        bool projectileThreat = context.projectileThreats > 0 || context.incomingProjectiles > 0;
        bool immediateMeleeThreat = context.attackingEnemies > 0 || context.telegraphingEnemies > 0;
        bool immediateThreat = projectileThreat || immediateMeleeThreat;
        bool preDodgeWindow = context.preDodgeEnemies > 0 || context.movingTowardDangerEnemies > 0;
        bool riskyClose = context.dangerousCloseEnemies > 0;
        bool pressure = immediateThreat || preDodgeWindow || riskyClose;
        bool crowded = context.whirlwindTargets >= 3 || context.closeEnemies >= 5;
        bool attackCommit = context.attackCommitTargets > 0 &&
                            !pressure &&
                            context.attackOpportunityScore >= context.dodgeUrgencyScore;

        if (lowHp && crowded && canHeal)
        {
            return ADUIContextScenario.CrowdLowHp;
        }

        if (lowHp && pressure && canHeal)
        {
            return ADUIContextScenario.LowHpThreat;
        }

        if (canWhirlwind && crowded)
        {
            return ADUIContextScenario.CrowdWhirlwind;
        }

        if (lowHp && canHeal)
        {
            return ADUIContextScenario.LowHpHeal;
        }

        if (projectileThreat)
        {
            return ADUIContextScenario.ProjectileDodgeThreat;
        }

        if (immediateMeleeThreat)
        {
            return ADUIContextScenario.ImmediateDodgeThreat;
        }

        if (moving && (riskyClose || context.movingTowardDangerEnemies > 0))
        {
            return ADUIContextScenario.MovingUnderPressure;
        }

        if (preDodgeWindow)
        {
            return ADUIContextScenario.PreDodgeWindow;
        }

        if (attackCommit)
        {
            return ADUIContextScenario.AttackCommitWindow;
        }

        if (riskyClose)
        {
            return ADUIContextScenario.RiskyCloseEnemy;
        }

        if (moving && context.closeEnemies > 0)
        {
            return ADUIContextScenario.MovementThreat;
        }

        if (context.attackingEnemies > 0 ||
            context.incomingProjectiles > 0 ||
            context.telegraphingEnemies > 0)
        {
            return ADUIContextScenario.DodgeThreat;
        }

        if (context.totalEnemies > 0)
        {
            float attackRange = playerController != null ? playerController.attackRange : 2f;
            if (context.closestEnemyDistance <= attackRange + 1.25f)
            {
                return ADUIContextScenario.AttackOpportunity;
            }
        }

        return ADUIContextScenario.General;
    }

    public void ApplyUserPriors(
        ADUIContextScenario scenario,
        ref float attackPrior,
        ref float dodgePrior,
        ref float healPrior,
        ref float whirlwindPrior)
    {
        float strength = EffectiveStrength(scenario);
        if (strength <= 0f)
        {
            return;
        }

        float attack = AdjustPrior(attackPrior, scenario, 0, strength);
        float dodge = AdjustPrior(dodgePrior, scenario, 1, strength);
        float heal = AdjustPrior(healPrior, scenario, 2, strength);
        float whirlwind = AdjustPrior(whirlwindPrior, scenario, 3, strength);
        Normalize(ref attack, ref dodge, ref heal, ref whirlwind);

        attackPrior = attack;
        dodgePrior = dodge;
        healPrior = heal;
        whirlwindPrior = whirlwind;
    }

    public bool RecordCalibrationResponse(ADUIContextScenario scenario, string actionName)
    {
        return AddObservation(scenario, actionName, calibrationObservationWeight, true);
    }

    public bool RecordOnlineResponse(ADUIContextScenario scenario, string actionName, float confidenceWeight = 1f)
    {
        if (!enableOnlineContextAdaptation)
        {
            return false;
        }

        float weight = onlineObservationWeight * Mathf.Clamp01(confidenceWeight);
        return AddObservation(scenario, actionName, weight, false);
    }

    public string Summary(ADUIContextScenario scenario)
    {
        return $"{ScenarioLabel(scenario)} user prior " +
               $"A={GetUserPrior(scenario, 0):P0} D={GetUserPrior(scenario, 1):P0} " +
               $"H={GetUserPrior(scenario, 2):P0} W={GetUserPrior(scenario, 3):P0} " +
               $"n={scenarioTotals[(int)scenario]:F1}";
    }

    public static string ScenarioLabel(ADUIContextScenario scenario)
    {
        switch (scenario)
        {
            case ADUIContextScenario.AttackOpportunity:
                return "공격 기회";
            case ADUIContextScenario.DodgeThreat:
                return "회피 위협";
            case ADUIContextScenario.LowHpHeal:
                return "낮은 HP";
            case ADUIContextScenario.CrowdWhirlwind:
                return "적 밀집";
            case ADUIContextScenario.MovementThreat:
                return "이동 중 위협";
            case ADUIContextScenario.LowHpThreat:
                return "낮은 HP + 위협";
            case ADUIContextScenario.CrowdLowHp:
                return "적 밀집 + 낮은 HP";
            case ADUIContextScenario.SafeAttackOpportunity:
                return "안전한 공격 창";
            case ADUIContextScenario.RiskyCloseEnemy:
                return "근접 위험";
            case ADUIContextScenario.ImmediateDodgeThreat:
                return "즉시 회피 위협";
            case ADUIContextScenario.ProjectileDodgeThreat:
                return "투사체 회피 위협";
            case ADUIContextScenario.MovingUnderPressure:
                return "이동 중 압박";
            case ADUIContextScenario.AttackCommitWindow:
                return "공격 확정 창";
            case ADUIContextScenario.PreDodgeWindow:
                return "회피 준비 창";
            default:
                return "일반 상황";
        }
    }

    public static string ScenarioInstruction(ADUIContextScenario scenario)
    {
        switch (scenario)
        {
            case ADUIContextScenario.AttackOpportunity:
                return "전투 캘리브레이션: 적 하나가 가까이 있습니다.";
            case ADUIContextScenario.DodgeThreat:
                return "전투 캘리브레이션: 적 공격과 투사체가 들어오고 있습니다.";
            case ADUIContextScenario.LowHpHeal:
                return "전투 캘리브레이션: HP가 낮고 Heal을 사용할 수 있습니다.";
            case ADUIContextScenario.CrowdWhirlwind:
                return "전투 캘리브레이션: 적들이 스킬 범위 안에 모여 있습니다.";
            case ADUIContextScenario.MovementThreat:
                return "전투 캘리브레이션: 조이스틱으로 이동 중이고 근접 적이 다가옵니다.";
            case ADUIContextScenario.LowHpThreat:
                return "전투 캘리브레이션: HP가 낮은데 공격 위협도 들어오고 있습니다.";
            case ADUIContextScenario.CrowdLowHp:
                return "전투 캘리브레이션: HP가 낮고 주변에 적이 모여 있습니다.";
            case ADUIContextScenario.SafeAttackOpportunity:
                return "전투 캘리브레이션: 적이 공격 범위 안에 있고 즉시 위협은 없습니다.";
            case ADUIContextScenario.RiskyCloseEnemy:
                return "전투 캘리브레이션: 적이 위험 거리 안에 있지만 아직 공격은 시작하지 않았습니다.";
            case ADUIContextScenario.ImmediateDodgeThreat:
                return "전투 캘리브레이션: 근접 공격 예고 또는 공격이 바로 들어오고 있습니다.";
            case ADUIContextScenario.ProjectileDodgeThreat:
                return "전투 캘리브레이션: 투사체가 충돌 경로로 날아오고 있습니다.";
            case ADUIContextScenario.MovingUnderPressure:
                return "전투 캘리브레이션: 이동 중 압박이 가까워지고 있습니다.";
            case ADUIContextScenario.AttackCommitWindow:
                return "전투 캘리브레이션: 적이 공격 범위 안에 있고 공격 예고는 없습니다.";
            case ADUIContextScenario.PreDodgeWindow:
                return "전투 캘리브레이션: 가까운 적이 곧 위험해지거나 이동 방향이 위험 쪽입니다.";
            default:
                return "전투 캘리브레이션: 일반 전투 상황입니다.";
        }
    }

    public static string DefaultResponseForScenario(ADUIContextScenario scenario)
    {
        switch (scenario)
        {
            case ADUIContextScenario.AttackOpportunity:
            case ADUIContextScenario.SafeAttackOpportunity:
            case ADUIContextScenario.AttackCommitWindow:
                return "Attack";
            case ADUIContextScenario.DodgeThreat:
            case ADUIContextScenario.MovementThreat:
            case ADUIContextScenario.RiskyCloseEnemy:
            case ADUIContextScenario.PreDodgeWindow:
            case ADUIContextScenario.ImmediateDodgeThreat:
            case ADUIContextScenario.ProjectileDodgeThreat:
            case ADUIContextScenario.MovingUnderPressure:
                return "Dodge";
            case ADUIContextScenario.LowHpHeal:
            case ADUIContextScenario.LowHpThreat:
                return "Heal";
            case ADUIContextScenario.CrowdWhirlwind:
            case ADUIContextScenario.CrowdLowHp:
                return "Whirlwind";
            default:
                return "Attack";
        }
    }

    private bool AddObservation(ADUIContextScenario scenario, string actionName, float weight, bool alsoUpdateGeneral)
    {
        if (!TryActionIndex(actionName, out int actionIndex))
        {
            return false;
        }

        int scenarioIndex = Mathf.Clamp((int)scenario, 0, ScenarioCount - 1);
        float safeWeight = Mathf.Max(0.01f, weight);
        scenarioActionCounts[scenarioIndex, actionIndex] += safeWeight;
        scenarioTotals[scenarioIndex] += safeWeight;

        if (alsoUpdateGeneral && scenario != ADUIContextScenario.General)
        {
            scenarioActionCounts[(int)ADUIContextScenario.General, actionIndex] += safeWeight * 0.35f;
            scenarioTotals[(int)ADUIContextScenario.General] += safeWeight * 0.35f;
        }

        Debug.Log($"[ADUI] User context prior update: {ScenarioLabel(scenario)} -> {actionName}, weight={safeWeight:F2}, {Summary(scenario)}");
        return true;
    }

    private float EffectiveStrength(ADUIContextScenario scenario)
    {
        int scenarioIndex = Mathf.Clamp((int)scenario, 0, ScenarioCount - 1);
        float total = scenarioTotals[scenarioIndex];
        if (total <= 0f && scenario != ADUIContextScenario.General)
        {
            total = scenarioTotals[(int)ADUIContextScenario.General] * 0.5f;
        }

        float maturity = total / Mathf.Max(0.001f, total + matureSampleCount);
        return userPriorStrength * maturity;
    }

    private float AdjustPrior(float publicPrior, ADUIContextScenario scenario, int actionIndex, float strength)
    {
        float userPrior = GetBackoffUserPrior(scenario, actionIndex);
        float userMultiplier = Mathf.Clamp(userPrior / 0.25f, 0.25f, 4f);
        return Mathf.Max(0.0001f, publicPrior) * Mathf.Pow(userMultiplier, strength);
    }

    private float GetBackoffUserPrior(ADUIContextScenario scenario, int actionIndex)
    {
        int scenarioIndex = Mathf.Clamp((int)scenario, 0, ScenarioCount - 1);
        if (scenarioTotals[scenarioIndex] > 0f)
        {
            return GetUserPrior(scenario, actionIndex);
        }

        return GetUserPrior(ADUIContextScenario.General, actionIndex);
    }

    private float GetUserPrior(ADUIContextScenario scenario, int actionIndex)
    {
        int scenarioIndex = Mathf.Clamp((int)scenario, 0, ScenarioCount - 1);
        float denominator = scenarioTotals[scenarioIndex] + alpha * ActionCount;
        return (scenarioActionCounts[scenarioIndex, actionIndex] + alpha) / Mathf.Max(0.001f, denominator);
    }

    private bool TryActionIndex(string actionName, out int index)
    {
        if (string.Equals(actionName, "Attack", StringComparison.OrdinalIgnoreCase))
        {
            index = 0;
            return true;
        }

        if (string.Equals(actionName, "Dodge", StringComparison.OrdinalIgnoreCase))
        {
            index = 1;
            return true;
        }

        if (string.Equals(actionName, "Heal", StringComparison.OrdinalIgnoreCase))
        {
            index = 2;
            return true;
        }

        if (string.Equals(actionName, "Whirlwind", StringComparison.OrdinalIgnoreCase))
        {
            index = 3;
            return true;
        }

        index = -1;
        return false;
    }

    private void Normalize(ref float attack, ref float dodge, ref float heal, ref float whirlwind)
    {
        float total = Mathf.Max(0.0001f, attack + dodge + heal + whirlwind);
        attack = Mathf.Clamp01(attack / total);
        dodge = Mathf.Clamp01(dodge / total);
        heal = Mathf.Clamp01(heal / total);
        whirlwind = Mathf.Clamp01(whirlwind / total);
    }
}
