using System.Collections;
using UnityEngine;
using TMPro; // TextMeshPro 사용을 위해 필요

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;

    [Header("UI Text Elements")]
    public TextMeshProUGUI stateText;
    public TextMeshProUGUI playerHpText;
    public TextMeshProUGUI enemyHpText;
    public TextMeshProUGUI feedbackLogText; // Success or Fail text

    [Header("HP System")]
    public int playerMaxHP = 100;
    public int enemyMaxHP = 500;
    private int currentPlayerHP;
    private int currentEnemyHP;

    public enum CombatState { Safe, Telegraph, Attacking }
    public CombatState currentState = CombatState.Safe;

    // prior Bayesian probability (for the Touch Manager to read)
    [HideInInspector] public float priorAttack = 0.5f;
    [HideInInspector] public float priorDodge = 0.5f;

    private bool isDodging = false;
    private bool isTextFading = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        currentPlayerHP = playerMaxHP;
        currentEnemyHP = enemyMaxHP;
        UpdateHPUI();
        StartCoroutine(CombatLoop());
    }

    private void Update()
    {
        // 텍스트 페이드 인/아웃 효과 (알림 텍스트)
        if (isTextFading && stateText)
        {
            Color c = stateText.color;
            // 시간에 따라 알파값이 0.3 ~ 1.0 사이를 오가게 함
            c.a = 0.3f + Mathf.PingPong(Time.time * 2f, 0.7f);
            stateText.color = c;
        }
    }

    private IEnumerator CombatLoop()
    {
        while (currentPlayerHP > 0 && currentEnemyHP > 0)
        {
            // Safe State
            SetState(CombatState.Safe, "Enemy Idle", Color.white, false, 0.9f, 0.1f);
            isDodging = false;
            yield return new WaitForSeconds(Random.Range(2.0f, 4.0f));

            // Telegraph State (readying to attack)
            SetState(CombatState.Telegraph, "Enemy Readying to Attack! (WARNING)", Color.yellow, true, 0.1f, 0.9f);
            // window time for the player to dodge
            yield return new WaitForSeconds(1.0f); 

            // Attacking State
            SetState(CombatState.Attacking, "ENEMY ATTACKING!", Color.red, true, 0.1f, 0.9f);
            
            if (!isDodging)
            {
                // damage the player if not dodged
                TakePlayerDamage(20);
                ShowFeedback("Dodge Failed!", Color.red);
            }
            else
            {
                ShowFeedback("Dodge Success!", Color.cyan);
            }

            yield return new WaitForSeconds(0.5f); // attack animation mimic
        }

        // game over logic
        stateText.text = currentPlayerHP <= 0 ? "Game Over" : "Game Clear";
        stateText.color = currentPlayerHP <= 0 ? Color.red : Color.green;
        isTextFading = false;
    }

    private void SetState(CombatState state, string msg, Color color, bool fading, float pAttack, float pDodge)
    {
        currentState = state;
        stateText.text = msg;
        stateText.color = color;
        isTextFading = fading;
        priorAttack = pAttack;
        priorDodge = pDodge;
    }

    // AdaptiveTouchManager calls this
    public void OnPlayerAttack()
    {
        if (currentState == CombatState.Safe)
        {
            TakeEnemyDamage(15);
            ShowFeedback("Successful Attack!", Color.green);
        }
        else
        {
            TakePlayerDamage(10);
            ShowFeedback("Enemy Counter Attacked!", Color.red);
        }
    }

    public void OnPlayerDodge()
    {
        if (currentState == CombatState.Telegraph || currentState == CombatState.Attacking)
        {
            isDodging = true;
            ShowFeedback("Good Dodge!", Color.cyan);
        }
        else
        {
            ShowFeedback("Unnecessary Dodge", Color.gray);
        }
    }

    private void TakePlayerDamage(int amount)
    {
        currentPlayerHP = Mathf.Max(0, currentPlayerHP - amount);
        UpdateHPUI();
    }

    private void TakeEnemyDamage(int amount)
    {
        currentEnemyHP = Mathf.Max(0, currentEnemyHP - amount);
        UpdateHPUI();
    }

    private void UpdateHPUI()
    {
        if (playerHpText) playerHpText.text = $"Player HP: {currentPlayerHP} / {playerMaxHP}";
        if (enemyHpText) enemyHpText.text = $"Enemy HP: {currentEnemyHP} / {enemyMaxHP}";
    }

    private void ShowFeedback(string message, Color color)
    {
        if (feedbackLogText)
        {
            feedbackLogText.text = message;
            feedbackLogText.color = color;
        }
    }
}