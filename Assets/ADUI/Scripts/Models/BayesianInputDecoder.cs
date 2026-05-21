using UnityEngine;

public class BayesianInputDecoder : MonoBehaviour
{
    [Header("Thresholds")]
    public float tau = 0.55f;
    public float delta = 0.12f;
    public float minLikelihoodThreshold = 0.01f;
    public float recoverableRadiusMultiplier = 1.75f;
    public float priorStrength = 1f;
    public float ambiguityMarginPx = 60f;

    [Header("Priors")]
    public Vector2 safePrior = new Vector2(0.9f, 0.1f);
    public Vector2 telegraphPrior = new Vector2(0.1f, 0.9f);
    public Vector2 attackingPrior = new Vector2(0.05f, 0.95f);
    public Vector2 urgentPrior = new Vector2(0.05f, 0.95f);
    public Vector2 idlePrior = new Vector2(0.9f, 0.1f);
    public Vector2 neutralPrior = new Vector2(0.5f, 0.5f);

    public UserTouchModel userTouchModel;
    public string publicPriorSource = "";
    public string publicVarianceSource = "";
    public bool useExternalActionPrior = false;
    public Vector2 externalActionPrior = new Vector2(0.5f, 0.5f);
    public string externalActionPriorSource = "";

    public ADUIDecodeResult Decode(ADUIDecodeInput input)
    {
        var result = new ADUIDecodeResult();
        result.tau = tau;
        result.delta = delta;
        result.priorStrength = priorStrength;
        result.publicPriorSource = publicPriorSource;
        result.publicVarianceSource = publicVarianceSource;

        result.distanceToAttack = Vector2.Distance(input.touchPosition, input.attackButton.Center);
        result.distanceToDodge = Vector2.Distance(input.touchPosition, input.dodgeButton.Center);
        result.visualBoundaryPrediction = VisualPrediction(input, 1f);
        result.expandedHitboxPrediction = VisualPrediction(input, input.attackButton.hitboxRadius / Mathf.Max(input.attackButton.visualRadius, 1f));

        if (userTouchModel)
        {
            result.likelihoodAttack = userTouchModel.SpatialLikelihood(ADUIAction.Attack, input.touchPosition, input.attackButton);
            result.likelihoodDodge = userTouchModel.SpatialLikelihood(ADUIAction.Dodge, input.touchPosition, input.dodgeButton);
            result.varianceAttack = userTouchModel.GetVariance(ADUIAction.Attack);
            result.varianceDodge = userTouchModel.GetVariance(ADUIAction.Dodge);
        }
        else
        {
            var variance = 180f * 180f;
            result.likelihoodAttack = GaussianDistanceLikelihood(result.distanceToAttack, variance);
            result.likelihoodDodge = GaussianDistanceLikelihood(result.distanceToDodge, variance);
            result.varianceAttack = variance;
            result.varianceDodge = variance;
        }

        result.userGaussianPrediction = result.likelihoodAttack >= result.likelihoodDodge ? ADUIAction.Attack : ADUIAction.Dodge;
        var priors = useExternalActionPrior ? externalActionPrior : PriorForState(input.enemyState);
        result.priorAttack = Mathf.Pow(Mathf.Clamp01(priors.x), Mathf.Max(priorStrength, 0.001f));
        result.priorDodge = Mathf.Pow(Mathf.Clamp01(priors.y), Mathf.Max(priorStrength, 0.001f));
        NormalizePair(ref result.priorAttack, ref result.priorDodge);
        if (useExternalActionPrior && !string.IsNullOrEmpty(externalActionPriorSource))
        {
            result.publicPriorSource = externalActionPriorSource;
        }
        result.contextPriorOnlyPrediction = result.priorAttack >= result.priorDodge ? ADUIAction.Attack : ADUIAction.Dodge;

        var attackScore = result.likelihoodAttack * result.priorAttack;
        var dodgeScore = result.likelihoodDodge * result.priorDodge;
        var total = attackScore + dodgeScore;
        if (total <= Mathf.Epsilon)
        {
            result.posteriorAttack = 0.5f;
            result.posteriorDodge = 0.5f;
        }
        else
        {
            result.posteriorAttack = attackScore / total;
            result.posteriorDodge = dodgeScore / total;
        }
        result.posteriorGap = Mathf.Abs(result.posteriorAttack - result.posteriorDodge);
        result.maxPosterior = Mathf.Max(result.posteriorAttack, result.posteriorDodge);
        result.bayesianPrediction = result.posteriorAttack >= result.posteriorDodge ? ADUIAction.Attack : ADUIAction.Dodge;
        result.invalidTouch = result.likelihoodAttack < minLikelihoodThreshold && result.likelihoodDodge < minLikelihoodThreshold;
        result.isNearBoundary = IsNearBoundary(input, result);
        result.isAmbiguous = result.visualBoundaryPrediction == ADUIAction.None || result.isNearBoundary || result.posteriorGap < delta * 2f;
        result.finalExecutedAction = result.bayesianPrediction;
        return result;
    }

