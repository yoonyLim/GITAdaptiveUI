using System.Collections;
using UnityEngine;

public class SimulatedEnemy : MonoBehaviour
{
    public Renderer enemyRenderer;
    public Color safeColor = Color.white;
    public Color attackingColor = Color.red;

    private float attackCooldown = 3.0f;
    private float telegraphDuration = 1.0f;

    private void Start()
    {
        StartCoroutine(EnemyBehaviorLoop());
    }

    private IEnumerator EnemyBehaviorLoop()
    {
        while (true)
        {
            // 1. Safe Phase
            GameStateManager.Instance.SetUrgency(false);
            enemyRenderer.material.color = safeColor;
            yield return new WaitForSeconds(attackCooldown);

            // 2. Telegraph / Urgent Phase
            // This immediately shifts the Bayesian Prior to favor the Dodge button
            GameStateManager.Instance.SetUrgency(true);
            enemyRenderer.material.color = attackingColor;
            
            // Simulating a highly urgent window to react
            yield return new WaitForSeconds(telegraphDuration); 
        }
    }
}