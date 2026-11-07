using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The end of a run: the explosion, then the results.
///
/// It is not a window. The car goes up in full view first - the camera pulls
/// back to watch, the screen flashes, letterbox bars slam shut and the car is
/// thrown off its wheels - and only then does the cause slam down across the
/// screen on a dark slash. The score rises in underneath and counts up, the
/// rest follows it in, and MENU comes up last. The wreck is left burning in
/// the middle of the screen, where nothing is drawn over it.
///
/// Nothing interrupts the results. What the run earned is celebrated on the
/// way out instead: MENU plays the new-high-score screen, then the weekly
/// ranking, then goes to the menu (see ResultsMenu).
/// </summary>
public partial class GameManager
{
    // ------------------------------------------------------------ timing
    // seconds from the moment of the crash

    const float GoBarsShut = 0.22f;        // the letterbox slams shut
    const float GoBandAt = 0.80f;          // the dark slash wipes across
    const float GoSlamAt = 0.88f;          // the headline starts to fall...
    const float GoSlamTime = 0.14f;        // ...and lands this long after
    const float GoBarsOpenAt = 1.15f;      // the letterbox opens back out
    const float GoResultsAt = 1.30f;       // the score and the rest rise in
    const float GoCountTime = 1.0f;        // the score counting up
    const float GoMenuAt = 2.45f;          // the way out comes up last
    const float GoSkipFrom = 0.5f;         // a tap before this is still the crash

    // ------------------------------------------------------------ layout

    const float GoBarHeight = 170f;
    const float GoTitleY = 600f;
    const float GoTilt = 4f;               // degrees the slash and headline lean
    const float GoBandWidth = 3200f;       // wider than any screen, iPad included
    const float GoBandHeight = 210f;
    const int GoTitleSize = 180;
    const float GoTitleRoom = 990f;
    const float GoResultsShadeTop = 300f;  // the dark fade behind the results starts here
    const int GoScoreSize = 200;
    const float GoScoreRoom = 960f;
    const int GoWeeklySize = 46;           // the weekly rank line (GameManager.Leaderboard.cs)

    static readonly Color GoTitleRed = new Color(1f, 0.30f, 0.24f);
    static readonly Color GoGold = new Color(1f, 0.82f, 0.15f);
    static readonly Color GoShadeColour = new Color(0.03f, 0.01f, 0.04f, 1f);
    static readonly Color GoVignetteRed = new Color(0.85f, 0.05f, 0.04f, 1f);
    static readonly Color GiveUpGrey = new Color(0.40f, 0.40f, 0.46f);

    // ------------------------------------------------------------ the screen

    RectTransform goCard;                 // holds the results (the weekly line hangs off it too)
    Text goTitle, goScore, goBestLine, goRunLabel;
    Text goGhostA, goGhostB;              // the headline's glitch copies
    Text[] goTitleTexts;
    readonly Text[] goStatValue = new Text[2];
    readonly Text[] goStatLabel = new Text[2];
    readonly GameObject[] goStatRoot = new GameObject[2];
    RawImage goBonusCoin;
    RectTransform goBand, goTitleRt, goMenuRt;
    CanvasGroup goMenuGroup;
    Image goFlash, goBarTop, goBarBottom;
    RawImage goVignette, goShadeTop, goShadeBottom;

    bool goRaceCrash;                     // the death screen of a race: the place, no points
    float goAnimT = -1f;
    int goTargetScore;
    bool goWasBest;
    bool goWhooshed, goSlammed, goCounted;
    // shown on the way out, by MENU
    bool goBestToShow;                    // the new-high-score screen
    bool goLbPending;                     // the weekly ranking
    bool lbLeaveAfter;                    // the ranking was opened by MENU: closing it carries on
    int goLastShown;
    float goLastTickAt;

    /// <summary>One line of the results, rising into place at its own moment.</summary>
    class GoPiece
    {
        public RectTransform rt;
        public CanvasGroup group;
        public Vector2 home, from;
        public float at;                  // seconds after GoResultsAt
    }

    readonly List<GoPiece> goPieces = new List<GoPiece>();
    bool goWeeklyAdded;

