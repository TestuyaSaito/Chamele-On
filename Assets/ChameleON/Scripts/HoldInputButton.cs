using UnityEngine;
using UnityEngine.EventSystems;

public sealed class HoldInputButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsPressed { get; private set; }

    private Vector3 releasedScale;

    private void Awake()
    {
        releasedScale = transform.localScale;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsPressed = true;
        transform.localScale = releasedScale * 0.92f;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Release();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Release();
    }

    private void OnDisable()
    {
        Release();
    }

    private void Release()
    {
        IsPressed = false;
        transform.localScale = releasedScale;
    }
}
