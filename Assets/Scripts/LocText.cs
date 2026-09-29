using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The game's UI text. It behaves exactly like Unity's Text, with two extras
/// for other languages:
///
/// - Whatever is written to it is translated on the way in (<see cref="Loc.T"/>),
///   so <c>label.text = "PLAY"</c> shows JUGAR in Spanish, and passed through
///   <see cref="Loc.Display(string)"/>, so Arabic and Hindi appear joined up
///   and in the right order. Text that is already translated (built with
///   Loc.F) or has no translation (numbers, car names) passes straight through.
/// - In any language but English, a line that would run past the edge of its
///   box is set a little smaller until it fits. Translations are often longer
///   than the English the layout was drawn around. (English keeps its
///   hand-tuned sizes exactly.)
/// </summary>
public class LocText : Text
{
    string lastRaw;
    string lastShown;
    int requestedSize = -1;   // the size the game asked for
    int fittedSize = -1;      // the size it was set to so it fits

    public override string text
    {
        get { return base.text; }
        set
        {
            if (value == lastRaw && lastShown != null && base.text == lastShown) return;
            lastRaw = value;
            lastShown = Loc.Display(Loc.T(value));
            base.text = lastShown;
            FitWidth();
        }
    }

    /// <summary>Shows text exactly as given - for words already prepared for another language.</summary>
    public void SetShown(string shown)
    {
        lastRaw = null;
        lastShown = null;
        base.text = shown;
        FitWidth();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        FitWidth();
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        FitWidth();
    }

    void FitWidth()
    {
        if (Loc.Current == Loc.Lang.English) return;
        if (horizontalOverflow != HorizontalWrapMode.Overflow) return;
        if (font == null) return;

        // a size the game set since the last fit is the new starting point
        if (fontSize != fittedSize) requestedSize = fontSize;
        if (requestedSize <= 0) return;

        float room = rectTransform.rect.width * 0.94f;
        string shown = base.text;
        if (room <= 1f || string.IsNullOrEmpty(shown)) { ApplySize(requestedSize); return; }

        var settings = GetGenerationSettings(Vector2.zero);
        settings.fontSize = requestedSize;
        settings.resizeTextForBestFit = false;
        float width = cachedTextGeneratorForLayout.GetPreferredWidth(shown, settings) / pixelsPerUnit;

        int size = requestedSize;
        if (width > room)
        {
            int smallest = Mathf.Max(1, Mathf.RoundToInt(requestedSize * 0.55f));
            size = Mathf.Max(smallest, Mathf.FloorToInt(requestedSize * room / width));
        }
        ApplySize(size);
    }

    void ApplySize(int size)
    {
        fittedSize = size;
        if (fontSize != size) fontSize = size;
    }
}
