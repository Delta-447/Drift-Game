using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The press for a raised, code-built button (see GameManager.AddButtonDepth).
/// Its sprite has the face sitting on a darker side; while a finger is down
/// the face swaps for one pushed down onto the side, and everything on the
/// face - label, icons - sinks with it, then comes back up on release.
///
/// The side takes up the bottom few units of the button, so the face sits a
/// little above its middle. Once the button is built, everything on it is
/// lifted by <see cref="lift"/> to sit centred on the face. That is done in
/// Start, after whoever made the button has finished placing things on it,
/// so callers keep positioning labels and icons around the middle as before.
/// </summary>
public class ButtonDepth : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
{
    public Image image;
    public Sprite upSprite, downSprite;
    public float lift;      // how far the face's middle is above the button's
    public float travel;    // how far the face sinks when pressed

    bool pressed;

    void Start()
    {
        Shift(lift);
    }

    public void OnPointerDown(PointerEventData e) { SetPressed(true); }
    public void OnPointerUp(PointerEventData e) { SetPressed(false); }
    public void OnPointerExit(PointerEventData e) { SetPressed(false); }

    // sliding back onto the button while still holding it presses it again
    public void OnPointerEnter(PointerEventData e)
    {
        if (e.pointerPress == gameObject) SetPressed(true);
    }

    // a button that hides its own screen never sees the finger lift
    void OnDisable() { SetPressed(false); }

    void SetPressed(bool down)
    {
        if (down == pressed) return;
        if (down)
        {
            var selectable = GetComponent<Selectable>();
            if (selectable != null && !selectable.IsInteractable()) return;
        }
        pressed = down;
        if (image != null) image.sprite = down ? downSprite : upSprite;
        Shift(down ? -travel : travel);
    }

    void Shift(float dy)
    {
        if (dy == 0f) return;
        foreach (Transform child in transform)
        {
            var rt = child as RectTransform;
            if (rt != null) rt.anchoredPosition += new Vector2(0f, dy);
        }
    }
}
