using System.Collections;
using UnityEngine;

public class MeleeEnemyController : MonoBehaviour
{
    [Header("Stats")]
    public float moveSpeed = 3f;
    public float attackRange = 1.5f;
    public float attackCooldown = 2f;
    public int attackDamage = 10;

    private Transform player;
    private PlayerController playerController;
    private bool isAttacking = false;

    private void Start()
    {
        // Find the player automatically when spawned
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerController = playerObj.GetComponent<PlayerController>();
        }
    }

    private void Update()
    {
        if (player == null || isAttacking) return;

        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        if (distanceToPlayer > attackRange)
        {
            // Chase the player
            transform.position = Vector2.MoveTowards(transform.position, player.position, moveSpeed * Time.deltaTime);
        }
        else
        {
            // In range, start attack
            StartCoroutine(MeleeAttack());
        }
    }

    private IEnumerator MeleeAttack()
    {
        isAttacking = true;
        Debug.Log("Melee Enemy winding up sword strike...");

        // 1. Wind up (visualize by changing color or slightly pulling back)
        GetComponent<SpriteRenderer>().color = Color.gray;
        yield return new WaitForSeconds(0.5f); // 0.5 second wind-up

        // 2. Strike
        GetComponent<SpriteRenderer>().color = Color.white;
        Debug.Log("Melee Enemy SWINGS!");

        // Check if player is still in range after wind-up
        if (player != null && Vector2.Distance(transform.position, player.position) <= attackRange + 0.5f)
        {
            if (playerController != null)
            {
                playerController.TakeDamage(attackDamage);
            }
        }

        // 3. Cooldown
        yield return new WaitForSeconds(attackCooldown);
        isAttacking = false;
    }
}