using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdaptiveGameHudController : MonoBehaviour
{
    [Header("Minimal Player HUD")]
    public CanvasGroup playerHudGroup;
    public Image hpFillImage;
    public TextMeshProUGUI hpText;
    public Image modeChipBackground;
    public TextMeshProUGUI modeChipText;
    public TextMeshProUGUI scenarioTagText;
    public TextMeshProUGUI dangerIndicatorText;
    public TextMeshProUGUI correctionToastText;
    public CanvasGroup bossHudGroup;
    public Image bossHpFillImage;
    public TextMeshProUGUI bossHpText;

    [Header("Research Overlay")]
    public bool showResearchOverlay;
    public CanvasGroup researchOverlayGroup;
    public TextMeshProUGUI researchOverlayText;

    private static readonly Color ActionFirstColor = new Color(0.95f, 0.28f, 0.18f, 0.92f);
    private static readonly Color CognitiveFirstColor = new Color(0.18f, 0.55f, 0.96f, 0.92f);

    public void ApplyRuntimeState(
        PlayerController player,
        ADUIInteractionDemand demand,
        ADUIAdjustmentPolicy policy,
        ADUIContextScenario scenario,
        string debugLine,
        string correctionMessage,
        Color correctionColor,
        bool correctionVisible)
    {
        ApplyHp(player);
        ApplyBossHp();
        ApplyModeAndPolicy(demand, policy);
        ApplyScenario(demand, scenario);
        ApplyCorrectionToast(correctionMessage, correctionColor, correctionVisible);
        ApplyResearchOverlay(debugLine, demand, policy);
    }

    private void ApplyHp(PlayerController player)
    {
        int currentHp = player != null ? player.CurrentHP : 0;
        int maxHp = player != null ? Mathf.Max(1, player.maxHP) : 1;
        float hpRatio = Mathf.Clamp01(currentHp / (float)maxHp);

        if (hpFillImage != null)
        {
            hpFillImage.fillAmount = Mathf.Max(hpRatio, 0.035f);
            hpFillImage.color = Color.Lerp(
                new Color(0.95f, 0.22f, 0.18f, 0.95f),
                new Color(0.24f, 0.82f, 0.42f, 0.95f),
                hpRatio);
        }

        if (hpText != null)
        {
            hpText.text = $"HP {currentHp}/{maxHp}";
        }
    }

    private void ApplyBossHp()
    {
        EnemyControllerBase boss = FindActiveBoss();
        bool showBoss = boss != null && boss.IsAlive;

        if (bossHudGroup != null)
        {
            bossHudGroup.alpha = showBoss ? 1f : 0f;
            bossHudGroup.interactable = false;
            bossHudGroup.blocksRaycasts = false;
        }

        if (!showBoss)
        {
            if (bossHpText != null)
            {
                bossHpText.text = string.Empty;
            }

            if (bossHpFillImage != null)
            {
                bossHpFillImage.fillAmount = 0f;
            }

            return;
        }

        int currentHp = Mathf.Max(0, boss.CurrentHP);
        int maxHp = Mathf.Max(1, boss.maxHP);
        float hpRatio = Mathf.Clamp01(currentHp / (float)maxHp);

        if (bossHpFillImage != null)
        {
            bossHpFillImage.fillAmount = hpRatio;
            bossHpFillImage.color = Color.Lerp(
                new Color(0.95f, 0.18f, 0.16f, 0.95f),
                new Color(0.7f, 0.28f, 0.92f, 0.95f),
                hpRatio);
        }

        if (bossHpText != null)
        {
            bossHpText.text = $"BOSS {currentHp}/{maxHp}";
        }
    }

    private EnemyControllerBase FindActiveBoss()
    {
        RoguelikeGameManager gameManager = RoguelikeGameManager.Instance;
        if (gameManager != null)
        {
            IReadOnlyList<EnemyControllerBase> enemies = gameManager.ActiveEnemies;
            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyControllerBase enemy = enemies[i];
                if (enemy != null && enemy.enemyKind == EnemyKind.Boss && enemy.IsAlive)
                {
                    return enemy;
                }
            }
        }

        EnemyControllerBase[] sceneEnemies = Object.FindObjectsByType<EnemyControllerBase>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneEnemies.Length; i++)
        {
            EnemyControllerBase enemy = sceneEnemies[i];
            if (enemy != null && enemy.enemyKind == EnemyKind.Boss && enemy.IsAlive)
            {
                return enemy;
            }
        }

        return null;
    }

    private void ApplyModeAndPolicy(ADUIInteractionDemand demand, ADUIAdjustmentPolicy policy)
    {
        ADUIInteractionMode mode = policy != null ? policy.mode : demand != null ? demand.mode : ADUIInteractionMode.ActionFirst;
        bool actionFirst = mode == ADUIInteractionMode.ActionFirst;

        if (modeChipText != null)
        {
            modeChipText.text = actionFirst ? "ACTION FIRST" : "COGNITIVE FIRST";
        }

        if (modeChipBackground != null)
        {
            float emphasis = policy != null ? Mathf.Clamp01(policy.emphasis) : 0.6f;
            Color color = actionFirst ? ActionFirstColor : CognitiveFirstColor;
            color.a = Mathf.Lerp(0.72f, 0.96f, emphasis);
            modeChipBackground.color = color;
        }

        if (playerHudGroup != null && policy != null)
        {
            playerHudGroup.alpha = Mathf.Clamp(policy.visibility, 0.88f, 1f);
        }
    }

    private void ApplyScenario(ADUIInteractionDemand demand, ADUIContextScenario scenario)
    {
        if (scenarioTagText != null)
        {
            scenarioTagText.text = UserContextPriorModel.ScenarioLabel(scenario);
        }

        if (dangerIndicatorText == null)
        {
            return;
        }

        float urgency = demand != null ? demand.temporalUrgency : 0f;
        if (urgency >= 0.72f)
        {
            dangerIndicatorText.text = "DANGER: IMMEDIATE";
            dangerIndicatorText.color = new Color(1f, 0.22f, 0.16f, 1f);
        }
        else if (urgency >= 0.45f)
        {
            dangerIndicatorText.text = "DANGER: PREPARE";
            dangerIndicatorText.color = new Color(1f, 0.78f, 0.18f, 1f);
        }
        else if (demand != null && demand.informationPriority >= 0.65f)
        {
            dangerIndicatorText.text = "READ STATE";
            dangerIndicatorText.color = new Color(0.52f, 0.78f, 1f, 1f);
        }
        else
        {
            dangerIndicatorText.text = "STABLE";
            dangerIndicatorText.color = new Color(0.8f, 0.92f, 0.86f, 1f);
        }
    }

    private void ApplyCorrectionToast(string message, Color color, bool visible)
    {
        if (correctionToastText == null)
        {
            return;
        }

        correctionToastText.text = visible ? message : string.Empty;
        correctionToastText.color = color;
    }

    private void ApplyResearchOverlay(string debugLine, ADUIInteractionDemand demand, ADUIAdjustmentPolicy policy)
    {
        if (researchOverlayGroup != null)
        {
            researchOverlayGroup.alpha = showResearchOverlay ? 1f : 0f;
            researchOverlayGroup.interactable = false;
            researchOverlayGroup.blocksRaycasts = false;
        }

        if (researchOverlayText == null)
        {
            return;
        }

        if (!showResearchOverlay)
        {
            researchOverlayText.text = string.Empty;
            return;
        }

        string demandLine = demand != null
            ? $"demand action={demand.actionIntensity:0.00} urgency={demand.temporalUrgency:0.00} info={demand.informationPriority:0.00} occlusion={demand.occlusionRisk:0.00} continuity={demand.controlContinuity:0.00} skill={demand.uiSkill:0.00}"
            : "demand unavailable";
        string policyLine = policy != null
            ? $"policy visibility={policy.visibility:0.00} emphasis={policy.emphasis:0.00} density={policy.density:0.00} posLock={policy.positionConstraint:0.00} errTol={policy.interactionErrorTolerance:0.00}"
            : "policy unavailable";

        researchOverlayText.text = $"{demandLine}\n{policyLine}\n{debugLine}";
    }
}

