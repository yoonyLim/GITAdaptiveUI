using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public int maxHP = 100;
    private int currentHP;

    private void Start()
    {
        ResetStats();
    }

    public void ResetStats()
    {
        currentHP = maxHP;
        Debug.Log("Player stats reset to full.");
        // Reset stamina, buffs, or other roguelike stats here
    }

    public void TakeDamage(int damage)
    {
        currentHP -= damage;
        if (currentHP <= 0) Debug.Log("Player Died!");
    }
}