using System.Collections;
using UnityEngine;

public class BossController : MonoBehaviour
{
    public Transform player;
    public float attackCooldown = 4f;
    
    [Header("Telegraph Prefabs")]
    // A translucent red circle sprite
    public GameObject smashWarningPrefab; 
    // A translucent red rectangle/line sprite
    public GameObject shockwaveWarningPrefab;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").transform;
        StartCoroutine(BossBehaviorLoop());
    }

    private IEnumerator BossBehaviorLoop()
    {
        // Give player a second before boss starts attacking
        yield return new WaitForSeconds(1f); 

        while (true)
        {
            // Randomly pick attack pattern (0 = Smash, 1 = Shockwave)
            int attackType = Random.Range(0, 2);

            if (attackType == 0)
                yield return StartCoroutine(SmashAttack());
            else
                yield return StartCoroutine(ShockwaveAttack());

            yield return new WaitForSeconds(attackCooldown);
        }
    }

    private IEnumerator SmashAttack()
    {
        Debug.Log("Boss preparing Smash!");
        
        // 1. Telegraph (Foreshadowing)
        Vector3 targetPos = player.position;
        GameObject warning = Instantiate(smashWarningPrefab, targetPos, Quaternion.identity);
        
        // Wait for player to react
        yield return new WaitForSeconds(1.5f);
        
        // 2. Execute Attack
        Destroy(warning);
        Debug.Log("Boss SMASHES!");
        
        // Check distance to apply damage
        if (Vector2.Distance(player.position, targetPos) < 3f) // Assuming 3f radius
        {
            player.GetComponent<PlayerController>().TakeDamage(30);
        }
    }

    private IEnumerator ShockwaveAttack()
    {
        Debug.Log("Boss preparing Shockwave!");

        // 1. Telegraph
        Vector2 direction = (player.position - transform.position).normalized;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        
        GameObject warning = Instantiate(shockwaveWarningPrefab, transform.position, Quaternion.Euler(0, 0, angle));
        
        // Wait for player to react
        yield return new WaitForSeconds(1.5f);

        // 2. Execute Attack
        Destroy(warning);
        Debug.Log("Boss fires SHOCKWAVE!");

        // In a full game, you would spawn a fast-moving projectile here. 
        // For prototyping, we check if player is in the line of fire.
        RaycastHit2D hit = Physics2D.Raycast(transform.position, direction, 15f, LayerMask.GetMask("Player"));
        if (hit.collider != null)
        {
            player.GetComponent<PlayerController>().TakeDamage(20);
        }
    }
}