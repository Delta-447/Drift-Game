using UnityEngine;

/// <summary>
/// Scales a piece of scenery up from nothing over a moment, so something
/// planted within sight of the camera grows in rather than popping into
/// existence. Removes itself when done.
/// </summary>
public class GrowIn : MonoBehaviour
{
    Vector3 full;
    float duration;
    float t;

    public static void Begin(GameObject go, float seconds)
    {
        if (go == null) return;
        var g = go.AddComponent<GrowIn>();
        g.full = go.transform.localScale;
        g.duration = Mathf.Max(0.05f, seconds);
        go.transform.localScale = g.full * 0.05f;
    }

    void Update()
    {
        t += Time.deltaTime;
        float p = Mathf.Clamp01(t / duration);
        float e = 1f - Mathf.Pow(1f - p, 3f);
        transform.localScale = full * Mathf.Lerp(0.05f, 1f, e);
        if (p >= 1f) Destroy(this);
    }
}
