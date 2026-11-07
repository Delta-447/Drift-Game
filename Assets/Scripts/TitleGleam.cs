using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Paints a piece of the opening title like the logo art: gold running from
/// pale at the top to deep orange at the bottom, a band of light that can
/// sweep across it, a white-hot flash, and a soft left-to-right reveal. It
/// only recolours the text's own vertices, so it costs nothing to draw.
///
/// Positions are measured across the whole word, so every letter of a word
/// made of separate letters is told where it sits (the offset passed to
/// <see cref="Apply"/>). Add it before the text's Outline, so the outline is
/// built from the recoloured letters and stays black.
/// </summary>
[DisallowMultipleComponent]
public class TitleGleam : BaseMeshEffect
{
    public Color top = new Color(1f, 0.94f, 0.58f);
    public Color bottom = new Color(1f, 0.55f, 0.05f);
    public Color flat = new Color(1f, 0.72f, 0.12f);
    public float shineWidth = 70f;
    public float revealSoftness = 40f;

    float offsetX;
    float flatten;
    float shineX = -100000f;
    float shineAmount;
    float flash;
    float revealX = 100000f;

    /// <summary>
    /// Sets everything for this frame, and only rebuilds the letters if
    /// something actually changed.
    /// </summary>
    /// <param name="offset">where this piece sits across the word</param>
    /// <param name="toFlat">0 = the gold gradient, 1 = the plain lobby colour</param>
    /// <param name="shineAt">where the band of light is, across the word</param>
    /// <param name="shine">how bright the band is, 0 to 1</param>
    /// <param name="white">0 = normal, 1 = white-hot all over</param>
    /// <param name="revealTo">everything left of this shows; right of it does not yet</param>
    public void Apply(float offset, float toFlat, float shineAt, float shine, float white, float revealTo)
    {
        if (offset == offsetX && toFlat == flatten && shineAt == shineX &&
            shine == shineAmount && white == flash && revealTo == revealX) return;
        offsetX = offset;
        flatten = toFlat;
        shineX = shineAt;
        shineAmount = shine;
        flash = white;
        revealX = revealTo;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        int count = vh.currentVertCount;
        if (count == 0) return;

        var v = new UIVertex();
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            if (v.position.y < minY) minY = v.position.y;
            if (v.position.y > maxY) maxY = v.position.y;
        }
        float height = Mathf.Max(1f, maxY - minY);
        float width2 = shineWidth * shineWidth;

        for (int i = 0; i < count; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float x = v.position.x + offsetX;

            // gold, pale at the top and deep at the bottom, or the lobby's flat colour
            float up = (v.position.y - minY) / height;
            Color c = Color.Lerp(bottom, top, up);
            c = Color.Lerp(c, flat, flatten);

            // the band of light leans like the lettering does
            float d = x + v.position.y * 0.35f - shineX;
            float band = Mathf.Exp(-(d * d) / width2) * shineAmount;
            c = Color.Lerp(c, Color.white, Mathf.Clamp01(band + flash));

            // the text's own colour only carries the fade
            float alpha = v.color.a / 255f;
            alpha *= Mathf.Clamp01((revealX - x) / Mathf.Max(1f, revealSoftness));
            c.a = alpha;
            v.color = c;
            vh.SetUIVertex(v, i);
        }
    }
}