    void BuildGameOverPanel(Transform uiRoot)
    {
        gameOverPanel = MakePanel(uiRoot, "GameOverPanel");
        Transform root = gameOverPanel.transform;

        // A tap anywhere hurries the results along (SkipGameOver). It is the
        // bottom layer, so MENU still gets its own taps.
        var skipImg = MakeGoLayer(root, "Skip").gameObject.AddComponent<Image>();
        skipImg.color = new Color(0f, 0f, 0f, 0f);
        var skipBtn = skipImg.gameObject.AddComponent<Button>();
        skipBtn.transition = Selectable.Transition.None;
        skipBtn.targetGraphic = skipImg;
        skipBtn.onClick.AddListener(SkipGameOver);

        // red round the edges of the screen, flooding in on the crash
        goVignette = MakeGoRaw(root, "Vignette", GoVignetteTexture(), Faded(GoVignetteRed, 0f));

        // Dark shading only where the words go - the top, behind the
        // headline, and the bottom, behind the results - so they read over a
        // bright sky. The middle of the screen is left clear for the wreck.
        goShadeTop = MakeGoRaw(root, "ShadeTop", GoShadeTexture(), Faded(GoShadeColour, 0f));
        goShadeTop.uvRect = new Rect(0f, 1f, 1f, -1f);    // the same fade, upside down
        var st = goShadeTop.rectTransform;
        st.anchorMin = new Vector2(0f, 0.5f);
        st.anchorMax = Vector2.one;
        st.offsetMin = new Vector2(0f, 260f);
        st.offsetMax = Vector2.zero;

        goShadeBottom = MakeGoRaw(root, "ShadeBottom", GoShadeTexture(), Faded(GoShadeColour, 0f));
        var sb = goShadeBottom.rectTransform;
        sb.anchorMin = Vector2.zero;
        sb.anchorMax = new Vector2(1f, 0.5f);
        sb.offsetMin = Vector2.zero;
        sb.offsetMax = new Vector2(0f, GoResultsShadeTop);

        // the letterbox the crash slams shut
        goBarTop = MakeGoBar(root, "BarTop", 1f);
        goBarBottom = MakeGoBar(root, "BarBottom", 0f);

        var holder = new GameObject("Holder");
        holder.transform.SetParent(root, false);
        goCard = holder.AddComponent<RectTransform>();
        goCard.anchorMin = goCard.anchorMax = goCard.pivot = new Vector2(0.5f, 0.5f);
        goCard.anchoredPosition = Vector2.zero;
        goCard.sizeDelta = new Vector2(1000f, 2000f);

        BuildGoHeadline();
        BuildGoResults();

        // MENU, the one way out. There is no RETRY - restarting straight from
        // here caused bugs, and the menu is one tap from PLAY. It sits in a
        // holder so it can pop in without fighting the button's own press.
        var menuGo = new GameObject("MenuHolder");
        menuGo.transform.SetParent(goCard, false);
        goMenuRt = menuGo.AddComponent<RectTransform>();
        goMenuRt.anchorMin = goMenuRt.anchorMax = goMenuRt.pivot = new Vector2(0.5f, 0.5f);
        goMenuRt.anchoredPosition = new Vector2(0f, -860f);
        goMenuRt.sizeDelta = new Vector2(660f, 150f);
        goMenuGroup = menuGo.AddComponent<CanvasGroup>();
        goMenuLabel = MakeButton(goMenuRt, "MENU", 66, Vector2.zero, new Vector2(660f, 150f), ResultsMenu);
        goMenuImage = goMenuLabel.transform.parent.GetComponent<Image>();
        goMenuColour = goMenuImage.color;
        // with boxes from the road to open, the button says so, with a box on it
        goMenuBox = MakeMenuIcon(goMenuLabel.transform.parent, "UI/icon_mystery_box",
            new Vector2(-218f, 2f), 124f).rectTransform;
        goMenuBox.gameObject.SetActive(false);

        // the impact flash, over everything
        goFlash = MakeGoLayer(root, "Flash").gameObject.AddComponent<Image>();
        goFlash.color = new Color(1f, 0.92f, 0.75f, 0f);
        goFlash.raycastTarget = false;

        // until a crash plays it, the screen sits finished
        PoseGameOver(GoMenuAt + 1f);
    }

