using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RoguelikeGameManager : MonoBehaviour
{
    public static RoguelikeGameManager Instance;

    [Header("Prefabs")]
    public GameObject meleeEnemyPrefab;
    public GameObject rangedEnemyPrefab;
    public GameObject bossEnemyPrefab;

    [Header("Player & Spawning")]
    public Transform playerTransform;
    public PlayerController playerController;
    public float spawnRadius = 8f;

    [Header("UI")]
    public Button skipButton;

    private int currentStage = 1;
    private List<GameObject> activeEnemies = new List<GameObject>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        skipButton.onClick.AddListener(SkipStage);
        StartStage(currentStage);
    }

    public void StartStage(int stageNumber)
    {
        ClearEnemies();
        currentStage = stageNumber;
        Debug.Log($"Starting Stage {currentStage}");

        switch (currentStage)
        {
            case 1:
                SpawnEnemies(meleeEnemyPrefab, Random.Range(10, 21));
                break;
            case 2:
                SpawnEnemies(meleeEnemyPrefab, 10);
                SpawnEnemies(rangedEnemyPrefab, 5);
                break;
            case 3:
                SpawnEnemies(bossEnemyPrefab, 1);
                SpawnEnemies(meleeEnemyPrefab, 3);
                SpawnEnemies(rangedEnemyPrefab, 3);
                break;
            default:
                Debug.Log("Game Cleared!");
                break;
        }
    }

    public void SkipStage()
    {
        Debug.Log("Stage Skipped!");
        ClearEnemies();
        playerController.ResetStats();
        
        currentStage++;
        if (currentStage <= 3)
        {
            StartStage(currentStage);
        }
        else
        {
            Debug.Log("All stages complete!");
        }
    }

    private void SpawnEnemies(GameObject prefab, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 randomDir = Random.insideUnitCircle.normalized;
            Vector3 spawnPos = playerTransform.position + new Vector3(randomDir.x, randomDir.y, 0) * spawnRadius;
            
            GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
            activeEnemies.Add(enemy);
        }
    }

    private void ClearEnemies()
    {
        foreach (GameObject enemy in activeEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        activeEnemies.Clear();
    }
}