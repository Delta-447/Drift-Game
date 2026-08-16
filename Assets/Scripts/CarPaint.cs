using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-car paint: a body colour and a wheel colour, chosen from a palette and
/// remembered for each car separately. Index -1 means the model's own colours.
/// </summary>
public static class CarPaint
{
    public static readonly string[] Names =
    {
        "STOCK", "WHITE", "BLACK", "SILVER", "RED", "ORANGE",
        "YELLOW", "LIME", "TEAL", "BLUE", "PURPLE", "PINK", "GOLD",
    };

    /// <summary>Index 0 is "stock" and is never used as a colour.</summary>
    public static readonly Color[] Swatches =
    {
        new Color(0.55f, 0.55f, 0.60f),          // stock (shown greyed)
        new Color(0.93f, 0.94f, 0.96f),
        new Color(0.07f, 0.07f, 0.09f),
        new Color(0.62f, 0.65f, 0.70f),
        new Color(0.80f, 0.11f, 0.12f),
        new Color(0.95f, 0.45f, 0.08f),
        new Color(0.97f, 0.83f, 0.12f),
        new Color(0.55f, 0.84f, 0.20f),
        new Color(0.10f, 0.70f, 0.66f),
        new Color(0.13f, 0.42f, 0.90f),
        new Color(0.52f, 0.24f, 0.82f),
        new Color(0.93f, 0.40f, 0.66f),
        new Color(0.85f, 0.68f, 0.22f),
    };

    public static int Count { get { return Swatches.Length; } }

    public static int BodyChoice(int car) { return PlayerPrefs.GetInt("PaintBody" + car, 0); }
    public static int WheelChoice(int car) { return PlayerPrefs.GetInt("PaintWheel" + car, 0); }

    public static void SetBody(int car, int choice)
    {
        PlayerPrefs.SetInt("PaintBody" + car, choice);
        PlayerPrefs.Save();
    }

    public static void SetWheel(int car, int choice)
    {
        PlayerPrefs.SetInt("PaintWheel" + car, choice);
        PlayerPrefs.Save();
    }

    static readonly string[] WheelWords = { "wheel", "tire", "tyre", "rim" };
    static readonly string[] SkipWords =
        { "glass", "window", "windshield", "windscreen", "light", "lamp",
          "decal", "logo", "badge", "text", "mirror", "plate" };

    static bool Matches(string name, string[] words)
    {
        string n = name.ToLowerInvariant();
        for (int i = 0; i < words.Length; i++)
        {
            if (n.Contains(words[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// Repaints a car model. Body colour goes on the big panels, wheel colour
    /// on the wheels; glass, lights and badges are left alone.
    /// </summary>
    /// <summary>
    /// While paint is locked nothing is repainted, so a colour picked while
    /// the feature was open does not stay on the car.
    /// </summary>
    public static bool Enabled;

    public static void Apply(GameObject model, int car)
    {
        if (model == null || !Enabled) return;

        int bodyChoice = BodyChoice(car);
        int wheelChoice = WheelChoice(car);
        if (bodyChoice <= 0 && wheelChoice <= 0) return;   // nothing to change

        var rends = model.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;

        // the biggest part of the car is its bodywork - anything roughly that
        // size gets the body colour, so trim and vents are not repainted
        float biggest = 0f;
        for (int i = 0; i < rends.Length; i++)
        {
            Vector3 s = rends[i].bounds.size;
            biggest = Mathf.Max(biggest, s.x * s.y * s.z);
        }

        for (int i = 0; i < rends.Length; i++)
        {
            Renderer r = rends[i];
            string objName = r.gameObject.name;
            if (Matches(objName, SkipWords)) continue;

            bool isWheel = Matches(objName, WheelWords);
            if (isWheel && wheelChoice <= 0) continue;

            if (!isWheel)
            {
                if (bodyChoice <= 0) continue;
                Vector3 s = r.bounds.size;
                if (s.x * s.y * s.z < biggest * 0.25f) continue;  // a small detail
            }

            Color target = Swatches[Mathf.Clamp(isWheel ? wheelChoice : bodyChoice,
                                                0, Swatches.Length - 1)];

            // .materials gives this renderer its own copies, so painting one
            // car never bleeds onto another using the same source material
            Material[] mats = r.materials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] == null) continue;
                if (Matches(mats[m].name, SkipWords)) continue;
                if (mats[m].HasProperty("_BaseColor")) mats[m].SetColor("_BaseColor", target);
                if (mats[m].HasProperty("_Color")) mats[m].SetColor("_Color", target);
            }
            r.materials = mats;
        }
    }
}