    /// <summary>The dark slash across the top of the screen and the cause written on it.</summary>
    void BuildGoHeadline()
    {
        var slashGo = new GameObject("Slash");
        slashGo.transform.SetParent(goCard, false);
        var slash = slashGo.AddComponent<RectTransform>();
        slash.anchorMin = slash.anchorMax = slash.pivot = new Vector2(0.5f, 0.5f);
        slash.anchoredPosition = new Vector2(0f, GoTitleY);
        slash.sizeDelta = new Vector2(GoBandWidth, GoBandHeight);
        slash.localEulerAngles = new Vector3(0f, 0f, GoTilt);

        // the band grows out from the left edge, so it can wipe across
        var bandImg = new GameObject("Band").AddComponent<Image>();
        bandImg.transform.SetParent(slash, false);
        bandImg.color = new Color(0.05f, 0.02f, 0.05f, 0.88f);
        bandImg.raycastTarget = false;
        goBand = bandImg.rectTransform;
        goBand.anchorMin = Vector2.zero;
        goBand.anchorMax = new Vector2(0f, 1f);
        goBand.pivot = new Vector2(0f, 0.5f);
        goBand.anchoredPosition = Vector2.zero;
        goBand.sizeDelta = new Vector2(GoBandWidth, 0f);

        // hot red edges along the top and bottom of it
        for (int i = 0; i < 2; i++)
        {
            float edge = i == 0 ? 1f : 0f;
            var line = new GameObject(i == 0 ? "EdgeTop" : "EdgeBottom").AddComponent<Image>();
            line.transform.SetParent(goBand, false);
            line.color = new Color(1f, 0.22f, 0.16f, 0.95f);
            line.raycastTarget = false;
            var lrt = line.rectTransform;
            lrt.anchorMin = new Vector2(0f, edge);
            lrt.anchorMax = new Vector2(1f, edge);
            lrt.pivot = new Vector2(0.5f, edge);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 10f);
        }

