using System.Collections;
using UnityEngine;

public class RangedEnemyController : MonoBehaviour
{
    [Header("Stats")]
    public float moveSpeed = 2f;
    public float shootingRange = 7f;
    public float fireRate = 3f;

    [Header("References")]
    public GameObject arrowPrefab;
    public Transform firePoint; // Where the arrow spawns

    private Transform player;
    private bool isShooting = false;

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;
    }

    private void Update()
    {
        if (player == null || isShooting) return;

        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        if (distanceToPlayer > shootingRange)
        {
            // Move closer
            transform.position = Vector2.MoveTowards(transform.position, player.position, moveSpeed * Time.deltaTime);
        }
        else
        {
            // In range, stop moving and shoot
            StartCoroutine(ShootArrow());
        }
    }

    private IEnumerator ShootArrow()
    {
        isShooting = true;

        // Visual telegraph (flash color before shooting)
        GetComponent<SpriteRenderer>().color = new Color(1f, 0.6f, 0f); // Orange
        yield return new WaitForSeconds(0.5f);
        GetComponent<SpriteRenderer>().color = Color.white;

        // Calculate direction to player
        Vector2 direction = (player.position - transform.position).normalized;
        
        // Calculate rotation so the arrow points at the player
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0, 0, angle);

        // Spawn arrow
        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;
        GameObject arrow = Instantiate(arrowPrefab, spawnPos, rotation);
        
        // Give it the direction
        arrow.GetComponent<EnemyProjectile>().SetDirection(direction);

        Debug.Log("Ranged Enemy fired an arrow!");

        // Cooldown
        yield return new WaitForSeconds(fireRate);
        isShooting = false;
    }
}