using UnityEngine;
using System.Collections.Generic;

public class EnemyProjectile : MonoBehaviour
{
    private static readonly List<EnemyProjectile> activeProjectiles = new List<EnemyProjectile>();

    public float speed = 8f;
    public int damage = 10;
    public float lifetime = 4f;

    public static IReadOnlyList<EnemyProjectile> ActiveProjectiles => activeProjectiles;

    private Vector2 moveDirection = Vector2.right;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void OnEnable()
    {
        if (!activeProjectiles.Contains(this))
        {
            activeProjectiles.Add(this);
        }
    }

    private void OnDisable()
    {
        activeProjectiles.Remove(this);
    }

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void SetDirection(Vector2 direction)
    {
        moveDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.right;
    }

    private void Update()
    {
        Vector2 nextPosition = (Vector2)transform.position + moveDirection * speed * Time.deltaTime;

        if (rb != null)
        {
            rb.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    public bool IsThreatening(Vector2 playerPosition, float nearRadius, float lookAheadSeconds)
    {
        Vector2 projectilePosition = transform.position;
        if (Vector2.Distance(projectilePosition, playerPosition) <= nearRadius)
        {
            return true;
        }

        float lookAheadDistance = speed * Mathf.Max(0.1f, lookAheadSeconds);
        return PrototypeVisualFactory.PointInLineArea(playerPosition, projectilePosition, moveDirection, lookAheadDistance, nearRadius);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            PlayerController player = collision.GetComponent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(damage);
            }

            Destroy(gameObject);
        }
        else if (collision.CompareTag("Wall"))
        {
            Destroy(gameObject);
        }
    }

    public static void DestroyAllProjectiles()
    {
        for (int i = activeProjectiles.Count - 1; i >= 0; i--)
        {
            if (activeProjectiles[i] != null)
            {
                Destroy(activeProjectiles[i].gameObject);
            }
        }

        activeProjectiles.Clear();
    }
}
