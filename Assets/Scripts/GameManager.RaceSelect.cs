using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The race screen. Each biome is a Grand Prix of five races, shown one at a
/// time and flicked through like the garage's cars: PREV / NEXT, or a swipe
/// across the middle of the screen. The car behind it drives through the
/// biome being looked at, and the lobby goes back to normal on the way out.
///
/// Every GP that is open is there, and the next one too - under a lock, with
/// what opens it. The ones after that are not shown at all.
/// </summary>
public partial class GameManager
{
    static readonly string[] GpNames = { "COUNTRY GP", "SUNSET GP", "STREET GP", "ALPINE GP" };

    // the screen
    Image raceFade;             // darkens the road while the biome changes over
    RectTransform raceCard;     // everything about the GP on show, slides as one
    Text raceGpName, raceLockText, raceHint;
    Image raceDiffFill;         // the difficulty bar's fill
    Text raceDiffWord;          // EASY / MEDIUM / HARD / EXTREME, under the bar
    const float RaceDiffWidth = 480f;
    Image[] raceDots = new Image[RaceMode.BiomeCount];
    RectTransform raceLockRoot;             // the padlock over a locked GP
    RawImage raceLockBody, raceLockShackle; // in two, so the shackle can spring open
    Text raceUnlockText;
    Image raceUnlockBlock, raceUnlockFlash;
    int raceUnlockBiome = -1;   // a GP a win has just opened, still to unlock on screen
    float raceUnlockT = -1f;
    const float UnlockPopAt = 1.35f, UnlockEnd = 3.7f;
    GameObject raceButtonsRoot;
    readonly Text[] raceStageLabels = new Text[RaceMode.LevelsPerBiome];
    readonly Image[] raceStageImages = new Image[RaceMode.LevelsPerBiome];
    readonly RawImage[] raceStageLocks = new RawImage[RaceMode.LevelsPerBiome];
    GameObject racePrevButton, raceNextButton;

    // which GP is on show
    int raceViewBiome = -1;     // -1 until the screen is first opened
    int lobbyBiomeShown;        // the biome the lobby road is in right now

    // changing GP: the card slides out, the road goes dark, the biome
    // changes, and it all comes back in from the other side
    float raceSwitchT = -1f;
    int raceSwitchDir, raceSwitchTarget;
    bool raceSwitchApplied;
    float raceBounceT = -1f;
    int raceBounceDir;
    const float RaceSwitchOut = 0.16f, RaceSwitchIn = 0.28f;

    static readonly Color RaceBandColor = new Color(0.07f, 0.05f, 0.12f, 0.86f);
    static readonly Color RaceWonColor = new Color(0.22f, 0.62f, 0.32f);
    static readonly Color RaceOpenColor = new Color(0.96f, 0.47f, 0.13f);

    // ------------------------------------------------------------ the highest GPs

    /// <summary>The last GP whose first race is open.</summary>
    static int HighestOpenBiome()
    {
        int b = 0;
        for (int i = 1; i < RaceMode.BiomeCount; i++)
            if (RaceMode.IsUnlocked(i * RaceMode.LevelsPerBiome)) b = i;
        return b;
    }

    /// <summary>The last GP shown: every open one, plus the next one (locked).</summary>
    static int LastShownBiome()
    {
        return Mathf.Min(HighestOpenBiome() + 1, RaceMode.BiomeCount - 1);
    }

    static bool BiomeOpen(int b) { return RaceMode.IsUnlocked(b * RaceMode.LevelsPerBiome); }

    /// <summary>Where the screen opens the first time: the GP with the next race to win.</summary>
    static int FrontierBiome()
    {
        for (int i = 0; i < RaceMode.TotalLevels; i++)
            if (RaceMode.IsUnlocked(i) && !RaceMode.IsCompleted(i)) return RaceMode.BiomeOf(i);
        return HighestOpenBiome();
    }

    // ------------------------------------------------------------ build