        // the glitch copies go behind the headline itself
        goGhostA = MakeGoTitleText(slash, "GOGhostA", new Color(0.15f, 0.9f, 1f, 0f), true);
        goGhostB = MakeGoTitleText(slash, "GOGhostB", new Color(1f, 0.1f, 0.35f, 0f), true);
        goTitle = MakeGoTitleText(slash, "GOTitle", GoTitleRed, false);
        var outline = goTitle.GetComponent<Outline>();
        if (outline != null) outline.effectDistance = new Vector2(3f, -3f);
        goTitleRt = goTitle.rectTransform;
        goTitleTexts = new[] { goGhostA, goGhostB, goTitle };
        foreach (Text t in goTitleTexts) t.text = "CRASHED";
    }

    Text MakeGoTitleText(Transform parent, string name, Color colour, bool ghost)
    {
        var t = MakeText(parent, name, GoTitleSize, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(1400f, GoBandHeight));
        t.color = colour;
        if (ghost)
        {
            var outline = t.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;
        }
        return t;
    }

    /// <summary>The score, the best, distance and bonus - no box round them.</summary>
    void BuildGoResults()
    {
        var mid = new Vector2(0.5f, 0.5f);

        goRunLabel = MakeText(goCard, "GORunLabel", 54, TextAnchor.MiddleCenter,
            mid, new Vector2(0f, -170f), new Vector2(950f, 80f));
        goRunLabel.text = "SCORE";
        goRunLabel.color = new Color(1f, 1f, 1f, 0.7f);
        AddGoPiece(goRunLabel.rectTransform, new Vector2(0f, -50f), 0f);

        goScore = MakeText(goCard, "GOScore", GoScoreSize, TextAnchor.MiddleCenter,
            mid, new Vector2(0f, -295f), new Vector2(1000f, 220f));
        AddGoPiece(goScore.rectTransform, new Vector2(0f, -60f), 0.06f);

        goBestLine = MakeText(goCard, "GOBest", 58, TextAnchor.MiddleCenter,
            mid, new Vector2(0f, -425f), new Vector2(950f, 80f), true);
        goBestLine.color = new Color(1f, 1f, 1f, 0.85f);
        AddGoPiece(goBestLine.rectTransform, new Vector2(0f, -40f), 0.30f);

        // distance and bonus side by side, sliding in from either side (the
        // coins picked up are already on the coin counter, so they are not
        // listed again)
        string[] labels = { "DISTANCE", "BONUS" };
        for (int i = 0; i < labels.Length; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var colGo = new GameObject("GOStat" + i);
            colGo.transform.SetParent(goCard, false);
            var col = colGo.AddComponent<RectTransform>();
            col.anchorMin = col.anchorMax = col.pivot = mid;
            col.anchoredPosition = new Vector2(side * 235f, -570f);
            col.sizeDelta = new Vector2(440f, 160f);
            goStatRoot[i] = colGo;

            var lab = MakeText(col, "Label", 42, TextAnchor.MiddleCenter,
                mid, new Vector2(0f, 38f), new Vector2(440f, 60f), true);
            lab.text = labels[i];
            lab.color = new Color(1f, 1f, 1f, 0.6f);
            goStatLabel[i] = lab;

            // the value has a row of its own, so the bonus's coin can sit
            // just after the number whatever its width
            var rowGo = new GameObject("ValueRow");
            rowGo.transform.SetParent(col, false);
            var row = rowGo.AddComponent<RectTransform>();
            row.anchorMin = row.anchorMax = row.pivot = mid;
            row.anchoredPosition = new Vector2(0f, -34f);
            row.sizeDelta = new Vector2(440f, 90f);
            goStatValue[i] = MakeText(row, "Value", 64, TextAnchor.MiddleCenter,
                mid, Vector2.zero, new Vector2(440f, 90f), true);
            if (i == 1) goBonusCoin = MakeCurrencyIcon(row, mid, Vector2.zero, 66f, false);

            AddGoPiece(col, new Vector2(side * 90f, 0f), 0.40f + i * 0.08f);
        }

        // a hairline between the two (on the right-hand column, so it comes
        // and goes with the stats)
        var div = new GameObject("GODivider").AddComponent<Image>();
        div.transform.SetParent(goStatRoot[1].transform, false);
        div.color = new Color(1f, 1f, 1f, 0.25f);
        div.raycastTarget = false;
        var drt = div.rectTransform;
        drt.anchorMin = drt.anchorMax = drt.pivot = mid;
        drt.anchoredPosition = new Vector2(-235f, 0f);
        drt.sizeDelta = new Vector2(3f, 140f);
    }

    void AddGoPiece(RectTransform rt, Vector2 from, float at)
    {
        var group = rt.GetComponent<CanvasGroup>();
        if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        goPieces.Add(new GoPiece { rt = rt, group = group, home = rt.anchoredPosition, from = from, at = at });
    }

    /// <summary>
    /// The weekly line (GameManager.Leaderboard.cs) is made after this screen,
    /// so it joins the results the first time they are shown.
    /// </summary>
    void AddGoWeeklyLine()
    {
        if (goWeeklyAdded || lbGameOverLine == null) return;
        goWeeklyAdded = true;
        lbGameOverLine.rectTransform.anchoredPosition = new Vector2(0f, -712f);
        AddGoPiece(lbGameOverLine.rectTransform, new Vector2(0f, -30f), 0.58f);
    }

    // ------------------------------------------------------------ showing it

    /// <summary>Fills in the results and plays the whole thing from the crash.</summary>
    void ShowGameOver(string cause, int finalScore, bool newBest,
                      int collected, int bonus)
    {
        foreach (Text t in goTitleTexts) t.text = cause;
        goTargetScore = finalScore;
        goWasBest = newBest;
        goScore.text = "0";
        goBestLine.text = newBest ? "NEW PERSONAL BEST" : Loc.F("BEST  {0}", best);
        goBestLine.color = newBest ? GoGold : new Color(1f, 1f, 1f, 0.85f);
        goStatValue[0].text = Loc.F("{0} M", Mathf.RoundToInt(car.DistanceTraveled));
        goStatValue[1].text = "+" + bonus;
        // an endless run's lines (a race crash hides them again afterwards)
        goRaceCrash = false;
        goRunLabel.text = "SCORE";
        goBestLine.gameObject.SetActive(true);
        foreach (GameObject stat in goStatRoot) stat.SetActive(true);

        AddGoWeeklyLine();
        BeginGameOver();

        // measured once the screen is up: the headline is sized to the words
        // ("OFF THE ROAD" is far wider than "CRASHED", and translations wider
        // still), and the bonus's coin goes just after its number
        foreach (Text t in goTitleTexts) FitToWidth(t, GoTitleSize, GoTitleRoom);
        if (goBonusCoin != null) IconAfterText(goBonusCoin, goStatValue[1]);
        // the score is sized for the number it will finish on, so a long one
        // fits on screen and it does not change size as it counts up
        goScore.text = finalScore.ToString();
        FitToWidth(goScore, GoScoreSize, GoScoreRoom);
        goScore.text = "0";
        // where the run left the player in the week (and who passed them)
        LbFillGameOverLine();
        // celebrated on the way out, not over the top of the results
        goBestToShow = newBest;
        goLbPending = lbShowAfterRun;
        lbShowAfterRun = false;
        lbLeaveAfter = false;
        RefreshGoMenuButton();
    }

    Text goMenuLabel;
    Image goMenuImage;
    Color goMenuColour;
    RectTransform goMenuBox;

    /// <summary>
    /// MENU on the results - or, while boxes from the road are waiting, a
    /// gold OPEN button with the box at its left end and how many there are.
    /// MENU is in the racing font across the whole button; OPEN is in the
    /// small-text font in the room beside the box, so it never runs into it.
    /// </summary>
    void RefreshGoMenuButton()
    {
        if (goMenuLabel == null) return;
        if (goMenuFont == null)
        {
            goMenuFont = goMenuLabel.font;
            goMenuStyle = goMenuLabel.fontStyle;
            goMenuSize = goMenuLabel.fontSize;
            goMenuLabelSize = goMenuLabel.rectTransform.sizeDelta;
            goMenuLabelY = goMenuLabel.rectTransform.anchoredPosition.y;
        }
        int boxes = RoadBoxesWaiting;
        bool open = boxes > 0;
        var lrt = goMenuLabel.rectTransform;
        if (open && SmallFont != null && Loc.DisplayFontFor(Loc.Current) == null)
        {
            goMenuLabel.font = SmallFont;
            goMenuLabel.fontStyle = FontStyle.Normal;
            goMenuLabel.fontSize = TextSize(56, false, SmallFont);
        }
        else
        {
            goMenuLabel.font = goMenuFont;
            goMenuLabel.fontStyle = goMenuStyle;
            goMenuLabel.fontSize = goMenuSize;
        }
        lrt.sizeDelta = open ? new Vector2(400f, goMenuLabelSize.y) : goMenuLabelSize;
        lrt.anchoredPosition = new Vector2(open ? 80f : 0f, goMenuLabelY);
        goMenuLabel.text = open ? Loc.F("OPEN  {0}", "x" + boxes) : "MENU";
        goMenuImage.color = open ? new Color(1f, 0.74f, 0.12f) : goMenuColour;
        goMenuBox.gameObject.SetActive(open);
    }

    Font goMenuFont;
    FontStyle goMenuStyle;
    int goMenuSize;
    Vector2 goMenuLabelSize;
    float goMenuLabelY;

    /// <summary>
    /// MENU on the results. Whatever the run earned plays first, one after
    /// the other - a new high score, then the weekly ranking - and each one
    /// comes back here when it is closed, until there is nothing left and it
    /// goes to the menu.
    /// </summary>
    void ResultsMenu()
    {
        // boxes picked up on the road open first (AfterRoadBox comes back here)
        if (RoadBoxesWaiting > 0)
        {
            OpenRoadBox();
            return;
        }
        if (goBestToShow)
        {
            goBestToShow = false;
            ShowBestRun(goTargetScore);      // CloseBestPanel comes back here
            return;
        }
        if (goLbPending)
        {
            goLbPending = false;
            lbLeaveAfter = true;             // and so does closing the ranking
            OpenLeaderboard(true);
            return;
        }
        LeaveResults();
    }

    /// <summary>A race's death screen: the place the car was in and how far was left.</summary>
    void ShowRaceCrashLines(int place, float togo)
    {
        goRaceCrash = true;
        goRunLabel.text = "PLACE";
        goScore.text = Loc.F("P{0}/{1}", place, RaceMode.Opponents + 1);
        FitToWidth(goScore, GoScoreSize, GoScoreRoom);
        goScore.color = Color.white;
        goBestLine.text = Loc.F("{0}M TO GO", Mathf.RoundToInt(togo));
        goBestLine.color = new Color(1f, 1f, 1f, 0.85f);
        foreach (GameObject stat in goStatRoot) stat.SetActive(false);
    }

    void BeginGameOver()
    {
        goAnimT = 0f;
        goWhooshed = false;
        goSlammed = false;
        goCounted = false;
        goLastShown = -1;
        goLastTickAt = -1f;
        gameOverPanel.SetActive(true);
        gameOverPanel.transform.SetAsLastSibling();
        PoseGameOver(0f);
    }

    /// <summary>A tap on the results: jump to the end, so MENU is there now.</summary>
    void SkipGameOver()
    {
        if (goAnimT < GoSkipFrom) return;
        if (goAnimT < GoMenuAt) goAnimT = GoMenuAt;
    }

    void TickGameOver()
    {
        if (goAnimT < 0f || gameOverPanel == null || !gameOverPanel.activeSelf) return;
        goAnimT += Time.unscaledDeltaTime;
        float t = goAnimT;

        if (!goWhooshed && t >= GoBandAt)
        {
            goWhooshed = true;
            audioMan.PlayFinishWhoosh();      // the slash wiping across
        }
        if (!goSlammed && t >= GoSlamAt + GoSlamTime)
        {
            goSlammed = true;                 // the headline hits the screen
            if (camFollow != null) camFollow.Shake(0.3f);
        }

        // the score ticks as it counts, and pops when it gets there
        if (!goRaceCrash && !goCounted && t >= GoResultsAt)
        {
            float c = GoCountProgress(t);
            int shown = GoShownScore(c);
            if (shown != goLastShown && t - goLastTickAt >= 0.055f)
            {
                goLastShown = shown;
                goLastTickAt = t;
                audioMan.PlayWheelTick(0.85f + 0.6f * c, 0.22f);
            }
            if (c >= 1f)
            {
                goCounted = true;
                audioMan.PlayPop();
            }
        }

        PoseGameOver(t);
    }

    float GoCountProgress(float t)
    {
        return Mathf.Clamp01((t - GoResultsAt - 0.06f) / GoCountTime);
    }

    int GoShownScore(float c)
    {
        return Mathf.RoundToInt(Mathf.Lerp(0f, goTargetScore, 1f - Mathf.Pow(1f - c, 3f)));
    }

    /// <summary>
    /// Puts every part of the screen where it is <paramref name="t"/> seconds
    /// after the crash. No sounds, no shake - those are TickGameOver's - so a
    /// skip can jump straight to the end.
    /// </summary>
    void PoseGameOver(float t)
    {
        // impact flash
        float flash = 1f - Mathf.Clamp01(t / 0.2f);
        goFlash.color = new Color(1f, 0.92f, 0.75f, 0.5f * flash * flash);

        // the letterbox slams shut on the crash, then opens back out
        float shut = GoEaseOut(Mathf.Clamp01(t / GoBarsShut));
        float open = GoEaseOut(Mathf.Clamp01((t - GoBarsOpenAt) / 0.35f));
        float bar = GoBarHeight * shut * (1f - open);
        SetGoBar(goBarTop, bar);
        SetGoBar(goBarBottom, bar);

        // red floods in round the edges, then beats slowly
        float surge = Mathf.Exp(-3f * t);
        float beat = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);
        goVignette.color = Faded(GoVignetteRed, 0.3f + 0.55f * surge + 0.08f * beat);

        // shading behind the words
        goShadeTop.color = Faded(GoShadeColour,
            0.5f * GoEaseOut(Mathf.Clamp01((t - GoBandAt) / 0.4f)));
        goShadeBottom.color = Faded(GoShadeColour,
            0.9f * GoEaseOut(Mathf.Clamp01((t - (GoResultsAt - 0.2f)) / 0.4f)));

        // the slash wipes across from the left
        float wipe = Mathf.Clamp01((t - GoBandAt) / 0.2f);
        goBand.sizeDelta = new Vector2(GoBandWidth * (1f - Mathf.Pow(1f - wipe, 4f)), 0f);
        if (goBand.gameObject.activeSelf != wipe > 0f) goBand.gameObject.SetActive(wipe > 0f);

        PoseGoHeadline(t);

        // the results rise in one after another
        foreach (GoPiece pc in goPieces)
        {
            float p = Mathf.Clamp01((t - GoResultsAt - pc.at) / 0.3f);
            pc.group.alpha = Mathf.Clamp01(p * 1.8f);
            pc.rt.anchoredPosition = pc.home + pc.from * (1f - GoEaseOut(p));
        }

        // the score counts up, and bounces when it gets there
        if (!goRaceCrash)
        {
            float count = GoCountProgress(t);
            goScore.text = GoShownScore(count).ToString();
            goScore.color = goWasBest
                ? Color.Lerp(Color.white, GoGold, 0.5f + 0.5f * Mathf.Sin(t * 3.5f))
                : Color.white;
            float since = t - (GoResultsAt + 0.06f + GoCountTime);
            float bounce = since > 0f ? Mathf.Exp(-8f * since) * Mathf.Sin(since * 22f) * 0.12f : 0f;
            goScore.rectTransform.localScale = Vector3.one * (1f + bounce);
        }
        else
        {
            goScore.rectTransform.localScale = Vector3.one;
        }

        // MENU pops up last, overshooting a little, then gently breathes
        float m = Mathf.Clamp01((t - GoMenuAt) / 0.32f);
        float pop = Mathf.LerpUnclamped(0.5f, 1f, GoEaseOutBack(m));
        float breathe = m >= 1f ? 1f + 0.025f * Mathf.Sin((t - GoMenuAt) * 4f) : 1f;
        goMenuRt.localScale = Vector3.one * (m > 0f ? pop * breathe : 0.5f);
        goMenuGroup.alpha = Mathf.Clamp01(m * 3f);
        bool live = m >= 0.5f;
        goMenuGroup.interactable = live;
        goMenuGroup.blocksRaycasts = live;
    }

    /// <summary>The cause falls onto the slash, lands with a punch and glitches.</summary>
    void PoseGoHeadline(float t)
    {
        float s = (t - GoSlamAt) / GoSlamTime;     // 0 to 1 while it falls
        float scale, alpha;
        if (s < 0f)
        {
            scale = 2.6f;
            alpha = 0f;
        }
        else if (s < 1f)
        {
            // speeding up as it comes, like it is being thrown at the glass
            scale = Mathf.Lerp(2.6f, 1f, s * s);
            alpha = Mathf.Clamp01(s * 2.5f);
        }
        else
        {
            float since = t - GoSlamAt - GoSlamTime;
            float punch = Mathf.Exp(-9f * since) * Mathf.Sin(since * 26f) * 0.10f;
            scale = 1f + punch + 0.012f * Mathf.Sin(t * 2.2f);
            alpha = 1f;
        }
        goTitleRt.localScale = Vector3.one * scale;
        goTitle.color = Faded(GoTitleRed, alpha);

        // Red and blue copies jump about behind it for a moment after it
        // lands, and again briefly every few seconds - a screen that took
        // the hit as well.
        float glitch = 0f;
        float after = t - GoSlamAt - GoSlamTime;
        if (after >= 0f && after < 0.4f) glitch = 1f - after / 0.4f;
        else if (after >= 0.4f && Mathf.Repeat(after - 0.4f, 3.1f) > 2.95f) glitch = 0.6f;

        float step = Mathf.Floor(t * 30f);          // it jumps 30 times a second
        PoseGoGhost(goGhostA, -1f, glitch, step, scale);
        PoseGoGhost(goGhostB, 1f, glitch, step + 17f, scale);
    }

    void PoseGoGhost(Text ghost, float side, float glitch, float step, float scale)
    {
        Color col = ghost.color;
        col.a = glitch > 0f ? 0.75f * glitch : 0f;
        ghost.color = col;
        if (glitch <= 0f) return;
        var rt = ghost.rectTransform;
        float jx = side * (8f + 18f * glitch * GoHash(step));
        float jy = (GoHash(step + 5f) - 0.5f) * 14f * glitch;
        rt.anchoredPosition = goTitleRt.anchoredPosition + new Vector2(jx, jy);
        rt.localScale = Vector3.one * scale;
    }

    // ------------------------------------------------------------ pieces

    static RectTransform MakeGoLayer(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    static RawImage MakeGoRaw(Transform parent, string name, Texture tex, Color tint)
    {
        var img = MakeGoLayer(parent, name).gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.color = tint;
        img.raycastTarget = false;
        return img;
    }

    static Image MakeGoBar(Transform parent, string name, float edge)
    {
        var img = new GameObject(name).AddComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, edge);
        rt.anchorMax = new Vector2(1f, edge);
        rt.pivot = new Vector2(0.5f, edge);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        img.enabled = false;
        return img;
    }

    /// <summary>
    /// A letterbox bar whose inner edge is <paramref name="height"/> in from
    /// the edge of the safe area. The panel reaches past the safe area, so the
    /// bar is that much taller - and is switched off when open, or it would
    /// leave a black strip over the notch.
    /// </summary>
    static void SetGoBar(Image bar, float height)
    {
        bool on = height > 0.5f;
        if (bar.enabled != on) bar.enabled = on;
        if (on) bar.rectTransform.sizeDelta = new Vector2(0f, PanelBleed.y + height);
    }

    static Color Faded(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }

    static float GoEaseOut(float p)
    {
        return 1f - Mathf.Pow(1f - p, 3f);
    }

    /// <summary>Overshoots past 1 and settles back, for things that pop in.</summary>
    static float GoEaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float q = p - 1f;
        return 1f + c3 * q * q * q + c1 * q * q;
    }

    /// <summary>A repeatable 0-1 jitter for a whole number step.</summary>
    static float GoHash(float step)
    {
        return Mathf.Repeat(Mathf.Sin(step * 12.9898f) * 43758.5453f, 1f);
    }

    static Texture2D goVignetteTex, goShadeTex;

    /// <summary>Clear in the middle, solid towards the edges.</summary>
    static Texture2D GoVignetteTexture()
    {
        if (goVignetteTex != null) return goVignetteTex;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f;
                float v = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.3f, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        goVignetteTex = tex;
        return tex;
    }

    /// <summary>Solid at the bottom, fading out over the top part.</summary>
    static Texture2D GoShadeTexture()
    {
        if (goShadeTex != null) return goShadeTex;
        const int n = 64;
        var tex = new Texture2D(4, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var px = new Color32[4 * n];
        for (int y = 0; y < n; y++)
        {
            float s = y / (float)(n - 1);
            float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, s));
            for (int x = 0; x < 4; x++)
                px[y * 4 + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        goShadeTex = tex;
        return tex;
    }

    // ------------------------------------------------------------ the wreck

    const float WreckHopTime = 0.55f;
    const float WreckHop = 0.7f;           // metres the blast throws the car up

    float wreckT = -1f;
    Vector3 wreckBlastAt;
    Transform wreckBody;
    Vector3 wreckBodyPos;
    Quaternion wreckBodyRot;
    float wreckRoll;
    bool wreckLanded, wreckSecondBlast, wreckBurning;

    /// <summary>
    /// What happens to the car after the first bang: the camera pulls back to
    /// watch, a second blast goes off, the car is thrown up off its wheels and
    /// lands, and it is left burning.
    /// </summary>
    void StartWreck(Vector3 blastAt)
    {
        EndWreck();
        wreckT = 0f;
        wreckBlastAt = blastAt;
        wreckLanded = wreckSecondBlast = wreckBurning = false;
        wreckBody = car != null ? car.CarModelRoot : null;
        if (wreckBody != null)
        {
            wreckBodyPos = wreckBody.localPosition;
            wreckBodyRot = wreckBody.localRotation;
        }
        wreckRoll = Random.value < 0.5f ? -1f : 1f;
        if (camFollow != null) camFollow.SetWreckShot(true);
    }

    void TickWreck()
    {
        if (wreckT < 0f) return;
        if (state != State.GameOver) { EndWreck(); return; }
        wreckT += Time.unscaledDeltaTime;

        if (!wreckSecondBlast && wreckT >= 0.16f)
        {
            wreckSecondBlast = true;
            var jitter = new Vector3(Random.Range(-0.6f, 0.6f), 0.3f, Random.Range(-0.6f, 0.6f));
            CrashExplosion.Play(wreckBlastAt + jitter, 0.9f);
        }
        if (!wreckBurning && wreckT >= 0.3f)
        {
            wreckBurning = true;
            CrashExplosion.Burn(wreckBlastAt + Vector3.down * 0.15f,
                car != null ? car.transform.rotation : Quaternion.identity, 1f);
        }

        // One arc up and back down, nose lifting and tipping onto one side
        // as it goes, and flat again on landing. The tilt is about the car's
        // own axes (the rig's), and the body is put back exactly as it was.
        if (wreckBody != null && !wreckLanded)
        {
            float p = Mathf.Clamp01(wreckT / WreckHopTime);
            if (p < 1f)
            {
                float arc = 4f * p * (1f - p);
                wreckBody.localPosition = wreckBodyPos + Vector3.up * (WreckHop * arc);
                wreckBody.localRotation = Quaternion.Euler(-8f * arc, 0f, 16f * wreckRoll * arc)
                                          * wreckBodyRot;
            }
            else
            {
                wreckLanded = true;
                wreckBody.localPosition = wreckBodyPos;
                wreckBody.localRotation = wreckBodyRot;
                if (camFollow != null) camFollow.Shake(0.22f);
            }
        }

        if (wreckSecondBlast && wreckBurning && (wreckLanded || wreckBody == null))
        {
            wreckT = -1f;
            wreckBody = null;
        }
    }

    /// <summary>Stops the wreck's moves, putting the body back if it is mid-air.</summary>
    void EndWreck()
    {
        if (wreckT >= 0f && wreckBody != null && !wreckLanded)
        {
            wreckBody.localPosition = wreckBodyPos;
            wreckBody.localRotation = wreckBodyRot;
        }
        wreckT = -1f;
        wreckBody = null;
    }
}
