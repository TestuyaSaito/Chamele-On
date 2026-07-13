using UnityEngine;
using UnityEngine.EventSystems;

public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private RectTransform background;
    private RectTransform handle;
    private Canvas canvas;
    private Camera eventCamera;

    public Vector2 Value { get; private set; }

    public void Initialize(RectTransform backgroundRect, RectTransform handleRect, Canvas ownerCanvas)
    {
        background = backgroundRect;
        handle = handleRect;
        canvas = ownerCanvas;
        eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        ResetStick();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (background == null || handle == null)
        {
            return;
        }

        Camera camera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(background, eventData.position, camera, out Vector2 point))
        {
            return;
        }

        Vector2 halfSize = background.rect.size * 0.5f;
        float radius = Mathf.Max(1f, Mathf.Min(halfSize.x, halfSize.y));
        Vector2 normalized = Vector2.ClampMagnitude(point / radius, 1f);
        const float deadZone = 0.10f;
        float magnitude = normalized.magnitude;
        Value = magnitude <= deadZone
            ? Vector2.zero
            : normalized.normalized * Mathf.InverseLerp(deadZone, 1f, magnitude);
        handle.anchoredPosition = normalized * (radius * 0.58f);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ResetStick();
    }

    private void OnDisable()
    {
        ResetStick();
    }

    private void ResetStick()
    {
        Value = Vector2.zero;
        if (handle != null)
        {
            handle.anchoredPosition = Vector2.zero;
        }
    }
}
