using UnityEngine;

public class TopDownCameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector2 followOffset = new Vector2(0f, 0.35f);
    [Range(1f, 40f)]
    public float followSharpness = 14f;
    public bool snapOnFirstFrame = true;
    public bool constrainToArena = true;

    private bool hasSnapped;
    private Camera cachedCamera;

    private void Awake()
    {
        cachedCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        ResolveTargetIfNeeded();
        if (target == null)
        {
            return;
        }

        Vector3 desired = new Vector3(
            target.position.x + followOffset.x,
            target.position.y + followOffset.y,
            transform.position.z);
        desired = ClampCameraPosition(desired);

        if (snapOnFirstFrame && !hasSnapped)
        {
            transform.position = desired;
            hasSnapped = true;
            return;
        }

        float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, t);
    }

    public void SetTarget(Transform nextTarget)
    {
        target = nextTarget;
        hasSnapped = false;
    }

    private void ResolveTargetIfNeeded()
    {
        if (target != null)
        {
            return;
        }

        PlayerController player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            SetTarget(player.transform);
        }
    }

    private Vector3 ClampCameraPosition(Vector3 desired)
    {
        if (!constrainToArena)
        {
            return desired;
        }

        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }

        Rect bounds = RoguelikeGameManager.ArenaBounds;
        if (cachedCamera == null || !cachedCamera.orthographic)
        {
            desired.x = Mathf.Clamp(desired.x, bounds.xMin, bounds.xMax);
            desired.y = Mathf.Clamp(desired.y, bounds.yMin, bounds.yMax);
            return desired;
        }

        float halfHeight = cachedCamera.orthographicSize;
        float halfWidth = halfHeight * cachedCamera.aspect;
        desired.x = ClampAxis(desired.x, bounds.xMin + halfWidth, bounds.xMax - halfWidth, bounds.center.x);
        desired.y = ClampAxis(desired.y, bounds.yMin + halfHeight, bounds.yMax - halfHeight, bounds.center.y);
        return desired;
    }

    private float ClampAxis(float value, float min, float max, float fallback)
    {
        return min <= max ? Mathf.Clamp(value, min, max) : fallback;
    }
}
