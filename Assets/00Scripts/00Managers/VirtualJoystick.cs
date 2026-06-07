using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class VirtualJoystick : MonoBehaviour
{
    [Header("References")]
    public PlayerController targetPlayer;
    public RectTransform joystickBase;
    public RectTransform joystickHandle;

    [Header("Tuning")]
    [Range(0f, 0.5f)]
    public float deadZone = 0.08f;
    [Range(0.25f, 1f)]
    public float handleTravelRatio = 0.42f;

    private int activeTouchId = -1;
    private bool mouseActive;

    private void Awake()
    {
        EnhancedTouchSupport.Enable();

        if (targetPlayer == null)
        {
            targetPlayer = FindAnyObjectByType<PlayerController>();
        }

        if (joystickBase == null)
        {
            joystickBase = transform as RectTransform;
        }
    }

    private void Update()
    {
        UpdateTouchInput();

#if UNITY_EDITOR
        UpdateMouseInput();
#endif
    }

    private void OnDisable()
    {
        ClearInput();
    }

    private void UpdateTouchInput()
    {
        if (activeTouchId >= 0)
        {
            bool foundTouch = false;
            foreach (Touch touch in Touch.activeTouches)
            {
                if (touch.touchId != activeTouchId)
                {
                    continue;
                }

                foundTouch = true;
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                    touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    ClearInput();
                }
                else
                {
                    UpdateInput(touch.screenPosition);
                }

                break;
            }

            if (!foundTouch)
            {
                ClearInput();
            }

            return;
        }

        foreach (Touch touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began &&
                IsInJoystickArea(touch.screenPosition))
            {
                activeTouchId = touch.touchId;
                UpdateInput(touch.screenPosition);
                return;
            }
        }
    }

    private void UpdateMouseInput()
    {
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        if (Mouse.current.leftButton.wasPressedThisFrame && IsInJoystickArea(mousePosition))
        {
            mouseActive = true;
            UpdateInput(mousePosition);
        }
        else if (mouseActive && Mouse.current.leftButton.isPressed)
        {
            UpdateInput(mousePosition);
        }
        else if (mouseActive && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            mouseActive = false;
            ClearInput();
        }
    }

    private void UpdateInput(Vector2 screenPosition)
    {
        if (joystickBase == null)
        {
            return;
        }

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            joystickBase,
            screenPosition,
            GetUiCamera(),
            out Vector2 localPoint);

        float radius = Mathf.Max(1f, Mathf.Min(joystickBase.rect.width, joystickBase.rect.height) * 0.5f);
        Vector2 normalizedInput = Vector2.ClampMagnitude(localPoint / radius, 1f);
        if (normalizedInput.magnitude < deadZone)
        {
            normalizedInput = Vector2.zero;
        }

        if (joystickHandle != null)
        {
            joystickHandle.anchoredPosition = normalizedInput * radius * handleTravelRatio;
        }

        if (targetPlayer != null)
        {
            targetPlayer.SetVirtualMoveInput(normalizedInput);
        }
    }

    private bool IsInJoystickArea(Vector2 screenPosition)
    {
        return joystickBase != null &&
               RectTransformUtility.RectangleContainsScreenPoint(joystickBase, screenPosition, GetUiCamera());
    }

    private Camera GetUiCamera()
    {
        Canvas canvas = joystickBase != null ? joystickBase.GetComponentInParent<Canvas>() : null;
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }

    private void ClearInput()
    {
        activeTouchId = -1;
        mouseActive = false;

        if (joystickHandle != null)
        {
            joystickHandle.anchoredPosition = Vector2.zero;
        }

        if (targetPlayer != null)
        {
            targetPlayer.SetVirtualMoveInput(Vector2.zero);
        }
    }
}
