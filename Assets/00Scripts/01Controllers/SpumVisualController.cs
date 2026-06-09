using System.Collections.Generic;
using UnityEngine;

public class SpumVisualController : MonoBehaviour
{
    [Header("SPUM Prefab")]
    public string resourcePath;
    public Vector3 visualLocalPosition = Vector3.zero;
    public Vector3 visualLocalScale = Vector3.one;
    public bool faceRightWithNegativeScale = true;
    public int sortingOrderOffset;

    [Header("Animation Indexes")]
    public int idleIndex;
    public int moveIndex;
    public int meleeAttackIndex;
    public int skillAttackIndex = 1;
    public int bowAttackIndex = 2;
    public int magicAttackIndex = 4;
    public int damagedIndex;
    public int deathIndex;
    public int concentrateIndex;
    public int buffIndex = 1;

    public bool HasVisual => spumPrefab != null;

    private SPUM_Prefabs spumPrefab;
    private Transform visualRoot;
    [SerializeField] private RuntimeAnimatorController baseAnimatorController;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalColors;
    private int[] originalSortingOrders;
    private bool initialized;
    private PlayerState currentState = PlayerState.IDLE;
    private float actionLockUntil;

    private void Awake()
    {
        if (!string.IsNullOrEmpty(resourcePath))
        {
            EnsureVisual();
        }
    }

    private void OnEnable()
    {
        if (EnsureInitialized())
        {
            PlayIdle(true);
        }
    }

    public void Configure(string newResourcePath, Vector3 localPosition, Vector3 localScale, int orderOffset = 0)
    {
        resourcePath = newResourcePath;
        visualLocalPosition = localPosition;
        visualLocalScale = localScale;
        sortingOrderOffset = orderOffset;

        EnsureVisual();
        if (gameObject.activeInHierarchy && EnsureInitialized())
        {
            ApplyLocalTransform();
            PlayIdle(true);
        }
    }

    public void SetMoving(bool moving)
    {
        if (Time.time < actionLockUntil)
        {
            return;
        }

        if (moving)
        {
            PlayMove();
        }
        else
        {
            PlayIdle();
        }
    }

    public void FaceDirection(Vector2 direction)
    {
        if (visualRoot == null || visualRoot == transform || Mathf.Abs(direction.x) < 0.025f)
        {
            return;
        }

        float baseScaleX = Mathf.Abs(visualLocalScale.x) > 0.001f ? Mathf.Abs(visualLocalScale.x) : 1f;
        float sign = direction.x > 0f
            ? (faceRightWithNegativeScale ? -1f : 1f)
            : (faceRightWithNegativeScale ? 1f : -1f);

        visualRoot.localScale = new Vector3(sign * baseScaleX, visualLocalScale.y, visualLocalScale.z);
    }

    public void PlayIdle(bool force = false)
    {
        PlayLoopState(PlayerState.IDLE, idleIndex, force);
    }

    public void PlayMove(bool force = false)
    {
        PlayLoopState(PlayerState.MOVE, moveIndex, force);
    }

    public void PlayMeleeAttack(float lockDuration = 0.35f)
    {
        PlayAction(PlayerState.ATTACK, meleeAttackIndex, lockDuration);
    }

    public void PlaySkillAttack(float lockDuration = 0.45f)
    {
        PlayAction(PlayerState.ATTACK, skillAttackIndex, lockDuration);
    }

    public void PlayBowAttack(float lockDuration = 0.45f)
    {
        PlayAction(PlayerState.ATTACK, bowAttackIndex, lockDuration);
    }

    public void PlayMagicAttack(float lockDuration = 0.55f)
    {
        PlayAction(PlayerState.ATTACK, magicAttackIndex, lockDuration);
    }

    public void PlayDamaged(float lockDuration = 0.22f)
    {
        PlayAction(PlayerState.DAMAGED, damagedIndex, lockDuration);
    }

    public void PlayDeath()
    {
        actionLockUntil = float.PositiveInfinity;
        TryPlay(PlayerState.DEATH, deathIndex);
    }

    public void PlayConcentrate(float lockDuration = 0.35f)
    {
        PlayAction(PlayerState.OTHER, concentrateIndex, lockDuration);
    }

    public void PlayBuff(float lockDuration = 0.42f)
    {
        PlayAction(PlayerState.OTHER, buffIndex, lockDuration);
    }

