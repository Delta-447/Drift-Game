using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tutorial ("coach"). It runs the very first time the game is played,
/// and again from Settings > General > TUTORIAL.
///
///  1. It starts by itself (the first time, straight after the opening
///     titles), on the same road every time, beginning on a long straight.
///  2. A practice drive. Each lesson slows the game right down and greys it
///     out while a white text box explains it and the finger shows what to
///     do; the moment the player does it, the game snaps back to full speed.
///     Steering, drifting, coins, then every item - each one is placed on the
///     road in turn - the springs jump, and finally the traffic. Crashing
///     rewinds instead of ending the run, and nothing from the drive counts.
///  3. Back in the menu, the finger walks through the coin and tire
///     counters, then RACE, SPIN, GARAGE, SHOP, QUESTS and SETTINGS: each is
///     opened, explained, and closed again with its X.
///  Between the drive and the menu, YOU GOT IT! plays on a screen like the
///  race finish (the bars slam shut, the screen goes dark, the words slide in
///  in gold) and the game changes over to the menu behind it. At the very
///  end, TUTORIAL COMPLETE plays the same way, and waits for a tap.
///
/// Everything sits on its own layer above the rest of the UI. A button the
/// finger is pointing at is lifted above the grey (it gets a Canvas of its
/// own) so it is the only thing that can be pressed.
/// </summary>
public partial class GameManager
{
    // ------------------------------------------------------------------ state

    enum CoachPhase { None, MenuStart, Drive, Tour }

    CoachPhase coachPhase = CoachPhase.None;
    bool coachWanted;          // start at the next moment the menu is free
    bool coachDrive;           // this run is the tutorial's practice drive
    int coachStep;
    int coachStage;
    float coachStageT;         // real seconds in this stage
    float coachSlowTarget = 1f;
    float coachGreyTarget;
    float coachGrey;
    bool coachTapped;          // "tap to continue"
    float coachHeldT;          // how long the asked-for input has been held

    // item lessons
    float coachItemDist = -1f;
    int coachItemTries;
    int coachItemSide = 1;
    bool coachItemGot;
    bool coachJumped;

    const float CoachSlowMo = 0.2f;

    /// <summary>The tutorial's road: the same seed lays the same road every time.</summary>
    const int TutorialTrackSeed = 20260930;

    /// <summary>Metres of straight road the tutorial starts on.</summary>
    const float TutorialOpeningStraight = 180f;

    /// <summary>The daily reward waits while the tutorial is due or running.</summary>
    bool CoachHoldsLogin { get { return coachWanted || coachPhase != CoachPhase.None; } }

    // the race-finish style banner: YOU GOT IT! and TUTORIAL COMPLETE
    Image coachDoneTop, coachDoneBottom, coachDoneBlack;
    RectTransform coachDoneTextRt;
    Text coachDoneText, coachDoneHint;
    float bannerT = -1f;            // < 0: no banner up
    float bannerCloseT = -1f;       // >= 0: opening back up
    float bannerHold;               // how long it stays before opening by itself
    bool bannerTap;                 // it can be tapped away (and says so)
    bool bannerDarkFired;
    System.Action bannerOnDark;     // runs once the screen is fully dark
    System.Action bannerOnDone;     // runs once it has opened back up

    // the practice drive, one lesson at a time
    const int CdGo = 0, CdSteer = 1, CdDrift = 2, CdCoins = 3,
              CdItem0 = 4, CdJump = 9, CdTraffic = 10, CdDone = 11;

    static readonly TrackGenerator.PowerUpType[] CoachItems =
    {
        TrackGenerator.PowerUpType.Invincible,
        TrackGenerator.PowerUpType.DoubleCoins,
        TrackGenerator.PowerUpType.Magnet,
        TrackGenerator.PowerUpType.DoubleScore,
        TrackGenerator.PowerUpType.Springs,
    };

    // what each item does, in the same order as ItemNames
    static readonly string[] CoachItemText =
    {
        "Crash-proof for a few seconds -\nsmash straight through traffic.",
        "Every coin counts double\nwhile it lasts.",
        "Pulls nearby coins\nstraight to your car.",
        "Doubles every point you score\nwhile it lasts.",
        "Lets you jump over anything\nin your way.",
    };

    // --------------------------------------------------------------- ui refs

    RectTransform coachRoot, coachTop, coachBox, coachFingerRt, coachRingRt, coachRippleRt;
    CanvasGroup coachGroup;
    Canvas coachCanvas, coachTopCanvas;
    Image coachDim, coachBoxBorder, coachBoxFill, coachRing, coachRipple;
    Text coachTitle, coachBody, coachHint;
    RawImage coachFinger;
    float coachBoxY;
    float coachBoxPopT = 1f;

    // the button currently lifted above the grey
    GameObject coachTarget;
    Canvas coachTargetCanvas;
    GraphicRaycaster coachTargetRay;

    // menu buttons the tour visits (set where the menu is built)
    GameObject playButtonGo, raceButtonGo, garageButtonGo, shopButtonGo, questsButtonGo, settingsButtonGo;

    enum FingerMode { Hidden, Tap, SlideLR, Hold, SwipeUp }
    FingerMode fingerMode = FingerMode.Hidden;
    RectTransform fingerAim;        // Tap: what it taps
    int fingerSide = 1;             // Hold: which way it slides
    float fingerT;

    const float BoxWidth = 900f;
    const float CoachDriveBoxY = 360f;
    const float CoachMenuBoxY = 330f;
    const float CoachScreenBoxY = 620f;
    const float FingerRowY = -640f;

    // ------------------------------------------------------------------ build

