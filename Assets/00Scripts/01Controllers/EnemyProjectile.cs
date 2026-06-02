using UnityEngine;

public class EnemyProjectile : MonoBehaviour
{
    public float speed = 8f;
    public int damage = 10;
    public float lifetime = 4f; // Destroy after 4 seconds to prevent memory leaks

    private Vector2 moveDirection;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    public void SetDirection(Vector2 direction)
    {
        moveDirection = direction.normalized;
    }

    private void Update()
    {
        // Move the arrow forward every frame
        transform.position += (Vector3)(moveDirection * speed * Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // If we hit the player, deal damage and destroy the arrow
        if (collision.CompareTag("Player"))
        {
            PlayerController player = collision.GetComponent<PlayerController>();
            if (player != null)
            {
                player.TakeDamage(damage);
            }
            Destroy(gameObject);
        }
        // Optional: Destroy arrow if it hits a wall (requires a "Wall" tag)
        else if (collision.CompareTag("Wall"))
        {
            Destroy(gameObject);
        }
    }
}