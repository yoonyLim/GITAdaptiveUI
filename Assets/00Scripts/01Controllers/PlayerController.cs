using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float movementSpeed = 0;
    private Vector2 _inputVector;
    
    public int maxHP = 100;
    private int _currentHP;
    
    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        ResetStats();
    }

    private void Update()
    {
        Move();
    }
    
    void Move()
    {
        Vector3 dir = transform.forward * _inputVector.y + transform.right * _inputVector.x;
        dir *= movementSpeed;
        dir.y = _rb.linearVelocity.y; 
        _rb.linearVelocity = dir;
    }
    
    public void OnMove(InputAction.CallbackContext context)
    {
        if (context.phase == InputActionPhase.Performed)
        {
            _inputVector = context.ReadValue<Vector2>();
        }
        else if (context.phase == InputActionPhase.Canceled)
        {
            _inputVector = Vector2.zero;
        }
    }

    public void ResetStats()
    {
        _currentHP = maxHP;
        Debug.Log("Player stats reset to full.");
        // Reset stamina, buffs, or other roguelike stats here
    }

    public void TakeDamage(int damage)
    {
        _currentHP -= damage;
        if (_currentHP <= 0) Debug.Log("Player Died!");
    }
}