    public void SetTint(Color tint, float strength = 0.35f)
    {
        if (!EnsureInitialized() || spriteRenderers == null)
        {
            return;
        }

        float blend = Mathf.Clamp01(strength);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = Color.Lerp(originalColors[i], tint, blend);
            }
        }
    }

    public void ResetTint()
    {
        if (spriteRenderers == null || originalColors == null)
        {
            return;
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].color = originalColors[i];
            }
        }
    }

    private void PlayLoopState(PlayerState state, int index, bool force)
    {
        if (force)
        {
            actionLockUntil = 0f;
        }

        if (!force && currentState == state)
        {
            return;
        }

        if (TryPlay(state, index))
        {
            currentState = state;
        }
    }

    private void PlayAction(PlayerState state, int index, float lockDuration)
    {
        actionLockUntil = Time.time + Mathf.Max(0.01f, lockDuration);
        if (TryPlay(state, index))
        {
            currentState = state;
        }
    }

    private bool TryPlay(PlayerState state, int requestedIndex)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        if (!TryGetSafeClipIndex(state, requestedIndex, out int safeIndex))
        {
            return false;
        }

        spumPrefab.PlayAnimation(state, safeIndex);
        currentState = state;
        return true;
    }

    private bool TryGetSafeClipIndex(PlayerState state, int requestedIndex, out int safeIndex)
    {
        safeIndex = 0;
        if (spumPrefab == null || spumPrefab.StateAnimationPairs == null)
        {
            return false;
        }

        string stateName = state.ToString();
        if (!spumPrefab.StateAnimationPairs.TryGetValue(stateName, out List<AnimationClip> clips) || clips == null || clips.Count == 0)
        {
            return false;
        }

        safeIndex = Mathf.Clamp(requestedIndex, 0, clips.Count - 1);
        if (clips[safeIndex] == null)
        {
            safeIndex = clips.FindIndex(clip => clip != null);
        }

        return safeIndex >= 0;
    }

    private bool EnsureVisual()
    {
        if (spumPrefab != null)
        {
            return true;
        }

        spumPrefab = GetComponentInChildren<SPUM_Prefabs>(true);
        if (spumPrefab != null)
        {
            visualRoot = spumPrefab.transform;
            CaptureBaseAnimatorController();
            ApplyLocalTransform();
            return true;
        }

        if (string.IsNullOrEmpty(resourcePath))
        {
            return false;
        }

        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"SPUM visual prefab not found at Resources/{resourcePath}.");
            return false;
        }

        GameObject visualObject = Instantiate(prefab, transform);
        visualObject.name = "SPUM Visual";
        visualRoot = visualObject.transform;
        SetLayerRecursively(visualObject, gameObject.layer);
        ApplyLocalTransform();

        spumPrefab = visualObject.GetComponentInChildren<SPUM_Prefabs>(true);
        if (spumPrefab == null)
        {
            Debug.LogWarning($"SPUM visual prefab at Resources/{resourcePath} has no SPUM_Prefabs component.");
            Destroy(visualObject);
            visualRoot = null;
            return false;
        }

        CaptureBaseAnimatorController();
        initialized = false;
        return true;
    }

    private bool EnsureInitialized()
    {
        if (initialized && spumPrefab != null)
        {
            return true;
        }

        if (!EnsureVisual())
        {
            return false;
        }

        if (spumPrefab._anim == null)
        {
            spumPrefab._anim = spumPrefab.GetComponentInChildren<Animator>(true);
        }

        if (spumPrefab._anim == null)
        {
            Debug.LogWarning($"SPUM visual on {name} is missing an Animator.");
            return false;
        }

        if (spumPrefab._anim.runtimeAnimatorController == null && baseAnimatorController != null)
        {
            spumPrefab._anim.runtimeAnimatorController = baseAnimatorController;
        }

        if (spumPrefab._anim.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"SPUM visual on {name} is missing a runtime animator controller.");
            return false;
        }

        CaptureBaseAnimatorController();

        if (!spumPrefab.allListsHaveItemsExist())
        {
            spumPrefab.PopulateAnimationLists();
        }

        if (!PrepareOverrideController())
        {
            return false;
        }

        CaptureRendererState();
        ApplySortingOffset();
        initialized = true;
        return true;
    }

    private bool PrepareOverrideController()
    {
        RuntimeAnimatorController currentController = spumPrefab._anim.runtimeAnimatorController;
        AnimatorOverrideController existingOverride = currentController as AnimatorOverrideController;
        if (existingOverride != null)
        {
            RuntimeAnimatorController overrideBaseController = UnwrapOverrideController(existingOverride);
            if (overrideBaseController == null)
            {
                if (baseAnimatorController == null)
                {
                    Debug.LogWarning($"SPUM visual on {name} has an invalid AnimatorOverrideController and no base controller to restore.");
                    return false;
                }

                spumPrefab._anim.runtimeAnimatorController = baseAnimatorController;
                spumPrefab.OverrideControllerInit();
                RefreshStateAnimationPairs();
                return true;
            }

            baseAnimatorController = overrideBaseController;
            spumPrefab.OverrideController = existingOverride;
            RefreshStateAnimationPairs();
            return true;
        }

        baseAnimatorController = currentController;
        spumPrefab.OverrideControllerInit();
        RefreshStateAnimationPairs();
        return true;
    }

    private void CaptureBaseAnimatorController()
    {
        if (spumPrefab == null)
        {
            return;
        }

        if (spumPrefab._anim == null)
        {
            spumPrefab._anim = spumPrefab.GetComponentInChildren<Animator>(true);
        }

        if (spumPrefab._anim == null || spumPrefab._anim.runtimeAnimatorController == null)
        {
            return;
        }

        RuntimeAnimatorController controller = UnwrapOverrideController(spumPrefab._anim.runtimeAnimatorController);
        if (controller != null)
        {
            baseAnimatorController = controller;
        }
    }

    private static RuntimeAnimatorController UnwrapOverrideController(RuntimeAnimatorController controller)
    {
        for (int i = 0; i < 8; i++)
        {
            AnimatorOverrideController overrideController = controller as AnimatorOverrideController;
            if (overrideController == null)
            {
                return controller;
            }

            controller = overrideController.runtimeAnimatorController;
            if (controller == null)
            {
                return null;
            }
        }

        return controller is AnimatorOverrideController ? null : controller;
    }

    private void RefreshStateAnimationPairs()
    {
        if (spumPrefab.StateAnimationPairs == null)
        {
            spumPrefab.StateAnimationPairs = new Dictionary<string, List<AnimationClip>>();
        }

        spumPrefab.StateAnimationPairs[PlayerState.IDLE.ToString()] = spumPrefab.IDLE_List;
        spumPrefab.StateAnimationPairs[PlayerState.MOVE.ToString()] = spumPrefab.MOVE_List;
        spumPrefab.StateAnimationPairs[PlayerState.ATTACK.ToString()] = spumPrefab.ATTACK_List;
        spumPrefab.StateAnimationPairs[PlayerState.DAMAGED.ToString()] = spumPrefab.DAMAGED_List;
        spumPrefab.StateAnimationPairs[PlayerState.DEBUFF.ToString()] = spumPrefab.DEBUFF_List;
        spumPrefab.StateAnimationPairs[PlayerState.DEATH.ToString()] = spumPrefab.DEATH_List;
        spumPrefab.StateAnimationPairs[PlayerState.OTHER.ToString()] = spumPrefab.OTHER_List;
    }

    private void ApplyLocalTransform()
    {
        if (visualRoot == null || visualRoot == transform)
        {
            return;
        }

        visualRoot.localPosition = visualLocalPosition;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = visualLocalScale;
    }

    private void CaptureRendererState()
    {
        if (spumPrefab == null)
        {
            return;
        }

        spriteRenderers = spumPrefab.GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[spriteRenderers.Length];
        originalSortingOrders = new int[spriteRenderers.Length];

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] == null)
            {
                continue;
            }

            originalColors[i] = spriteRenderers[i].color;
            originalSortingOrders[i] = spriteRenderers[i].sortingOrder;
        }
    }

    private void ApplySortingOffset()
    {
        if (spriteRenderers == null || originalSortingOrders == null)
        {
            return;
        }

        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            if (spriteRenderers[i] != null)
            {
                spriteRenderers[i].sortingOrder = originalSortingOrders[i] + sortingOrderOffset;
            }
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
