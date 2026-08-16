using UnityEngine;

/// <summary>
/// Animates the shop's mystery toolbox: gentle idle wobble, and a springy
/// lid-pop when opened.
/// </summary>
public class ToolboxAnimator : MonoBehaviour
{
    public Transform lidPivot;
    public Transform prizeItem; // rises out of the box while the lid is open

    float openT = -1f; // -1 = idle
    Vector3 prizeStart;
    const float OpenDuration = 1.4f;

    public void PlayOpen()
    {
        openT = 0f;
        if (prizeItem != null)
        {
            prizeItem.gameObject.SetActive(true);
            prizeStart = prizeItem.localPosition;
        }
    }

    void Update()
    {
        // idle wobble makes the box look alive
        float wobble = Mathf.Sin(Time.time * 2.1f) * 3f;
        transform.localRotation = Quaternion.Euler(0f, wobble * 2f, wobble * 0.4f);

        if (lidPivot == null) return;

        float lidAngle = 0f;
        if (openT >= 0f)
        {
            openT += Time.deltaTime / OpenDuration;
            if (openT >= 1f)
            {
                openT = -1f;
            }
            else if (openT < 0.25f)
            {
                // pop open with overshoot
                float p = openT / 0.25f;
                lidAngle = Mathf.Sin(p * Mathf.PI * 0.5f) * 130f;
            }
            else if (openT < 0.7f)
            {
                lidAngle = 130f - Mathf.Sin((openT - 0.25f) * 14f) * 6f; // rattle
            }
            else
            {
                float p = (openT - 0.7f) / 0.3f;
                lidAngle = Mathf.Lerp(130f, 0f, p * p); // slam shut
            }
        }
        lidPivot.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);

        // the prize rises out of the box and HOVERS just above it in clear
        // view, only vanishing right as the lid slams shut
        if (prizeItem != null)
        {
            if (openT >= 0f && openT < 0.9f)
            {
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((openT - 0.1f) / 0.3f));
                float hoverBob = Mathf.Sin(Time.time * 5f) * 0.06f;
                prizeItem.localPosition = prizeStart + Vector3.up * (0.95f * rise + hoverBob * rise);
            }
            else if (openT >= 0.9f || openT < 0f)
            {
                prizeItem.gameObject.SetActive(false);
            }
        }
    }
}
