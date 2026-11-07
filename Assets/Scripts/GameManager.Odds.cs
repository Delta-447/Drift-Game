using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prize odds. Everything the player can pay for that hands out a random
/// prize - a spin of the mystery wheel, the mystery box, the token box - has
/// an ODDS button beside it that lists every prize and its chance, so the
/// odds are there before anything is bought (App Store guideline 3.1.1).
///
/// The draws read the very same tables the ODDS screen prints - the wheel's
/// slice weights, <see cref="MysteryBoxTiers"/> and the token box pool - so
/// what the screen says is always exactly what the game does. Change a
/// prize's chance in one of those tables and the screen follows by itself;
/// never add a draw that does not go through them.
/// </summary>
public partial class GameManager
{
    // ------------------------------------------------------- mystery box

    enum BoxPrizeKind { Coins, Tires, Tokens }

    struct BoxTier
    {
        public float chance;        // share of all opens, 0-1; the tiers add up to 1
        public BoxPrizeKind kind;
        public int min, max;        // the amount, both ends included
        public string textKey;      // what the box says when it opens
        public Color color;

        public BoxTier(float chance, BoxPrizeKind kind, int min, int max,
                       string textKey, Color color)
        {
            this.chance = chance; this.kind = kind; this.min = min; this.max = max;
            this.textKey = textKey; this.color = color;
        }
    }

    // TODO: add car skins as prizes when new skins exist
    static readonly BoxTier[] MysteryBoxTiers =
    {
        new BoxTier(0.40f, BoxPrizeKind.Coins,   200,  450, "+{0} COINS",         new Color(1f, 0.82f, 0.1f)),
        new BoxTier(0.30f, BoxPrizeKind.Coins,   500,  900, "+{0} COINS!",        new Color(1f, 0.82f, 0.1f)),
        new BoxTier(0.15f, BoxPrizeKind.Tires,     1,    3, "+{0} TIRES!",        new Color(0.7f, 0.9f, 1f)),
        new BoxTier(0.10f, BoxPrizeKind.Coins,  1200, 2000, "RARE!  +{0} COINS!", new Color(0.8f, 0.4f, 1f)),
        new BoxTier(0.03f, BoxPrizeKind.Tires,     5,    8, "EPIC!  +{0} TIRES!", new Color(0.35f, 1f, 0.45f)),
        // brand tokens, of any marque (the colour comes from the marque)
        new BoxTier(0.02f, BoxPrizeKind.Tokens,    1,    5, "+{0} {1}!",          Color.white),
    };

    /// <summary>One draw on a tier table: each tier comes up with its own chance.</summary>
    static int PickBoxTier(BoxTier[] tiers)
    {
        float roll = Random.value;
        for (int i = 0; i < tiers.Length; i++)
        {
            roll -= tiers[i].chance;
            if (roll < 0f) return i;
        }
        return tiers.Length - 1;
    }

    // -------------------------------------------------------- token box

    // weighted toward the cheaper marques
    static readonly Currency[] TokenBoxBrands =
        { Currency.Caldera, Currency.Cyber, Currency.Tempasta, Currency.Vettura };
    static readonly float[] TokenBoxWeights = { 34f, 30f, 23f, 13f };
    const int TokenBoxMin = 1, TokenBoxMax = 5;

    /// <summary>
    /// The marques a token box can pay right now - only those that still have
    /// a car left to buy - with their weights. Empty once every marque is
    /// collected, when the box pays coins instead.
    /// </summary>
    void TokenBoxPool(out List<Currency> pool, out List<float> weights)
    {
        pool = new List<Currency>();
        weights = new List<float>();
        for (int i = 0; i < TokenBoxBrands.Length; i++)
        {
            if (!TokenStillUseful(TokenBoxBrands[i])) continue;
            pool.Add(TokenBoxBrands[i]);
            weights.Add(TokenBoxWeights[i]);
        }
    }

    // ---------------------------------------------------- what each pays

    enum OddsFor { Wheel, MysteryBox, TokenBox }

