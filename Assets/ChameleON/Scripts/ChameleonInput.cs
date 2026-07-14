using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using LegacyTouchPhase = UnityEngine.TouchPhase;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

internal readonly struct ChameleonTouch
{
    public ChameleonTouch(int fingerId, Vector2 position, Vector2 deltaPosition, LegacyTouchPhase phase)
    {
        FingerId = fingerId;
        Position = position;
        DeltaPosition = deltaPosition;
        Phase = phase;
    }

    public int FingerId { get; }
    public Vector2 Position { get; }
    public Vector2 DeltaPosition { get; }
    public LegacyTouchPhase Phase { get; }
}

/// <summary>
/// Small input compatibility layer. The URP project uses the new Input System,
/// while this prototype can still be opened in a project configured for the
/// legacy Input Manager.
/// </summary>
internal static class ChameleonInput
{
    private static readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>(16);
#if ENABLE_INPUT_SYSTEM
    private const int MaximumTouches = 16;
    private static readonly ChameleonTouch[] touches = new ChameleonTouch[MaximumTouches];
    private static int cachedTouchFrame = -1;
    private static int cachedTouchCount;
#endif

    public static int TouchCount
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            RefreshTouches();
            return cachedTouchCount;
#else
            return Input.touchCount;
#endif
        }
    }

    public static ChameleonTouch GetTouch(int index)
    {
#if ENABLE_INPUT_SYSTEM
        RefreshTouches();
        return index >= 0 && index < cachedTouchCount
            ? touches[index]
            : new ChameleonTouch(-1, Vector2.zero, Vector2.zero, LegacyTouchPhase.Canceled);
#else
        Touch touch = Input.GetTouch(index);
        return new ChameleonTouch(touch.fingerId, touch.position, touch.deltaPosition, touch.phase);
#endif
    }

    public static Vector2 MousePosition
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
    }

    public static Vector2 MouseDelta
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        }
    }

    public static Vector2 MouseScrollDelta
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue() : Vector2.zero;
#else
            return Input.mouseScrollDelta;
#endif
        }
    }

    public static bool GetMouseButtonDown(int button)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        switch (button)
        {
            case 0: return Mouse.current.leftButton.wasPressedThisFrame;
            case 1: return Mouse.current.rightButton.wasPressedThisFrame;
            case 2: return Mouse.current.middleButton.wasPressedThisFrame;
            default: return false;
        }
#else
        return Input.GetMouseButtonDown(button);
#endif
    }

    public static bool GetMouseButton(int button)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        switch (button)
        {
            case 0: return Mouse.current.leftButton.isPressed;
            case 1: return Mouse.current.rightButton.isPressed;
            case 2: return Mouse.current.middleButton.isPressed;
            default: return false;
        }
#else
        return Input.GetMouseButton(button);
#endif
    }

    public static bool GetMouseButtonUp(int button)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        switch (button)
        {
            case 0: return Mouse.current.leftButton.wasReleasedThisFrame;
            case 1: return Mouse.current.rightButton.wasReleasedThisFrame;
            case 2: return Mouse.current.middleButton.wasReleasedThisFrame;
            default: return false;
        }
#else
        return Input.GetMouseButtonUp(button);
#endif
    }

    public static bool GetKey(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;
        switch (key)
        {
            case KeyCode.A: return keyboard.aKey.isPressed;
            case KeyCode.D: return keyboard.dKey.isPressed;
            case KeyCode.S: return keyboard.sKey.isPressed;
            case KeyCode.W: return keyboard.wKey.isPressed;
            case KeyCode.LeftArrow: return keyboard.leftArrowKey.isPressed;
            case KeyCode.RightArrow: return keyboard.rightArrowKey.isPressed;
            case KeyCode.DownArrow: return keyboard.downArrowKey.isPressed;
            case KeyCode.UpArrow: return keyboard.upArrowKey.isPressed;
            default: return false;
        }
#else
        return Input.GetKey(key);
#endif
    }

    public static bool IsPointerOverUi(Vector2 screenPosition)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        var pointer = new PointerEventData(eventSystem) { position = screenPosition };
        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointer, uiRaycastResults);
        return uiRaycastResults.Count > 0;
    }

#if ENABLE_INPUT_SYSTEM
    private static void RefreshTouches()
    {
        if (cachedTouchFrame == Time.frameCount)
        {
            return;
        }

        cachedTouchFrame = Time.frameCount;
        cachedTouchCount = 0;
        Touchscreen screen = Touchscreen.current;
        if (screen == null)
        {
            return;
        }

        foreach (UnityEngine.InputSystem.Controls.TouchControl control in screen.touches)
        {
            bool pressed = control.press.isPressed;
            if (!pressed && !control.press.wasPressedThisFrame && !control.press.wasReleasedThisFrame)
            {
                continue;
            }

            if (cachedTouchCount >= touches.Length)
            {
                break;
            }

            UnityEngine.InputSystem.TouchPhase sourcePhase = control.phase.ReadValue();
            LegacyTouchPhase phase;
            switch (sourcePhase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began: phase = LegacyTouchPhase.Began; break;
                case UnityEngine.InputSystem.TouchPhase.Moved: phase = LegacyTouchPhase.Moved; break;
                case UnityEngine.InputSystem.TouchPhase.Stationary: phase = LegacyTouchPhase.Stationary; break;
                case UnityEngine.InputSystem.TouchPhase.Ended: phase = LegacyTouchPhase.Ended; break;
                case UnityEngine.InputSystem.TouchPhase.Canceled: phase = LegacyTouchPhase.Canceled; break;
                default:
                    phase = control.press.wasReleasedThisFrame ? LegacyTouchPhase.Ended : LegacyTouchPhase.Stationary;
                    break;
            }

            touches[cachedTouchCount++] = new ChameleonTouch(
                control.touchId.ReadValue(),
                control.position.ReadValue(),
                control.delta.ReadValue(),
                phase);
        }
    }
#endif
}