    void BuildCoach(Transform uiRoot)
    {
        var rootGo = new GameObject("Coach");
        rootGo.transform.SetParent(uiRoot, false);
        coachRoot = rootGo.AddComponent<RectTransform>();
        coachRoot.anchorMin = Vector2.zero;
        coachRoot.anchorMax = Vector2.one;
        coachRoot.offsetMin = coachRoot.offsetMax = Vector2.zero;
        // its own layer, above every panel and popup
        coachCanvas = rootGo.AddComponent<Canvas>();
        coachCanvas.overrideSorting = true;
        coachCanvas.sortingOrder = 300;
        rootGo.AddComponent<GraphicRaycaster>();
        coachGroup = rootGo.AddComponent<CanvasGroup>();

        // the grey: over the menu it also swallows taps (tap-to-continue)
        var dimGo = new GameObject("Grey");
        dimGo.transform.SetParent(coachRoot, false);
        coachDim = dimGo.AddComponent<Image>();
        coachDim.color = new Color(0f, 0f, 0f, 0f);
        var dimRt = coachDim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = new Vector2(-200f, -400f);
        dimRt.offsetMax = new Vector2(200f, 400f);
        var dimBtn = dimGo.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => coachTapped = true);

        // everything drawn over the lifted button: text box, rings, finger
        var topGo = new GameObject("CoachTop");
        topGo.transform.SetParent(coachRoot, false);
        coachTop = topGo.AddComponent<RectTransform>();
        coachTop.anchorMin = Vector2.zero;
        coachTop.anchorMax = Vector2.one;
        coachTop.offsetMin = coachTop.offsetMax = Vector2.zero;
        coachTopCanvas = topGo.AddComponent<Canvas>();
        coachTopCanvas.overrideSorting = true;
        coachTopCanvas.sortingOrder = 320;

        coachRing = MakeCoachRing("Ring", new Color(1f, 0.85f, 0.2f, 1f), out coachRingRt);
        coachRipple = MakeCoachRing("Ripple", Color.white, out coachRippleRt);

        BuildCoachBox();
        BuildCoachDone();

        var fingerGo = new GameObject("Finger");
        fingerGo.transform.SetParent(coachTop, false);
        coachFinger = fingerGo.AddComponent<RawImage>();
        coachFinger.texture = Resources.Load<Texture2D>("UI/tutorial_finger");
        coachFinger.raycastTarget = false;
        coachFingerRt = coachFinger.rectTransform;
        coachFingerRt.anchorMin = coachFingerRt.anchorMax = new Vector2(0.5f, 0.5f);
        // the pivot is the fingertip, so placing the finger puts its tip there
        coachFingerRt.pivot = new Vector2(0.16f, 0.90f);
        coachFingerRt.sizeDelta = new Vector2(230f, 230f);