    /// <summary>Every prize and its chance (0-1), in the order shown.</summary>
    List<KeyValuePair<string, float>> OddsRows(OddsFor what)
    {
        var rows = new List<KeyValuePair<string, float>>();
        switch (what)
        {
            case OddsFor.Wheel:
            {
                int total = 0;
                for (int i = 0; i < Wheel.Length; i++) total += Wheel[i].weight;
                for (int i = 0; i < Wheel.Length; i++)
                    rows.Add(new KeyValuePair<string, float>(
                        WheelPrizeName(Wheel[i]), (float)Wheel[i].weight / total));
                break;
            }

            case OddsFor.MysteryBox:
                for (int i = 0; i < MysteryBoxTiers.Length; i++)
                    rows.Add(new KeyValuePair<string, float>(
                        BoxTierName(MysteryBoxTiers[i]), MysteryBoxTiers[i].chance));
                break;

            default:
            {
                TokenBoxPool(out List<Currency> pool, out List<float> weights);
                if (pool.Count == 0)
                {
                    rows.Add(new KeyValuePair<string, float>(Loc.F("+{0}-{1} COINS",
                        CoinsInsteadOfTokens(TokenBoxMin), CoinsInsteadOfTokens(TokenBoxMax)), 1f));
                    break;
                }
                float total = 0f;
                for (int i = 0; i < weights.Count; i++) total += weights[i];
                for (int i = 0; i < pool.Count; i++)
                    rows.Add(new KeyValuePair<string, float>(
                        Loc.F("+{0}-{1} {2}", TokenBoxMin, TokenBoxMax,
                              Loc.T(TokenNames[(int)pool[i]])),
                        weights[i] / total));
                break;
            }
        }
        return rows;
    }

    /// <summary>What a wheel slice pays today - as GrantSpinPrize will pay it.</summary>
    string WheelPrizeName(WheelSlot s)
    {
        switch (s.kind)
        {
            case SpinPrize.Coins: return Loc.F("+{0} COINS", s.amount);
            case SpinPrize.Tires: return Loc.F("+{0} TIRES", s.amount);
            case SpinPrize.Tokens:
                // a slice without a marque of its own picks one at random
                // (there are none like that on the wheel today)
                if (s.currency == Currency.Coins)
                    return Loc.F("+{0}-{1} TOKENS, ANY BRAND", s.amount, s.amount);
                if (!TokenStillUseful(s.currency))
                    return Loc.F("+{0} COINS", CoinsInsteadOfTokens(s.amount));
                return Loc.F("+{0} {1}", s.amount, Loc.T(TokenNames[(int)s.currency]));
            default:
                return PickWheelCar() >= 0 ? Loc.T("A NEW CAR")
                                           : Loc.F("+{0} COINS", WheelCarCoins);
        }
    }

    static string BoxTierName(BoxTier t)
    {
        switch (t.kind)
        {
            case BoxPrizeKind.Coins: return Loc.F("+{0}-{1} COINS", t.min, t.max);
            case BoxPrizeKind.Tires: return Loc.F("+{0}-{1} TIRES", t.min, t.max);
            default: return Loc.F("+{0}-{1} TOKENS, ANY BRAND", t.min, t.max);
        }
    }

    /// <summary>40%, 27.1% - a whole number when it is one.</summary>
    static string OddsPercent(float chance)
    {
        float v = chance * 100f;
        float whole = Mathf.Round(v);
        string s = Mathf.Abs(v - whole) < 0.05f
            ? whole.ToString("0", CultureInfo.InvariantCulture)
            : v.ToString("0.0", CultureInfo.InvariantCulture);
        return s + "%";
    }

    // ------------------------------------------------------------ screen

    GameObject oddsPanel;
    Text oddsTitle;
    const int OddsMaxRows = 10;
    const float OddsRowStep = 80f;
    const float OddsRowsCentre = 40f;     // the list is centred on this height
    readonly RectTransform[] oddsRow = new RectTransform[OddsMaxRows];
    readonly Text[] oddsPrize = new Text[OddsMaxRows];
    readonly Text[] oddsChance = new Text[OddsMaxRows];

    static readonly Color OddsButtonColor = new Color(0.32f, 0.28f, 0.48f);

