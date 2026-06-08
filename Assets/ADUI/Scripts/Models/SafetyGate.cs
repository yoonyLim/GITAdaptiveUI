using UnityEngine;

public class SafetyGate : MonoBehaviour
{
    public float recoverableRadiusMultiplier = 1.75f;
    public bool preserveClearInsideButtonInput = true;

    public ADUIDecodeResult Apply(ADUIDecodeInput input, ADUIDecodeResult result)
    {
        result.safetyGatePassed = false;
        result.safetyGateReason = "";

        if (result.invalidTouch)
        {
            result.finalExecutedAction = ADUIAction.None;
            result.safetyGateReason = "invalid_far_touch";
            return result;
        }

        var withinRecoverable =
            result.distanceToAttack <= input.attackButton.visualRadius * recoverableRadiusMultiplier ||
            result.distanceToDodge <= input.dodgeButton.visualRadius * recoverableRadiusMultiplier;
        if (!withinRecoverable)
        {
            result.invalidTouch = true;
            result.finalExecutedAction = ADUIAction.None;
            result.safetyGateReason = "outside_recoverable_radius";
            return result;
        }

        if (preserveClearInsideButtonInput && result.visualBoundaryPrediction != ADUIAction.None && !result.isNearBoundary)
        {
            result.finalExecutedAction = result.visualBoundaryPrediction;
            result.safetyGatePassed = false;
            result.safetyGateReason = "preserve_clear_visual_input";
            return result;
        }

        if (!result.isAmbiguous && !result.isNearBoundary)
        {
            result.finalExecutedAction = result.visualBoundaryPrediction != ADUIAction.None ? result.visualBoundaryPrediction : result.userGaussianPrediction;
            result.safetyGateReason = "not_ambiguous";
            return result;
        }

        if (result.maxPosterior < result.tau)
        {
            result.finalExecutedAction = result.visualBoundaryPrediction != ADUIAction.None ? result.visualBoundaryPrediction : ADUIAction.None;
            result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
            result.safetyGateReason = "posterior_below_tau";
            return result;
        }

        if (result.posteriorGap < result.delta)
        {
            result.finalExecutedAction = result.visualBoundaryPrediction != ADUIAction.None ? result.visualBoundaryPrediction : ADUIAction.None;
            result.invalidTouch = result.finalExecutedAction == ADUIAction.None;
            result.safetyGateReason = "posterior_gap_below_delta";
            return result;
        }

        result.finalExecutedAction = result.bayesianPrediction;
        result.safetyGatePassed = true;
        result.safetyGateReason = "correction_allowed";
        return result;
    }
}

