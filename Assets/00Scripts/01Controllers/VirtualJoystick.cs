using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public PlayerController playerController;
    public RectTransform background;
    public RectTransform handle;
    public Canvas canvas;
    public float radius = 86f;
    [Range(0f, 0.9f)]
    public float deadZone = 0.12f;
    public Color idleHandleColor = new Color(0.85f, 0.95f, 1f, 0.9f);
    public Color activeHandleColor = new Color(0.25f, 0.85f, 1f, 0.98f);

    private Image handleImage;

    private void Awake()
    {
        if (background == null)
        {
            background = transform as RectTransform;
        }

        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
        }

        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }

        if (handle != null)
        {
            handleImage = handle.GetComponent<Image>();
        }

        SetInput(Vector2.zero);
    }

    private void OnDisable()
    {
        SetInput(Vector2.zero);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        UpdateDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        SetInput(Vector2.zero);
    }

    public void SetInput(Vector2 input)
    {
        Vector2 clamped = Vector2.ClampMagnitude(input, 1f);
        if (clamped.magnitude < deadZone)
        {
            clamped = Vector2.zero;
        }

        if (handle != null)
        {
            handle.anchoredPosition = clamped * radius;
        }

        if (handleImage != null)
        {
            handleImage.color = clamped.sqrMagnitude > 0.001f ? activeHandleColor : idleHandleColor;
        }

        if (playerController == null)
        {
            return;
        }

        if (clamped.sqrMagnitude > 0.001f)
        {
            playerController.SetVirtualMoveInput(clamped);
        }
        else
        {
            playerController.ClearVirtualMoveInput();
        }
    }

    private void UpdateDrag(PointerEventData eventData)
    {
        if (background == null)
        {
            return;
        }

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, eventCamera, out Vector2 localPoint))
        {
            return;
        }

        SetInput(localPoint / Mathf.Max(1f, radius));
    }
}
