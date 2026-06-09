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

        if (preDodgeWindow)
        {
            return ADUIContextScenario.PreDodgeWindow;
        }

        if (moving && riskyClose)
        {
            return ADUIContextScenario.MovingUnderPressure;
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
                return "Attack opportunity";
            case ADUIContextScenario.DodgeThreat:
                return "Dodge threat";
            case ADUIContextScenario.LowHpHeal:
                return "Low HP";
            case ADUIContextScenario.CrowdWhirlwind:
                return "Enemy crowd";
            case ADUIContextScenario.MovementThreat:
                return "Moving threat";
            case ADUIContextScenario.LowHpThreat:
                return "Low HP + threat";
            case ADUIContextScenario.CrowdLowHp:
                return "Crowd + low HP";
            case ADUIContextScenario.SafeAttackOpportunity:
                return "Safe attack window";
            case ADUIContextScenario.RiskyCloseEnemy:
                return "Risky close enemy";
            case ADUIContextScenario.ImmediateDodgeThreat:
                return "Immediate dodge threat";
            case ADUIContextScenario.ProjectileDodgeThreat:
                return "Projectile dodge threat";
            case ADUIContextScenario.MovingUnderPressure:
                return "Moving under pressure";
            case ADUIContextScenario.AttackCommitWindow:
                return "Attack commit window";
            case ADUIContextScenario.PreDodgeWindow:
                return "Pre-dodge window";
            default:
                return "General";
        }
    }

    public static string ScenarioInstruction(ADUIContextScenario scenario)
    {
        switch (scenario)
        {
            case ADUIContextScenario.AttackOpportunity:
                return "Combat calibration: one enemy is close. Use the action you would take now.";
            case ADUIContextScenario.DodgeThreat:
                return "Combat calibration: an enemy attack and projectile are incoming. Use the action you would take now.";
            case ADUIContextScenario.LowHpHeal:
                return "Combat calibration: your HP is low and Heal is ready. Use the action you would take now.";
            case ADUIContextScenario.CrowdWhirlwind:
                return "Combat calibration: enemies are clustered inside skill range. Use the action you would take now.";
            case ADUIContextScenario.MovementThreat:
                return "Combat calibration: move with the joystick while a melee enemy closes in. Use the action you would take now.";
            case ADUIContextScenario.LowHpThreat:
                return "Combat calibration: HP is critical while an attack is incoming. Use the action you would take now.";
            case ADUIContextScenario.CrowdLowHp:
                return "Combat calibration: HP is low and enemies are clustered around you. Use the action you would take now.";
            case ADUIContextScenario.SafeAttackOpportunity:
                return "Combat calibration: a target is in range and no immediate attack is incoming. Use the action you would take now.";
            case ADUIContextScenario.RiskyCloseEnemy:
                return "Combat calibration: an enemy is inside danger range but has not fully attacked yet. Use the action you would take now.";
            case ADUIContextScenario.ImmediateDodgeThreat:
                return "Combat calibration: a melee attack is telegraphed or active at close range. Use the action you would take now.";
            case ADUIContextScenario.ProjectileDodgeThreat:
                return "Combat calibration: a projectile is on a collision path. Use the action you would take now.";
            case ADUIContextScenario.MovingUnderPressure:
                return "Combat calibration: you are moving while pressure is closing in. Use the action you would take now.";
            case ADUIContextScenario.AttackCommitWindow:
                return "Combat calibration: a target is inside your attack range and no attack is being telegraphed. Use the action you would take now.";
            case ADUIContextScenario.PreDodgeWindow:
                return "Combat calibration: a close enemy is about to become dangerous or your movement is carrying you into danger. Use the action you would take now.";
            default:
                return "Combat calibration: normal combat state. Use the action you would take now.";
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
