using UnityEngine;

/// <summary>
/// Brings a prize chest (PrizeChest) to life.
///
/// Waiting, it sways gently, its neon breathes, and every few seconds
/// something inside knocks it - a little hop and the lid lifting a crack.
/// Opened, it gathers itself first (a hard shake and a squat), then the lid
/// bursts back past upright, light floods out of it, the prize rises into
/// view and hovers, and finally the lid slams shut.
/// </summary>
public class ToolboxAnimator : MonoBehaviour
{
    public Transform lidPivot;
    public Transform prizeItem; // rises out of the box while the lid is open

    PrizeChest.Rig rig;
    float openT = -1f;          // through the opening, 0 to 1; -1 = waiting
    Vector3 prizeStart;
    Vector3 home;
    bool homeSet;
    const float OpenDuration = 1.4f;
    const float LidOpen = 125f;

    /// <summary>Hooks up the lid and the lights of a chest built by PrizeChest.</summary>
    public void UseRig(PrizeChest.Rig chest)
    {
        rig = chest;
        lidPivot = chest.lidPivot;
    }

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
        if (!homeSet)
        {
            home = transform.localPosition;
            homeSet = true;
        }

        // a gentle sway makes the box look alive
        float sway = Mathf.Sin(Time.time * 2.1f) * 3f;
        Quaternion turn = Quaternion.Euler(0f, sway * 2f, sway * 0.4f);
        Vector3 pos = home;
        Vector3 scale = Vector3.one;
        float lidAngle = 0f;
        float flare = 0f;        // how lit up it is, 0 to 1
        float shaft = 0f;        // how far the light shaft has risen, 0 to 1

        if (openT < 0f)
        {
            // every few seconds something inside knocks it
            float cycle = Mathf.Repeat(Time.time, 3.2f);
            if (cycle > 2.85f)
            {
                float k = (cycle - 2.85f) / 0.35f;
                float hop = Mathf.Sin(k * Mathf.PI);
                pos += Vector3.up * (hop * 0.05f);
                lidAngle = hop * 9f;
                turn *= Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI * 3f) * 2f);
                flare = hop * 0.25f;
            }
        }
        else
        {
            openT += Time.deltaTime / OpenDuration;
            float s = openT * OpenDuration;          // seconds since it was tapped
            if (openT >= 1f)
            {
                openT = -1f;
            }
            else if (s < 0.18f)
            {
                // it gathers itself: shaking harder and squatting down
                float k = s / 0.18f;
                turn *= Quaternion.Euler(Mathf.Sin(s * 90f) * 3f * k, 0f, Mathf.Sin(s * 113f) * 4f * k);
                scale = new Vector3(1f + 0.04f * k, 1f - 0.07f * k, 1f + 0.04f * k);
                flare = 0.3f * k;
            }
            else if (s < 0.42f)
            {
                // bursts open, past upright and back, and springs up tall
                float k = (s - 0.18f) / 0.24f;
                lidAngle = LidOpen * EaseOutBack(k);
                float pop = Mathf.Sin(k * Mathf.PI);
                scale = new Vector3(1f - 0.03f * pop, 1f + 0.08f * pop, 1f - 0.03f * pop);
                flare = 0.3f + 0.7f * Mathf.Clamp01(k * 2f);
                shaft = 1f - (1f - k) * (1f - k);
            }
            else if (openT < 0.7f)
            {
                // open and lit while the prize hovers; the lid rattles to rest
                float since = s - 0.42f;
                lidAngle = LidOpen - Mathf.Sin(since * 18f) * 5f * Mathf.Exp(-since * 3f);
                flare = Mathf.Lerp(1f, 0.55f, since / (0.7f * OpenDuration - 0.42f));
                shaft = 1f;
            }
            else
            {
                // slams shut
                float p = (openT - 0.7f) / 0.3f;
                lidAngle = Mathf.Lerp(LidOpen, 0f, p * p);
                flare = 0.55f * (1f - p);
                shaft = 1f - p;
            }
        }

        transform.localPosition = pos;
        transform.localRotation = turn;
        transform.localScale = scale;
        if (lidPivot != null) lidPivot.localRotation = Quaternion.Euler(-lidAngle, 0f, 0f);
        LightUp(flare, shaft);

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

    /// <summary>The neon breathes; opening floods it towards white and lifts the shaft.</summary>
    void LightUp(float flare, float shaft)
    {
        if (rig == null) return;
        float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.6f);
        rig.glow.color = Toward(rig.glowColour, 0.08f * breathe + 0.5f * flare, 1f);
        rig.inner.color = Toward(rig.innerColour, 0.6f * flare, 1f);
        rig.emblem.color = Toward(rig.emblemColour, 0.06f * breathe + 0.35f * flare, 1f);

        bool lit = shaft > 0.01f;
        if (rig.beamQuad.gameObject.activeSelf != lit) rig.beamQuad.gameObject.SetActive(lit);
        if (lit)
        {
            rig.beamQuad.localScale = new Vector3(1.15f, 1.7f * shaft, 1f);
            rig.beam.color = Toward(rig.beamColour, 0f, 0.85f * shaft);
        }
    }

    static Color Toward(Color c, float white, float alpha)
    {
        Color o = Color.Lerp(c, Color.white, Mathf.Clamp01(white));
        o.a = alpha;
        return o;
    }

    static float EaseOutBack(float p)
    {
        p = Mathf.Clamp01(p);
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float q = p - 1f;
        return 1f + c3 * q * q * q + c1 * q * q;
    }
}