    void BuildRaceSelect(Transform uiRoot)
    {
        racePanel = MakePanel(uiRoot, "RacePanel");
        Transform p = racePanel.transform;

        // 1. the road behind goes dark while the biome changes over
        raceFade = MakeFullImage(p, "RoadFade", new Color(0f, 0f, 0f, 0f));

        // 2. the road shows all the way down; the header sits on a black box
        MakeCard(p, new Vector2(0f, 815f), new Vector2(620f, 250f), new Color(0f, 0f, 0f, 0.86f));

        var title = MakeText(p, "RaceTitle", 80, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 868f), new Vector2(900f, 100f));
        title.text = "RACES";
        title.color = new Color(1f, 0.72f, 0.12f);

        BuildRaceUpgrades(p);

        // 3. MENU, right under the title (the tutorial's tour looks for it by name)
        var menuLabel = MakeButton(p, "MENU", 40, new Vector2(0f, 762f), new Vector2(320f, 92f), CloseRaces);
        menuLabel.transform.parent.gameObject.name = "CloseBtn";

        // 4. swiping across the middle flicks to the next or previous GP
        var swipeGo = new GameObject("Swipe");
        swipeGo.transform.SetParent(p, false);
        var swipeImg = swipeGo.AddComponent<Image>();
        swipeImg.color = new Color(0f, 0f, 0f, 0f);
        var swipeRt = swipeImg.rectTransform;
        swipeRt.anchorMin = swipeRt.anchorMax = swipeRt.pivot = new Vector2(0.5f, 0.5f);
        swipeRt.anchoredPosition = new Vector2(0f, 245f);
        swipeRt.sizeDelta = new Vector2(1600f, 880f);
        var swipe = swipeGo.AddComponent<SwipeArea>();
        swipe.onSwipe = dir => RaceCardGo(dir);

        // 5. the GP on show
        var cardGo = new GameObject("RaceCard");
        cardGo.transform.SetParent(p, false);
        raceCard = cardGo.AddComponent<RectTransform>();
        // stretched over the panel: the middle stays the middle, and the races
        // can hold on to the bottom of the screen
        raceCard.anchorMin = Vector2.zero;
        raceCard.anchorMax = Vector2.one;
        raceCard.pivot = new Vector2(0.5f, 0.5f);
        raceCard.offsetMin = raceCard.offsetMax = Vector2.zero;
        Transform c = raceCard;