    public Vector2 PriorForState(ADUIEnemyState state)
    {
        switch (state)
        {
            case ADUIEnemyState.Safe:
                return safePrior;
            case ADUIEnemyState.Telegraph:
                return telegraphPrior;
            case ADUIEnemyState.Attacking:
                return attackingPrior;
            case ADUIEnemyState.Urgent:
                return urgentPrior;
            case ADUIEnemyState.Idle:
                return idlePrior;
            default:
                return neutralPrior;
        }
    }

    public void SetExternalActionPrior(float attackPrior, float dodgePrior, string source)
    {
        externalActionPrior = new Vector2(Mathf.Clamp01(attackPrior), Mathf.Clamp01(dodgePrior));
        NormalizePair(ref externalActionPrior.x, ref externalActionPrior.y);
        externalActionPriorSource = source;
        useExternalActionPrior = true;
    }

    public void ClearExternalActionPrior()
    {
        useExternalActionPrior = false;
        externalActionPrior = neutralPrior;
        externalActionPriorSource = "";
    }

    public float DynamicRadius(float variance, float prior)
    {
        var std = Mathf.Sqrt(Mathf.Max(variance, 1f));
        var effective = Mathf.Clamp(minLikelihoodThreshold / Mathf.Max(prior, 0.001f), 0.0001f, 0.999f);
        return std * Mathf.Sqrt(-2f * Mathf.Log(effective));
    }

    private ADUIAction VisualPrediction(ADUIDecodeInput input, float margin)
    {
        var attackRadius = input.attackButton.visualRadius * margin;
        var dodgeRadius = input.dodgeButton.visualRadius * margin;
        var inAttack = Vector2.Distance(input.touchPosition, input.attackButton.Center) <= attackRadius;
        var inDodge = Vector2.Distance(input.touchPosition, input.dodgeButton.Center) <= dodgeRadius;
        if (inAttack && inDodge)
        {
            var attackDistance = Vector2.Distance(input.touchPosition, input.attackButton.Center);
            var dodgeDistance = Vector2.Distance(input.touchPosition, input.dodgeButton.Center);
            return attackDistance <= dodgeDistance ? ADUIAction.Attack : ADUIAction.Dodge;
        }
        if (inAttack) return ADUIAction.Attack;
        if (inDodge) return ADUIAction.Dodge;
        return ADUIAction.None;
    }

    private bool IsNearBoundary(ADUIDecodeInput input, ADUIDecodeResult result)
    {
        var attackMargin = Mathf.Abs(result.distanceToAttack - input.attackButton.visualRadius);
        var dodgeMargin = Mathf.Abs(result.distanceToDodge - input.dodgeButton.visualRadius);
        var margin = Mathf.Max(Mathf.Min(input.attackButton.visualRadius, input.dodgeButton.visualRadius) * 0.25f, ambiguityMarginPx);
        var betweenGap = Mathf.Abs(result.distanceToAttack - result.distanceToDodge);
        return attackMargin <= margin || dodgeMargin <= margin || betweenGap <= margin;
    }

    private float GaussianDistanceLikelihood(float distance, float variance)
    {
        return Mathf.Exp(-(distance * distance) / (2f * Mathf.Max(variance, 1f)));
    }

    private void NormalizePair(ref float a, ref float b)
    {
        var total = Mathf.Max(a + b, 0.000001f);
        a /= total;
        b /= total;
    }
}
