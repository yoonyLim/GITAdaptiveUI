using UnityEngine;

public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance;

    public enum GameState { Safe, Urgent }
    public GameState currentState = GameState.Safe;

    // Bayesian Priors P(A)
    [HideInInspector] public float priorAttack = 0.5f;
    [HideInInspector] public float priorDodge = 0.5f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Update()
    {
        // Update Priors based on Contextual Interaction-Demand
        if (currentState == GameState.Urgent)
        {
            // [cite_start]// When the boss is attacking, dodging is mathematically expected 
            priorDodge = 0.90f;
            priorAttack = 0.10f;
        }
        else
        {
            // In a safe state, attacking is the primary action
            priorDodge = 0.10f;
            priorAttack = 0.90f;
        }
    }

    public void SetUrgency(bool isUrgent)
    {
        currentState = isUrgent ? GameState.Urgent : GameState.Safe;
    }
}