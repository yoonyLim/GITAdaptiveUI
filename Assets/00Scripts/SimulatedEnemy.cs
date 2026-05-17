using UnityEngine;

public class SimulatedEnemy : MonoBehaviour
{
    public Renderer enemyRenderer;
    
    [Header("State Colors")]
    public Color safeColor = Color.white;
    public Color telegraphColor = Color.yellow;
    public Color attackingColor = Color.red;

    private void Update()
    {
        // Ensure CombatManager exists before trying to read from it
        if (!CombatManager.Instance) return;

        // Sync the enemy's color perfectly with the CombatManager's current state
        switch (CombatManager.Instance.currentState)
        {
            case CombatManager.CombatState.Safe:
                enemyRenderer.material.color = safeColor;
                break;
            
            case CombatManager.CombatState.Telegraph:
                // The "Window Time" where the player must prepare to dodge
                enemyRenderer.material.color = telegraphColor;
                break;
            
            case CombatManager.CombatState.Attacking:
                // The actual hit frame
                enemyRenderer.material.color = attackingColor;
                break;
        }
    }
}