        raceGpName = MakeText(c, "GpName", 96, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 590f), new Vector2(980f, 130f));
        raceGpName.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);   // the title's tilt

        // which GP it is: a dot for each
        const float DotGap = 46f;
        for (int i = 0; i < RaceMode.BiomeCount; i++)
        {
            raceDots[i] = MakeCard(c, new Vector2((i - (RaceMode.BiomeCount - 1) * 0.5f) * DotGap, 505f),
                new Vector2(26f, 26f), Color.white);
        }
        // how hard the GP's next race is: a bar that fills across the whole
        // series of races, and the word for it underneath, in the bar's colour
        var diffBack = MakeCard(c, new Vector2(0f, 450f),
            new Vector2(RaceDiffWidth + 12f, 38f), new Color(0f, 0f, 0f, 0.55f));
        raceDiffFill = MakeCard(diffBack.transform, Vector2.zero, new Vector2(RaceDiffWidth, 26f), Color.green);
        var frt = raceDiffFill.rectTransform;
        frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 0.5f);
        frt.anchoredPosition = new Vector2(6f, 0f);
        raceDiffWord = MakeText(c, "DiffWord", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 393f), new Vector2(700f, 70f));
        AddDarkOutline(raceDiffWord);

        // the lock, for the next GP: shackle behind, body in front
        var lockGo = new GameObject("Lock");
        lockGo.transform.SetParent(c, false);
        raceLockRoot = lockGo.AddComponent<RectTransform>();
        raceLockRoot.anchorMin = raceLockRoot.anchorMax = raceLockRoot.pivot = new Vector2(0.5f, 0.5f);
        raceLockRoot.anchoredPosition = new Vector2(0f, 245f);
        raceLockRoot.sizeDelta = new Vector2(LockSize, LockSize);
        // the shackle turns about its left leg
        raceLockShackle = MakeLockPart(raceLockRoot, "Shackle", GetLockPartTexture(2), ShacklePivot);
        raceLockBody = MakeLockPart(raceLockRoot, "Body", GetLockPartTexture(1), new Vector2(0.5f, 0.5f));

        raceUnlockText = MakeText(c, "UnlockText", 68, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 245f), new Vector2(1060f, 110f));
        raceUnlockText.color = new Color(1f, 0.82f, 0.15f);
        AddDarkOutline(raceUnlockText);
        raceUnlockText.gameObject.SetActive(false);

        // the GP's five races, held to the bottom of the screen
        var buttonsGo = new GameObject("Races");
        buttonsGo.transform.SetParent(c, false);
        var brt = buttonsGo.AddComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = new Vector2(0f, PanelBleed.y);   // the safe area's bottom edge
        brt.sizeDelta = Vector2.zero;
        raceButtonsRoot = buttonsGo;
        for (int s = 0; s < RaceMode.LevelsPerBiome; s++)
        {
            int stage = s;
            raceStageLabels[s] = MakeButton(buttonsGo.transform, "Stage" + s, 46,
                new Vector2(-380f + s * 190f, RaceRowY), new Vector2(170f, 160f),
                () => StartRaceStage(stage), RaceOpenColor);
            raceStageImages[s] = raceStageLabels[s].transform.parent.GetComponent<Image>();
            var lgo = new GameObject("StageLock");
            lgo.transform.SetParent(raceStageLabels[s].transform.parent, false);
            raceStageLocks[s] = lgo.AddComponent<RawImage>();
            raceStageLocks[s].texture = GetLockTexture();
            raceStageLocks[s].color = Color.white;
            raceStageLocks[s].raycastTarget = false;
            var srt = raceStageLocks[s].rectTransform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(70f, 70f);
        }
        raceHint = MakeText(buttonsGo.transform, "RaceHint", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, RaceHintY), new Vector2(980f, 60f), true);
        raceHint.color = Color.white;
        AddDarkOutline(raceHint);
        // what opens a locked GP, in the hint's place
        raceLockText = MakeText(buttonsGo.transform, "LockText", 38, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, RaceHintY), new Vector2(980f, 110f), true);
        raceLockText.horizontalOverflow = HorizontalWrapMode.Wrap;
        raceLockText.color = new Color(1f, 0.82f, 0.15f);
        AddDarkOutline(raceLockText);

        // 6. PREV / NEXT, as in the garage
        racePrevButton = MakeButton(p, "PREV", 36, new Vector2(-380f, 245f), new Vector2(220f, 100f),
            () => RaceCardGo(-1)).transform.parent.gameObject;
        raceNextButton = MakeButton(p, "NEXT", 36, new Vector2(380f, 245f), new Vector2(220f, 100f),
            () => RaceCardGo(1)).transform.parent.gameObject;

        // 8. the unlock: a flash, and a cover that holds every tap until it is over
        raceUnlockFlash = MakeFullImage(p, "UnlockFlash", new Color(1f, 1f, 1f, 0f));
        raceUnlockFlash.gameObject.SetActive(false);
        raceUnlockBlock = MakeFullImage(p, "UnlockBlock", new Color(0f, 0f, 0f, 0f));
        raceUnlockBlock.raycastTarget = true;
        raceUnlockBlock.gameObject.SetActive(false);

        racePanel.SetActive(false);
    }

    const float LockSize = 150f;
    static readonly Vector2 ShacklePivot = new Vector2(36f / 128f, 60f / 128f);

    RawImage MakeLockPart(Transform parent, string name, Texture2D tex, Vector2 pivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(LockSize, LockSize);
        rt.anchoredPosition = (pivot - new Vector2(0.5f, 0.5f)) * LockSize;
        return img;
    }

    Image MakeFullImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-200f, -400f);
        rt.offsetMax = new Vector2(200f, 400f);
        return img;
    }

    // the bottom of the screen, measured up from the safe area's bottom edge
    const float RaceHintY = 48f, RaceRowY = 150f, RaceUpgradesY = 455f;

    static void AddDarkOutline(Text t)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(2.5f, -2.5f);
    }

    /// <summary>The three upgrades, big, along the bottom above the races.</summary>
    void BuildRaceUpgrades(Transform panel)
    {
        var groupGo = new GameObject("Upgrades");
        groupGo.transform.SetParent(panel, false);
        var grt = groupGo.AddComponent<RectTransform>();
        grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0f);
        grt.pivot = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(0f, PanelBleed.y);
        grt.sizeDelta = Vector2.zero;
        Transform p = grt;
        const float Y = RaceUpgradesY;
        MakeCard(p, new Vector2(0f, Y), new Vector2(1020f, 410f),
            new Color(0.22f, 0.17f, 0.34f, 1f));
        string[] upgIcons = { "UI/icon_engine", "UI/icon_chassis", "UI/icon_coinbonus" };
        Color[] upgColors =
        {
            new Color(0.85f, 0.22f, 0.20f),
            new Color(0.20f, 0.62f, 0.30f),
            new Color(0.20f, 0.45f, 0.85f),
        };
        for (int u = 0; u < 3; u++)
        {
            RaceMode.Upgrade up = (RaceMode.Upgrade)u;
            upgradeLabels[u] = MakeButton(p, "Upg" + u, 30,
                new Vector2(-325f + u * 325f, Y), new Vector2(315f, 380f),
                () => BuyUpgrade(up), upgColors[u], true);
            Transform tile = upgradeLabels[u].transform.parent;
            // the level lines are sized in the text itself, so the whole label scales up
            upgradeLabels[u].rectTransform.anchoredPosition = new Vector2(0f, -48f);
            upgradeLabels[u].rectTransform.sizeDelta = new Vector2(262f, 80f);
            upgradeLabels[u].rectTransform.localScale = Vector3.one * 1.15f;

            var upgName = MakeText(tile, "UpgName" + u, 44, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(300f, 56f), true);
            upgName.text = RaceMode.UpgradeNames[u];

            var icoGo = new GameObject("UpgIcon" + u);
            icoGo.transform.SetParent(tile, false);
            var ico = icoGo.AddComponent<RawImage>();
            ico.raycastTarget = false;
            ico.texture = Resources.Load<Texture2D>(upgIcons[u]);
            var icoRt = ico.rectTransform;
            icoRt.anchorMin = icoRt.anchorMax = icoRt.pivot = new Vector2(0.5f, 0.5f);
            icoRt.anchoredPosition = new Vector2(0f, 60f);
            icoRt.sizeDelta = new Vector2(125f, 125f);

            // the price, on a darker bar along the bottom
            MakeCard(tile, new Vector2(0f, -142f), new Vector2(287f, 76f), new Color(0f, 0f, 0f, 0.32f));
            upgradePriceCoins[u] = MakeCurrencyIcon(tile, new Vector2(0.5f, 0.5f),
                new Vector2(-60f, -142f), 48f, false);
            upgradePriceTexts[u] = MakeText(tile, "UpgPrice" + u, 50, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(10f, -142f), new Vector2(230f, 76f), true);
            var chipGo = new GameObject("AdChip");
            chipGo.transform.SetParent(tile, false);
            var chip = chipGo.AddComponent<RawImage>();
            chip.texture = GetAdChipTexture();
            chip.raycastTarget = false;
            var chipRt = chip.rectTransform;
            chipRt.anchorMin = chipRt.anchorMax = chipRt.pivot = new Vector2(0.5f, 0.5f);
            chipRt.anchoredPosition = new Vector2(100f, -142f);
            chipRt.sizeDelta = new Vector2(40f, 40f);
            chipGo.SetActive(false);
            upgradeAdChips[u] = chipGo;
        }
    }

    // ------------------------------------------------------------ the GP on show

    /// <summary>Fills in the card for <see cref="raceViewBiome"/>.</summary>
    void RefreshRaceCard()
    {
        if (raceCard == null) return;
        int b = Mathf.Clamp(raceViewBiome, 0, RaceMode.BiomeCount - 1);
        int last = LastShownBiome();
        // a GP just won open shows locked until its lock comes off on screen
        bool unlocking = raceUnlockBiome == b;
        bool open = BiomeOpen(b) && !(unlocking && raceUnlockT < UnlockPopAt);

        raceGpName.text = GpNames[b];
        raceGpName.color = open ? new Color(1f, 0.82f, 0.15f) : new Color(0.6f, 0.6f, 0.64f);
        // the GP's next race: the first one open and not yet won (the last
        // one once they are all won, the first while the GP is locked)
        int next = b * RaceMode.LevelsPerBiome + RaceMode.LevelsPerBiome - 1;
        for (int s = 0; s < RaceMode.LevelsPerBiome; s++)
        {
            int lv = b * RaceMode.LevelsPerBiome + s;
            if (!open) { next = b * RaceMode.LevelsPerBiome; break; }
            if (RaceMode.IsUnlocked(lv) && !RaceMode.IsCompleted(lv)) { next = lv; break; }
        }
        // how hard that race is for the car as it is now
        int tier = RaceTier(next, out float diff);
        raceDiffFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(26f, RaceDiffWidth * diff), 26f);
        raceDiffFill.color = DiffColors[tier];
        raceDiffWord.text = DiffWords[tier];
        raceDiffWord.color = DiffColors[tier];

        for (int i = 0; i < RaceMode.BiomeCount; i++)
        {
            // the ones not shown yet are only faint outlines of a dot
            Color col = i == b ? (open ? new Color(1f, 0.82f, 0.15f) : new Color(0.6f, 0.6f, 0.64f))
                      : i > last ? new Color(1f, 1f, 1f, 0.12f)
                      : new Color(1f, 1f, 1f, 0.75f);
            raceDots[i].color = col;
            raceDots[i].rectTransform.localScale = Vector3.one * (i == b ? 1.35f : 1f);
        }

        // the next GP, still locked, shows everything - greyed, with padlocks
        raceLockRoot.gameObject.SetActive(!open || unlocking);
        raceLockText.gameObject.SetActive(!open);
        raceHint.gameObject.SetActive(open);
        raceButtonsRoot.SetActive(true);
        if (!open && b > 0)
            raceLockText.text = Loc.F("WIN RACE 5 OF {0} TO UNLOCK", Loc.T(GpNames[b - 1]));

        for (int s = 0; s < RaceMode.LevelsPerBiome; s++)
        {
            int level = b * RaceMode.LevelsPerBiome + s;
            bool unlocked = open && RaceMode.IsUnlocked(level);
            bool done = RaceMode.IsCompleted(level);
            raceStageLabels[s].text = !unlocked ? ""
                : done ? (s + 1) + "\n<size=18>" + Loc.T("WON") + "</size>"
                : (s + 1).ToString();
            raceStageImages[s].color = !open ? LockedGrey : done ? RaceWonColor : RaceOpenColor;
            raceStageLocks[s].gameObject.SetActive(!unlocked);
        }

        raceHint.text = "1ST PLACE UNLOCKS THE NEXT RACE";
        raceHint.color = Color.white;

        // PREV / NEXT stand aside while a lock comes off, so the words are clear
        racePrevButton.SetActive(b > 0 && !unlocking);
        raceNextButton.SetActive(b < last && !unlocking);
    }

    // ------------------------------------------------------------ difficulty

    static readonly string[] DiffWords = { "EASY", "MEDIUM", "HARD", "EXTREME" };
    static readonly Color[] DiffColors =
    {
        new Color(0.35f, 0.85f, 0.40f),   // green
        new Color(1f, 0.82f, 0.18f),      // yellow
        new Color(1f, 0.50f, 0.15f),      // orange
        new Color(0.95f, 0.22f, 0.20f),   // red
    };

    /// <summary>
    /// How hard a race is for the car the player has right now. It comes down
    /// to engine levels: the race's fastest rival runs about two engine levels
    /// below what the race asks for (see RaceMode.TopRivalSpeed), so the
    /// "spare" levels are how far the car is ahead of that rival. The first
    /// race's rival runs under stock speed, a few levels' worth.
    /// 3+ spare: EASY; 2 (just what the race asks for): MEDIUM; 1: HARD;
    /// 0 or less - level with the rival or behind it: EXTREME.
    /// </summary>
    static int RaceTier(int level, out float fill)
    {
        int have = RaceMode.Level(RaceMode.Upgrade.Speed);
        int spare = level <= 0 ? have + 3
                  : have - (RaceMode.EngineNeeded(level) - RaceMode.EngineLevelsPerRace);
        fill = spare >= 3 ? Mathf.Max(0.06f, 0.25f - 0.08f * (spare - 3))
             : spare == 2 ? 0.45f
             : spare == 1 ? 0.68f
             : spare == 0 ? 0.86f
             : 1f;
        return spare >= 3 ? 0 : spare == 2 ? 1 : spare == 1 ? 2 : 3;
    }

    void StartRaceStage(int stage)
    {
        if (raceSwitchT >= 0f) return;    // mid-flick
        int b = Mathf.Clamp(raceViewBiome, 0, RaceMode.BiomeCount - 1);
        StartRace(b * RaceMode.LevelsPerBiome + stage);
    }

    /// <summary>Flicks to the next (1) or previous (-1) GP.</summary>
    void RaceCardGo(int dir)
    {
        if (raceSwitchT >= 0f || racePanel == null || !racePanel.activeSelf) return;
        if (raceUnlockBiome >= 0) return;   // the lock is coming off: watch it
        int target = raceViewBiome + dir;
        if (target < 0 || target > LastShownBiome())
        {
            // nothing that way: a little nudge says so
            raceBounceT = 0f;
            raceBounceDir = dir;
            return;
        }
        raceSwitchDir = dir;
        raceSwitchTarget = target;
        raceSwitchApplied = false;
        raceSwitchT = 0f;
        if (audioMan != null) audioMan.PlayNearMiss();
    }

    /// <summary>Slides the card and runs the biome change. From LateUpdate.</summary>
    void TickRaceSelect()
    {
        if (racePanel == null || !racePanel.activeSelf) return;
        float udt = Time.unscaledDeltaTime;
        TickRaceUnlock(udt);

        if (raceSwitchT >= 0f)
        {
            raceSwitchT += udt;
            if (raceSwitchT < RaceSwitchOut)
            {
                float t = raceSwitchT / RaceSwitchOut;
                raceCard.anchoredPosition = new Vector2(-raceSwitchDir * 900f * UiWidthScale() * t * t, 0f);
                raceFade.color = new Color(0f, 0f, 0f, t);
            }
            else
            {
                if (!raceSwitchApplied)
                {
                    // behind the black: the new GP and its biome
                    raceSwitchApplied = true;
                    raceViewBiome = raceSwitchTarget;
                    SetLobbyBiome(raceViewBiome);
                    RefreshRaceCard();
                }
                float t = Mathf.Clamp01((raceSwitchT - RaceSwitchOut) / RaceSwitchIn);
                float e = 1f - Mathf.Pow(1f - t, 3f);
                raceCard.anchoredPosition = new Vector2(raceSwitchDir * 900f * UiWidthScale() * (1f - e), 0f);
                raceFade.color = new Color(0f, 0f, 0f, 1f - e);
                if (t >= 1f)
                {
                    raceSwitchT = -1f;
                    raceCard.anchoredPosition = Vector2.zero;
                    raceFade.color = new Color(0f, 0f, 0f, 0f);
                }
            }
        }
        else if (raceBounceT >= 0f)
        {
            raceBounceT += udt;
            float t = Mathf.Clamp01(raceBounceT / 0.3f);
            raceCard.anchoredPosition = new Vector2(-raceBounceDir * 40f * Mathf.Sin(t * Mathf.PI), 0f);
            if (t >= 1f) { raceBounceT = -1f; raceCard.anchoredPosition = Vector2.zero; }
        }
    }

    // ------------------------------------------------------------ a GP unlocking

    /// <summary>
    /// After the win that opens a GP, the race list comes up on it still
    /// locked, and the lock comes off: it grows, rattles, the shackle springs
    /// open with a flash, the GP lights up, and the lock floats away under
    /// "STREET GP UNLOCKED!". Nothing can be tapped until it is over.
    /// </summary>
    void TickRaceUnlock(float udt)
    {
        if (raceUnlockBiome < 0) return;
        if (raceUnlockT < 0f)
        {
            if (raceViewBiome != raceUnlockBiome) { raceUnlockBiome = -1; return; }
            raceUnlockBlock.gameObject.SetActive(true);
            raceUnlockBlock.transform.SetAsLastSibling();
            racePrevButton.SetActive(false);
            raceNextButton.SetActive(false);
            // it starts once the screen has come up out of the black
            if (raceSwitchT >= 0f || finishRevealT >= 0f) return;
            raceUnlockT = 0f;
        }
        float prev = raceUnlockT;
        raceUnlockT += udt;
        float t = raceUnlockT;
        bool Crossed(float at) { return prev < at && t >= at; }

        // 1. the lock grows
        float grow = EaseOutBack(Mathf.Clamp01(t / 0.45f));
        float scale = Mathf.LerpUnclamped(1f, 1.55f, grow);
        // 2. and rattles, harder and harder
        float rot = 0f;
        if (t > 0.45f && t < UnlockPopAt)
        {
            float k = (t - 0.45f) / (UnlockPopAt - 0.45f);
            rot = Mathf.Sin(t * 42f) * Mathf.Lerp(3f, 14f, k);
        }
        if (Crossed(0.55f) || Crossed(0.85f) || Crossed(1.12f)) audioMan.PlayCountBeep();

        // 3. the shackle springs open
        float open = Mathf.Clamp01((t - UnlockPopAt) / 0.22f);
        float swing = EaseOutBack(open);
        var srt = raceLockShackle.rectTransform;
        srt.anchoredPosition = (ShacklePivot - new Vector2(0.5f, 0.5f)) * LockSize
                               + new Vector2(0f, 20f * swing);
        srt.localRotation = Quaternion.Euler(0f, 0f, 14f * swing);
        if (Crossed(UnlockPopAt))
        {
            audioMan.PlayCountGo();
            RefreshRaceCard();          // the GP lights up behind the open lock
            raceUnlockFlash.gameObject.SetActive(true);
        }
        float flash = t >= UnlockPopAt ? Mathf.Clamp01(1f - (t - UnlockPopAt) / 0.45f) : 0f;
        raceUnlockFlash.color = new Color(1f, 1f, 1f, 0.55f * flash);

        // 4. the open lock floats up and away
        float away = Mathf.Clamp01((t - 1.85f) / 0.5f);
        scale *= 1f + 0.5f * away;
        float alpha = 1f - away;
        raceLockRoot.localScale = Vector3.one * scale;
        raceLockRoot.localRotation = Quaternion.Euler(0f, 0f, rot);
        raceLockRoot.anchoredPosition = new Vector2(0f, 245f + 90f * away * away);
        raceLockBody.color = raceLockShackle.color = new Color(1f, 1f, 1f, alpha);

        // 5. "... UNLOCKED!" pops in where the lock was
        if (Crossed(1.95f))
        {
            raceUnlockText.text = Loc.F("{0} UNLOCKED!", Loc.T(GpNames[raceUnlockBiome]));
            FitToWidth(raceUnlockText, 68, 1000f);
            raceUnlockText.gameObject.SetActive(true);
            audioMan.PlayNearMiss();
        }
        if (t >= 1.95f)
        {
            float pop = EaseOutBack(Mathf.Clamp01((t - 1.95f) / 0.3f));
            raceUnlockText.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, pop);
            float fade = Mathf.Clamp01((UnlockEnd - t) / 0.35f);
            var col = raceUnlockText.color;
            raceUnlockText.color = new Color(col.r, col.g, col.b, fade);
        }

        if (t >= UnlockEnd)
        {
            raceUnlockBiome = -1;
            raceUnlockT = -1f;
            raceUnlockBlock.gameObject.SetActive(false);
            raceUnlockFlash.gameObject.SetActive(false);
            raceUnlockText.gameObject.SetActive(false);
            raceUnlockText.color = new Color(1f, 0.82f, 0.15f);
            ResetRaceLock();
            RefreshRaceCard();
        }
    }

    /// <summary>The padlock back as it sits on a locked GP.</summary>
    void ResetRaceLock()
    {
        raceLockRoot.localScale = Vector3.one;
        raceLockRoot.localRotation = Quaternion.identity;
        raceLockRoot.anchoredPosition = new Vector2(0f, 245f);
        var srt = raceLockShackle.rectTransform;
        srt.anchoredPosition = (ShacklePivot - new Vector2(0.5f, 0.5f)) * LockSize;
        srt.localRotation = Quaternion.identity;
        raceLockBody.color = raceLockShackle.color = Color.white;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ------------------------------------------------------------ the road behind

    /// <summary>
    /// Rebuilds the lobby road in a GP's biome: its scenery, its sky and its
    /// light, with the car cruising on it as usual. Biome 0 is the lobby as
    /// it normally is.
    /// </summary>
    void SetLobbyBiome(int b)
    {
        if (track == null || car == null) return;
        b = Mathf.Clamp(b, 0, RaceMode.BiomeCount - 1);
        lobbyBiomeShown = b;
        RaceMode.BlendsForBiome(b, out float dayNight, out float snow);

        ApplyLobbyTrack();
        track.Init(startPos, startYaw, dayNight, snow);
        track.SetBiomeBlend(dayNight);
        track.SetSnowBlend(snow);
        car.ResetRun(track);
        car.baseSpeed = lobbyCruiseSpeed;
        car.maxSpeed = lobbyCruiseSpeed;
        car.speedGainPerSecond = 0f;
        car.GrantMercy(9999f);
        if (camFollow != null) { camFollow.SnapToTarget(); camFollow.SetShowcase(true); }

        if (b == 0) ResetBiome();   // the lobby exactly as it always is
        else
        {
            biomeBlend = dayNight;
            snowBlend = snow;
            ApplyBiomeVisuals(dayNight);
        }
    }

    // ------------------------------------------------------------ the lock

    static readonly Texture2D[] lockTextures = new Texture2D[3];

    /// <summary>A padlock, drawn in code: white, smooth-edged, on transparent.</summary>
    static Texture2D GetLockTexture() { return GetLockPartTexture(0); }

    /// <summary>0: the whole padlock; 1: its body; 2: its shackle, legs running on down inside the body.</summary>
    static Texture2D GetLockPartTexture(int part)
    {
        if (lockTextures[part] != null) return lockTextures[part];
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // body: a rounded box in the lower part
                float bx = Mathf.Abs(fx - 64f) - 40f + 12f, by = Mathf.Abs(fy - 42f) - 34f + 12f;
                float body = Mathf.Sqrt(Mathf.Max(bx, 0f) * Mathf.Max(bx, 0f) + Mathf.Max(by, 0f) * Mathf.Max(by, 0f))
                             + Mathf.Min(Mathf.Max(bx, by), 0f) - 12f;
                // shackle: a thick ring across the top, with straight legs into the body
                float r = Mathf.Sqrt((fx - 64f) * (fx - 64f) + (fy - 80f) * (fy - 80f));
                float ring = Mathf.Abs(r - 28f) - 8f;
                if (fy < 80f) ring = Mathf.Min(Mathf.Abs(Mathf.Abs(fx - 64f) - 28f) - 8f, 999f);
                if (fy < (part == 2 ? 46f : 66f)) ring = 999f;
                float d = part == 1 ? body : part == 2 ? ring : Mathf.Min(body, ring);
                // the keyhole is cut out of the body
                float kh = Mathf.Sqrt((fx - 64f) * (fx - 64f) + (fy - 48f) * (fy - 48f)) - 8f;
                float slot = Mathf.Max(Mathf.Abs(fx - 64f) - 3.5f, Mathf.Abs(fy - 34f) - 10f);
                float hole = Mathf.Min(kh, slot);
                if (part != 2) d = Mathf.Max(d, -hole);
                float a = Mathf.Clamp01(0.5f - d);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        lockTextures[part] = tex;
        return tex;
    }
}

/// <summary>
/// A horizontal swipe on an area of the screen: left goes to the next item,
/// right to the previous one.
/// </summary>
public class SwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public System.Action<int> onSwipe;
    public float threshold = 70f;   // canvas units of travel
    Vector2 start;

    public void OnBeginDrag(PointerEventData e) { start = e.position; }
    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        var canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float dx = (e.position.x - start.x) / Mathf.Max(0.01f, scale);
        float dy = (e.position.y - start.y) / Mathf.Max(0.01f, scale);
        if (Mathf.Abs(dx) < threshold || Mathf.Abs(dx) < Mathf.Abs(dy)) return;
        if (onSwipe != null) onSwipe(dx < 0f ? 1 : -1);
    }
}