        HideCoachUi();
        // the very first time, the tutorial starts as soon as the menu is free
        coachWanted = !TutorialSeen;
    }

    /// <summary>The TUTORIAL COMPLETE screen, built like the race finish.</summary>
    void BuildCoachDone()
    {
        coachDoneBlack = new GameObject("DoneBlackout").AddComponent<Image>();
        coachDoneBlack.transform.SetParent(coachTop, false);
        coachDoneBlack.color = new Color(0f, 0f, 0f, 0f);
        coachDoneBlack.raycastTarget = false;
        var bRt = coachDoneBlack.rectTransform;
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(-200f, -400f);
        bRt.offsetMax = new Vector2(200f, 400f);

        coachDoneTop = MakeLetterboxBar(coachTop, "DoneBarTop", 1f);
        coachDoneBottom = MakeLetterboxBar(coachTop, "DoneBarBottom", 0f);

        var textGo = new GameObject("DoneWords");
        textGo.transform.SetParent(coachTop, false);
        coachDoneTextRt = textGo.AddComponent<RectTransform>();
        coachDoneTextRt.anchorMin = coachDoneTextRt.anchorMax = coachDoneTextRt.pivot = new Vector2(0.5f, 0.5f);
        coachDoneTextRt.sizeDelta = new Vector2(1000f, 420f);
        // the game's big lettering, in the gold the race placing uses
        coachDoneText = MakeText(coachDoneTextRt, "Words", 92, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 420f));
        coachDoneText.color = new Color(1f, 0.82f, 0.12f);
        coachDoneText.lineSpacing = 0.95f;

        coachDoneHint = MakeText(coachTop, "DoneHint", 38, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(900f, 80f), true);
        coachDoneHint.color = new Color(1f, 1f, 1f, 0f);

        HideCoachDone();
    }

    void HideCoachDone()
    {
        bannerT = -1f;
        bannerCloseT = -1f;
        bannerOnDark = null;
        bannerOnDone = null;
        if (coachDoneBlack == null) return;
        coachDoneBlack.gameObject.SetActive(false);
        coachDoneTop.gameObject.SetActive(false);
        coachDoneBottom.gameObject.SetActive(false);
        coachDoneTextRt.gameObject.SetActive(false);
        coachDoneHint.gameObject.SetActive(false);
    }

    void BuildCoachBox()
    {
        var boxGo = new GameObject("CoachBox");
        boxGo.transform.SetParent(coachTop, false);
        coachBox = boxGo.AddComponent<RectTransform>();
        coachBox.anchorMin = coachBox.anchorMax = coachBox.pivot = new Vector2(0.5f, 0.5f);

        // a white card on a slightly bigger black one: the black outline
        coachBoxBorder = MakeCard(coachBox, Vector2.zero, new Vector2(BoxWidth + 14f, 200f), Color.black);
        coachBoxFill = MakeCard(coachBox, Vector2.zero, new Vector2(BoxWidth, 186f), Color.white);

        coachTitle = MakeCoachText("CoachTitle", 54, new Color(0.04f, 0.04f, 0.06f));
        coachBody = MakeCoachText("CoachBody", 42, new Color(0.08f, 0.08f, 0.10f));
        coachBody.lineSpacing = 1.1f;
        coachHint = MakeCoachText("CoachHint", 31, new Color(0.38f, 0.38f, 0.44f));
    }

    Text MakeCoachText(string name, int size, Color color)
    {
        // the plain font: this is reading text, it has to be easy on the eye
        var t = MakeText(coachBox, name, size, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BoxWidth - 50f, 60f), true);
        t.color = color;
        // black text on white needs no outline
        var outline = t.GetComponent<Outline>();
        if (outline != null) Destroy(outline);
        return t;
    }

    Image MakeCoachRing(string name, Color color, out RectTransform rt)
    {
        var go = new GameObject(name);
        go.transform.SetParent(coachTop, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetCoachRingSprite();
        img.color = color;
        img.raycastTarget = false;
        rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(120f, 120f);
        go.SetActive(false);
        return img;
    }

    static Sprite coachRingSprite;

    /// <summary>A plain white ring, drawn in code: the target marker and the tap ripple.</summary>
    static Sprite GetCoachRingSprite()
    {
        if (coachRingSprite != null) return coachRingSprite;
        const int S = 128;
        const float C = 63.5f, Outer = 61f, Thick = 9f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        var pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float a = Disc(x, y, C, C, Outer) - Disc(x, y, C, C, Outer - Thick);
                pixels[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        coachRingSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return coachRingSprite;
    }

    // ------------------------------------------------------------ entry points

    /// <summary>Settings > General > TUTORIAL: straight from Settings into the drive.</summary>
    void ReplayTutorial()
    {
        if (state != State.Menu) return;
        settingsPanel.SetActive(false);
        audioMan.PlayTap();
        StartCoachDrive();
    }

    /// <summary>Shows the tutorial layer. A canvas only keeps its own draw order while live, so it is set again.</summary>
    void ShowCoachRoot()
    {
        if (!coachRoot.gameObject.activeSelf) coachRoot.gameObject.SetActive(true);
        coachCanvas.overrideSorting = true;
        coachCanvas.sortingOrder = 300;
        coachTopCanvas.overrideSorting = true;
        coachTopCanvas.sortingOrder = 320;
    }

    /// <summary>
    /// The tutorial can start by itself: in the lobby, the opening title has
    /// finished and no popup is up. The menu itself is not needed - on the
    /// first launch it is kept hidden until the tutorial is over.
    /// </summary>
    bool CoachCanStart()
    {
        if (state != State.Menu || introRunning) return false;
        if (loginPanel != null && loginPanel.activeSelf) return false;
        if (unlockPanel != null && unlockPanel.activeSelf) return false;
        return true;
    }

    /// <summary>The menu is showing with nothing on top of it.</summary>
    bool CoachMenuFree()
    {
        if (state != State.Menu || introRunning) return false;
        if (menuPanel == null || !menuPanel.activeSelf) return false;
        if (loginPanel != null && loginPanel.activeSelf) return false;
        if (unlockPanel != null && unlockPanel.activeSelf) return false;
        return true;
    }

    /// <summary>
    /// The practice drive begins: on its own fixed road, starting on a long
    /// straight, rebuilt from the same seed every time.
    /// </summary>
    void StartCoachDrive()
    {
        CoachClearHighlight();
        HideCoachUi();
        coachWanted = false;
        coachPhase = CoachPhase.Drive;
        coachDrive = true;
        tutorialOnly = true;             // practice: no quests, no coins banked
        coachStep = CdGo;
        SetCoachStage(0);
        coachItemTries = 0;
        coachItemSide = 1;
        coachItemDist = -1f;
        PlayerPrefs.SetInt(TutorialKey, 1);
        PlayerPrefs.Save();
        ShowCoachRoot();
        SetCoachGreyForMenu(false);

        // the tutorial's own road: rebuilt from the same seed every time, so
        // it is the same road, and starting on a long straight
        track.UseFixedLayout(TutorialTrackSeed);
        track.openingStraight = TutorialOpeningStraight;
        track.roadWidth = endlessRoadWidth;
        ApplyRunTrack();
        track.Init(startPos, startYaw);
        car.ResetRun(track);
        if (camFollow != null) camFollow.SnapToTarget();
        StartRun();
    }

    /// <summary>The spawners the practice drive keeps off until its traffic lesson.</summary>
    void CoachRoadRules()
    {
        if (!coachDrive || track == null) return;
        bool traffic = coachStep >= CdTraffic;
        track.spawnObstacles = traffic;
        track.spawnTraffic = traffic;
        // items are laid out one at a time by the lessons themselves
        track.spawnPowerUps = false;
        track.spawnTirePickups = false;
        track.spawnCoins = true;
    }

    /// <summary>An item was picked up (from ActivateItem).</summary>
    void CoachOnItem(TrackGenerator.PowerUpType type)
    {
        if (coachPhase == CoachPhase.Drive) coachItemGot = true;
    }

    /// <summary>A springs jump happened.</summary>
    void CoachOnJump()
    {
        if (coachPhase == CoachPhase.Drive) coachJumped = true;
    }

    /// <summary>A crash in the practice drive rewinds instead of ending it.</summary>
    void CoachCrash()
    {
        Time.timeScale = 1f;
        coachSlowTarget = 1f;
        coachGreyTarget = 0f;
        FingerHide();
        if (coachRing != null) coachRing.gameObject.SetActive(false);
        StartRewind();
        track.ClearObstaclesAhead(car.DistanceTraveled - RewindDistance, 160f);
        // an item lesson shows its item again as it comes back into view
        if (coachStep >= CdItem0 && coachStep < CdItem0 + CoachItems.Length && coachStage >= 1 && coachStage <= 3)
            SetCoachStage(1);
    }

    /// <summary>Stops the tutorial wherever it is (left it some other way).</summary>
    void CoachAbort()
    {
        coachPhase = CoachPhase.None;
        coachDrive = false;
        coachWanted = false;
        if (coachItemDist >= 0f && track != null) track.RemovePowerUpAt(coachItemDist);
        coachItemDist = -1f;
        if (state == State.Playing) Time.timeScale = 1f;
        CoachClearHighlight();
        HideCoachUi();
    }

    /// <summary>PLAY at the very end of the tour: the tutorial is done.</summary>
    void CoachFinish()
    {
        coachPhase = CoachPhase.None;
        CoachClearHighlight();
        HideCoachUi();
    }

    // ------------------------------------------------------------------- tick

    /// <summary>Runs every frame, in every state (from Update).</summary>
    void TickCoach()
    {
        if (coachRoot == null) return;
        float udt = Time.unscaledDeltaTime;
        coachStageT += udt;

        // while a banner is up, the rest of the tutorial waits for it
        if (TickCoachBanner(udt))
        {
            coachTapped = false;
            return;
        }

        switch (coachPhase)
        {
            case CoachPhase.None:
                // the first time (and from Settings) it starts by itself
                if (coachWanted && CoachCanStart())
                {
                    // first of all, a name for the weekly ranking
                    // (GameManager.Leaderboard.cs); the drive starts once it is picked
                    if (NeedsUsername()) { if (!namePanel.activeSelf) OpenNamePanel(true); }
                    else StartCoachDrive();
                }
                break;
            case CoachPhase.MenuStart:
                // started a run some other way, or left the menu
                if (state != State.Menu) CoachAbort();
                else KeepCoachTarget();
                break;
            case CoachPhase.Drive:
                TickCoachDrive(udt);
                break;
            case CoachPhase.Tour:
                TickCoachTour();
                break;
        }

        if (coachPhase != CoachPhase.None) AnimateCoach(udt);
        coachTapped = false;
    }

    void SetCoachStage(int stage)
    {
        coachStage = stage;
        coachStageT = 0f;
        coachHeldT = 0f;
        coachTapped = false;
    }

    void NextCoachStep()
    {
        coachStep++;
        SetCoachStage(0);
        HideCoachBox();
        FingerHide();
        if (coachRing != null) coachRing.gameObject.SetActive(false);
        CoachDemo(false);
    }

    /// <summary>Slow-mo and grey while a lesson is being shown; full speed once the player acts.</summary>
    void CoachDemo(bool on)
    {
        coachSlowTarget = on ? CoachSlowMo : 1f;
        coachGreyTarget = on ? 1f : 0f;
    }

    // ------------------------------------------------------------ the drive

    void TickCoachDrive(float udt)
    {
        // quit to the menu from pause, or anything else that ended the run
        if (state == State.Menu || state == State.GameOver || mode != Mode.Endless)
        {
            CoachAbort();
            return;
        }

        // hidden while the pause menu is up
        bool driving = state == State.Playing || state == State.Rewinding;
        coachGroup.alpha = driving ? 1f : 0f;

        if (state == State.Playing)
        {
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, coachSlowTarget, udt * 2.5f);
        }
        coachGrey = Mathf.MoveTowards(coachGrey, coachGreyTarget, udt * 3f);
        coachDim.color = new Color(0.30f, 0.30f, 0.34f, 0.5f * coachGrey);

        if (state != State.Playing || resumeCountdown > 0f) return;
        // the 3-2-1 plays first, with a hello while the camera shows the car off
        if (raceCountdown > 0f)
        {
            if (coachStep == CdGo && coachStage == 0)
            {
                ShowCoachBox("WELCOME TO DRIFTLINE!", "Let's learn how to drive.", null, CoachDriveBoxY);
                coachStage = 1;
            }
            coachStageT = 0f;
            return;
        }

        float steer = Mathf.Abs(car.SteerInput);

        switch (coachStep)
        {
            case CdGo:
                if (coachStageT > 0.8f) NextCoachStep();
                break;

            case CdSteer:
                if (coachStage == 0)
                {
                    CoachDemo(true);
                    ShowCoachBox("SLIDE TO STEER",
                        "Put your thumb anywhere on the\nscreen and slide it left or right.", null, CoachDriveBoxY);
                    FingerSlide();
                    SetCoachStage(1);
                }
                else if (coachStage == 1)
                {
                    coachHeldT = steer > 0.3f ? coachHeldT + udt : 0f;
                    if (coachHeldT > 0.25f) { CoachDemo(false); FingerHide(); SetCoachStage(2); }
                }
                else if (coachStageT > 1.6f) NextCoachStep();
                break;

            case CdDrift:
                if (coachStage == 0)
                {
                    CoachDemo(true);
                    ShowCoachBox("HOLD IT TO DRIFT",
                        "Keep your thumb over to one side\nand the car slides into a drift.\nDrifts build your combo.",
                        null, CoachDriveBoxY);
                    FingerHold(1);
                    SetCoachStage(1);
                }
                else if (coachStage == 1)
                {
                    if (steer > 0.5f) { CoachDemo(false); FingerHide(); SetCoachStage(2); }
                }
                else if (coachStage == 2)
                {
                    coachHeldT = car.IsDrifting ? coachHeldT + udt : 0f;
                    if (coachHeldT > 0.4f || coachStageT > 10f) { HideCoachBox(); SetCoachStage(3); }
                }
                else if (coachStageT > 1.2f) NextCoachStep();
                break;

            case CdCoins:
                if (coachStage == 0)
                {
                    ShowCoachBox("GRAB COINS",
                        "Drive through coins to collect them.\nSpend them on cars and upgrades.", null, CoachDriveBoxY);
                    SetCoachStage(1);
                }
                else if (coachStageT > 5f) NextCoachStep();
                break;

            case CdJump:
                if (coachStage == 0)
                {
                    coachJumped = false;
                    CoachDemo(true);
                    ShowCoachBox("SWIPE UP TO JUMP",
                        "While springs are on, flick your\nthumb up to hop over things.", null, CoachDriveBoxY);
                    FingerSwipeUp();
                    SetCoachStage(1);
                }
                else if (coachStage == 1)
                {
                    if (coachJumped) { CoachDemo(false); FingerHide(); SetCoachStage(2); }
                    else if (springsT <= 0f || coachStageT > 20f) NextCoachStep();
                }
                else if (coachStageT > 1.5f) NextCoachStep();
                break;

            case CdTraffic:
                if (coachStage == 0)
                {
                    CoachRoadRules();
                    track.ResumeSpawnsAt(car.DistanceTraveled + 90f);
                    ShowCoachBox("WATCH THE ROAD",
                        "Dodge cars and obstacles.\nPass close for a NEAR MISS bonus.\nCrashing ends your run.",
                        null, CoachDriveBoxY);
                    SetCoachStage(1);
                }
                else if (coachStageT > 6f) NextCoachStep();
                break;

            case CdDone:
                if (coachStage == 0)
                {
                    // nothing can end the drive in the moment before it goes dark
                    car.GrantMercy(5f);
                    SetCoachStage(1);
                    // the menu is swapped in behind the dark, and the tour
                    // starts as it opens back up
                    PlayCoachBanner("YOU GOT IT!", false, 2.8f, EndCoachDrive, null);
                }
                break;

            default:
                if (coachStep >= CdItem0 && coachStep < CdItem0 + CoachItems.Length)
                    TickCoachItem(coachStep - CdItem0, steer);
                else
                    NextCoachStep();
                break;
        }
    }

    /// <summary>One item lesson: lay it on the road, show it as it arrives, wait for the pickup.</summary>
    void TickCoachItem(int item, float steer)
    {
        var type = CoachItems[item];

        if (coachStage == 0)
        {
            // a few car-widths to one side, so it has to be steered for
            float half = track.roadWidth * 0.5f - 2f;
            float lat = car.LateralOffset + coachItemSide * 3.2f;
            if (Mathf.Abs(lat) > half) lat = car.LateralOffset - coachItemSide * 3.2f;
            float dist = car.DistanceTraveled + 95f;
            if (track.PlacePowerUp(dist, lat, type))
            {
                coachItemDist = dist;
                coachItemGot = false;
                coachItemSide = -coachItemSide;
                SetCoachStage(1);
            }
            return;
        }

        if (coachStage == 4)
        {
            if (coachStageT > 2.5f)
            {
                coachItemTries = 0;
                if (type == TrackGenerator.PowerUpType.Springs) { coachStep = CdJump - 1; }
                NextCoachStep();
            }
            return;
        }

        // picked up
        if (coachItemGot)
        {
            coachItemDist = -1f;
            CoachDemo(false);
            FingerHide();
            HideCoachBox();
            coachRing.gameObject.SetActive(false);
            SetCoachStage(4);
            return;
        }

        // gone past it: take it away and lay it out again
        bool onRoad = track.PowerUpAt(coachItemDist, out Vector3 itemPos);
        if (!onRoad || car.DistanceTraveled > coachItemDist + 4f)
        {
            track.RemovePowerUpAt(coachItemDist);
            coachItemDist = -1f;
            CoachDemo(false);
            FingerHide();
            coachRing.gameObject.SetActive(false);
            coachItemTries++;
            if (coachItemTries >= 3)
            {
                coachItemTries = 0;
                // springs skipped means no jump lesson either
                if (type == TrackGenerator.PowerUpType.Springs) coachStep = CdJump;
                NextCoachStep();
            }
            else SetCoachStage(0);
            return;
        }

        // mark it on screen
        bool seen = RingOnWorld(itemPos, out float itemScreenX);

        if (coachStage == 1)
        {
            if (coachItemDist - car.DistanceTraveled < 38f)
            {
                CoachDemo(true);
                ShowCoachBox(ItemNames[item], CoachItemText[item], "STEER INTO IT TO GRAB IT", CoachDriveBoxY);
                Vector3 carScreen = mainCam != null ? mainCam.WorldToScreenPoint(car.transform.position) : Vector3.zero;
                FingerHold(seen && itemScreenX < carScreen.x ? -1 : 1);
                SetCoachStage(2);
            }
        }
        else if (coachStage == 2)
        {
            if (steer > 0.25f) { CoachDemo(false); FingerHide(); SetCoachStage(3); }
        }
    }

    void EndCoachDrive()
    {
        coachDrive = false;
        coachPhase = CoachPhase.Tour;
        coachStep = 0;
        SetCoachStage(0);
        Time.timeScale = 1f;
        coachSlowTarget = 1f;
        coachGreyTarget = 0f;
        coachGrey = 0f;
        HideCoachBox();
        FingerHide();
        coachRing.gameObject.SetActive(false);
        audioMan.StopDriving();
        GoToMenu();
    }

    // ------------------------------------------------------------- the tour

    /// <summary>One menu button: tap it, see its screen explained, close it.</summary>
    struct TourStop
    {
        public GameObject button;
        public GameObject panel;
        public string name, tapText, screenTitle, screenText;
    }

    TourStop[] TourStops()
    {
        GameObject spin = spinMenuLabel != null ? ButtonOf(spinMenuLabel.transform) : null;
        return new[]
        {
            new TourStop { button = raceButtonGo, panel = racePanel, name = "RACE",
                tapText = "Race rivals through 20 levels.\nTap it to take a look.",
                screenTitle = "RACES",
                screenText = "Win a race to unlock the next one.\nUpgrades here make your car\nfaster and tougher." },
            new TourStop { button = spin, panel = wheelPanel, name = "SPIN",
                tapText = "Spin the mystery wheel for coins,\ntires and cars. Tap it!",
                screenTitle = "MYSTERY WHEEL",
                screenText = "You get a free spin every day.\nBuy more with tires or watch an ad." },
            new TourStop { button = garageButtonGo, panel = garagePanel, name = "GARAGE",
                tapText = "Your cars live here. Tap it!",
                screenTitle = "GARAGE",
                screenText = "Flip through the cars, unlock\nnew ones and pick the one you drive." },
            new TourStop { button = shopButtonGo, panel = shopPanel, name = "SHOP",
                tapText = "Tires, coins and item upgrades.\nTap it!",
                screenTitle = "SHOP",
                screenText = "Buy tires, trade tires for coins,\nand make your items last longer." },
            new TourStop { button = questsButtonGo, panel = questsPanel, name = "QUESTS",
                tapText = "Little goals for extra rewards.\nTap it!",
                screenTitle = "QUESTS",
                screenText = "Finish all 3 quests to raise\nyour score multiplier and win tires." },
            new TourStop { button = settingsButtonGo, panel = settingsPanel, name = "SETTINGS",
                tapText = "Sound, steering and language.\nTap the gear!",
                screenTitle = "SETTINGS",
                screenText = "Change the sound, steering\nsensitivity and language -\nor watch this tutorial again." },
        };
    }

    void TickCoachTour()
    {
        // started a run some other way (it can only be PLAY at the end)
        if (state != State.Menu) { CoachAbort(); return; }

        var stops = TourStops();
        int loginStep = 2 + stops.Length * 2;   // after the screens: LOGIN STREAK
        int last = loginStep + 1;

        // something else is up (a popup) - wait, greyed, until it is gone
        bool anyStopOpen = false;
        foreach (var s in stops) if (s.panel != null && s.panel.activeSelf) anyStopOpen = true;
        if (!anyStopOpen && !CoachMenuFree())
        {
            coachRoot.gameObject.SetActive(false);
            return;
        }
        ShowCoachRoot();

        // the end: TUTORIAL COMPLETE
        if (coachStep >= last)
        {
            if (coachStage == 0)
            {
                SetCoachStage(1);
                PlayCoachBanner("TUTORIAL\nCOMPLETE!", true, 6f, null, () =>
                {
                    CoachFinish();
                    // the daily reward that waited for the tutorial
                    if (loginPending) EnterMenu();
                });
            }
            return;
        }
        SetCoachGreyForMenu(true);

        // 0, 1: the counters, tap anywhere to go on
        if (coachStep == 0 || coachStep == 1)
        {
            bool coins = coachStep == 0;
            if (coachStage == 0)
            {
                if (coins)
                    ShowCoachBox("COINS", "You earn coins on every run.\nSpend them on cars, spins\nand upgrades.",
                                 "TAP TO CONTINUE", CoachMenuBoxY);
                else
                    ShowCoachBox("TIRES", "Tires bring you back after a crash\nand buy extra spins.\nGet more in the shop.",
                                 "TAP TO CONTINUE", CoachMenuBoxY);
                GameObject pill = coins ? (coinPillRt != null ? coinPillRt.gameObject : null)
                                        : (tirePillRt != null ? tirePillRt.gameObject : null);
                CoachHighlight(pill);
                PointAt(pill);
                SetCoachStage(1);
            }
            else if (coachTapped && coachStageT > 0.4f)
            {
                audioMan.PlayTap();
                AdvanceTour();
            }
            else KeepCoachTarget();
            return;
        }

        // LOGIN STREAK is pointed at but not opened: its popup comes up for
        // real, with the first day's reward, as soon as the tutorial is over
        if (coachStep == loginStep)
        {
            if (loginButtonGo == null) { AdvanceTour(); return; }
            if (coachStage == 0)
            {
                ShowCoachBox("LOGIN STREAK", "Come back every day for a reward.\nDay 7 wins you a car!",
                             "TAP TO CONTINUE", CoachMenuBoxY);
                CoachHighlight(loginButtonGo);
                PointAt(loginButtonGo);
                SetCoachStage(1);
            }
            else if (coachTapped && coachStageT > 0.4f)
            {
                audioMan.PlayTap();
                AdvanceTour();
            }
            else KeepCoachTarget();
            return;
        }

        int index = (coachStep - 2) / 2;
        bool inside = (coachStep - 2) % 2 == 1;
        TourStop stop = stops[index];

        // a button that is missing (should not happen) is skipped
        if (stop.button == null || stop.panel == null) { AdvanceTour(); return; }

        if (!inside)
        {
            if (coachStage == 0)
            {
                ShowCoachBox(stop.name, stop.tapText, null, CoachMenuBoxY);
                CoachHighlight(stop.button);
                PointAt(stop.button);
                SetCoachStage(1);
            }
            else if (stop.panel.activeSelf) AdvanceTour();
            else KeepCoachTarget();
        }
        else
        {
            GameObject close = CloseButtonOf(stop.panel);
            if (coachStage == 0)
            {
                ShowCoachBox(stop.screenTitle, stop.screenText, "TAP THE X TO GO BACK", CoachScreenBoxY);
                CoachHighlight(close);
                PointAt(close);
                SetCoachStage(1);
            }
            else if (!stop.panel.activeSelf) AdvanceTour();
            else KeepCoachTarget();
        }
    }

    /// <summary>
    /// A banner the way a race finish shows your placing: black bars slam
    /// shut, the screen goes dark, and the words slide in, in gold. After
    /// <paramref name="hold"/> seconds (or a tap, if it can be tapped away) it
    /// opens back up. <paramref name="onDark"/> runs the moment the screen is
    /// fully dark - anything changed then is never seen changing.
    /// </summary>
    void PlayCoachBanner(string words, bool tapToContinue, float hold,
                         System.Action onDark, System.Action onDone)
    {
        ShowCoachRoot();
        coachGroup.alpha = 1f;
        CoachClearHighlight();
        HideCoachBox();
        FingerHide();
        if (coachRing != null) coachRing.gameObject.SetActive(false);
        // clear, but catching taps - nothing under it can be pressed
        coachDim.color = new Color(0f, 0f, 0f, 0f);
        coachDim.raycastTarget = true;

        bannerT = 0f;
        bannerCloseT = -1f;
        bannerHold = hold;
        bannerTap = tapToContinue;
        bannerDarkFired = false;
        bannerOnDark = onDark;
        bannerOnDone = onDone;

        coachDoneBlack.gameObject.SetActive(true);
        coachDoneTop.gameObject.SetActive(true);
        coachDoneBottom.gameObject.SetActive(true);
        coachDoneTextRt.gameObject.SetActive(true);
        coachDoneText.text = words;
        coachDoneHint.gameObject.SetActive(tapToContinue);
        if (tapToContinue) coachDoneHint.text = "TAP TO CONTINUE";
        audioMan.PlayFinishWhoosh();
        AnimateCoachDone();
    }

    /// <summary>Runs the banner. True while it is up.</summary>
    bool TickCoachBanner(float udt)
    {
        if (bannerT < 0f) return false;

        if (bannerCloseT < 0f)
        {
            bannerT += udt;
            if (!bannerDarkFired && bannerT >= 0.35f)
            {
                bannerDarkFired = true;
                var dark = bannerOnDark;
                bannerOnDark = null;
                if (dark != null) dark();
                // whatever changed behind it, the layer stays up and dark
                ShowCoachRoot();
                coachGroup.alpha = 1f;
                coachDim.color = new Color(0f, 0f, 0f, 0f);
                coachDim.raycastTarget = true;
            }
            bool open = bannerTap ? (coachTapped && bannerT > 1.2f) || bannerT > bannerHold
                                  : bannerT > bannerHold;
            if (open) bannerCloseT = 0f;
        }
        else
        {
            bannerCloseT += udt;
            if (bannerCloseT >= 0.45f)
            {
                var then = bannerOnDone;
                HideCoachDone();
                coachDim.raycastTarget = false;
                if (then != null) then();
                return false;
            }
        }
        AnimateCoachDone();
        return true;
    }

    void AnimateCoachDone()
    {
        float t = bannerT;
        float open = bannerCloseT < 0f ? 1f : 1f - Mathf.Clamp01(bannerCloseT / 0.45f);

        // the bars slam shut and the screen goes dark
        float shut = Mathf.Clamp01(t / 0.25f) * open;
        float h = Mathf.Lerp(0f, 1200f, shut) + PanelBleed.y;
        coachDoneTop.rectTransform.sizeDelta = new Vector2(400f, h);
        coachDoneBottom.rectTransform.sizeDelta = new Vector2(400f, h);
        coachDoneBlack.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / 0.3f) * 0.96f * open);

        // the words slide in from the side once it is dark, and out the other way
        float p = Mathf.Clamp01((t - 0.3f) / 0.45f);
        float eased = 1f - Mathf.Pow(1f - p, 3f);
        float leave = 1f - open;
        coachDoneTextRt.anchoredPosition = new Vector2(Mathf.Lerp(900f, 0f, eased) - 900f * leave * leave, 60f);
        coachDoneTextRt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, eased);
        coachDoneText.color = new Color(1f, 0.82f, 0.12f, Mathf.Clamp01(p * 2f) * open);

        if (bannerTap)
        {
            float hint = Mathf.Clamp01((t - 1.2f) / 0.4f) * open * (0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 4f));
            coachDoneHint.color = new Color(1f, 1f, 1f, hint);
        }
    }

    void AdvanceTour()
    {
        coachStep++;
        SetCoachStage(0);
        CoachClearHighlight();
        HideCoachBox();
        FingerHide();
    }

    static GameObject CloseButtonOf(GameObject panel)
    {
        if (panel == null) return null;
        Transform t = panel.transform.Find("CloseBtn");
        return t != null ? t.gameObject : null;
    }

    static GameObject ButtonOf(Transform t)
    {
        var b = t != null ? t.GetComponentInParent<Button>(true) : null;
        return b != null ? b.gameObject : null;
    }

    // --------------------------------------------------------- the greyed menu

    void SetCoachGreyForMenu(bool menu)
    {
        coachGroup.alpha = 1f;
        coachDim.raycastTarget = menu;
        if (menu)
        {
            coachGrey = coachGreyTarget = 1f;
            coachDim.color = new Color(0.04f, 0.04f, 0.06f, 0.62f);
        }
    }

    /// <summary>Lifts one button above the grey - the only thing that can be pressed.</summary>
    void CoachHighlight(GameObject go)
    {
        if (go == coachTarget) { KeepCoachTarget(); return; }
        CoachClearHighlight();
        if (go == null || !go.activeInHierarchy) return;
        coachTarget = go;
        coachTargetCanvas = go.GetComponent<Canvas>();
        if (coachTargetCanvas != null) { coachTarget = null; coachTargetCanvas = null; return; }
        coachTargetCanvas = go.AddComponent<Canvas>();
        coachTargetCanvas.overrideSorting = true;
        coachTargetCanvas.sortingOrder = 310;
        if (go.GetComponent<Selectable>() != null) coachTargetRay = go.AddComponent<GraphicRaycaster>();
    }

    /// <summary>A canvas only keeps its own draw order while it is live - re-assert it.</summary>
    void KeepCoachTarget()
    {
        if (coachTargetCanvas == null) return;
        if (!coachTargetCanvas.overrideSorting) coachTargetCanvas.overrideSorting = true;
        if (coachTargetCanvas.sortingOrder != 310) coachTargetCanvas.sortingOrder = 310;
    }

    void CoachClearHighlight()
    {
        if (coachTargetRay != null) DestroyImmediate(coachTargetRay);
        if (coachTargetCanvas != null) DestroyImmediate(coachTargetCanvas);
        coachTargetRay = null;
        coachTargetCanvas = null;
        coachTarget = null;
    }

    // --------------------------------------------------------------- text box

    void ShowCoachBox(string title, string body, string hint, float y)
    {
        coachBox.gameObject.SetActive(true);
        coachTitle.text = title;
        coachBody.text = body;
        bool hasHint = !string.IsNullOrEmpty(hint);
        coachHint.gameObject.SetActive(hasHint);
        if (hasHint) coachHint.text = hint;
        coachBoxY = y;
        LayoutCoachBox();
        coachBoxPopT = 0f;
    }

    void HideCoachBox()
    {
        if (coachBox != null) coachBox.gameObject.SetActive(false);
    }

    /// <summary>Sizes the box to its text: title, body, and the grey hint line if there is one.</summary>
    void LayoutCoachBox()
    {
        const float Pad = 26f, Gap = 10f;
        float th = Mathf.Max(40f, coachTitle.preferredHeight);
        float bh = Mathf.Max(30f, coachBody.preferredHeight);
        bool hasHint = coachHint.gameObject.activeSelf;
        float hh = hasHint ? Mathf.Max(28f, coachHint.preferredHeight) : 0f;
        float h = Pad + th + Gap + bh + (hasHint ? Gap + hh : 0f) + Pad;

        coachBox.anchoredPosition = new Vector2(0f, coachBoxY);
        coachBox.sizeDelta = new Vector2(BoxWidth, h);
        coachBoxBorder.rectTransform.sizeDelta = new Vector2(BoxWidth + 14f, h + 14f);
        coachBoxFill.rectTransform.sizeDelta = new Vector2(BoxWidth, h);

        float top = h * 0.5f - Pad;
        SetCoachLine(coachTitle, top - th * 0.5f, th);
        top -= th + Gap;
        SetCoachLine(coachBody, top - bh * 0.5f, bh);
        top -= bh + Gap;
        if (hasHint) SetCoachLine(coachHint, top - hh * 0.5f, hh);
    }

    static void SetCoachLine(Text t, float y, float h)
    {
        var rt = t.rectTransform;
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(BoxWidth - 50f, h);
    }

    void HideCoachUi()
    {
        HideCoachDone();
        HideCoachBox();
        FingerHide();
        if (coachRing != null) coachRing.gameObject.SetActive(false);
        if (coachRipple != null) coachRipple.gameObject.SetActive(false);
        coachSlowTarget = 1f;
        coachGreyTarget = 0f;
        coachGrey = 0f;
        if (coachDim != null)
        {
            coachDim.color = new Color(0f, 0f, 0f, 0f);
            coachDim.raycastTarget = false;
        }
        if (coachRoot != null) coachRoot.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------- finger

    void FingerHide()
    {
        fingerMode = FingerMode.Hidden;
        fingerAim = null;
        if (coachFinger != null) coachFinger.gameObject.SetActive(false);
        if (coachRipple != null) coachRipple.gameObject.SetActive(false);
    }

    void PointAt(GameObject target)
    {
        fingerAim = target != null ? target.transform as RectTransform : null;
        fingerMode = fingerAim != null ? FingerMode.Tap : FingerMode.Hidden;
        fingerT = 0f;
    }

    void FingerSlide() { fingerMode = FingerMode.SlideLR; fingerT = 0f; }
    void FingerHold(int side) { fingerMode = FingerMode.Hold; fingerSide = side; fingerT = 0f; }
    void FingerSwipeUp() { fingerMode = FingerMode.SwipeUp; fingerT = 0f; }

    /// <summary>
    /// Puts the fingertip at <paramref name="tip"/>. The hand normally comes in
    /// from the lower right; where that would run it off the bottom of the
    /// screen it turns to come in from above, and where it would run off the
    /// right-hand side it is mirrored.
    /// </summary>
    Vector2 PlaceFinger(Vector2 tip, float alpha, float scale)
    {
        Rect area = coachRoot.rect;
        bool fromAbove = tip.y < area.yMin + 240f;
        bool mirrored = !fromAbove && tip.x > area.xMax - 210f;
        coachFingerRt.localRotation = Quaternion.Euler(0f, 0f, fromAbove ? 90f : 0f);
        coachFingerRt.localScale = new Vector3(mirrored ? -scale : scale, scale, 1f);
        coachFingerRt.anchoredPosition = tip;
        coachFinger.color = new Color(1f, 1f, 1f, alpha);
        if (!coachFinger.gameObject.activeSelf) coachFinger.gameObject.SetActive(true);

        // the way the hand lies from its tip, for the tapping bob
        Vector2 away = new Vector2(0.72f, -0.70f);
        if (fromAbove) away = new Vector2(0.70f, 0.72f);
        else if (mirrored) away = new Vector2(-0.72f, -0.70f);
        return away;
    }

    /// <summary>Where a UI element sits in the tutorial layer.</summary>
    Vector2 CoachLocal(RectTransform rt, Vector2 offsetFrac)
    {
        Rect r = rt.rect;
        Vector3 local = new Vector3(r.center.x + r.width * offsetFrac.x, r.center.y + r.height * offsetFrac.y, 0f);
        Vector3 world = rt.TransformPoint(local);
        return coachRoot.InverseTransformPoint(world);
    }

    /// <summary>Rings a spot on the road; false if it is not on screen.</summary>
    bool RingOnWorld(Vector3 world, out float screenX)
    {
        screenX = 0f;
        if (mainCam == null) return false;
        Vector3 sp = mainCam.WorldToScreenPoint(world);
        if (sp.z <= 0f) { coachRing.gameObject.SetActive(false); return false; }
        screenX = sp.x;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(coachRoot, sp, null, out Vector2 local);
        coachRing.gameObject.SetActive(true);
        coachRingRt.anchoredPosition = local;
        float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
        coachRingRt.sizeDelta = new Vector2(150f, 150f) * pulse;
        return true;
    }

    void AnimateCoach(float udt)
    {
        fingerT += udt;

        // the box pops in
        if (coachBox != null && coachBox.gameObject.activeSelf)
        {
            coachBoxPopT = Mathf.Min(1f, coachBoxPopT + udt / 0.18f);
            float s = Mathf.Lerp(0.86f, 1f, 1f - (1f - coachBoxPopT) * (1f - coachBoxPopT));
            coachBox.localScale = new Vector3(s, s, 1f);
        }

        if (coachFinger == null || coachFinger.texture == null) return;

        switch (fingerMode)
        {
            case FingerMode.Hidden:
                break;

            case FingerMode.Tap:
            {
                if (fingerAim == null || !fingerAim.gameObject.activeInHierarchy) { coachFinger.gameObject.SetActive(false); break; }
                Vector2 tip = CoachLocal(fingerAim, new Vector2(0.12f, -0.12f));
                float phase = Mathf.Repeat(fingerT, 1.3f) / 1.3f;
                float press = Mathf.Clamp01(1f - Mathf.Abs(phase - 0.4f) / 0.18f);
                Vector2 away = PlaceFinger(tip, 1f, 1f - 0.08f * press);
                coachFingerRt.anchoredPosition = tip + away * (40f * (1f - press));
                // a ripple where it taps
                if (phase >= 0.4f)
                {
                    float r = (phase - 0.4f) / 0.6f;
                    coachRipple.gameObject.SetActive(true);
                    coachRippleRt.anchoredPosition = tip;
                    coachRippleRt.sizeDelta = Vector2.one * (60f + 170f * r);
                    coachRipple.color = new Color(1f, 1f, 1f, 0.9f * (1f - r));
                }
                else coachRipple.gameObject.SetActive(false);
                break;
            }

            case FingerMode.SlideLR:
            {
                float x = 290f * Mathf.Sin(fingerT * 2.4f);
                PlaceFinger(new Vector2(x, FingerRowY), 1f, 0.94f);
                break;
            }

            case FingerMode.Hold:
            {
                float phase = Mathf.Repeat(fingerT, 2.2f) / 2.2f;
                float x = fingerSide * 300f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, phase));
                float a = Mathf.Clamp01(phase / 0.08f) * Mathf.Clamp01((1f - phase) / 0.1f);
                PlaceFinger(new Vector2(x, FingerRowY), a, 0.94f);
                break;
            }

            case FingerMode.SwipeUp:
            {
                float phase = Mathf.Repeat(fingerT, 1.3f) / 1.3f;
                float y = Mathf.Lerp(FingerRowY - 120f, FingerRowY + 200f,
                                     Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.4f, phase)));
                float a = Mathf.Clamp01(phase / 0.08f) * Mathf.Clamp01((0.7f - phase) / 0.25f);
                PlaceFinger(new Vector2(0f, y), a, 0.94f);
                break;
            }
        }
    }
}
