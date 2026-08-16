using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Makes a panel spring into view instead of snapping on: it scales up from
/// small with a slight overshoot while fading in, and an optional dimmer
/// behind it fades with it. Runs on unscaled time so it still animates while
/// the game is paused.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PopIn : MonoBehaviour
{
    public float duration = 0.32f;
    public float startScale = 0.7f;

    [Tooltip("Optional full-screen dimmer that fades in behind the panel.")]
    public Graphic dimmer;
    public float dimmerAlpha = 0.65f;

    RectTransform rt;
    CanvasGroup group;
    float t;
    bool running;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        if (rt == null) Awake();
        t = 0f;
        running = true;
        Apply(0f);
    }

    /// <summary>Ease-out-back: overshoots slightly, then settles.</summary>
    static float Back(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float q = p - 1f;
        return 1f + c3 * q * q * q + c1 * q * q;
    }

    void Apply(float p)
    {
        float eased = Back(p);
        rt.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, eased);
        // fades in over the first half so the panel is readable by the bounce
        group.alpha = Mathf.Clamp01(p / 0.5f);
        if (dimmer != null)
        {
            Color c = dimmer.color;
            dimmer.color = new Color(c.r, c.g, c.b, dimmerAlpha * Mathf.Clamp01(p / 0.5f));
        }
    }

    void Update()
    {
        if (!running) return;
        t += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01(t / duration);
        Apply(p);
        if (p >= 1f)
        {
            running = false;
            rt.localScale = Vector3.one;
            group.alpha = 1f;
        }
    }
}