    /// <summary>The small ODDS button that sits beside a paid draw.</summary>
    void MakeOddsButton(Transform parent, Vector2 at, Vector2 size, OddsFor what)
    {
        MakeButton(parent, "ODDS", 38, at, size, () => OpenOdds(what), OddsButtonColor, true);
    }

    void OpenOdds(OddsFor what)
    {
        if (oddsPanel == null) BuildOddsPanel(wheelPanel.transform.parent);

        oddsTitle.text = what == OddsFor.Wheel ? "MYSTERY WHEEL"
                       : what == OddsFor.MysteryBox ? "MYSTERY BOX" : "TOKEN BOX";

        var rows = OddsRows(what);
        int n = Mathf.Min(rows.Count, OddsMaxRows);
        float top = OddsRowsCentre + (n - 1) * OddsRowStep * 0.5f;
        for (int i = 0; i < OddsMaxRows; i++)
        {
            bool on = i < n;
            oddsRow[i].gameObject.SetActive(on);
            if (!on) continue;
            oddsRow[i].anchoredPosition = new Vector2(0f, top - i * OddsRowStep);
            oddsPrize[i].text = rows[i].Key;
            oddsChance[i].text = OddsPercent(rows[i].Value);
        }

        oddsPanel.SetActive(true);
        oddsPanel.transform.SetAsLastSibling();
        audioMan.PlayTap();
    }

    void CloseOdds()
    {
        oddsPanel.SetActive(false);
        audioMan.PlayTap();
    }

    void BuildOddsPanel(Transform uiRoot)
    {
        oddsPanel = MakePanel(uiRoot, "OddsPanel");

        // dims, and swallows taps meant for the screen underneath
        var dim = new GameObject("Dim");
        dim.transform.SetParent(oddsPanel.transform, false);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.85f);
        var drt = dimImg.rectTransform;
        drt.anchorMin = Vector2.zero;
        drt.anchorMax = Vector2.one;
        drt.offsetMin = drt.offsetMax = Vector2.zero;

        MakeCard(oddsPanel.transform, new Vector2(0f, 40f), new Vector2(1010f, 1400f),
            new Color(0.13f, 0.09f, 0.2f, 0.97f));

        oddsTitle = MakeText(oddsPanel.transform, "OddsTitle", 64, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 640f), new Vector2(900f, 90f));
        oddsTitle.color = new Color(1f, 0.72f, 0.12f);
        StyleScreenTitle(oddsTitle, null);

        var sub = MakeText(oddsPanel.transform, "OddsSub", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 562f), new Vector2(900f, 60f), true);
        sub.text = "PRIZE ODDS";
        sub.color = new Color(0.86f, 0.92f, 1f);

        for (int i = 0; i < OddsMaxRows; i++)
        {
            // a faint band behind every other row, so the eye stays on its line
            var rowGo = new GameObject("OddsRow" + i);
            rowGo.transform.SetParent(oddsPanel.transform, false);
            var band = rowGo.AddComponent<Image>();
            band.color = new Color(1f, 1f, 1f, i % 2 == 0 ? 0.07f : 0f);
            band.raycastTarget = false;
            var rt = band.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(920f, OddsRowStep - 8f);
            oddsRow[i] = rt;

            oddsPrize[i] = MakeText(rowGo.transform, "Prize", 44, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(-95f, 0f), new Vector2(690f, 70f), true);
            oddsChance[i] = MakeText(rowGo.transform, "Chance", 44, TextAnchor.MiddleRight,
                new Vector2(0.5f, 0.5f), new Vector2(345f, 0f), new Vector2(170f, 70f), true);
            oddsChance[i].color = new Color(1f, 0.82f, 0.25f);
        }

        var note = MakeText(oddsPanel.transform, "OddsNote", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -425f), new Vector2(900f, 60f), true);
        note.text = "YOU ALWAYS GET ONE OF THESE PRIZES";
        note.color = new Color(0.75f, 0.75f, 0.85f);

        MakeCloseButton(oddsPanel.transform, new Vector2(0f, -560f), CloseOdds);
        oddsPanel.SetActive(false);
    }
}
