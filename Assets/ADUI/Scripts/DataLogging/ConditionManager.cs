using System;
using System.Collections.Generic;
using UnityEngine;

public class ConditionManager : MonoBehaviour
{
    public string currentCondition = DatasetSchema.ConditionContextBayesianSafety;
    public bool randomizeConditionOrder = true;
    public string[] conditionOrder = DatasetSchema.Conditions;
    public int currentConditionIndex;

    public void BuildConditionOrder(int seed = 17)
    {
        var list = new List<string>(DatasetSchema.Conditions);
        if (randomizeConditionOrder)
        {
            var rng = new System.Random(seed);
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
        conditionOrder = list.ToArray();
        currentConditionIndex = 0;
        currentCondition = conditionOrder.Length > 0 ? conditionOrder[0] : DatasetSchema.ConditionContextBayesianSafety;
    }

    public void SetCondition(string condition)
    {
        currentCondition = condition;
    }

    public string NextCondition()
    {
        if (conditionOrder == null || conditionOrder.Length == 0) BuildConditionOrder();
        currentConditionIndex = (currentConditionIndex + 1) % conditionOrder.Length;
        currentCondition = conditionOrder[currentConditionIndex];
        return currentCondition;
    }

    public void SaveConditionOrder(ExperimentSessionManager sessionManager)
    {
        if (!sessionManager || !sessionManager.exporter) return;
        var order = new ADUIConditionOrder
        {
            session_id = sessionManager.sessionId,
            participant_id = sessionManager.ParticipantId(),
            condition_order = conditionOrder
        };
        sessionManager.exporter.WriteJson(sessionManager.EnsureSession(), "condition_order.json", order, true);
    }
}