public static class KoreanTmpFontUtility
{
    private static readonly string[] FontCandidates =
    {
        "Malgun Gothic",
        "맑은 고딕",
        "Noto Sans CJK KR",
        "Noto Sans KR",
        "NotoSansCJK-Regular",
        "NanumGothic",
        "SamsungOneKorean",
        "Droid Sans Fallback"
    };

    private static TMP_FontAsset cachedFontAsset;
    private static bool attemptedCreate;

    public static void Apply(TextMeshProUGUI text)
    {
        if (text == null)
        {
            return;
        }

        TMP_FontAsset fontAsset = GetFontAsset();
        if (fontAsset == null)
        {
            return;
        }

        text.font = fontAsset;
        text.fontSharedMaterial = fontAsset.material;
    }

    public static void ApplyToAllText()
    {
        foreach (TextMeshProUGUI text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Apply(text);
        }
    }

    private static TMP_FontAsset GetFontAsset()
    {
        if (cachedFontAsset != null)
        {
            return cachedFontAsset;
        }

        if (attemptedCreate)
        {
            return null;
        }

        attemptedCreate = true;
        Font font = Resources.Load<Font>("Fonts/NotoSansKR");
        if (font == null)
        {
            font = CreateOsFont();
        }

        if (font == null)
        {
            Debug.LogWarning("[ADUI] Korean TMP font unavailable. Korean glyphs may render as boxes.");
            return null;
        }

        cachedFontAsset = TMP_FontAsset.CreateFontAsset(font);
        if (cachedFontAsset == null)
        {
            Debug.LogWarning($"[ADUI] Failed to create TMP font asset from {font.name}. Korean glyphs may render as boxes.");
            return null;
        }

        cachedFontAsset.name = "Runtime Korean TMP Font";
        cachedFontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        Debug.Log($"[ADUI] Korean TMP font active: {font.name}");
        return cachedFontAsset;
    }

    private static Font CreateOsFont()
    {
        for (int i = 0; i < FontCandidates.Length; i++)
        {
            Font font = Font.CreateDynamicFontFromOSFont(FontCandidates[i], 24);
            if (font != null)
            {
                return font;
            }
        }

        return null;
    }
}
