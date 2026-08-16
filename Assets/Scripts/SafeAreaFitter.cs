using UnityEngine;

/// <summary>
/// Keeps a RectTransform inside the device safe area (iPhone notch,
/// rounded corners, home indicator). All UI lives under this container.
/// </summary>
public class SafeAreaFitter : MonoBehaviour
{
    [Tooltip("Extra top inset in pixels when the device reports no safe area " +
             "(Unity Remote and the editor never report a notch).")]
    public float fallbackTopInset = 90f;

    Rect applied;

    void Update()
    {
        Rect sa = Screen.safeArea;

        // Unity Remote streams the editor's view, which has no notch data.
        // Tall screens almost certainly have one, so inset the top anyway.
        bool reportsFullScreen = Mathf.Approximately(sa.height, Screen.height);
        bool tallScreen = Screen.height > Screen.width * 1.9f;
        if (reportsFullScreen && tallScreen)
        {
            sa = new Rect(sa.x, sa.y, sa.width, sa.height - fallbackTopInset);
        }

        if (sa == applied) return;
        applied = sa;

        var rt = (RectTransform)transform;
        Vector2 min = sa.position;
        Vector2 max = sa.position + sa.size;
        min.x /= Screen.width;
        min.y /= Screen.height;
        max.x /= Screen.width;
        max.y /= Screen.height;

        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
