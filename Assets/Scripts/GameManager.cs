using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runs the whole game: menu -> playing -> crash -> instant retry.
/// Builds its own UI at runtime, so the only scene setup needed is:
/// an empty GameObject with this component, and a PlayerCar with CarController.
/// </summary>
public partial class GameManager : MonoBehaviour
{
    enum State { Menu, Playing, Paused, Rewinding, ReviveOffer, GameOver }
    enum Mode { Endless, Race }

    [Header("Scoring")]
    // a constant for the same reason as the speed values below
    float pointsPerMeter { get { return PointsPerMetre; } }
    [Tooltip("How fast the car cruises behind the main menu.")]
    public float lobbyCruiseSpeed = 11f;
    [Tooltip("Road kept behind the car in the lobby - the camera looks back " +
             "down it, so it must not be pruned in view.")]
    public float lobbyBehindDistance = 260f;
    [Tooltip("Road kept behind the car during a run.")]
    public float runBehindDistance = 110f;
    public float driftPointsPerSecond = 30f;
    public int nearMissBonus = 150;

    [Header("Economy")]
    [Tooltip("Score needed per bonus coin awarded at the end of a run.")]
    public float scorePerBonusCoin = 35f;
    [Tooltip("Bonus coins are reduced by this fraction for each revive used.")]
    [Range(0f, 1f)] public float revivePenalty = 0.35f;

    [Header("Look")]
    public Color daySkyColor = new Color(0.42f, 0.70f, 0.96f); // clear blue
    public float fogStart = 110f;
    public float fogEnd = 245f;

    [Header("Race")]
    [Tooltip("Extra yaw applied to rival car models if they face the wrong way.")]
    public float racerYawOffset = -90f;
    [Tooltip("Road width during races - wide enough for three lanes.")]
    public float raceRoadWidth = 16f;
    [Tooltip("Constant speed a stock car races at.")]
    public float raceBaseSpeed = 24f;
    /// <summary>Models authored nose-backwards - they get turned around.</summary>
    static readonly string[] BackwardsModels =
        { "VORTEX HATCH", "TENSAI SPRINT", "STRIKER", "HARWICK GT" };

    /// <summary>
    /// Per-model height corrections, in metres. A model whose bounds include
    /// something below the wheels gets seated too high, and this pulls it back
    /// down. Tune the numbers if a car still sits wrong.
    /// </summary>
    // Empty now. The VORTEX HATCH entry (-0.32) was covering for a part whose
    // world box reached half a metre below the road; cars are seated on their
    // measured tyres now, so that correction would sink it.
    static readonly string[] HeightFixNames = { };
    static readonly float[] HeightFixValues = { };

    static float HeightFixFor(string carName)
    {
        int i = System.Array.IndexOf(HeightFixNames, carName);
        return i >= 0 ? HeightFixValues[i] : 0f;
    }
    public float endlessRoadWidth = 11f;

    // ---------------------------------------------------------------- tuning
    // These are CONSTANTS, not inspector fields, deliberately. Unity saves a
    // copy of every public field into the scene, and that saved copy wins over
    // whatever the code says - so editing a public default here would silently
    // do nothing on an object that already exists in the scene. Anything that
    // has to be reliably tunable from code lives here instead.

    /// <summary>Speed at the start of an endless run, in m/s.</summary>
    // 45 mph of real travel, which the speedometer shows as about 70 - and
    // leaves real room to climb, rather than opening a hair under the cap.
    const float RunBaseSpeed = 20.3f;
    /// <summary>
    /// Top speed of an endless run before car bonuses. Pulled down from 96:
    /// starting at 55 and capping ten minutes later, a 96 top would mean
    /// climbing over 4 mph a minute - twice the old rate, not slower. A 78 cap
    /// keeps the climb gentle AND gets there in ten minutes.
    /// </summary>
    const float RunMaxSpeed = 34.87f;   // 78 mph, shown as 120
    /// <summary>
    /// Derived, not typed in: whatever the two speeds above are, the car
    /// climbs between them at a constant rate and arrives exactly ten minutes
    /// into the run. Change either speed and the timing still holds.
    /// </summary>
    const float RunSpeedGain = (RunMaxSpeed - RunBaseSpeed) / 600f;


    /// <summary>Score per metre travelled, before multipliers.</summary>
    const float PointsPerMetre = 0.09f;

    /// <summary>
    /// Sideways speed, in m/s, at which a slide starts counting as a drift and
    /// building combo. Well under the old 6.5: a tidy line through a corner is
    /// a drift now, rather than only a deliberate kick of the tail.
    /// </summary>
    const float DriftScoreThreshold = 1.3f;

    /// <summary>Combo points below this are banked quietly, with no fanfare.</summary>
    const int DriftFlashMin = 8;

    /// <summary>
    /// Points a second while drifting, before the multiplier. Dropped from 30:
    /// the threshold for what counts as a drift is now so low that the car is
    /// scoring for most of a corner, and 30 a second on top of an x8
    /// multiplier made everything else in the run irrelevant.
    /// </summary>
    const float DriftPointsPerSecond = 11f;

    /// <summary>
    /// How fast the thumb's position reaches the car. Higher is more direct
    /// and less filtered - the input lag was part of what felt like the car
    /// being guided rather than steered.
    /// </summary>
    /// <summary>
    /// Back to the value the game shipped with. Sharpening this was meant to
    /// feel more direct and instead just let every small wobble through.
    /// </summary>
    const float SteerSmoothing = 19f;

    /// <summary>
    /// How quickly the car's slide settles to where the thumb is - the
    /// stiffness of the spring the slide follows. Half-way in about a quarter
    /// of a second, and it eases in and out of every change, so a
    /// left-to-right flick swings through smoothly instead of jolting.
    /// </summary>
    // 14, up from 10: recorded driving showed the slide trailing the thumb by
    // about a quarter of a second. Still eases in and out of every change.
    // 16 now: a touch more responsive again, asked for after playtesting.
    const float AngleSeek = 16f;

    /// <summary>
    /// How much quicker the sideways speed is allowed to build than the
    /// scene's CarController says (the sensitivity setting scales it from
    /// there). Goes with the AngleSeek bump: a tad more responsive.
    /// </summary>
    const float SteerAccelBoost = 1.12f;

    /// <summary>
    /// How gentle the steering is around the middle of the thumb's travel
    /// (see CarController.steerCenterSoftness): small and medium moves slide
    /// less, while a full slide of the thumb still gives the full drift.
    /// </summary>
    // 0.55: 0.45 was a bit too sensitive; 0.6 made small moves so gentle the
    // car felt on rails until the thumb went past a certain point. Full lock,
    // the drift angle and how quickly the car responds are unchanged.
    const float SteerCenterSoftness = 0.55f;

    /// <summary>
    /// How far the body turns for small and medium slides - pure look, the
    /// handling is untouched. The angle now grows smoothly with the slide all
    /// the way to full lock (see CarController.EaseSmallDrifts): small moves
    /// turn the body a little more than before, medium-to-big ones a little
    /// less, and full drifts keep their full angle.
    /// </summary>
    // share of the full angle the smallest slide gets
    const float SmallDriftAngleScale = 0.45f;
    // how much the nose turns into a bend by itself (was 0.45)
    const float CornerDriftVisual = 0.35f;

    /// <summary>
    /// How fast the body swings to the angle the physics is asking for. This
    /// is presentation, not handling - lower means the car rolls into and out
    /// of a drift rather than snapping between poses. 8.2, up from 6: the
    /// body was trailing the thumb by nearly half a second, which felt like
    /// delay. (CarController.bodyLead also has the body start turning with
    /// the thumb.)
    /// </summary>
    const float DriftRotationSpeed = 8.2f;

    // --- how the car is framed in the lobby
    /// <summary>0 is dead behind the car, 180 is dead in front of it.</summary>
    const float LobbyShowcaseAngle = 171f;
    /// <summary>Metres from the car. Further back shows more of the whole car.</summary>
    const float LobbyShowcaseRadius = 9.4f;
    const float LobbyShowcaseHeight = 1.42f;

    /// <summary>
    /// Logs what the generator is actually being told when a race starts.
    /// Settled: races were straight because a straight requested near the end
    /// of an endless run was never cleared when the race track was built (see
    /// TrackGenerator.Init). Left here, off, in case it is ever needed again.
    /// </summary>
    const bool RaceTrackDebug = false;

    /// <summary>
    /// How far off the road a hover car floats, in metres. (It used to be
    /// cancelled out by the way the car was seated, so it sat on the road
    /// whatever this said; it now means what it says.)
    /// </summary>
    const float HoverHeight = 0.8f;


    /// <summary>
    /// Shown in the corner of the lobby. Read from Unity's own version number
    /// (Project Settings > Player > Version), so the two can never disagree -
    /// change it there, not here.
    /// </summary>
    static string GameVersion { get { return "V" + Application.version; } }



    // kept as properties so the rest of the file reads the same as before
    float runSpeedGain { get { return RunSpeedGain; } }
    float runBaseSpeed { get { return RunBaseSpeed; } }
    float runMaxSpeed { get { return RunMaxSpeed; } }

    [Header("Night city")]
    [Tooltip("Score at which the city is fully established.")]
    public int nightCityScore = 10000;   // kept for the old inspector layout
    [Tooltip("Seconds into a run when the sunset starts creeping in.")]
    public float sunsetAtSeconds = 50f;
    [Tooltip("Seconds into a run when the city is fully established.")]
    public float cityAtSeconds = 110f;
    [Tooltip("Seconds into a run when the snowy mountains take over.")]
    public float snowAtSeconds = 200f;
    [Tooltip("Fraction of that score where the sunset begins.")]
    [Range(0.1f, 0.95f)] public float sunsetStartFraction = 0.45f;
    public Color sunsetSkyColor = new Color(0.98f, 0.45f, 0.22f); // warm orange
    public Color duskSkyColor = new Color(0.35f, 0.20f, 0.42f);   // purple dusk
    public Color nightSkyColor = new Color(0.045f, 0.05f, 0.11f);
    public float nightFogStart = 70f;
    public float nightFogEnd = 190f;

    [Header("Snow mountains")]
    [Tooltip("Score at which the snowy mountain range is fully established.")]
    public int snowScore = 30000;
    [Range(0.1f, 0.95f)] public float snowStartFraction = 0.6f;
    public Color dawnSkyColor = new Color(0.55f, 0.62f, 0.78f);   // cold morning
    public Color snowSkyColor = new Color(0.74f, 0.82f, 0.92f);   // pale overcast
    public float snowFogStart = 60f;
    public float snowFogEnd = 210f;

    State state;
    TrackGenerator track;
    CarController car;
    CameraFollow camFollow;
    AudioManager audioMan;

    float score;
    float pendingDrift;   // combo points building during a drift chain
    float prevDriftTime;
    float lastCoinSweepDist;
    int best;
    Vector3 startPos;
    float startYaw;
    Camera mainCam;
    float bonusShownAt = -10f;
    Vector2 bonusBasePos;
    const float BonusDuration = 1.2f;

    Font uiFont;
    bool usingCustomFont;
    Transform uiRootCanvas;
    // display fonts render wide - shrink everything uniformly
    const float FontScale = 0.62f;

    Text scoreText, bestText, speedText, centerText, bonusText, driftText;
    GameObject menuPanel, settingsPanel, gameOverPanel, garagePanel, pausePanel, pauseButton;
    bool settingsFromPause;
    Text menuBestText, coinHudText;
    Text shopCarName, shopStats, shopPrice, shopActionLabel, shopCoinsText;
    float volumeSetting, sensSetting;
    float volMusic, volEngine, volDrift, volCoins, volSfx = 1f;
    bool useMph = true;
    float baseSteerAccel;
    int invertSteer;
    Text invertBtnLabel, unitsBtnLabel, languageBtnLabel;
    GameObject languagePanel;
    static bool reopenSettingsAfterReload;

    /// <summary>
    /// For the game's name: it stays in its own lettering whatever the
    /// language, like a logo.
    /// </summary>
    void KeepGameFont(Text t, int size)
    {
        t.font = uiFont;
        t.fontSize = Mathf.RoundToInt(size * FontScale);
        t.fontStyle = usingCustomFont ? FontStyle.Bold : FontStyle.BoldAndItalic;
    }

    /// <summary>
    /// The list of languages, each written in its own language and lettering,
    /// over the settings screen.
    /// </summary>
    void BuildLanguagePanel(Transform uiRoot)
    {
        languagePanel = MakePanel(uiRoot, "LanguagePanel");

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(languagePanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;
        // tapping outside the list closes it
        var dimBtn = dimGo.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(CloseLanguages);

        MakeCard(languagePanel.transform, new Vector2(0f, 0f), new Vector2(980f, 1180f),
            new Color(0.13f, 0.09f, 0.2f, 0.97f));
        var title = MakeText(languagePanel.transform, "LanguageTitle", 70, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 500f), new Vector2(900f, 100f));
        title.text = "LANGUAGE";
        title.color = new Color(1f, 0.72f, 0.12f);

        const float ColX = 225f;
        const float Top = 360f;
        const float Step = 124f;
        for (int i = 0; i < Loc.Count; i++)
        {
            var lang = (Loc.Lang)i;
            int row = i / 2;
            bool last = i == Loc.Count - 1 && Loc.Count % 2 == 1;
            float x = last ? 0f : (i % 2 == 0 ? -ColX : ColX);
            bool chosen = lang == Loc.Current;
            var label = MakeButton(languagePanel.transform, "", 40,
                new Vector2(x, Top - row * Step), new Vector2(420f, 104f),
                () => PickLanguage(lang),
                chosen ? new Color(0.30f, 0.62f, 0.35f) : (Color?)null);

            // each name in its own lettering, whatever the current language
            Font own = Loc.DisplayFontFor(lang);
            label.font = own != null ? own : uiFont;
            label.fontStyle = own != null ? FontStyle.Normal
                            : usingCustomFont ? FontStyle.Bold : FontStyle.BoldAndItalic;
            label.fontSize = Mathf.RoundToInt(40 * FontScale * Loc.SizeScale(lang));
            var shown = label as LocText;
            string name = Loc.Display(Loc.NativeName(lang), lang);
            if (shown != null) shown.SetShown(name);
            else label.text = name;

            // a language whose font is missing cannot be picked
            if (!Loc.Usable(lang))
                label.transform.parent.GetComponent<Button>().interactable = false;
        }

        // inside the card with room to spare: the card ends at -590, the last
        // row of names at -312
        MakeCloseButton(languagePanel.transform, new Vector2(0f, -475f), CloseLanguages);

        languagePanel.SetActive(false);
    }

    void OpenLanguages()
    {
        if (languagePanel == null) return;
        languagePanel.SetActive(true);
        languagePanel.transform.SetAsLastSibling();
        audioMan.PlayPop();
    }

    void CloseLanguages()
    {
        if (languagePanel != null) languagePanel.SetActive(false);
        audioMan.PlayTap();
    }

    /// <summary>
    /// Saves the language and reloads the scene, so every screen is rebuilt in
    /// it. Only reachable from the menu, so no run is lost; the player comes
    /// back to the settings screen they were on.
    /// </summary>
    void PickLanguage(Loc.Lang lang)
    {
        if (lang == Loc.Current) { CloseLanguages(); return; }
        Loc.Set(lang);
        PlayerPrefs.Save();
        reopenSettingsAfterReload = true;
        Time.timeScale = 1f;
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
    }

    // ------------------------------------------------------------- car shop

    // brand tokens: rare alternate currencies, one per exotic marque
    public enum Currency { Coins = 0, Cyber = 1, Tempasta = 2, Caldera = 3, Vettura = 4 }
    static readonly string[] TokenNames = { "COINS", "VOLT TOKENS", "TAURION TOKENS",
                                            "CALDERA TOKENS", "STELLARA TOKENS" };
    // Token art (index matches Currency). Flat 2D emblems rather than the
    // scanned 3D props these used to be: at the size a token is ever drawn -
    // an inch of phone screen - a silhouette reads instantly where a lit,
    // shaded model just reads as a dark blob.
    static readonly string[] TokenIcons = {
        "UI/token_coin", "UI/token_bolt", "UI/token_bull",
        "UI/token_key", "UI/token_horse" };

    // UI text colours per currency
    static readonly Color[] TokenColors = {
        new Color(1f, 0.82f, 0.1f),    // coins - gold
        new Color(1f, 0.9f, 0.2f),     // cyber - yellow bolt
        new Color(0.85f, 0.6f, 0.3f),  // tempasta - bronze bull
        new Color(1f, 0.45f, 0.75f),   // caldera - pink keys
        new Color(0.75f, 0.75f, 0.8f), // vettura - black horse (light text)
    };

    /// <summary>
    /// A brand token is only worth winning while a car still needs it. Once
    /// every car of that marque is in the garage the token buys nothing, so
    /// the wheel and the boxes stop handing it out and pay coins instead.
    /// </summary>
    bool TokenStillUseful(Currency c)
    {
        if (c == Currency.Coins) return true;
        for (int i = 0; i < Cars.Length; i++)
        {
            if (Cars[i].currency != c) continue;
            if (DevOnlyCar(i)) continue;
            if (!OwnedCar(i)) return true;
        }
        return false;
    }

    /// <summary>What a retired token pays out instead.</summary>
    int CoinsInsteadOfTokens(int tokens) { return Mathf.Max(500, tokens * 600); }

    int GetToken(Currency c)
    {
        return c == Currency.Coins ? totalCoins : PlayerPrefs.GetInt("Token" + (int)c, 0);
    }

    void SetToken(Currency c, int value)
    {
        if (c == Currency.Coins)
        {
            totalCoins = value;
            PlayerPrefs.SetInt("Coins", totalCoins);
        }
        else
        {
            PlayerPrefs.SetInt("Token" + (int)c, value);
        }
        PlayerPrefs.Save();
    }

    class CarDef
    {
        public string name;
        public string path;      // Resources path, null = original scene car
        public int cost;         // coins; -1 = login reward; -2 = real-money IAP
        public float speedBonus; // added to max speed (m/s)
        public float pointMult;  // multiplies all points earned
        public bool hover;       // floats + futuristic engine sound
        public string iapPrice;  // display price for IAP cars
        public float yaw;        // per-model facing correction (degrees)
        public Currency currency = Currency.Coins;

        public CarDef(string name, string path, int cost, float speedBonus, float pointMult,
            bool hover = false, string iapPrice = null, float yaw = 0f,
            Currency currency = Currency.Coins)
        {
            this.name = name; this.path = path; this.cost = cost;
            this.speedBonus = speedBonus; this.pointMult = pointMult;
            this.hover = hover; this.iapPrice = iapPrice; this.yaw = yaw;
            this.currency = currency;
        }
    }

    // cars are pure skins now - no stat differences, prestige pricing.
    // PackYaw corrects the new pack's models, which face 90 degrees off.
    const float PackYaw = 90f;

    static readonly CarDef[] Cars =
    {
        // index 0 is always the free starter car
        new CarDef("TENSAI SPRINT", "CarsFBX/TENSAI R6",           0,       0f, 1f, false, null, PackYaw),
        new CarDef("TRAILMASTER",   "Cars/Landyroamer",            2000,    0f, 1f),
        new CarDef("TUNDRO",        "Cars/Toyoyo",                 4500,    0f, 1f),
        new CarDef("HANSEN 92",     "CarsFBX/HANSEN EK",           9000,    0f, 1f, false, null, PackYaw),
        new CarDef("VORTEX HATCH",  "CarsFBX/VORTEX HATCH",        16000,   0f, 1f, false, null, PackYaw),
        new CarDef("TORINA CLUB",   "CarsFBX/TORINA R5",           32000,   0f, 1f, false, null, PackYaw),
        new CarDef("HAULER X",      "CarsFBX/CYBERHAUL",           140,     0f, 1f, false, null, PackYaw, Currency.Cyber),
        new CarDef("AUTEN QX",      "CarsFBX/AUTEN QX",            60000,   0f, 1f, false, null, PackYaw),
        new CarDef("STRIKER",       "CarsFBX/STRIKER SRT",         100000,  0f, 1f, false, null, PackYaw),
        new CarDef("OVERLAND V8",   "CarsFBX/OVERLAND V8",         130000,  0f, 1f, false, null, PackYaw),
        new CarDef("VELORA CX",     "CarsFBX/VELORA CX",           165000,  0f, 1f, false, null, PackYaw),
        new CarDef("STRATA GX",     "CarsFBX/STRATOS M5",          210000,  0f, 1f, false, null, PackYaw),
        new CarDef("NORDVEL 8",     "CarsFBX/BAVARIA E8",          260000,  0f, 1f, false, null, PackYaw),
        new CarDef("TAURION SUV",   "CarsFBX/TEMPASTA SUV",        180,     0f, 1f, false, null, PackYaw, Currency.Tempasta),
        new CarDef("CALDERA ELDARO","CarsFBX/CALDERA ELDARO",      90,      0f, 1f, false, null, PackYaw, Currency.Caldera),
        new CarDef("HARWICK GT",    "CarsFBX/BELLINGTON GT",       460000,  0f, 1f, false, null, PackYaw),
        new CarDef("REGENT NOIR",   "CarsFBX/ROYALE SHADOW",       560000,  0f, 1f, false, null, PackYaw),
        new CarDef("VELORA SPEED",  "CarsFBX/VELORA 911 CARRERA",  680000,  0f, 1f, false, null, PackYaw),
        new CarDef("MERIDIAN GT",   "CarsFBX/MARTON GT",           800000,  0f, 1f, false, null, PackYaw),
        new CarDef("ARGENT S",      "Cars/Tristar",                950000,  0f, 1f),
        new CarDef("TAURION EVO",   "CarsFBX/TEMPASTA EVO",        1100000, 0f, 1f, false, null, PackYaw),
        new CarDef("STELLARA V8",   "CarsFBX/VETTURA 458",         400,     0f, 1f, false, null, PackYaw, Currency.Vettura),
        // cost -3: not for sale and not even listed. Handed out with a code.
        new CarDef("FLUXWAY 88",    "Cars/DocLorean",              -3,      0f, 1f, true),
        // 7-day login reward - not purchasable at any price
        new CarDef("AUTEN RX",      "CarsFBX/AUTEN RX",            -1,      0f, 1f, false, null, PackYaw),
    };
    static int RewardCarIndex { get { return Cars.Length - 1; } }

    // ------------------------------------------------------- daily login
    /// <summary>What a day of the week's streak gives.</summary>
    enum LoginPrize { MysteryBox, Coins, TokenBox, Spin, Car }
    static readonly LoginPrize[] LoginPrizes =
    {
        LoginPrize.MysteryBox, LoginPrize.Coins, LoginPrize.TokenBox, LoginPrize.Coins,
        LoginPrize.Spin, LoginPrize.Coins, LoginPrize.Car,
    };
    // coins on the coin days; day 7's pays out only if the car is already yours
    static readonly int[] LoginRewards = { 0, 200, 0, 300, 0, 500, 1000 };
    const string LoginCarWonKey = "LoginCarWon";   // the date day 7 handed the car over
    bool loginPending;
    bool loginUnlocksCar;
    int loginStreakNew;
    int loginRewardCoins;
    GameObject loginPanel;
    Text loginDayText, loginRewardText;
    readonly Image[] loginCells = new Image[7];
    readonly Text[] loginCellTexts = new Text[7];
    readonly RawImage[] loginCellIcons = new RawImage[7];
    Image loginCellFrame, loginCarEdge;
    RawImage loginCarRays;
    int loginToday = -1;            // the cell that is today's, while it is unclaimed
    Text claimBtnLabel;
    bool loginAlreadyClaimed;
    static readonly Vector2 LoginCellSize = new Vector2(310f, 300f);
    static readonly Color LoginTodayColor = new Color(1f, 0.58f, 0.12f);
    static readonly Color LoginDoneColor = new Color(0.24f, 0.58f, 0.32f);
    static readonly Color LoginAheadColor = new Color(0.30f, 0.25f, 0.46f);
    static readonly Color LoginCarAheadColor = new Color(0.27f, 0.20f, 0.52f);
    /// <summary>The menu's LOGIN STREAK yellow - the same as SPIN's.</summary>
    static readonly Color LoginYellow = new Color(1f, 0.78f, 0.12f);

    // off-screen rigs that render the spinning coin / reward car into the popup cells
    const int ShowcaseLayer = 30;
    GameObject showcaseRoot;
    RenderTexture rewardCarRT;

    int totalCoins, coinsThisRun, selectedCar, shopIndex;
    GameObject garagePreview;
    float baseMaxSpeed;
    float carPointMult = 1f;

    // Bump this string to wipe every save on the next launch - useful for
    // testing first-run flow and the new-high-score reveal. It only fires once
    // per token, so a player never loses progress twice to the same bump.
    const string SaveResetToken = "reset-2026-10-02-second-reset";

    static void WipeSaveOnce()
    {
        if (PlayerPrefs.GetString("SaveResetToken", "") == SaveResetToken) return;
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetString("SaveResetToken", SaveResetToken);
        PlayerPrefs.Save();
        Debug.Log("[SAVE] progress reset (" + SaveResetToken + ")");
    }

    void Awake()
    {
        WipeSaveOnce();
        // paint stays off the cars while the feature is locked
        CarPaint.Enabled = !PaintLocked;
        // ads start loading as soon as the player has answered Apple's
        // tracking popup, which comes up as the game first opens - Apple's
        // rule: nothing that could track the player before that answer
        TrackingPrompt.WhenAnswered(AdManager.Init);
        // real-money purchases: prices come from the store, paid packs are
        // handed out here (also ones paid for before a crash or restart)
        IapStore.Init();
        IapStore.GrantHandler = GrantPurchase;
        IapStore.PricesChanged += RefreshPackPrices;
        Application.targetFrameRate = 60;
        Screen.orientation = ScreenOrientation.Portrait; // ignored in editor, applies on device
        best = PlayerPrefs.GetInt("HighScore", 0);
        volumeSetting = PlayerPrefs.GetFloat("Volume", 1f);
        sensSetting = PlayerPrefs.GetFloat("Sensitivity", 0.5f);
        volMusic = PlayerPrefs.GetFloat("VolMusic", 1f);
        volEngine = PlayerPrefs.GetFloat("VolEngine", 0.5f);
        volDrift = PlayerPrefs.GetFloat("VolDrift", 1f);
        volCoins = PlayerPrefs.GetFloat("VolCoins", 1f);
        invertSteer = PlayerPrefs.GetInt("InvertSteer", 0);
        volSfx = PlayerPrefs.GetFloat("VolSfx", 1f);
        useMph = PlayerPrefs.GetInt("UseMph", 1) == 1;
        totalCoins = PlayerPrefs.GetInt("Coins", 0);
        selectedCar = Mathf.Clamp(PlayerPrefs.GetInt("SelectedCar", 0), 0, Cars.Length - 1);
        LoadQuests();
        ComputeDailyLogin();
    }

    static string TodayKey() { return System.DateTime.Now.ToString("yyyyMMdd"); }

    /// <summary>Today's reward has not been collected yet.</summary>
    static bool LoginRewardWaiting()
    {
        return PlayerPrefs.GetString("LastLogin", "") != TodayKey();
    }

    /// <summary>1-7: where a streak is in its week.</summary>
    static int LoginDayOf(int streak) { return (Mathf.Max(1, streak) - 1) % 7 + 1; }

    void ComputeDailyLogin()
    {
        string today = TodayKey();
        string last = PlayerPrefs.GetString("LastLogin", "");
        if (last == today)
        {
            // already claimed - still show the popup once per app launch
            loginAlreadyClaimed = true;
            loginStreakNew = Mathf.Max(1, PlayerPrefs.GetInt("LoginStreak", 1));
            loginRewardCoins = 0;
            loginUnlocksCar = false;
            loginPending = true;
            return;
        }

        string yesterday = System.DateTime.Now.AddDays(-1).ToString("yyyyMMdd");
        int streak = PlayerPrefs.GetInt("LoginStreak", 0);
        loginStreakNew = last == yesterday ? streak + 1 : 1; // miss a day = start over
        int day = LoginDayOf(loginStreakNew);
        loginAlreadyClaimed = false;
        loginRewardCoins = LoginRewards[day - 1];
        loginUnlocksCar = day == 7 && !OwnedCar(RewardCarIndex);
        loginPending = true;
    }

    void ClaimLogin()
    {
        loginPending = false;
        if (loginAlreadyClaimed)
        {
            audioMan.PlayTap();
            CloseLoginPopup();
            return;
        }

        int day = LoginDayOf(loginStreakNew);
        PlayerPrefs.SetString("LastLogin", TodayKey());
        PlayerPrefs.SetInt("LoginStreak", loginStreakNew);
        loginAlreadyClaimed = true;
        CloseLoginPopup();

        // every day of the week gives something different, and each one is
        // handed over on its own full screen so it is seen arriving
        switch (LoginPrizes[day - 1])
        {
            case LoginPrize.MysteryBox:
                OpenMysteryBox();          // the shop's box screen, free today
                break;
            case LoginPrize.TokenBox:
                OpenTokenBox();
                break;
            case LoginPrize.Spin:
                SpinsLeft = SpinsLeft + 1;
                RefreshSpinBadge();
                ShowPrizeReveal(Loc.T("+1 SPIN"), false, null, wheelFaceTex);
                break;
            case LoginPrize.Car when loginUnlocksCar:
                PlayerPrefs.SetInt("CarOwned" + RewardCarIndex, 1);
                PlayerPrefs.SetString(LoginCarWonKey, TodayKey());
                // the car keeps turning on its stand behind the reveal
                BuildLoginShowcase();
                showcaseRoot.SetActive(true);
                ShowPrizeReveal(Loc.T("NEW CAR!"), false, null, rewardCarRT);
                break;
            default:
                // the coin days, and day 7 once the car is already in the garage
                totalCoins += loginRewardCoins;
                PlayerPrefs.SetInt("Coins", totalCoins);
                ShowPrizeReveal(Loc.F("+{0} COINS", loginRewardCoins), false, CoinIconPath);
                break;
        }
        PlayerPrefs.Save();
        RefreshWeeklyMenu();
        RefreshLoginButton();
    }

    void CloseLoginPopup()
    {
        loginToday = -1;
        if (loginPanel != null) loginPanel.SetActive(false);
        if (showcaseRoot != null) showcaseRoot.SetActive(false);
        // the shop's two boxes were only lit up for the week's cells
        if (shopShowcaseRoot != null && (shopPanel == null || !shopPanel.activeSelf))
            shopShowcaseRoot.SetActive(false);
    }

    /// <summary>The menu's LOGIN STREAK button: the week so far, and today's reward.</summary>
    void OpenLoginStreak()
    {
        // in the tutorial it is only pointed at - the tap just moves it on
        if (coachPhase != CoachPhase.None) { coachTapped = true; return; }
        if (loginPanel == null || loginPanel.activeSelf) return;
        ComputeDailyLogin();
        ShowLoginPopup();
    }

    /// <summary>Fills in the daily reward popup and brings it up.</summary>
    void ShowLoginPopup()
    {
        BuildLoginShowcase();
        showcaseRoot.SetActive(true);
        // the boxes in the week are the shop's own, drawn live
        EnsureShopShowcase();
        shopShowcaseRoot.SetActive(true);

        int day = LoginDayOf(loginStreakNew);
        loginDayText.text = Loc.F("DAY {0}", day)
            + (loginStreakNew > 7 ? "  " + Loc.F("(STREAK {0})", loginStreakNew) : "");
        loginRewardText.fontSize = Mathf.RoundToInt((loginAlreadyClaimed ? 44 : 62) * FontScale);
        loginRewardText.text = loginAlreadyClaimed ? "COME BACK TOMORROW!" : LoginPrizeLine(day);
        claimBtnLabel.text = loginAlreadyClaimed ? "OK" : "CLAIM";

        // day 7 shows the car while there is a car to win (or it was won today)
        bool showCar = !OwnedCar(RewardCarIndex)
            || (loginAlreadyClaimed && day == 7 && PlayerPrefs.GetString(LoginCarWonKey, "") == TodayKey());

        loginToday = loginAlreadyClaimed ? -1 : day - 1;
        for (int i = 0; i < 7; i++)
        {
            bool current = i == loginToday;
            bool past = i + 1 < day || (i + 1 == day && loginAlreadyClaimed);
            loginCells[i].color = current ? LoginTodayColor : past ? LoginDoneColor
                                : i == 6 ? LoginCarAheadColor : LoginAheadColor;
            loginCells[i].rectTransform.localScale = Vector3.one;
            loginCellTexts[i].color = Color.white;

            RawImage icon = loginCellIcons[i];
            LoginPrize prize = LoginPrizes[i];
            bool box = prize == LoginPrize.MysteryBox || prize == LoginPrize.TokenBox;
            icon.texture = LoginPicture(prize, showCar);
            // the box cameras frame the lid swinging open too - close in on the box
            icon.uvRect = box ? new Rect(0.15f, 0.05f, 0.7f, 0.7f) : new Rect(0f, 0f, 1f, 1f);
            icon.color = new Color(1f, 1f, 1f, past ? 0.6f : 1f);
        }
        // the car is not named - which one it is, is the surprise
        loginCellTexts[6].text = showCar ? Loc.T("NEW CAR!") : Loc.F("+{0} COINS", LoginRewards[6]);
        // the coins line is wider than NEW CAR!
        loginCellTexts[6].fontSize = Mathf.RoundToInt((showCar ? 66 : 50) * FontScale
                                                      * Loc.SizeScale(Loc.Current));
        if (loginCarRays != null) loginCarRays.gameObject.SetActive(showCar);
        loginCarEdge.rectTransform.localScale = Vector3.one;

        // today's cell wears a gold frame (day 7 always has its own)
        bool framed = loginToday >= 0 && loginToday < 6;
        loginCellFrame.gameObject.SetActive(framed);
        if (framed)
        {
            loginCellFrame.rectTransform.anchoredPosition =
                loginCells[loginToday].rectTransform.anchoredPosition;
            loginCellFrame.rectTransform.localScale = Vector3.one;
        }

        loginPanel.SetActive(true);
        if (audioMan != null) audioMan.PlayPop();
        RefreshLoginButton();
    }

    /// <summary>The picture in a day's cell.</summary>
    Texture LoginPicture(LoginPrize prize, bool showCar)
    {
        switch (prize)
        {
            case LoginPrize.MysteryBox: return toolboxRT;
            case LoginPrize.TokenBox: return tokenBoxRT;
            case LoginPrize.Spin: return wheelFaceTex;
            case LoginPrize.Car when showCar: return rewardCarRT;
            default: return Resources.Load<Texture2D>(CoinIconPath);
        }
    }

    /// <summary>The big line under the week: what today gives.</summary>
    string LoginPrizeLine(int day)
    {
        switch (LoginPrizes[day - 1])
        {
            case LoginPrize.MysteryBox: return Loc.T("MYSTERY BOX");
            case LoginPrize.TokenBox: return Loc.T("TOKEN BOX");
            case LoginPrize.Spin: return Loc.T("+1 SPIN");
            case LoginPrize.Car when loginUnlocksCar:
                return Loc.T("NEW CAR!");
            default: return Loc.F("+{0} COINS", loginRewardCoins);
        }
    }

    /// <summary>The label under a day's picture (days 1-6).</summary>
    static string LoginCellCaption(int index)
    {
        switch (LoginPrizes[index])
        {
            case LoginPrize.MysteryBox: return "MYSTERY BOX";
            case LoginPrize.TokenBox: return "TOKEN BOX";
            case LoginPrize.Spin: return "+1 SPIN";
            default: return "+" + LoginRewards[index];
        }
    }

    /// <summary>Today's cell breathes, and the rays behind the car turn.</summary>
    void TickLoginPopup()
    {
        if (loginPanel == null || !loginPanel.activeSelf) return;
        float t = Time.unscaledTime;
        if (loginCarRays != null && loginCarRays.gameObject.activeSelf)
            loginCarRays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 16f);
        if (loginToday < 0) return;
        Vector3 s = Vector3.one * (1f + Mathf.Sin(t * 4f) * 0.035f);
        loginCells[loginToday].rectTransform.localScale = s;
        if (loginToday < 6) loginCellFrame.rectTransform.localScale = s;
        else loginCarEdge.rectTransform.localScale = s;
    }

    // ---- the menu's LOGIN STREAK button: yellow, bouncing and dotted while
    //      today's reward waits, like SPIN is while a spin does
    Text loginMenuLabel;
    GameObject loginButtonGo, loginBadge;
    RectTransform loginButtonRt;
    Vector2 loginButtonHome;
    bool loginButtonLive;
    float loginButtonCheckT;

    void RefreshLoginButton()
    {
        if (loginButtonRt == null) return;
        loginButtonLive = LoginRewardWaiting();
        loginButtonCheckT = 0f;
        if (loginBadge != null) loginBadge.SetActive(loginButtonLive);
        var img = loginButtonRt.GetComponent<Image>();
        if (img != null) img.color = loginButtonLive ? LoginYellow : new Color(0.35f, 0.32f, 0.45f);
        loginMenuLabel.color = Color.white;
        if (!loginButtonLive)
        {
            loginButtonRt.anchoredPosition = loginButtonHome;
            loginButtonRt.localScale = Vector3.one;
            loginButtonRt.localRotation = Quaternion.identity;
        }
    }

    void AnimateLoginButton()
    {
        if (loginButtonRt == null) return;
        // midnight can pass with the game open: look again now and then
        loginButtonCheckT += Time.unscaledDeltaTime;
        if (loginButtonCheckT > 1f)
        {
            loginButtonCheckT = 0f;
            if (LoginRewardWaiting() != loginButtonLive) RefreshLoginButton();
        }
        if (!loginButtonLive) return;
        // half a beat after SPIN's, so the two take turns
        BounceButton(loginButtonRt, loginButtonHome, 1.3f);
    }

    void Start()
    {
        car = FindAnyObjectByType<CarController>();
        if (car == null)
        {
            Debug.LogError("GameManager: no CarController found in the scene. " +
                           "Add the CarController component to your PlayerCar object.");
            enabled = false;
            return;
        }

        track = FindAnyObjectByType<TrackGenerator>();
        if (track == null)
        {
            var go = new GameObject("TrackGenerator");
            track = go.AddComponent<TrackGenerator>();
        }

        mainCam = Camera.main;
        if (mainCam != null)
        {
            camFollow = mainCam.GetComponent<CameraFollow>();
            if (camFollow == null) camFollow = mainCam.gameObject.AddComponent<CameraFollow>();
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = daySkyColor;
        }

        CacheLighting();
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = daySkyColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        if (mainCam != null) mainCam.farClipPlane = Mathf.Max(150f, fogEnd + 25f);

        // the old hand-made menu is replaced by the runtime UI
        GameObject oldMenu = GameObject.Find("MainMenuPanel");
        if (oldMenu != null) oldMenu.SetActive(false);

        audioMan = FindAnyObjectByType<AudioManager>();
        if (audioMan == null) audioMan = gameObject.AddComponent<AudioManager>();
        audioMan.SetCar(car);

        BuildUi();

        startPos = car.transform.position;
        startYaw = car.transform.eulerAngles.y;
        // the very first road built is the menu backdrop, so it has to be
        // clear before it is generated - not cleaned up afterwards
        ApplyLobbyTrack();
        track.Init(startPos, startYaw);
        car.ResetRun(track);
        if (camFollow != null) camFollow.SetTarget(car.transform);

        // difficulty tuning lives here so it applies regardless of what the
        // scene's CarController has saved on it
        car.baseSpeed = runBaseSpeed;
        car.maxSpeed = runMaxSpeed;
        car.speedGainPerSecond = runSpeedGain;
        baseMaxSpeed = runMaxSpeed;

        // How far the car has to step out before the slide SCORES. Forced from
        // here for the same reason the speeds are: the value saved on the
        // scene's CarController wins over the one in the code, so editing the
        // field alone changes nothing.
        car.driftThreshold = DriftScoreThreshold;
        driftPointsPerSecond = DriftPointsPerSecond;

        // The thumb owns the angle. Both of these are saved on the scene's
        // CarController, so they have to be forced from here to take effect.
        car.steerSmoothing = SteerSmoothing;
        car.angleSeek = AngleSeek;
        car.steerCenterSoftness = SteerCenterSoftness;
        car.smallDriftAngleScale = SmallDriftAngleScale;
        car.cornerDriftVisual = CornerDriftVisual;
        car.driftRotationSpeed = DriftRotationSpeed;
        car.hoverHeight = HoverHeight;

        // Lobby framing, forced for the same reason as everything else here:
        // the CameraFollow in the scene has its own saved values and they win
        // over the ones written in the code.
        if (camFollow != null)
        {
            camFollow.showcaseAngle = LobbyShowcaseAngle;
            camFollow.showcaseRadius = LobbyShowcaseRadius;
            camFollow.showcaseHeight = LobbyShowcaseHeight;
        }

        // Speed and scoring are forced from code (see the constants above);
        // everything about how the car handles is left to the CarController.
        Debug.Log("[TUNING] speed " + runBaseSpeed + "->" + runMaxSpeed +
                  " gain " + runSpeedGain + " | points/m " + pointsPerMeter +
                  " | drift thr " + car.driftThreshold +
                  " angle " + car.driftAngle);
        baseSteerAccel = car.steerAcceleration * SteerAccelBoost;
        ApplySettings();
        EquipSelected();
        AssignCurrencyIcons();
        EnterMenu();

        // back from switching language: straight back to where the player was
        if (reopenSettingsAfterReload)
        {
            reopenSettingsAfterReload = false;
            OpenSettings();
            SetSettingsTab(1);
        }
    }

    // Flat art everywhere a coin or a tyre is used as an ICON. The 3D models
    // are kept for the things that are actually objects in the world: the
    // pickups on the road, and the spinning stacks you buy in the shop.
    const string CoinIconPath = "UI/wheel_coins";
    const string TireIconPath = "UI/wheel_tires";

    void AssignCurrencyIcons()
    {
        Texture2D coin = Resources.Load<Texture2D>(CoinIconPath);
        Texture2D tire = Resources.Load<Texture2D>(TireIconPath);
        foreach (var icon in currencyCoinIcons) icon.texture = coin;
        foreach (var icon in currencyTireIcons) icon.texture = tire;
    }

    readonly List<RawImage> currencyCoinIcons = new List<RawImage>();
    readonly List<RawImage> currencyTireIcons = new List<RawImage>();

    RawImage MakeCurrencyIcon(Transform parent, Vector2 anchor, Vector2 offset, float size, bool tire)
    {
        var go = new GameObject(tire ? "TireIcon" : "CoinIcon");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(size, size);
        (tire ? currencyTireIcons : currencyCoinIcons).Add(img);
        return img;
    }


    /// <summary>A picture in a daily reward cell (filled in when the popup opens).</summary>
    RawImage MakeLoginPicture(Transform parent, Vector2 at, float size)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<RawImage>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject, layer);
    }

    void BuildLoginShowcase()
    {
        if (showcaseRoot != null) return;

        showcaseRoot = new GameObject("LoginShowcase");
        Vector3 rewardPos = new Vector3(60f, -400f, 0f);

        // the day-7 reward car, spinning in its cell
        GameObject rewardCar = BuildPreviewModel(RewardCarIndex);
        if (rewardCar != null)
        {
            rewardCar.transform.SetParent(showcaseRoot.transform, false);
            rewardCar.transform.position = rewardPos;
            var spin = rewardCar.GetComponent<Coin>();
            if (spin == null) spin = rewardCar.AddComponent<Coin>();
            spin.spinSpeed = 55f;
        }

        SetLayerRecursively(showcaseRoot, ShowcaseLayer);
        if (mainCam != null) mainCam.cullingMask &= ~(1 << ShowcaseLayer);

        rewardCarRT = new RenderTexture(512, 512, 16);
        MakeShowcaseCam(showcaseRoot.transform,
            rewardPos + new Vector3(0f, 1.0f, -3.4f), rewardPos, rewardCarRT);

        showcaseRoot.SetActive(false);
    }

    void MakeShowcaseCam(Transform parent, Vector3 pos, Vector3 lookAt, RenderTexture rt)
    {
        var go = new GameObject("ShowcaseCam");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.LookAt(lookAt);
        var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent
        cam.cullingMask = 1 << ShowcaseLayer;
        cam.fieldOfView = 40f;
        cam.targetTexture = rt;
    }

    // ---------------------------------------------- new high score reveal

    static readonly Vector3 BestStagePos = new Vector3(-1400f, -400f, 0f);
    GameObject bestStage, bestCar;
    RenderTexture bestCarRT;
    GameObject bestPanel;
    RawImage bestCarImage;
    Text bestHeadline, bestNumber, bestHint;
    float bestAnimT = -1f;
    bool bestCarFlipped;
    int bestShownScore;

    void EnsureBestStage()
    {
        if (bestStage != null) return;

        bestStage = new GameObject("BestRunStage");
        bestStage.transform.position = BestStagePos;
        SetLayerRecursively(bestStage, ShowcaseLayer);
        if (mainCam != null) mainCam.cullingMask &= ~(1 << ShowcaseLayer);

        // side on and low, so the car reads as a silhouette crossing the frame
        // framed for a car sitting on the ground at its driving size
        bestCarRT = new RenderTexture(900, 900, 16);
        MakeShowcaseCam(bestStage.transform,
            BestStagePos + new Vector3(3.4f, 1.9f, -6.6f),
            BestStagePos + new Vector3(0f, 0.75f, 0f), bestCarRT);
    }

    void BuildBestPanel(Transform uiRoot)
    {
        bestPanel = MakePanel(uiRoot, "BestPanel");

        // solid black - this screen is meant to feel like the game cut out
        var blackGo = new GameObject("Black");
        blackGo.transform.SetParent(bestPanel.transform, false);
        var black = blackGo.AddComponent<Image>();
        black.color = Color.black;
        var blackRt = black.rectTransform;
        blackRt.anchorMin = Vector2.zero;
        blackRt.anchorMax = Vector2.one;
        blackRt.offsetMin = blackRt.offsetMax = Vector2.zero;
        black.raycastTarget = true;

        bestHeadline = MakeText(bestPanel.transform, "BestHeadline", 66,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, 330f), new Vector2(1000f, 120f));
        bestHeadline.text = "NEW HIGH SCORE";
        bestHeadline.color = new Color(1f, 0.82f, 0.15f);

        bestNumber = MakeText(bestPanel.transform, "BestNumber", 150,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, 190f), new Vector2(1000f, 230f));

        // the car passes in front of the number as it is uncovered
        var carGo = new GameObject("BestCarView");
        carGo.transform.SetParent(bestPanel.transform, false);
        bestCarImage = carGo.AddComponent<RawImage>();
        bestCarImage.raycastTarget = false;
        var carRt = bestCarImage.rectTransform;
        carRt.anchorMin = carRt.anchorMax = carRt.pivot = new Vector2(0.5f, 0.5f);
        carRt.anchoredPosition = new Vector2(0f, -140f);
        carRt.sizeDelta = new Vector2(1200f, 1200f);

        bestHint = MakeText(bestPanel.transform, "BestHint", 38,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -530f), new Vector2(900f, 80f));
        bestHint.text = "TAP TO CONTINUE";
        bestHint.color = new Color(1f, 1f, 1f, 0f);

        var btn = bestPanel.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = black;
        btn.onClick.AddListener(CloseBestPanel);

        bestPanel.SetActive(false);
    }

    /// <summary>
    /// Black screen, and the car you were driving drifts across it laying the
    /// new number down in its wake. It plays on the way out of the results,
    /// from MENU (ResultsMenu), so it never covers the crash or the results.
    /// </summary>
    void ShowBestRun(int finalScore)
    {
        EnsureBestStage();

        if (bestCar != null) Destroy(bestCar);
        // The race/swap builder, not the garage one. The garage spins its cars
        // so nobody ever notices which way they face, and its per-model flips
        // are tuned for that; this builder is the one whose cars are visibly
        // driving nose-first every time a rival passes you.
        bestCar = BuildCarModel(selectedCar);
        bestCarFlipped = false;
        if (bestCar == null)
        {
            bestCar = BuildPreviewModel(selectedCar);   // the starter car
            bestCarFlipped = true;                      // and it faces the camera
        }
        if (bestCar != null)
        {
            bestCar.transform.SetParent(bestStage.transform, false);
            SetLayerRecursively(bestCar, ShowcaseLayer);
            CarPaint.Apply(bestCar, selectedCar);
        }

        bestShownScore = finalScore;
        bestNumber.text = "0";
        bestNumber.color = new Color(1f, 1f, 1f, 0f);
        bestHeadline.color = new Color(1f, 0.82f, 0.15f, 0f);
        bestHint.color = new Color(1f, 1f, 1f, 0f);
        bestAnimT = 0f;
        bestPanel.SetActive(true);
        bestPanel.transform.SetAsLastSibling();
        bestCarImage.texture = bestCarRT;

        audioMan.PlayFinishWhoosh();
    }

    void TickBestRun()
    {
        if (bestAnimT < 0f) return;
        bestAnimT += Time.unscaledDeltaTime;

        const float Cross = 1.7f;
        float p = Mathf.Clamp01(bestAnimT / Cross);
        // slides in hard from the left and parks broadside in the middle of
        // the screen - it has to hold still long enough to be looked at
        float eased = 1f - Mathf.Pow(1f - p, 3f);
        if (bestCar != null)
        {
            bestCar.transform.position =
                BestStagePos + new Vector3(Mathf.Lerp(-8f, 0f, eased), 0f, 0f);
            // Slides in broadside - nose to the right, the way it is
            // travelling - then swings its front toward the camera and holds
            // a three-quarter view.
            float yaw = Mathf.Lerp(95f, 142f, eased) + (bestCarFlipped ? 180f : 0f);
            bestCar.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // the headline lands with the car
        float head = Mathf.Clamp01((bestAnimT - 0.25f) / 0.35f);
        float headEase = 1f - Mathf.Pow(1f - head, 3f);
        bestHeadline.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, headEase);
        bestHeadline.color = new Color(1f, 0.82f, 0.15f, head);

        // the number is uncovered behind it, counting up
        float count = Mathf.Clamp01((bestAnimT - 0.55f) / 1.15f);
        int shown = Mathf.RoundToInt(Mathf.Lerp(0f, bestShownScore,
            1f - Mathf.Pow(1f - count, 3f)));
        bestNumber.text = shown.ToString();
        bestNumber.color = new Color(1f, 1f, 1f, Mathf.Clamp01(count * 4f));
        bestNumber.rectTransform.localScale = Vector3.one * (1f + (1f - count) * 0.22f);

        if (bestAnimT > Cross + 0.3f)
        {
            bestHint.color = new Color(1f, 1f, 1f,
                0.35f + 0.3f * Mathf.Sin(bestAnimT * 4f));
        }
    }

    void CloseBestPanel()
    {
        if (bestAnimT < 1.1f) return;          // let the drift finish first
        bestAnimT = -1f;
        bestPanel.SetActive(false);
        if (bestCar != null) { Destroy(bestCar); bestCar = null; }
        audioMan.PlayTap();
        // on with the way out: the weekly ranking if there is one, then the menu
        ResultsMenu();
    }

    void LateUpdate()
    {
        TickBestRun();
        TickWreck();
        TickGameOver();
        TickLeaderboard();
        TickRaceTags();
        TickFinishReveal();
        TickRaceSelect();
    }

    bool OwnedCar(int i)
    {
        return i == 0 || PlayerPrefs.GetInt("CarOwned" + i, 0) == 1;
    }

    /// <summary>Handed out with a code - hidden from the garage until owned.</summary>
    static bool DevOnlyCar(int i) { return Cars[i].cost == -3; }

    bool ListedInGarage(int i) { return !DevOnlyCar(i) || OwnedCar(i); }

    void EquipSelected()
    {
        displayedCar = selectedCar;
        CarDef d = Cars[selectedCar];
        // the garage and the race field already turn nose-backwards models
        // around; the car you actually drive never did
        float flip = System.Array.IndexOf(BackwardsModels, d.name) >= 0 ? 180f : 0f;
        car.modelHeightFix = HeightFixFor(d.name);
        car.SetCarModel(d.path == null ? null : Resources.Load<GameObject>(d.path), d.yaw + flip);

        // NOT during a race. A race runs at a fixed pace set by SetConstantSpeed,
        // and stamping the endless speeds over it left the car with the endless
        // base speed - which the oil spill then snapped it up to, because the
        // spin-out floors the speed at baseSpeed. That is the "hit oil, jump to
        // 112" bug: the floor was a speed the race never runs at.
        if (mode != Mode.Race)
        {
            car.baseSpeed = runBaseSpeed;
            car.speedGainPerSecond = runSpeedGain;
            car.maxSpeed = baseMaxSpeed + d.speedBonus;
        }
        carPointMult = d.pointMult;
        car.hoverMode = d.hover;
        audioMan.SetEngineStyle(d.hover);
        if (car.CarModelRoot != null) CarPaint.Apply(car.CarModelRoot.gameObject, selectedCar);

        // in the lobby the car is mid-shot, so it carries on from where it is
        // instead of being thrown back to the start line
        float keepDistance = car.DistanceTraveled;
        car.ResetRun(track);
        if (state == State.Menu) car.ResumeFrom(keepDistance);
        else if (camFollow != null) camFollow.SnapToTarget();
    }

    // ------------------------------------------------------------------ states

    void EnterMenu()
    {
        mode = Mode.Endless;
        if (race != null) race.ClearRacers();
        if (raceHudText != null) raceHudText.gameObject.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        raceCountdown = 0f;
        if (track != null) track.ClearFinishLine();
        state = State.Menu;
        Time.timeScale = 1f;
        EndTutorialHints();
        tutorialOnly = false;
        CancelInvoke(nameof(FinishTutorialOnly));

        // the wreck does not follow you into the lobby - nor does its fire
        EndWreck();
        CrashExplosion.ClearAll();
        goAnimT = -1f;
        goBestToShow = false;
        goLbPending = false;
        lbLeaveAfter = false;
        if (car != null && car.IsScorched)
        {
            EquipSelected();
            car.ClearScorch();
        }
        // On the very first launch the tutorial starts by itself as soon as
        // the opening title is done; the menu stays hidden until then, so the
        // player goes straight from the title into the drive
        menuPanel.SetActive(!coachWanted);
        settingsPanel.SetActive(false);
        garagePanel.SetActive(false);
        gameOverPanel.SetActive(false);
        pausePanel.SetActive(false);
        questsPanel.SetActive(false);
        revivePanel.SetActive(false);
        shopPanel.SetActive(false);
        creditsPanel.SetActive(false);
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        if (unlockPanel != null) unlockPanel.SetActive(false);
        if (wheelPanel != null) wheelPanel.SetActive(false);
        if (racePanel != null) racePanel.SetActive(false);
        RefreshSpinBadge();
        RefreshLoginButton();
        ClearGaragePreview();

        EndFinishCinematic();
        ApplyLobbyTrack();
        // whatever biome the last run ended in, the lobby starts over in the
        // base one - the lighting and fog have to come back with it
        ResetBiome();
        car.baseSpeed = lobbyCruiseSpeed;
        car.maxSpeed = lobbyCruiseSpeed;
        car.speedGainPerSecond = 0f;
        car.GrantMercy(9999f);
        if (camFollow != null) { camFollow.CancelIntro(); camFollow.SetShowcase(true); }
        // the lobby is always daylight - a run that ended at night or in the
        // snow used to leave its sky behind
        ResetBiome();
        RefreshWeeklyMenu();

        // a new day since the game was opened (it was left running
        // overnight): that day's reward is waiting too
        if (!loginPending && LoginRewardWaiting()) ComputeDailyLogin();

        // the daily reward waits until the opening sequence has finished
        if (loginPending && !introRunning && !CoachHoldsLogin)
        {
            if (!loginPanel.activeSelf) ShowLoginPopup();
        }
        else if (showcaseRoot != null && !loginPanel.activeSelf)
        {
            showcaseRoot.SetActive(false);
        }
        centerText.gameObject.SetActive(false);
        bonusText.gameObject.SetActive(false);
        driftText.gameObject.SetActive(false);
        SetHudVisible(false);
        menuBestText.text = Loc.F("HIGH SCORE  {0}", best);
        FitToWidth(menuBestText, 52, 960f);   // a long score stays on screen
        // back to the lobby theme (the intro owns the music until it hands over)
        if (audioMan != null && !introRunning) audioMan.PlayMenuMusic();
    }

    void SetHudVisible(bool visible)
    {
        bool endless = mode == Mode.Endless;
        scoreText.gameObject.SetActive(visible && endless);
        bestText.gameObject.SetActive(visible && endless);
        speedText.gameObject.SetActive(visible);
        coinHudText.gameObject.SetActive(false);   // the run's coin count is not shown
        multHudText.gameObject.SetActive(visible && endless);   // points only count in endless
        pauseButton.SetActive(visible);
        if (raceHudText != null) raceHudText.gameObject.SetActive(visible && mode == Mode.Race);
        ShowRivalHud(visible);
        if (!visible) HideItemBars();
    }

    // ---------------------------------------------------------------- pause

    void PauseGame()
    {
        if (state != State.Playing) return;
        state = State.Paused;
        Time.timeScale = 0f;
        HideDriftText();
        // the same lines, at the same size, as the quests window
        for (int i = 0; i < 3; i++) pauseQuestRows[i].text = QuestLine(i);
        pausePanel.SetActive(true);
        pauseButton.SetActive(false);
        audioMan.StopDriving(); // engine/skid fade out, music keeps playing
        audioMan.PlayTap();
    }

    void ResumeGame()
    {
        state = State.Playing;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
        settingsPanel.SetActive(false);
        pauseButton.SetActive(true);
        audioMan.StartDriving();
        audioMan.PlayTap();
    }

    void QuitToMenu()
    {
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
        audioMan.StopDriving();
        LeaveResults();     // a race goes back to the race list
    }

    void ApplySettings()
    {
        AudioListener.volume = volumeSetting;
        if (audioMan != null)
        {
            // sliders scale each channel around its tuned base level
            audioMan.musicVolume = 0.60f * volMusic;
            audioMan.engineVolume = 0.30f * volEngine;
            audioMan.skidVolume = 0.55f * volDrift;
            audioMan.coinVolume = 0.70f * volCoins;
            audioMan.oneShotVolume = 0.90f * volSfx;
        }
        if (car != null)
        {
            // higher sensitivity = shorter thumb slide for full lock AND
            // stronger steering response, so the whole range is clearly felt
            // The low end has to be genuinely gentle to be worth having: at 0
            // a full-lock turn takes most of the screen width and the car
            // builds into it slowly; at 1 it is a flick of the thumb.
            car.steerZoneFraction = Mathf.Lerp(0.90f, 0.12f, sensSetting);
            if (baseSteerAccel > 0f)
            {
                car.steerAcceleration = baseSteerAccel * Mathf.Lerp(0.45f, 1.6f, sensSetting);
            }
            car.invertSteering = invertSteer == 1;
        }
    }

    GameObject settingsAudioTab, settingsGeneralTab, settingsCreditsTab, settingsContactTab;
    Text settingsTabAudio, settingsTabGeneral, settingsTabCredits, settingsTabContact;

    const string ContactWebsite = "www.meindgames.com";
    const string ContactEmail = "delanomeindl@meindgames.com";

    static string CreditsText
    {
        get
        {
            return "DRIFTLINE ETERNAL\n" + Loc.T("by MEIND GAMES") + "\n\n" +
                Loc.T("3D MODELS") + "\n\n" +
                "Nature Pack - Quaternius\n" +
                "Low Poly Car Pack - Designersoup\n\n" +
                Loc.T("AUDIO") + "\n\n" +
                Loc.T("Music and sound effects\ncreated for this game") + "\n\n" +
                Loc.T("FONTS") + "\n\n" +
                "Noto Sans - Google\nRusso One - Jovanny Lemonad\n" +
                Loc.T("(SIL Open Font License)") + "\n\n" +
                Loc.T("All models used under their\nrespective licenses.");
        }
    }

    /// <summary>The website and the email, each a button that opens it.</summary>
    void BuildContactTab(Transform t)
    {
        var head = MakeText(t, "ContactHead", 60, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(900f, 90f));
        head.text = "GET IN TOUCH";
        head.color = new Color(1f, 0.72f, 0.12f);

        var webLabel = MakeText(t, "WebLabel", 32, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 215f), new Vector2(800f, 50f), true);
        webLabel.text = "WEBSITE";
        webLabel.color = new Color(1f, 1f, 1f, 0.6f);
        var web = MakeButton(t, "", 40, new Vector2(0f, 130f), new Vector2(780f, 110f),
            () => Application.OpenURL("https://" + ContactWebsite), null, true);
        ShowAsTyped(web, ContactWebsite);

        var mailLabel = MakeText(t, "MailLabel", 32, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -15f), new Vector2(800f, 50f), true);
        mailLabel.text = "EMAIL";
        mailLabel.color = new Color(1f, 1f, 1f, 0.6f);
        var mail = MakeButton(t, "", 36, new Vector2(0f, -100f), new Vector2(780f, 110f),
            () => Application.OpenURL("mailto:" + ContactEmail + "?subject=Driftline%20Eternal"), null, true);
        ShowAsTyped(mail, ContactEmail);

        var company = MakeText(t, "Company", 30, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(800f, 50f), true);
        ShowAsTyped(company, "Meind Games LLC");
        company.color = new Color(1f, 1f, 1f, 0.45f);
    }

    /// <summary>An address or a name: shown exactly as written, never translated.</summary>
    static void ShowAsTyped(Text t, string s)
    {
        if (t is LocText lt) lt.SetShown(s);
        else t.text = s;
    }

    void SetSettingsTab(int tab)
    {
        settingsAudioTab.SetActive(tab == 0);
        settingsGeneralTab.SetActive(tab == 1);
        settingsCreditsTab.SetActive(tab == 2);
        settingsContactTab.SetActive(tab == 3);

        var on = new Color(1f, 0.78f, 0.2f);
        var off = new Color(1f, 1f, 1f, 0.55f);
        settingsTabAudio.color = tab == 0 ? on : off;
        settingsTabGeneral.color = tab == 1 ? on : off;
        settingsTabCredits.color = tab == 2 ? on : off;
        settingsTabContact.color = tab == 3 ? on : off;
        if (audioMan != null) audioMan.PlayTap();
    }

    void ToggleInvert()
    {
        invertSteer = 1 - invertSteer;
        PlayerPrefs.SetInt("InvertSteer", invertSteer);
        ApplySettings();
        invertBtnLabel.text = InvertLabel();
        audioMan.PlayTap();
    }

    string InvertLabel()
    {
        return Loc.F("STEERING: {0}", Loc.T(invertSteer == 1 ? "INVERTED" : "NORMAL"));
    }

    void SetVolume(float v) { volumeSetting = v; PlayerPrefs.SetFloat("Volume", v); ApplySettings(); }
    void SetMusicVol(float v) { volMusic = v; PlayerPrefs.SetFloat("VolMusic", v); ApplySettings(); }
    void SetEngineVol(float v) { volEngine = v; PlayerPrefs.SetFloat("VolEngine", v); ApplySettings(); }
    void SetDriftVol(float v) { volDrift = v; PlayerPrefs.SetFloat("VolDrift", v); ApplySettings(); }
    void SetCoinVol(float v) { volCoins = v; PlayerPrefs.SetFloat("VolCoins", v); ApplySettings(); }
    void SetSensitivity(float v) { sensSetting = v; PlayerPrefs.SetFloat("Sensitivity", v); ApplySettings(); }
    void SetSfxVol(float v) { volSfx = v; PlayerPrefs.SetFloat("VolSfx", v); ApplySettings(); }

    /// <summary>
    /// The speedometer reads high on purpose. The car handles the way it does
    /// at its real pace - which tops out around 78mph of actual travel - but
    /// a drift game wants a big number on the dial, so the readout is scaled
    /// to show 120 at full speed. Nothing else uses this: every piece of
    /// tuning, and the physics, stay in true m/s.
    /// </summary>
    const float SpeedoScale = 120f / 78f;

    /// <summary>Speed in whichever unit the player picked, with its label.</summary>
    string SpeedLabel(float metresPerSecond)
    {
        float shown = metresPerSecond * SpeedoScale;
        return useMph
            ? Loc.F("{0} MPH", Mathf.RoundToInt(shown * 2.23694f))
            : Loc.F("{0} KM/H", Mathf.RoundToInt(shown * 3.6f));
    }

    void ToggleUnits()
    {
        useMph = !useMph;
        PlayerPrefs.SetInt("UseMph", useMph ? 1 : 0);
        unitsBtnLabel.text = UnitsLabel();
        audioMan.PlayTap();
    }

    string UnitsLabel() { return Loc.F("SPEED: {0}", Loc.T(useMph ? "MPH" : "KM/H")); }

    // --------------------------------------------------------------- garage

    // ----------------------------------------------------------- quests
    // complete all 3 active quests -> permanent score multiplier +1 (max x30)
    const int QuestTypeCount = 8;
    static readonly string[] QuestNames =
    {
        "DRIVE {0}M IN ONE RUN", "DRIVE {0}M TOTAL", "COLLECT {0} COINS IN ONE RUN",
        "COLLECT {0} COINS TOTAL", "GET {0} NEAR MISSES", "BANK A {0}+ DRIFT COMBO",
        "HIT {0} OIL SPILLS", "PLAY {0} RUNS",
    };
    static readonly int[] QuestBaseTargets = { 800, 4000, 40, 250, 12, 600, 4, 6 };
    static readonly bool[] QuestIsBestOf = { true, false, true, false, false, true, false, false };

    int[] questType = new int[3];
    int[] questTarget = new int[3];
    int[] questProgress = new int[3];
    int questLevel;
    int tires;
    int adRevivesUsed;
    int revivesThisRun;
    CarController.TickResult pendingCrash;

    // active item timers
    float invincibleT, doubleCoinsT, magnetT, doubleScoreT, springsT;

    readonly Text[] itemUpNames = new Text[ItemUpgrades.Count];
    readonly Text[] itemUpInfo = new Text[ItemUpgrades.Count];
    readonly Text[] itemUpButtons = new Text[ItemUpgrades.Count];
    readonly RawImage[] itemUpCoins = new RawImage[ItemUpgrades.Count];

    GameObject questsPanel, revivePanel, shopPanel, creditsPanel, unlockPanel;
    InputField unlockInput;
    Text unlockMsg;
    Text boxResultText;
    const int MysteryBoxCost = 500;

    // shop 3D showcase: animated toolbox + tire stack icons
    GameObject shopShowcaseRoot;
    RenderTexture toolboxRT, tokenBoxRT;
    GameObject tokenBoxRoot;
    ToolboxAnimator tokenBoxAnim;
    RawImage tokenBoxImage;
    Text tokenBoxResultText;
    const int TokenBoxTireCost = 2;
    readonly RenderTexture[] tireStackRTs = new RenderTexture[3];
    readonly RenderTexture[] coinStackRTs = new RenderTexture[3];
    readonly RawImage[] coinPackIcons = new RawImage[3];
    ToolboxAnimator toolboxAnim;
    RawImage toolboxImage;
    readonly RawImage[] packIcons = new RawImage[6];
    readonly Text[] packLabels = new Text[6];

    // tire packs, in shop order - the same order as IapStore.TirePackIds.
    // The dollar prices are only shown until the store sends the real ones.
    static readonly string[] PackFallbackPrices = { "$0.99", "$4.99", "$9.99", "$24.99", "$49.99", "$99.99" };
    static readonly int[] PackTires = { 10, 55, 120, 325, 700, 1500 };
    string pendingBoxText;
    Color pendingBoxColor;
    Currency pendingBoxCurrency = Currency.Coins;
    float boxRevealAt = -1f;
    bool pendingBoxIsToken;
    bool pendingBoxTires;        // the prize waiting in the box is tyres
    Material rubberMat;
    Text persistentCoinsText, persistentTiresText;
    RectTransform coinPillRt, tirePillRt;
    int shownCoins = int.MinValue, shownTires = int.MinValue;

    // corner counter layout, in canvas units
    const float CounterEdge = 44f;       // in from the side of the screen
    const float CounterTop = 14f;        // down from the top
    const float CounterHeight = 72f;
    const float CounterIcon = 58f;
    const float CounterMinWidth = 170f;

    /// <summary>
    /// A corner counter: a dark pill with the currency's icon at the outer end
    /// and the amount beside it in big, upright numbers. (The racing font is
    /// slanted and thin at small sizes, which is what made the old counters
    /// hard to read, so the numbers use the plain bold font.)
    /// </summary>
    Text MakeCornerCounter(Transform parent, bool tire, Color numberColor,
        out RectTransform pillRt)
    {
        Vector2 corner = tire ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        Vector2 end = tire ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
        float inward = tire ? -1f : 1f;

        var pillGo = new GameObject(tire ? "TireCounter" : "CoinCounter");
        pillGo.transform.SetParent(parent, false);
        var pill = pillGo.AddComponent<Image>();
        pill.sprite = GetRoundedSprite();
        pill.type = Image.Type.Sliced;
        pill.color = new Color(0.05f, 0.04f, 0.09f, 0.8f);
        pill.raycastTarget = false;
        pillRt = pill.rectTransform;
        pillRt.anchorMin = pillRt.anchorMax = pillRt.pivot = corner;
        pillRt.anchoredPosition = new Vector2(inward * CounterEdge, -CounterTop);
        pillRt.sizeDelta = new Vector2(CounterMinWidth, CounterHeight);

        MakeCurrencyIcon(pillGo.transform, end, new Vector2(inward * 8f, 0f), CounterIcon, tire);

        var number = MakeText(pillGo.transform, tire ? "TotalTires" : "TotalCoins", 50,
            tire ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, end,
            new Vector2(inward * (8f + CounterIcon + 10f), 1f),
            new Vector2(400f, CounterHeight), true);
        number.color = numberColor;
        return number;
    }

    /// <summary>Updates the corner counters, only when an amount changes.</summary>
    void RefreshCornerCounters()
    {
        if (totalCoins != shownCoins)
        {
            shownCoins = totalCoins;
            persistentCoinsText.text = FormatCount(totalCoins);
            FitCounterPill(coinPillRt, persistentCoinsText);
        }
        if (tires != shownTires)
        {
            shownTires = tires;
            persistentTiresText.text = FormatCount(tires);
            FitCounterPill(tirePillRt, persistentTiresText);
        }
    }

    /// <summary>12345 -> "12,345", the same on every phone.</summary>
    static string FormatCount(int amount)
    {
        return amount.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Grows or shrinks a counter's pill to fit its number.</summary>
    static void FitCounterPill(RectTransform pillRt, Text number)
    {
        if (pillRt == null) return;
        float width = 8f + CounterIcon + 10f + number.preferredWidth + 24f;
        pillRt.sizeDelta = new Vector2(Mathf.Max(CounterMinWidth, width), CounterHeight);
    }
    GameObject toolboxRoot, boxPrizeGo;

    // static currency icons rendered once from the real 3D models
    RawImage garagePriceIcon;
    Text[] questRowTexts = new Text[3];

    // active-item timer bars along the bottom of the screen
    const int ItemSlots = 5;
    readonly GameObject[] itemBarRoots = new GameObject[ItemSlots];
    readonly Image[] itemBarFills = new Image[ItemSlots];
    readonly Text[] itemBarLabels = new Text[ItemSlots];
    static readonly string[] ItemNames = { "SHIELD", "2X COINS", "MAGNET", "2X SCORE", "SPRINGS" };
    static readonly float[] ItemDurations = { 8f, 20f, 15f, 15f, 15f };
    static readonly Color[] ItemColors = {
        new Color(1f, 0.95f, 0.55f),   // shield
        new Color(1f, 0.8f, 0.15f),    // double coins
        new Color(0.35f, 0.6f, 1f),    // magnet
        new Color(0.8f, 0.4f, 1f),     // double score
        new Color(0.35f, 1f, 0.45f),   // springs
    };
    Text questMultText, itemHudText, multHudText;
    readonly Text[] pauseQuestRows = new Text[3];
    Text reviveTireLabel, reviveAdLabel, reviveTimerText, reviveHaveText;
    /// <summary>An ad was asked for and has not come back yet.</summary>
    bool reviveAdPending;

    [Tooltip("Seconds the player has to decide whether to revive.")]
    public float reviveDecisionTime = 5f;
    float reviveTimer;
    Image reviveBarFill;              // runs out with the count
    const float ReviveBarWidth = 700f;

    void SetReviveBar(float t)
    {
        if (reviveBarFill == null) return;
        float f = Mathf.Clamp01(t / Mathf.Max(0.01f, reviveDecisionTime));
        reviveBarFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(22f, ReviveBarWidth * f), 22f);
        reviveBarFill.gameObject.SetActive(f > 0f);
        reviveBarFill.color = reviveTimerText != null ? reviveTimerText.color : Color.white;
    }

    int ScoreMultiplier
    {
        get { return Mathf.Min(30, 1 + questLevel) * (doubleScoreT > 0f ? 2 : 1); }
    }

    // 1, 2, 4, 8... - the first save of a run is nearly free, and each one
    // after it costs as much as everything before it put together
    int ReviveTireCost { get { return 1 << Mathf.Min(revivesThisRun, 9); } }

    void LoadQuests()
    {
        // one-off wipe of the score multiplier, now that scoring is rebalanced
        if (PlayerPrefs.GetInt("MultReset", 0) == 0)
        {
            PlayerPrefs.SetInt("QuestLevel", 0);
            PlayerPrefs.SetInt("MultReset", 1);
            PlayerPrefs.Save();
        }
        questLevel = PlayerPrefs.GetInt("QuestLevel", 0);
        tires = PlayerPrefs.GetInt("Tires", 0);
        if (PlayerPrefs.GetInt("QuestsInit", 0) == 0)
        {
            GenerateQuests();
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                questType[i] = PlayerPrefs.GetInt("Q" + i + "Type", i);
                questTarget[i] = PlayerPrefs.GetInt("Q" + i + "Target", 100);
                questProgress[i] = PlayerPrefs.GetInt("Q" + i + "Prog", 0);
            }
        }
    }

    void SaveQuests()
    {
        PlayerPrefs.SetInt("QuestLevel", questLevel);
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.SetInt("QuestsInit", 1);
        for (int i = 0; i < 3; i++)
        {
            PlayerPrefs.SetInt("Q" + i + "Type", questType[i]);
            PlayerPrefs.SetInt("Q" + i + "Target", questTarget[i]);
            PlayerPrefs.SetInt("Q" + i + "Prog", questProgress[i]);
        }
        PlayerPrefs.Save();
    }

    void GenerateQuests()
    {
        // three distinct quest types, targets scale with level
        var pool = new List<int>();
        for (int i = 0; i < QuestTypeCount; i++) pool.Add(i);
        float scale = 1f + questLevel * 0.25f;
        for (int i = 0; i < 3; i++)
        {
            int pick = pool[Random.Range(0, pool.Count)];
            pool.Remove(pick);
            questType[i] = pick;
            questTarget[i] = Mathf.Max(1, Mathf.RoundToInt(QuestBaseTargets[pick] * scale));
            questProgress[i] = 0;
        }
        SaveQuests();
    }

    string QuestLine(int i)
    {
        bool isDone = questProgress[i] >= questTarget[i];
        string line = Loc.F(QuestNames[questType[i]], questTarget[i])
               + "\n<size=38>" + Mathf.Min(questProgress[i], questTarget[i]) + " / " + questTarget[i]
               + (isDone ? "  " + Loc.T("DONE!") : "") + "</size>";
        return isDone ? "<color=#66E28C>" + line + "</color>" : line;
    }

    void UpdateQuest(int type, int amount, bool bestOf)
    {
        if (tutorialOnly) return;   // a tutorial replay is practice
        bool changed = false;
        for (int i = 0; i < 3; i++)
        {
            if (questType[i] != type || questProgress[i] >= questTarget[i]) continue;
            questProgress[i] = bestOf ? Mathf.Max(questProgress[i], amount)
                                      : questProgress[i] + amount;
            changed = true;
        }
        if (!changed) return;

        bool allDone = true;
        for (int i = 0; i < 3; i++)
        {
            if (questProgress[i] < questTarget[i]) { allDone = false; break; }
        }
        if (allDone) CompleteQuestSet();
        SaveQuests();
    }

    /// <summary>All three quests done: the reward, and a fresh set.</summary>
    void CompleteQuestSet()
    {
        questLevel = Mathf.Min(questLevel + 1, 29);
        tires += 6;
        FlashBonus(Loc.F("QUESTS DONE! X{0}  +6 TIRES", Mathf.Min(30, 1 + questLevel)),
            new Color(0.6f, 0.9f, 1f));
        audioMan.PlayCoin();
        GenerateQuests();
    }

    // ------------------------------------------------- day -> night city
    bool nightAnnounced, snowAnnounced;
    float nextCycleTime;
    float runTime;
    bool snowRunInDone, cityRunInDone, mountainShown;
    float biomeBlend;          // 0 = day forest, 1 = night city
    float snowBlend;           // 0 = city, 1 = snowy mountains
    Light sunLight;
    Color sunDayColor;
    float sunDayIntensity;
    Color ambientDay;

    bool lightingCached;

    /// <summary>
    /// Remembers what daylight looks like, ONCE. Every later biome colour is
    /// mixed from these values, so re-reading them while the world happens to
    /// be lit for night or snow would bake that lighting in as the new
    /// daytime and there would be no way back.
    /// </summary>
    void CacheLighting()
    {
        if (lightingCached) return;
        foreach (var l in FindObjectsByType<Light>())
        {
            if (l.type == LightType.Directional) { sunLight = l; break; }
        }
        if (sunLight != null)
        {
            sunDayColor = sunLight.color;
            sunDayIntensity = sunLight.intensity;
        }
        ambientDay = RenderSettings.ambientLight;
        lightingCached = true;
    }

    void ResetBiome()
    {
        CacheLighting();
        nightAnnounced = false;
        snowAnnounced = false;
        cyclingStarted = false;
        cyclesDone = 0;
        nextCycleTime = 0f;
        runTime = 0f;
        snowRunInDone = false;
        cityRunInDone = false;
        mountainShown = false;
        biomeBlend = 0f;
        snowBlend = 0f;
        ApplyBiomeVisuals(0f);
        if (track != null) { track.SetBiomeBlend(0f); track.SetSnowBlend(0f); }
    }

    /// <summary>t: 0 = day forest, 0.5 = sunset, 1 = full night city.</summary>
    void ApplyBiomeVisuals(float t)
    {
        // day -> golden sunset -> purple dusk -> night
        Color sky = t < 0.4f ? Color.Lerp(daySkyColor, sunsetSkyColor, t / 0.4f)
                  : t < 0.7f ? Color.Lerp(sunsetSkyColor, duskSkyColor, (t - 0.4f) / 0.3f)
                  : Color.Lerp(duskSkyColor, nightSkyColor, (t - 0.7f) / 0.3f);

        // night city -> cold dawn -> snowy daylight on the mountain
        float s = snowBlend;
        if (s > 0f)
        {
            sky = s < 0.5f ? Color.Lerp(sky, dawnSkyColor, s / 0.5f)
                           : Color.Lerp(dawnSkyColor, snowSkyColor, (s - 0.5f) / 0.5f);
        }

        if (mainCam != null) mainCam.backgroundColor = sky;
        RenderSettings.fogColor = sky;
        RenderSettings.fogStartDistance = Mathf.Lerp(Mathf.Lerp(fogStart, nightFogStart, t), snowFogStart, s);
        RenderSettings.fogEndDistance = Mathf.Lerp(Mathf.Lerp(fogEnd, nightFogEnd, t), snowFogEnd, s);
        // Nothing past the fog's end can be seen - it is the sky colour - so
        // the camera stops drawing there. The track is generated well beyond
        // it, and this keeps all of that from costing anything.
        if (mainCam != null) mainCam.farClipPlane = Mathf.Max(150f, RenderSettings.fogEndDistance + 25f);
        RenderSettings.ambientLight = Color.Lerp(
            Color.Lerp(ambientDay, new Color(0.10f, 0.11f, 0.20f), t),
            new Color(0.55f, 0.60f, 0.68f), s);

        if (sunLight != null)
        {
            // sunlight warms and reddens, then fades to cool moonlight
            Color sunNow = t < 0.5f
                ? Color.Lerp(sunDayColor, new Color(1f, 0.62f, 0.35f), t / 0.5f)
                : Color.Lerp(new Color(1f, 0.62f, 0.35f), new Color(0.55f, 0.62f, 0.95f), (t - 0.5f) / 0.5f);
            // the mountain dawn brings the light back up, cold and flat
            sunLight.color = Color.Lerp(sunNow, new Color(0.92f, 0.95f, 1f), s);
            sunLight.intensity = Mathf.Lerp(
                Mathf.Lerp(sunDayIntensity, sunDayIntensity * 0.22f, t * t),
                sunDayIntensity * 0.85f, s);
            // sun sinks toward the horizon as the sunset progresses
            Vector3 e = sunLight.transform.eulerAngles;
            sunLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(50f, 6f, t), e.y, e.z);
        }
    }

    // ------------------------------------------------------------ race mode
    Mode mode = Mode.Endless;
    RaceMode race;
    int raceLevel;
    float raceFinishDistance;
    bool raceFinished;
    float raceCountdown;
    Text countdownText;
    GameObject racePanel;
    Text raceHudText;
    readonly Text[] upgradeLabels = new Text[3];
    // the price bar along the bottom of each upgrade tile
    readonly Text[] upgradePriceTexts = new Text[3];
    readonly RawImage[] upgradePriceCoins = new RawImage[3];
    readonly GameObject[] upgradeAdChips = new GameObject[3];
    GameObject upgradeAdPopup;
    RaceMode.Upgrade upgradeAdTarget;

    // one upgrade a day can be paid for with an ad instead of coins
    const string UpgradeAdKey = "UpgradeAdDay";
    static bool UpgradeAdReady { get { return PlayerPrefs.GetString(UpgradeAdKey, "") != TodayKey(); } }

    void EnsureRace()
    {
        if (race != null) return;
        race = gameObject.AddComponent<RaceMode>();
        race.Setup(track);
    }

    void BuyUpgrade(RaceMode.Upgrade u)
    {
        if (RaceMode.IsMaxed(u)) { audioMan.PlayTap(); return; }
        int cost = RaceMode.CostOf(u);
        if (totalCoins < cost)
        {
            // short of coins: today's free one can still be had for an ad
            if (UpgradeAdReady) { OpenUpgradeAd(u); return; }
            FlashUpgradeLine(u, "NOT ENOUGH COINS");
            audioMan.PlayTap();
            return;
        }
        totalCoins -= cost;
        PlayerPrefs.SetInt("Coins", totalCoins);
        RaceMode.Buy(u);
        ApplyUpgrades();
        RefreshRacePanel();
        audioMan.PlayCoin();
    }

    /// <summary>"Not enough coins - watch an ad for it?", over the race screen.</summary>
    void BuildUpgradeAdPopup()
    {
        upgradeAdPopup = new GameObject("UpgradeAdPopup");
        upgradeAdPopup.transform.SetParent(racePanel.transform, false);
        var rt = upgradeAdPopup.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(upgradeAdPopup.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.75f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;    // blocks the screen behind

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(upgradeAdPopup.transform, false);
        var bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;
        Transform body = bodyGo.transform;
        var pop = bodyGo.AddComponent<PopIn>();
        pop.dimmer = dim;
        pop.dimmerAlpha = 0.75f;

        MakeCard(body, new Vector2(0f, 20f), new Vector2(880f, 640f),
            new Color(0.13f, 0.09f, 0.2f, 1f));
        var title = MakeText(body, "AdTitle", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(820f, 80f));
        title.text = "NOT ENOUGH COINS";
        title.color = new Color(1f, 0.55f, 0.45f);
        var text = MakeText(body, "AdText", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(820f, 170f), true);
        text.text = "Watch an ad to get this upgrade\nfor free? One free upgrade a day.";
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.lineSpacing = 1.1f;
        MakeButton(body, "WATCH AD", 48, new Vector2(0f, -60f), new Vector2(560f, 130f),
            WatchAdForUpgrade, GoGreen);
        MakeButton(body, "BACK", 40, new Vector2(0f, -205f), new Vector2(400f, 100f),
            CloseUpgradeAd, LockedGrey);

        upgradeAdPopup.SetActive(false);
    }

    /// <summary>A short message in place of a tile's level line, then back.</summary>
    void FlashUpgradeLine(RaceMode.Upgrade u, string message)
    {
        upgradeLabels[(int)u].text = Loc.T(message);
        CancelInvoke(nameof(RefreshRacePanel));
        Invoke(nameof(RefreshRacePanel), 1.6f);
    }

    void OpenUpgradeAd(RaceMode.Upgrade u)
    {
        upgradeAdTarget = u;
        upgradeAdPopup.SetActive(true);
        audioMan.PlayPop();
    }

    void CloseUpgradeAd()
    {
        upgradeAdPopup.SetActive(false);
        audioMan.PlayTap();
    }

    void WatchAdForUpgrade()
    {
        upgradeAdPopup.SetActive(false);
        RaceMode.Upgrade u = upgradeAdTarget;
        upgradeLabels[(int)u].text = Loc.T("LOADING AD...");
        AdManager.ShowRewarded("upgrade", () =>
        {
            // only reached when the ad reports a completed view
            PlayerPrefs.SetString(UpgradeAdKey, TodayKey());
            if (!RaceMode.IsMaxed(u)) RaceMode.Buy(u);
            PlayerPrefs.Save();
            ApplyUpgrades();
            CancelInvoke(nameof(RefreshRacePanel));
            RefreshRacePanel();
            audioMan.PlayCoin();
        },
        () =>
        {
            // nothing to show, or closed early: no upgrade, and the free one
            // is still there to try again
            FlashUpgradeLine(u, "NO AD AVAILABLE");
            audioMan.PlayTap();
        });
    }

    /// <summary>
    /// The price bar: coin, amount (red when it is out of reach) and, while
    /// today's free one is unused, the green play sign - centred as a group.
    /// </summary>
    void RefreshUpgradePrice(int u)
    {
        if (upgradePriceTexts[u] == null) return;
        var up = (RaceMode.Upgrade)u;
        bool maxed = RaceMode.IsMaxed(up);
        int cost = RaceMode.CostOf(up);
        bool afford = totalCoins >= cost;
        bool adOffer = !maxed && !afford && UpgradeAdReady;

        Text price = upgradePriceTexts[u];
        price.text = maxed ? "MAX" : FormatCount(cost);
        price.color = maxed ? new Color(0.65f, 1f, 0.7f)
                    : afford ? Color.white : new Color(1f, 0.62f, 0.58f);
        upgradePriceCoins[u].gameObject.SetActive(!maxed);
        upgradeAdChips[u].SetActive(adOffer);

        const float Coin = 46f, Gap = 10f, Chip = 42f;
        float textW = price.preferredWidth;
        float total = (maxed ? 0f : Coin + Gap) + textW + (adOffer ? Gap + Chip : 0f);
        float left = -total * 0.5f;
        // only across: up and down belong to the button's press
        if (!maxed)
        {
            SetX(upgradePriceCoins[u].rectTransform, left + Coin * 0.5f);
            left += Coin + Gap;
        }
        SetX(price.rectTransform, left + textW * 0.5f);
        left += textW;
        if (adOffer) SetX((RectTransform)upgradeAdChips[u].transform, left + Gap + Chip * 0.5f);
    }

    static void SetX(RectTransform rt, float x)
    {
        rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
    }

    static Texture2D adChipTex;

    /// <summary>The ad sign: a white play triangle on a green rounded square.</summary>
    static Texture2D GetAdChipTexture()
    {
        if (adChipTex != null) return adChipTex;
        const int S = 64;
        const float Rad = 16f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        Color green = new Color(0.22f, 0.70f, 0.30f, 1f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float qx = Mathf.Max(Mathf.Abs(fx - 32f) - (32f - Rad), 0f);
                float qy = Mathf.Max(Mathf.Abs(fy - 32f) - (32f - Rad), 0f);
                float edge = Rad - Mathf.Sqrt(qx * qx + qy * qy);   // how far inside
                float box = Mathf.Clamp01(edge + 0.5f);
                // a white rim, so it stands out on the green tile as well
                float rim = 1f - Mathf.Clamp01(edge - 3.5f);
                // the triangle, pointing right, its weight on the middle
                float t = (fx - 23f) / 26f;
                float half = (1f - t) * 15f;
                float tri = t >= 0f && t <= 1f ? Mathf.Clamp01(half - Mathf.Abs(fy - 32f) + 0.5f) : 0f;
                tri *= Mathf.Clamp01(fx - 23f + 0.5f);
                Color c = Color.Lerp(green, Color.white, Mathf.Max(tri, rim));
                c.a = box;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        adChipTex = tex;
        return tex;
    }

    /// <summary>Pushes upgrade effects onto the car.</summary>
    void ApplyUpgrades()
    {
        if (car == null) return;
        car.maxSpeed = baseMaxSpeed + Cars[selectedCar].speedBonus + RaceMode.SpeedBonus();
    }

    string UpgradeLine(RaceMode.Upgrade u)
    {
        int lv = RaceMode.Level(u);
        string value = u == RaceMode.Upgrade.CoinBonus ? RaceMode.CoinBonusLabel()
                     : u == RaceMode.Upgrade.Speed ? "+" + Loc.F(useMph ? "{0} MPH" : "{0} KM/H", Mathf.RoundToInt(RaceMode.SpeedBonus() * SpeedoScale * (useMph ? 2.23694f : 3.6f)))
                     : "+" + Mathf.RoundToInt(RaceMode.Strength() * 100f) + "%";
        // the picture carries the meaning, so the text just states the numbers
        // the price has a bar of its own along the bottom of the tile
        return "<size=33>" + Loc.F("LV {0}", lv) + "</size>\n<size=28>" + value + "</size>";
    }

    void OpenRaces()
    {
        menuPanel.SetActive(false);
        racePanel.SetActive(true);
        // the GP with the next race to win, the first time; after that the
        // one last looked at (or raced)
        if (raceViewBiome < 0) raceViewBiome = FrontierBiome();
        raceViewBiome = Mathf.Clamp(raceViewBiome, 0, LastShownBiome());
        raceSwitchT = -1f;
        raceBounceT = -1f;
        raceCard.anchoredPosition = Vector2.zero;
        raceFade.color = new Color(0f, 0f, 0f, 0f);
        RefreshRacePanel();
        if (lobbyBiomeShown != raceViewBiome)
        {
            // the car drives on in the GP's biome, coming up out of the dark
            SetLobbyBiome(raceViewBiome);
            raceFade.color = Color.black;
            raceSwitchDir = 0;
            raceSwitchApplied = true;
            raceSwitchT = RaceSwitchOut;
        }
        audioMan.PlayTap();
    }

    void CloseRaces()
    {
        if (upgradeAdPopup != null) upgradeAdPopup.SetActive(false);
        racePanel.SetActive(false);
        menuPanel.SetActive(true);
        if (lobbyBiomeShown != 0)
        {
            // back to the lobby as it always is, out of a quick black
            SetLobbyBiome(0);
            if (finishBlackout != null)
            {
                finishBlackout.gameObject.SetActive(true);
                finishBlackout.color = new Color(0f, 0f, 0f, 0.96f);
                finishRevealT = 0f;
            }
        }
        audioMan.PlayTap();
    }

    void RefreshRacePanel()
    {
        for (int u = 0; u < 3; u++)
        {
            upgradeLabels[u].text = UpgradeLine((RaceMode.Upgrade)u);
            RefreshUpgradePrice(u);
        }
        RefreshRaceCard();
    }

    void StartRace(int level)
    {
        if (!RaceMode.IsUnlocked(level)) { audioMan.PlayTap(); return; }

        EnsureRace();
        mode = Mode.Race;
        raceLevel = level;
        raceViewBiome = RaceMode.BiomeOf(level);
        raceFinished = false;

        racePanel.SetActive(false);

        // clean racing surface: no obstacles or traffic, boost pads instead
        track.spawnObstacles = false;
        track.spawnTraffic = false;
        track.spawnBoostPads = true;
        track.roadWidth = raceRoadWidth;     // three proper lanes to fight over
        track.roadBehindStart = 8f;          // no empty road visible behind the grid
        track.flatTrack = true;              // a level circuit, whatever the biome
        // A flat circuit with the stock corner mix reads as one long straight.
        // Tighter bends, held longer, with less road between them.
        track.curveTightness = 1.8f;
        track.straightBias = 0.45f;
        track.logSegments = RaceTrackDebug;

        // The biome goes in with the build request: Init generates the opening
        // stretch immediately, so a race in the city or the snow has to be
        // told before the track exists, not after.
        RaceMode.BlendsForBiome(RaceMode.BiomeOf(level), out float dayNight, out float snow);
        biomeBlend = dayNight;
        snowBlend = snow;
        nightAnnounced = snowAnnounced = true;   // no mid-race banners
        cyclingStarted = false;
        ApplyBiomeVisuals(biomeBlend);

        track.Init(startPos, startYaw, dayNight, snow);
        car.ResetRun(track);
        if (camFollow != null) camFollow.SnapToTarget();

        StartRun();

        // and hold that biome for the whole race - nothing cycles it
        track.SetBiomeBlend(dayNight);
        track.SetSnowBlend(snow);
        biomeBlend = dayNight;
        snowBlend = snow;

        // a race is a straight fight: no power-ups, tires or obstacles
        // scattered around, just the track, the field - and coins to pick up
        // on the way round, starting a little past the line
        track.spawnCoins = true;
        track.ResumeSpawnsAt(car.DistanceTraveled + 60f);
        track.spawnPowerUps = false;
        track.spawnTirePickups = false;
        track.spawnObstacles = false;
        track.spawnTraffic = false;
        track.ClearObstaclesAhead(car.DistanceTraveled, 600f);

        // races run at a constant pace - no ramp for anyone
        float racePace = raceBaseSpeed + RaceMode.SpeedBonus();
        car.SetConstantSpeed(racePace);
        car.ClearBoost();

        raceFinishDistance = car.DistanceTraveled + RaceMode.RaceDistance(level);
        track.RequestFinishLine(raceFinishDistance);
        // the whole field, player included, forms up behind the line
        track.BuildStartLine(car.DistanceTraveled + 24f);

        raceCountdown = 3f;      // 3 - 2 - 1, then GO on release
        car.steeringLocked = true;
        ApplyUpgrades();
        race.stockSpeedReference = raceBaseSpeed;
        race.BuildField(level, car.DistanceTraveled, raceBaseSpeed, MakeRacerVisual);
        race.PlaceAll();         // rivals sit on the grid while it counts down
        NameRaceRivals(level);   // gamertags over their cars (GameManager.RaceExtras.cs)
    }

    /// <summary>
    /// Builds an opponent car model at full size, upright and facing forward.
    /// </summary>
    GameObject MakeRacerVisual(int index)
    {
        int carIdx = 1 + ((index * 3 + raceLevel) % (Cars.Length - 1));
        if (carIdx == selectedCar) carIdx = 1 + (carIdx % (Cars.Length - 1));

        // the code-only car is not part of the world - it never lines up on a
        // grid, however the index happens to land
        for (int guard = 0; guard < Cars.Length && DevOnlyCar(carIdx); guard++)
        {
            carIdx = 1 + (carIdx % (Cars.Length - 1));
        }

        // The same builder the player's own car uses. This used to be a
        // near-copy that seated the model on the very lowest point of its
        // bounds and skipped the per-model height corrections - so any car
        // with a stray low part, or one that needed a correction, sat in the
        // air on the grid while the identical model on the road did not.
        return BuildCarModel(carIdx);
    }

    /// <summary>Runs the 3-2-1-GO sequence. Returns true while it is running.</summary>
    bool TickCountdown(float dt)
    {
        if (raceCountdown <= 0f) return false;

        raceCountdown -= dt;
        if (raceCountdown <= 0f)
        {
            // GO! stays up briefly while everyone is already moving
            goFlashT = 0.85f;
            countdownText.text = "GO!";
            countdownText.color = new Color(0.4f, 1f, 0.5f);
            countdownText.transform.localRotation = Quaternion.identity;
            countdownText.gameObject.SetActive(true);
            audioMan.PlayCountGo();
            car.steeringLocked = false;
            if (mode == Mode.Race) track.SetStartLights(0, true);   // lights out, go
            else OpenTheRoad();                                     // items start now
            audioMan.StartDriving();
            audioMan.PlayGameMusic();     // the driving theme takes over
            return false;
        }

        // 3, 2, 1 - and GO! is not shown until the cars are actually released,
        // so the word and the launch happen on the same frame
        int n = Mathf.CeilToInt(raceCountdown);
        string label = n.ToString();

        // the gantry lights fill up red as the count runs down
        if (mode == Mode.Race)
        {
            // one row per number: 3 lights the top row, 2 the next, 1 the third
            int rows = Mathf.Clamp(4 - Mathf.CeilToInt(raceCountdown), 0, 3);
            track.SetStartLights(rows, false);
        }
        if (countdownText.text != label)
        {
            countdownText.text = label;
            countdownText.color = new Color(1f, 0.8f, 0.2f);
            audioMan.PlayCountBeep();
        }

        // frac counts 1 -> 0 across each number
        float frac = raceCountdown - Mathf.Floor(raceCountdown);
        float age = 1f - frac;                       // 0 the instant it appears

        // slams in oversized, settles, then shrinks away as the next one comes
        float pop = 2.4f * Mathf.Exp(-11f * age);    // big overshoot, fast settle
        float shrink = Mathf.Clamp01((age - 0.72f) / 0.28f);
        float scale = (1f + pop) * (1f - shrink * 0.55f);

        countdownText.transform.localScale = Vector3.one * scale;
        // a little kick of rotation that straightens out
        countdownText.transform.localRotation =
            Quaternion.Euler(0f, 0f, Mathf.Sin(age * 26f) * 9f * Mathf.Exp(-7f * age));

        // flashes white on impact, then fades out before the next number
        Color c = Color.Lerp(Color.white, new Color(1f, 0.8f, 0.2f), Mathf.Clamp01(age * 6f));
        c.a = 1f - shrink;
        countdownText.color = c;

        countdownText.gameObject.SetActive(true);
        return true;
    }

    float goFlashT;

    /// <summary>Fades the GO! away while the race is already under way.</summary>
    void TickGoFlash()
    {
        if (goFlashT <= 0f) return;

        goFlashT -= Time.deltaTime;
        float p = Mathf.Clamp01(1f - goFlashT / 0.85f);
        countdownText.transform.localScale = Vector3.one * (1.9f - 0.7f * Mathf.Min(1f, p * 3f));
        countdownText.color = new Color(0.4f, 1f, 0.5f, 1f - p * p);

        if (goFlashT <= 0f)
        {
            countdownText.gameObject.SetActive(false);
            countdownText.transform.localScale = Vector3.one;
        }
    }

    // ------------------------------------------------------- daily spin wheel

    enum SpinPrize { Coins, Tires, Tokens, Car }

    struct WheelSlot
    {
        public SpinPrize kind;
        public int amount;
        public string label;
        public Color color;
        public int weight;

        // which token a token slice pays out - so the emblem drawn on the
        // slice is the one you actually win
        public Currency currency;

        public WheelSlot(SpinPrize k, int amt, string text, Color c, int w,
                         Currency cur = Currency.Coins)
        {
            kind = k; amount = amt; label = text; color = c; weight = w;
            currency = cur;
        }
    }

    static readonly WheelSlot[] Wheel =
    {
        // Every token has a slice of its own - four marques, four emblems.
        // Their weights together come to about what the two generic token
        // slices used to be worth, so tokens are no more common than before.
        new WheelSlot(SpinPrize.Coins,   500,  "500",  new Color(0.95f,0.72f,0.15f), 26),
        new WheelSlot(SpinPrize.Tires,     3,  "3",    new Color(0.35f,0.62f,0.92f), 18),
        new WheelSlot(SpinPrize.Tokens,    5,  "5",    new Color(0.62f,0.50f,0.16f),  7, Currency.Cyber),
        new WheelSlot(SpinPrize.Coins,  2000,  "2000", new Color(0.98f,0.55f,0.18f), 16),
        new WheelSlot(SpinPrize.Tokens,    4,  "4",    new Color(0.50f,0.31f,0.15f),  5, Currency.Tempasta),
        new WheelSlot(SpinPrize.Tires,    10,  "10",   new Color(0.22f,0.70f,0.78f),  8),
        new WheelSlot(SpinPrize.Tokens,    3,  "3",    new Color(0.58f,0.24f,0.42f),  4, Currency.Caldera),
        new WheelSlot(SpinPrize.Coins,  8000,  "8000", new Color(0.92f,0.40f,0.30f),  8),
        new WheelSlot(SpinPrize.Tokens,    2,  "2",    new Color(0.38f,0.40f,0.48f),  3, Currency.Vettura),
        new WheelSlot(SpinPrize.Car,       0,  "CAR",  new Color(0.20f,0.78f,0.42f),  1),
    };

    const int DailySpins = 2;
    GameObject wheelPanel;
    RectTransform wheelDisc;
    readonly RawImage[] wheelSliceIcons = new RawImage[Wheel.Length];
    readonly Text[] wheelSliceLabels = new Text[Wheel.Length];

    /// <summary>
    /// Repaints any token slice whose marque is fully collected as a coin
    /// prize, so the wheel never advertises something worthless.
    /// </summary>
    void RefreshWheelSlices()
    {
        for (int i = 0; i < Wheel.Length; i++)
        {
            if (wheelSliceIcons[i] == null) continue;
            WheelSlot slot = Wheel[i];
            bool retired = slot.kind == SpinPrize.Tokens
                           && !TokenStillUseful(slot.currency);

            wheelSliceIcons[i].texture = Resources.Load<Texture2D>(
                retired ? CoinIconPath : WheelIconPath(slot));
            if (wheelSliceLabels[i] != null)
            {
                wheelSliceLabels[i].text = retired
                    ? CoinsInsteadOfTokens(slot.amount).ToString()
                    : slot.label;
            }
        }
    }
    Text wheelSpinsText, wheelResultText, spinMenuLabel, wheelSpinLabel, wheelAdLabel;
    Texture2D wheelFaceTex;     // the wheel's face - also the daily reward's spin picture
    float wheelAngle, wheelFrom, wheelTo, wheelT, wheelDuration;
    int lastWheelTick;
    const int WheelPegsPerSlice = 1;
    bool wheelSpinning;
    int wheelResultIndex = -1;

    int SpinsLeft
    {
        get
        {
            string today = System.DateTime.Now.ToString("yyyyMMdd");
            if (PlayerPrefs.GetString("SpinDay", "") != today)
            {
                PlayerPrefs.SetString("SpinDay", today);
                PlayerPrefs.SetInt("SpinsLeft", DailySpins);
                PlayerPrefs.Save();
            }
            return PlayerPrefs.GetInt("SpinsLeft", DailySpins);
        }
        set { PlayerPrefs.SetInt("SpinsLeft", value); PlayerPrefs.Save(); }
    }

    void OpenWheel()
    {
        menuPanel.SetActive(false);
        wheelPanel.SetActive(true);
        wheelResultText.text = "";
        RefreshWheel();
        audioMan.PlayTap();
    }

    void CloseWheel()
    {
        if (wheelSpinning) return;          // no walking out mid-spin
        wheelPanel.SetActive(false);
        menuPanel.SetActive(true);
        RefreshSpinBadge();
        audioMan.PlayTap();
    }

    void RefreshWheel()
    {
        int left = SpinsLeft;
        wheelSpinsText.text = left > 0 ? Loc.F("{0}  SPINS LEFT", left) : Loc.T("BUY A SPIN BELOW");
        wheelSpinLabel.text = left > 0 ? "SPIN" : "NONE LEFT";
        var img = wheelSpinLabel.transform.parent.GetComponent<Image>();
        if (img != null)
        {
            img.color = left > 0 ? new Color(0.55f, 0.30f, 0.85f)
                                 : new Color(0.35f, 0.32f, 0.45f);
        }

        if (wheelAdLabel != null)
        {
            bool adReady = !AdSpinUsed;
            wheelAdLabel.text = adReady ? "WATCH AD" : "AD USED";
            var adImg = wheelAdLabel.transform.parent.GetComponent<Image>();
            if (adImg != null)
            {
                adImg.color = adReady ? new Color(0.30f, 0.62f, 0.35f)
                                      : new Color(0.35f, 0.32f, 0.45f);
            }
        }
        RefreshWheelSlices();
        RefreshSpinBadge();
    }

    /// <summary>Marks the menu button while there are spins waiting.</summary>
    void RefreshSpinBadge()
    {
        if (spinMenuLabel == null) return;
        int left = SpinsLeft;
        spinMenuLabel.text = "SPIN";
        // the red dot in the corner says there is a spin waiting
        if (spinBadge != null) spinBadge.SetActive(left > 0);

        var img = spinMenuLabel.transform.parent.GetComponent<Image>();
        if (img != null)
        {
            // gold and lively while there is a spin waiting, grey once used
            img.color = left > 0 ? new Color(1f, 0.78f, 0.12f)
                                 : new Color(0.35f, 0.32f, 0.45f);
        }
        spinMenuLabel.color = Color.white;

        if (spinButtonRt == null)
        {
            spinButtonRt = spinMenuLabel.transform.parent as RectTransform;
            if (spinButtonRt != null) spinButtonHome = spinButtonRt.anchoredPosition;
        }
        if (left <= 0 && spinButtonRt != null)
        {
            spinButtonRt.anchoredPosition = spinButtonHome;
            spinButtonRt.localScale = Vector3.one;
            spinButtonRt.localRotation = Quaternion.identity;
        }
    }

    RectTransform spinButtonRt;
    Vector2 spinButtonHome;

    /// <summary>The red dot on the menu's SPIN button, shown while spins are waiting.</summary>
    GameObject spinBadge;

    /// <summary>
    /// Nudges the spin button about while a spin is unclaimed - a small,
    /// constant motion in the corner of the eye is what actually gets it
    /// noticed among six identical buttons.
    /// </summary>
    void AnimateSpinButton()
    {
        if (spinButtonRt == null || SpinsLeft <= 0) return;
        BounceButton(spinButtonRt, spinButtonHome, 0f);
    }

    /// <summary>
    /// A slow breath with a quick double-bounce every few seconds - the
    /// "something is waiting here" motion of SPIN and LOGIN STREAK.
    /// </summary>
    static void BounceButton(RectTransform rt, Vector2 home, float phase)
    {
        float t = Time.unscaledTime + phase;
        float beat = Mathf.Repeat(t, 2.6f);
        float pop = beat < 0.5f ? Mathf.Sin(beat / 0.5f * Mathf.PI * 2f) : 0f;

        rt.anchoredPosition = home + new Vector2(0f, Mathf.Sin(t * 2.2f) * 4f + pop * 9f);
        rt.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.2f) * 0.015f + Mathf.Abs(pop) * 0.05f);
        rt.localRotation = Quaternion.Euler(0f, 0f, pop * 2.5f);
    }

    const int SpinTireCost = 5;

    /// <summary>One free spin a day in exchange for watching an ad.</summary>
    bool AdSpinUsed
    {
        get
        {
            return PlayerPrefs.GetString("SpinAdDay", "")
                   == System.DateTime.Now.ToString("yyyyMMdd");
        }
    }

    void WatchAdForSpin()
    {
        if (wheelSpinning) return;
        if (AdSpinUsed)
        {
            wheelResultText.text = "AD SPIN USED TODAY";
            wheelResultText.color = new Color(0.75f, 0.75f, 0.8f);
            audioMan.PlayTap();
            return;
        }

        wheelResultText.text = "LOADING AD...";
        wheelResultText.color = new Color(0.75f, 0.75f, 0.8f);

        AdManager.ShowRewarded("spin", () =>
        {
            // only reached when the ad reports a completed view
            PlayerPrefs.SetString("SpinAdDay", System.DateTime.Now.ToString("yyyyMMdd"));
            SpinsLeft = SpinsLeft + 1;

            wheelResultText.text = "+1 SPIN";
            wheelResultText.color = new Color(0.55f, 1f, 0.65f);
            RefreshWheel();
            audioMan.PlayCoin();
        },
        () =>
        {
            // nothing to show, or the ad was closed early - no spin, no charge
            wheelResultText.text = "NO AD AVAILABLE";
            wheelResultText.color = new Color(1f, 0.55f, 0.5f);
            audioMan.PlayTap();
        });
    }

    /// <summary>Buys one extra spin for tires, on top of the daily two.</summary>
    void BuySpinWithTires()
    {
        if (wheelSpinning) return;
        if (tires < SpinTireCost)
        {
            wheelResultText.text = "NOT ENOUGH TIRES";
            wheelResultText.color = new Color(1f, 0.4f, 0.35f);
            audioMan.PlayTap();
            return;
        }

        tires -= SpinTireCost;
        PlayerPrefs.SetInt("Tires", tires);
        SpinsLeft = SpinsLeft + 1;

        wheelResultText.text = "+1 SPIN";
        wheelResultText.color = new Color(0.7f, 0.9f, 1f);
        RefreshWheel();
        audioMan.PlayCoin();
    }

    void SpinWheel()
    {
        if (wheelSpinning) return;
        if (SpinsLeft <= 0) { audioMan.PlayTap(); return; }

        SpinsLeft = SpinsLeft - 1;
        wheelResultText.text = "";

        // pick the prize first, then spin the wheel so it lands on it
        int idx = PickWheelSlot(out float frac);

        float seg = 360f / Wheel.Length;
        // Slice i spans (i*seg) to ((i+1)*seg) measured clockwise from the top,
        // and a positive Z rotation turns the disc anticlockwise - so rotating
        // by that angle brings the chosen spot up under the pointer.
        float landing = idx * seg + seg * frac;
        wheelFrom = wheelAngle;
        wheelTo = landing + 360f * Random.Range(7, 11);
        while (wheelTo < wheelFrom + 2520f) wheelTo += 360f;
        wheelT = 0f;
        wheelDuration = 5.6f;
        lastWheelTick = Mathf.FloorToInt(
            wheelAngle / (360f / (Wheel.Length * WheelPegsPerSlice)));
        wheelResultIndex = idx;
        wheelSpinning = true;
        RefreshWheel();
        audioMan.PlayNearMiss();   // the swoosh doubles as the wheel spin
    }

    /// <summary>
    /// Chooses the slice: a straight weighted draw on the slice weights, every
    /// spin, nothing steered. Those weights are exactly what the ODDS screen
    /// shows, so they must stay the only thing that decides the prize.
    /// </summary>
    /// <param name="frac">
    /// Where inside the slice to stop, 0 to 1. Never dead centre, never on a
    /// seam, so the pointer always reads clearly as the prize won.
    /// </param>
    int PickWheelSlot(out float frac)
    {
        // anywhere in the slice except the very middle and the seams
        frac = Random.value < 0.5f ? Random.Range(0.14f, 0.40f)
                                   : Random.Range(0.60f, 0.86f);

        int total = 0;
        for (int i = 0; i < Wheel.Length; i++) total += Wheel[i].weight;
        int roll = Random.Range(0, total);
        for (int i = 0; i < Wheel.Length; i++)
        {
            roll -= Wheel[i].weight;
            if (roll < 0) return i;
        }
        return 0;
    }

    void TickWheel()
    {
        if (!wheelSpinning) return;

        wheelT += Time.unscaledDeltaTime;
        float p = Mathf.Clamp01(wheelT / wheelDuration);
        // heavy flywheel: whips round, then creeps into place
        // a long, heavy run-down: quick at first, then a slow final crawl
        float eased = 1f - Mathf.Pow(1f - p, 5f);
        wheelAngle = Mathf.Lerp(wheelFrom, wheelTo, eased);
        wheelDisc.localRotation = Quaternion.Euler(0f, 0f, wheelAngle);

        // one click every time a new slice passes the pointer - it slows down
        // with the wheel, which is the whole sound of a real prize wheel
        float pegSpacing = 360f / (Wheel.Length * WheelPegsPerSlice);
        int tick = Mathf.FloorToInt(wheelAngle / pegSpacing);
        if (tick != lastWheelTick)
        {
            lastWheelTick = tick;
            audioMan.PlayWheelTick(1.25f - 0.35f * p + Random.Range(-0.03f, 0.03f),
                                   0.7f + 0.3f * p);
        }

        if (p < 1f) return;

        wheelSpinning = false;
        wheelAngle = wheelTo % 360f;
        GrantSpinPrize(wheelResultIndex);
        RefreshWheel();
    }

    void GrantSpinPrize(int idx)
    {
        WheelSlot s = Wheel[idx];
        string msg;
        Color col = s.color;

        switch (s.kind)
        {
            case SpinPrize.Coins:
                totalCoins += s.amount;
                PlayerPrefs.SetInt("Coins", totalCoins);
                msg = Loc.F("+{0} COINS", s.amount);
                break;

            case SpinPrize.Tires:
                tires += s.amount;
                PlayerPrefs.SetInt("Tires", tires);
                msg = Loc.F("+{0} TIRES", s.amount);
                break;

            case SpinPrize.Tokens:
            {
                Currency cur = s.currency == Currency.Coins
                    ? (Currency)Random.Range(1, 5) : s.currency;

                // that marque is fully collected - pay coins rather than a
                // currency with nothing left to spend it on
                if (!TokenStillUseful(cur))
                {
                    int paid = CoinsInsteadOfTokens(s.amount);
                    totalCoins += paid;
                    PlayerPrefs.SetInt("Coins", totalCoins);
                    msg = Loc.F("+{0} COINS", paid);
                    col = TokenColors[0];
                    break;
                }
                SetToken(cur, GetToken(cur) + s.amount);
                msg = Loc.F("+{0} {1}", s.amount, Loc.T(TokenNames[(int)cur]));
                col = TokenColors[(int)cur];
                break;
            }

            default:
            {
                int car = PickWheelCar();
                if (car < 0)
                {
                    // every coin car already owned - pay out instead
                    totalCoins += WheelCarCoins;
                    PlayerPrefs.SetInt("Coins", totalCoins);
                    msg = Loc.F("BONUS!  +{0} COINS", WheelCarCoins);
                }
                else
                {
                    PlayerPrefs.SetInt("CarOwned" + car, 1);
                    msg = Loc.F("NEW CAR!  {0}", Cars[car].name);
                }
                break;
            }
        }

        PlayerPrefs.Save();
        wheelResultText.text = msg;
        wheelResultText.color = col;
        RefreshWeeklyMenu();
        // the wheel gets the same full-screen celebration as the boxes
        ShowPrizeReveal(msg, s.kind == SpinPrize.Tokens, WheelIconPath(s));
    }

    /// <summary>What the CAR slice pays once every coin car is in the garage.</summary>
    const int WheelCarCoins = 25000;

    /// <summary>A coin-priced car the player does not own yet.</summary>
    int PickWheelCar()
    {
        var pool = new List<int>();
        for (int i = 0; i < Cars.Length; i++)
        {
            if (OwnedCar(i) || Cars[i].cost <= 0) continue;
            if (Cars[i].currency != Currency.Coins) continue;
            pool.Add(i);
        }
        return pool.Count == 0 ? -1 : pool[Random.Range(0, pool.Count)];
    }

    static string WheelIconPath(WheelSlot slot)
    {
        switch (slot.kind)
        {
            case SpinPrize.Coins: return "UI/wheel_coins";
            case SpinPrize.Tires: return "UI/wheel_tires";
            case SpinPrize.Tokens:
                // each token slice wears its own emblem
                return slot.currency == Currency.Coins
                    ? "UI/wheel_token" : TokenIcons[(int)slot.currency];
            default: return "UI/wheel_car";
        }
    }

    /// <summary>Gold triangle pointing straight down at the wheel.</summary>
    static Texture2D MakePointerTexture()
    {
        const int W = 96, H = 112;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        Color gold = new Color(1f, 0.82f, 0.12f, 1f);
        Color edge = new Color(0.35f, 0.22f, 0.04f, 1f);
        Color clear = new Color(0f, 0f, 0f, 0f);

        for (int y = 0; y < H; y++)
        {
            // y = 0 is the bottom of the texture: the tip
            float t = y / (float)(H - 1);
            float halfWidth = t * (W * 0.5f);
            for (int x = 0; x < W; x++)
            {
                float dx = Mathf.Abs(x - (W - 1) * 0.5f);
                Color c = dx > halfWidth ? clear
                        : dx > halfWidth - 5f ? edge
                        : gold;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>Flat-coloured pie chart used as the wheel face.</summary>
    static Texture2D MakeWheelTexture(int slices)
    {
        const int S = 512;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float half = (S - 1) * 0.5f;

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) { tex.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }

                // rim
                if (r > 0.94f) { tex.SetPixel(x, y, new Color(0.14f, 0.10f, 0.22f, 1f)); continue; }

                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;   // 0 at the top
                if (ang < 0f) ang += 360f;
                int idx = Mathf.Clamp((int)(ang / (360f / slices)), 0, slices - 1);
                Color c = Wheel[idx].color;

                // a darker band at every seam so the slices read apart
                float within = (ang % (360f / slices)) / (360f / slices);
                if (within < 0.012f || within > 0.988f) c *= 0.45f;
                if (r < 0.16f) c = new Color(0.14f, 0.10f, 0.22f);  // hub

                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void BuildWheelPanel(Transform uiRoot)
    {
        wheelPanel = MakePanel(uiRoot, "WheelPanel");

        var backGo = new GameObject("Backdrop");
        backGo.transform.SetParent(wheelPanel.transform, false);
        var back = backGo.AddComponent<Image>();
        back.color = new Color(0.06f, 0.05f, 0.12f, 0.94f);
        var backRt = back.rectTransform;
        backRt.anchorMin = Vector2.zero;
        backRt.anchorMax = Vector2.one;
        backRt.offsetMin = new Vector2(-200f, -400f);
        backRt.offsetMax = new Vector2(200f, 400f);

        var title = MakeText(wheelPanel.transform, "WheelTitle", 76, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 700f), new Vector2(900f, 120f));
        title.text = "MYSTERY WHEEL";
        title.color = new Color(1f, 0.72f, 0.12f);

        // information is set upright and plain - the racing font is for titles
        wheelSpinsText = MakeText(wheelPanel.transform, "WheelSpins", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 610f), new Vector2(900f, 70f), true);
        wheelSpinsText.color = new Color(0.86f, 0.92f, 1f);

        // the disc itself, with its labels riding around with it
        var discGo = new GameObject("Disc");
        discGo.transform.SetParent(wheelPanel.transform, false);
        wheelDisc = discGo.AddComponent<RectTransform>();
        wheelDisc.anchorMin = wheelDisc.anchorMax = wheelDisc.pivot = new Vector2(0.5f, 0.5f);
        wheelDisc.anchoredPosition = new Vector2(0f, 170f);
        wheelDisc.sizeDelta = new Vector2(700f, 700f);

        var face = new GameObject("Face");
        face.transform.SetParent(discGo.transform, false);
        var faceImg = face.AddComponent<RawImage>();
        wheelFaceTex = MakeWheelTexture(Wheel.Length);
        faceImg.texture = wheelFaceTex;
        faceImg.raycastTarget = false;
        var faceRt = faceImg.rectTransform;
        faceRt.anchorMin = Vector2.zero;
        faceRt.anchorMax = Vector2.one;
        faceRt.offsetMin = faceRt.offsetMax = Vector2.zero;

        float seg = 360f / Wheel.Length;
        for (int i = 0; i < Wheel.Length; i++)
        {
            float ang = i * seg + seg * 0.5f;
            float rad = ang * Mathf.Deg2Rad;
            Vector2 at = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

            // a picture of the prize, with the amount written under it
            var icoGo = new GameObject("SliceIcon" + i);
            wheelSliceIcons[i] = null;   // filled in just below
            icoGo.transform.SetParent(discGo.transform, false);
            var ico = icoGo.AddComponent<RawImage>();
            ico.raycastTarget = false;
            ico.texture = Resources.Load<Texture2D>(WheelIconPath(Wheel[i]));
            var icoRt = ico.rectTransform;
            icoRt.anchorMin = icoRt.anchorMax = icoRt.pivot = new Vector2(0.5f, 0.5f);
            // well inside the rim: at 288 the art ran off the edge of the disc
            icoRt.anchoredPosition = at * 238f;
            icoRt.sizeDelta = new Vector2(92f, 92f);
            icoRt.localRotation = Quaternion.Euler(0f, 0f, -ang);
            wheelSliceIcons[i] = ico;

            var label = MakeText(discGo.transform, "Slice" + i, 36, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 70f), true);
            label.text = Wheel[i].label;
            label.color = Color.white;
            label.rectTransform.anchoredPosition = at * 152f;
            label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -ang);
            wheelSliceLabels[i] = label;
        }
        RefreshWheelSlices();

        // pointer at the top, a triangle biting down into the wheel
        var ptr = new GameObject("Pointer");
        ptr.transform.SetParent(wheelPanel.transform, false);
        var ptrImg = ptr.AddComponent<RawImage>();
        ptrImg.texture = MakePointerTexture();
        ptrImg.raycastTarget = false;
        var ptrRt = ptrImg.rectTransform;
        ptrRt.anchorMin = ptrRt.anchorMax = ptrRt.pivot = new Vector2(0.5f, 0.5f);
        ptrRt.anchoredPosition = new Vector2(0f, 170f + 342f);
        ptrRt.sizeDelta = new Vector2(84f, 100f);

        wheelResultText = MakeText(wheelPanel.transform, "WheelResult", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(960f, 90f), true);

        wheelSpinLabel = MakeButton(wheelPanel.transform, "SPIN", 54,
            new Vector2(0f, -390f), new Vector2(520f, 124f), SpinWheel,
            new Color(0.55f, 0.30f, 0.85f));

        // the chance of every slice, there before a spin is ever bought
        MakeOddsButton(wheelPanel.transform, new Vector2(385f, -390f),
            new Vector2(200f, 96f), OddsFor.Wheel);

        // two ways to get another go, side by side
        var buyLabel = MakeButton(wheelPanel.transform, "+1  " + SpinTireCost, 34,
            new Vector2(-140f, -520f), new Vector2(260f, 104f), BuySpinWithTires,
            new Color(0.20f, 0.45f, 0.80f));
        MakeCurrencyIcon(buyLabel.transform.parent, new Vector2(0.5f, 0.5f),
            new Vector2(72f, 0f), 46f, true);
        buyLabel.rectTransform.anchoredPosition = new Vector2(-22f, 0f);

        wheelAdLabel = MakeButton(wheelPanel.transform, "WATCH AD", 30,
            new Vector2(140f, -520f), new Vector2(260f, 104f), WatchAdForSpin,
            new Color(0.30f, 0.62f, 0.35f));

        // clear of the +1 / WATCH AD pair, which end at -572
        MakeCloseButton(wheelPanel.transform, new Vector2(0f, -690f), CloseWheel);

        wheelPanel.SetActive(false);
    }

    // ------------------------------------------------- race finish cinematic

    Image finishBarTop, finishBarBottom, finishBlackout;
    Text finishPlaceText, finishPlaceSuffix;
    RectTransform finishPlaceRt;
    float finishAnimT = -1f;
    int finishPlace;
    const float FinishTension = 140f;   // metres of run-in that closes the bars
    const float BarHeight = 300f;

    void BuildFinishCinematic(Transform uiRoot)
    {
        finishBarTop = MakeLetterboxBar(uiRoot, "FinishBarTop", 1f);
        finishBarBottom = MakeLetterboxBar(uiRoot, "FinishBarBottom", 0f);

        var blackGo = new GameObject("FinishBlackout");
        blackGo.transform.SetParent(uiRoot, false);
        finishBlackout = blackGo.AddComponent<Image>();
        finishBlackout.color = new Color(0f, 0f, 0f, 0f);
        finishBlackout.raycastTarget = false;
        var bRt = finishBlackout.rectTransform;
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = new Vector2(-200f, -400f);
        bRt.offsetMax = new Vector2(200f, 400f);
        blackGo.SetActive(false);

        // The number and its suffix are separate so the suffix can ride high
        // and small, the way an ordinal is actually written.
        var placeGo = new GameObject("FinishPlace");
        placeGo.transform.SetParent(uiRoot, false);
        finishPlaceRt = placeGo.AddComponent<RectTransform>();
        finishPlaceRt.anchorMin = finishPlaceRt.anchorMax = finishPlaceRt.pivot =
            new Vector2(0.5f, 0.5f);
        finishPlaceRt.sizeDelta = new Vector2(1000f, 300f);

        finishPlaceText = MakeText(placeGo.transform, "Number", 180, TextAnchor.MiddleRight,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 300f));
        finishPlaceText.color = new Color(1f, 0.82f, 0.12f);
        // pivot on its right edge, sitting just left of centre
        var numRt = finishPlaceText.rectTransform;
        numRt.pivot = new Vector2(1f, 0.5f);
        numRt.anchoredPosition = new Vector2(-34f, 0f);

        finishPlaceSuffix = MakeText(placeGo.transform, "Suffix", 76, TextAnchor.MiddleLeft,
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 140f));
        finishPlaceSuffix.color = new Color(1f, 0.82f, 0.12f);
        // pivot on its left edge, raised, so it reads as a proper ordinal
        var sufRt = finishPlaceSuffix.rectTransform;
        sufRt.pivot = new Vector2(0f, 0.5f);
        sufRt.anchoredPosition = new Vector2(8f, 34f);

        placeGo.SetActive(false);
    }

    Image MakeLetterboxBar(Transform uiRoot, string name, float edge)
    {
        var go = new GameObject(name);
        go.transform.SetParent(uiRoot, false);
        var img = go.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, edge);
        rt.anchorMax = new Vector2(1f, edge);
        rt.pivot = new Vector2(0.5f, edge);
        rt.offsetMin = new Vector2(-200f, 0f);
        rt.offsetMax = new Vector2(200f, 0f);
        rt.sizeDelta = new Vector2(400f, 0f);
        // Pushed out past the safe area. Anchored flush to the edge, the bar
        // stopped where the UI stops and the road showed through the strip
        // above the notch and below the home bar.
        rt.anchoredPosition = new Vector2(0f, (edge > 0.5f ? 1f : -1f) * PanelBleed.y);
        go.SetActive(false);
        return img;
    }

    /// <summary>Closes the letterbox as the finish line comes up.</summary>
    void UpdateFinishTension()
    {
        if (finishBarTop == null || finishAnimT >= 0f) return;

        float remaining = raceFinishDistance - car.DistanceTraveled;
        float t = Mathf.Clamp01(1f - remaining / FinishTension);
        // eases in, so the squeeze builds rather than starting abruptly
        float h = BarHeight * t * t;

        bool show = t > 0.001f;
        if (finishBarTop.gameObject.activeSelf != show)
        {
            finishBarTop.gameObject.SetActive(show);
            finishBarBottom.gameObject.SetActive(show);
        }
        if (!show) return;

        // the extra height is the part hanging off the screen; the inner edge
        // still lands exactly h from the safe-area edge
        finishBarTop.rectTransform.sizeDelta = new Vector2(400f, h + PanelBleed.y);
        finishBarBottom.rectTransform.sizeDelta = new Vector2(400f, h + PanelBleed.y);
    }

    void PlayFinishCinematic(int place)
    {
        finishPlace = place;
        finishAnimT = 0f;
        audioMan.PlayFinishWhoosh();   // big whoosh as the screen slams shut
        if (finishBlackout != null) finishBlackout.gameObject.SetActive(true);
        if (finishPlaceRt != null)
        {
            finishPlaceRt.gameObject.SetActive(true);
            finishPlaceText.text = place.ToString();
            finishPlaceSuffix.text = Ordinal(place);
        }
    }

    static string Ordinal(int place)
    {
        // each language's own ending (French 1er/2e, Japanese 1位 ...),
        // and some languages have none
        return Loc.T(place == 1 ? "ORD 1" : place == 2 ? "ORD 2" : place == 3 ? "ORD 3" : "ORD N");
    }

    /// <summary>
    /// Screen goes black, then the finishing position slides in from the side
    /// and settles in the middle.
    /// </summary>
    void TickFinishCinematic()
    {
        if (finishAnimT < 0f) return;

        finishAnimT += Time.unscaledDeltaTime;

        // bars slam the rest of the way shut as the screen blacks out
        float shut = Mathf.Clamp01(finishAnimT / 0.25f);
        if (finishBarTop != null && finishBarTop.gameObject.activeSelf)
        {
            float h = Mathf.Lerp(BarHeight, 1200f, shut) + PanelBleed.y;
            finishBarTop.rectTransform.sizeDelta = new Vector2(400f, h);
            finishBarBottom.rectTransform.sizeDelta = new Vector2(400f, h);
        }
        finishBlackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01(finishAnimT / 0.3f) * 0.96f);

        // the position slides in once the screen is dark
        float p = Mathf.Clamp01((finishAnimT - 0.3f) / 0.45f);
        float eased = 1f - Mathf.Pow(1f - p, 3f);
        finishPlaceRt.anchoredPosition = new Vector2(Mathf.Lerp(900f * UiWidthScale(), 0f, eased), 0f);
        finishPlaceRt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, eased);
        float a = Mathf.Clamp01(p * 2f);
        finishPlaceText.color = new Color(1f, 0.82f, 0.12f, a);
        finishPlaceSuffix.color = new Color(1f, 0.82f, 0.12f, a);

        // then out: the place slides away and the race list comes up
        // (GameManager.RaceExtras.cs)
        TickFinishOut();
    }

    void EndFinishCinematic()
    {
        bool wasPlaying = finishAnimT >= 0f;
        finishAnimT = -1f;
        if (finishBarTop != null) finishBarTop.gameObject.SetActive(false);
        if (finishBarBottom != null) finishBarBottom.gameObject.SetActive(false);
        if (finishBlackout != null) finishBlackout.gameObject.SetActive(false);
        if (finishPlaceRt != null) finishPlaceRt.gameObject.SetActive(false);

        if (!wasPlaying || state != State.GameOver) return;

        // a finished race drops you straight back into the race list, so you
        // can go again without a screen in between
        if (mode == Mode.Race)
        {
            GoToMenu();
            OpenRaces();
        }
        else
        {
            // the results card was already filled in by ShowGameOver
            gameOverPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Switches the spawners back on once the countdown is over, starting far
    /// enough ahead that the first coin or cone is not already on screen.
    /// </summary>
    void OpenTheRoad()
    {
        if (track == null) return;
        track.spawnObstacles = true;
        track.spawnTraffic = true;
        track.spawnCoins = true;
        track.spawnPowerUps = true;
        track.spawnTirePickups = true;
        track.ResumeSpawnsAt(car.DistanceTraveled + 120f);
        // the camera is behind the car from here on, so the world no longer
        // needs to be kept alive way back down the road
        track.behindDistance = runBehindDistance;
        CoachRoadRules();     // the tutorial's drive keeps it clear at first
    }

    void TickRace(float dt)
    {
        if (race == null || raceFinished) return;

        race.Tick(dt, raceFinishDistance, car.DistanceTraveled, car.CurrentSpeed);

        // cars are solid: you cannot steer through a rival alongside you
        if (race.GetPlayerLateralLimits(car.DistanceTraveled, car.LateralOffset, car.carRadius,
                RaceMode.Strength(), dt, out float minLat, out float maxLat))
        {
            car.BlockLateral(minLat, maxLat, dt);
        }

        // boost pads
        if (track.TryCollectBoost(car.DistanceTraveled, car.LateralOffset, car.carRadius, dt))
        {
            car.Boost(14f);
            FlashBonus("BOOST!", new Color(0.3f, 0.9f, 1f));
            audioMan.PlayNearMiss();
        }

        float togo = Mathf.Max(0f, raceFinishDistance - car.DistanceTraveled);
        int place = race.PlayerPlace(car.DistanceTraveled);
        raceHudText.text = Loc.F("P{0}/{1}", place, RaceMode.Opponents + 1)
                           + "\n<size=24>" + Loc.F("{0}M", Mathf.RoundToInt(togo)) + "</size>";

        UpdateFinishTension();
        if (car.DistanceTraveled >= raceFinishDistance) FinishRace(place);
    }

    void FinishRace(int place)
    {
        raceFinished = true;
        int nextGp = RaceMode.BiomeOf(raceLevel) + 1;
        bool nextWasOpen = nextGp < RaceMode.BiomeCount && BiomeOpen(nextGp);
        RaceMode.RecordResult(raceLevel, place);
        // the first time it opens, the race list plays its lock coming off
        if (nextGp < RaceMode.BiomeCount && !nextWasOpen && BiomeOpen(nextGp))
            raceUnlockBiome = nextGp;
        // winning a GP's last race opens the next GP: that is the one shown next
        if (place == 1 && RaceMode.StageOf(raceLevel) == RaceMode.LevelsPerBiome - 1
            && RaceMode.BiomeOf(raceLevel) + 1 < RaceMode.BiomeCount)
            raceViewBiome = RaceMode.BiomeOf(raceLevel) + 1;

        int reward = place == 1 ? RaceMode.Reward(raceLevel)
                   : place <= 3 ? RaceMode.Reward(raceLevel) / 3 : 0;
        int prize = Mathf.RoundToInt(reward * RaceMode.CoinMultiplier());
        // the coins picked up on the way round are banked with the prize
        int raceCoins = prize + coinsThisRun;
        if (raceCoins > 0)
        {
            totalCoins += raceCoins;
            PlayerPrefs.SetInt("Coins", totalCoins);
            PlayerPrefs.Save();
        }

        state = State.GameOver;
        PlayFinishCinematic(place);
        SetFinishCoins(raceCoins);
        centerText.text = (place == 1 ? Loc.T("WINNER!") : Loc.F("FINISHED P{0}", place)) +
            "\n<size=30>" + Loc.T(place == 1 ? "NEXT RACE UNLOCKED" : "1ST PLACE UNLOCKS THE NEXT RACE") + "</size>" +
            (raceCoins > 0 ? "\n<size=34>" + Loc.F("+{0} COINS", raceCoins) + "</size>" : "");
        // the result screen waits for the cinematic to play out
        centerText.gameObject.SetActive(false);
        gameOverPanel.SetActive(false);
        pauseButton.SetActive(false);
        raceHudText.gameObject.SetActive(false);
        audioMan.StopDriving();
        audioMan.PlayMenuMusic();
        race.ClearRacers();
        track.ClearFinishLine();
    }

    // after the scripted forest -> sunset -> city -> snow run, the world
    // keeps changing: back to forest, then a random biome each cycle
    [Header("Biome cycling")]
    [Tooltip("Seconds between biome changes once the first full cycle is done.")]
    public float biomeCycleSeconds = 100f;

    int cyclesDone;
    float targetDayNight, targetSnow;
    bool cyclingStarted;

    void PickNextBiome()
    {
        cyclesDone++;

        // first change after the snow always returns to the forest, so the
        // loop reads as "a new lap of the world" rather than random noise
        int stage = cyclesDone == 1 ? 0 : Random.Range(0, 4);
        RaceMode.BlendsForBiome(stage, out targetDayNight, out targetSnow);

        string[] names = { "FOREST", "SUNSET", "CITY", "SNOW PEAKS" };
        FlashBonus(names[stage], new Color(0.7f, 0.95f, 1f));
    }

    void TickBiome(float dt)
    {
        // --- scripted opening: day -> sunset -> night city
        // The world changes on the CLOCK, not on score, so a cautious player
        // and a fast one see the same journey at the same pace.
        runTime += dt;
        float target = Mathf.Clamp01(
            Mathf.InverseLerp(sunsetAtSeconds, cityAtSeconds, runTime));

        // --- leaving the city: forest again, with the mountain on the horizon
        float approachAt = cityAtSeconds + 20f;
        float climbAt = snowAtSeconds - 30f;
        if (!mountainShown && !cyclingStarted && runTime >= approachAt)
        {
            mountainShown = true;
            // a clean straight to come out of the city onto
            track.RequestStraightFor(200f);
            track.RequestFlatFor(200f);
        }

        // --- then the climb itself. Nothing turns white until the car is at
        // the foot of the mountain it has been driving toward.
        float snowTarget = Mathf.Clamp01(
            Mathf.InverseLerp(climbAt, snowAtSeconds, runTime));

        // Each biome gets a clean straight to arrive on, so one never starts
        // in the middle of the last one's scenery.
        if (!cityRunInDone && target > 0.6f)
        {
            cityRunInDone = true;
            track.RequestStraightFor(200f);
            track.RequestFlatFor(200f);
        }
        if (!snowRunInDone && snowTarget > 0.01f)
        {
            snowRunInDone = true;
            track.RequestStraightFor(260f);
            track.RequestFlatFor(260f);
        }

        if (!cyclingStarted)
        {
            // the city falls behind and daylight comes back for the run at
            // the mountain, so the approach is green forest, not night
            if (mountainShown) biomeBlend = Mathf.MoveTowards(biomeBlend, 0f, dt * 0.3f);
            else if (target > biomeBlend) biomeBlend = Mathf.MoveTowards(biomeBlend, target, dt * 0.25f);
            if (snowTarget > snowBlend) snowBlend = Mathf.MoveTowards(snowBlend, snowTarget, dt * 0.2f);

            if (!nightAnnounced && biomeBlend > 0.92f)
            {
                nightAnnounced = true;
                FlashBonus("CITY", new Color(1f, 0.35f, 0.75f));
            }
            if (!snowAnnounced && snowBlend > 0.92f)
            {
                snowAnnounced = true;
                FlashBonus("SNOW MOUNTAINS", new Color(0.75f, 0.92f, 1f));
                cyclingStarted = true;          // the world starts looping now
                targetDayNight = biomeBlend;
                targetSnow = snowBlend;
                nextCycleTime = runTime + biomeCycleSeconds;
            }
        }
        else
        {
            // --- endless variety: ease toward whichever biome is next
            if (runTime >= nextCycleTime)
            {
                nextCycleTime = runTime + biomeCycleSeconds;
                PickNextBiome();
                // every change of scene starts on a clean, level straight
                track.RequestStraightFor(220f);
                track.RequestFlatFor(220f);
            }
            biomeBlend = Mathf.MoveTowards(biomeBlend, targetDayNight, dt * 0.22f);
            snowBlend = Mathf.MoveTowards(snowBlend, targetSnow, dt * 0.2f);
        }

        ApplyBiomeVisuals(biomeBlend);
        track.SetBiomeBlend(biomeBlend);
        track.SetSnowBlend(snowBlend);
    }

    int titleTaps;
    bool introRunning = true;
    bool introSeen;            // the title sequence has actually appeared
    Text menuTitle, menuSubTitle;
    float titleFadeT;

    /// <summary>Brings the lobby title up as the intro's copy fades away.</summary>
    void FadeInMenuTitle()
    {
        titleFadeT += Time.unscaledDeltaTime;
        SetMenuTitleAlpha(Mathf.Clamp01(titleFadeT / 0.25f));
    }

    void SetMenuTitleAlpha(float a)
    {
        if (menuTitle != null)
        {
            Color c = menuTitle.color;
            menuTitle.color = new Color(c.r, c.g, c.b, a);
        }
        if (menuSubTitle != null)
        {
            Color c = menuSubTitle.color;
            menuSubTitle.color = new Color(c.r, c.g, c.b, a);
        }
    }

    // ------------------------------------------ DocLorean time-travel powerup
    bool rewindUsed;
    float rewindT;
    float rewindFromDist, rewindToDist, rewindFromLat;
    const float RewindDistance = 42f;   // must stay under track behindDistance
    const float RewindDuration = 1.25f;
    GameObject rewindFxGo;

    void BuildRewindFx()
    {
        if (rewindFxGo != null) return;
        rewindFxGo = new GameObject("RewindFX");
        var vol = rewindFxGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 100f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var ca = profile.Add<ColorAdjustments>(true);
        ca.saturation.Override(-100f); // full black & white
        vol.profile = profile;
        if (mainCam != null)
        {
            var camData = mainCam.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = true;
        }
        rewindFxGo.SetActive(false);
    }

    void StartRewind()
    {
        BuildRewindFx();
        rewindUsed = true;
        state = State.Rewinding;
        rewindT = 0f;
        rewindFromDist = car.DistanceTraveled;
        rewindToDist = car.DistanceTraveled - RewindDistance;
        rewindFromLat = car.LateralOffset;
        rewindFxGo.SetActive(true);
        audioMan.StopDriving();
        audioMan.PlayTimeTravel();
        if (camFollow != null) camFollow.Shake(0.25f);
        FlashBonus("TIME REWIND!", new Color(0.6f, 0.9f, 1f));
    }

    void TickRewinding()
    {
        rewindT += Time.deltaTime / RewindDuration;
        float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rewindT));
        car.SetRewound(
            Mathf.Lerp(rewindFromDist, rewindToDist, p),
            Mathf.Lerp(rewindFromLat, 0f, p));

        if (rewindT >= 1f)
        {
            rewindFxGo.SetActive(false);
            car.FinishRewind(1.6f);
            state = State.Playing;
            audioMan.StartDriving();
        }
    }

    // ---------------------------------------------------------- unlock code
    const string UnlockPassword = "mfjk575jkf745";
    /// <summary>Hands out the hidden car only - safe to give to friends.</summary>
    const string DevCarPassword = "flux88";
    const int TapsToPrompt = 15;

    /// <summary>Unlocks and equips every code-only car.</summary>
    void GrantDevCar()
    {
        int granted = -1;
        for (int i = 0; i < Cars.Length; i++)
        {
            if (!DevOnlyCar(i) || OwnedCar(i)) continue;
            PlayerPrefs.SetInt("CarOwned" + i, 1);
            granted = i;
        }

        if (granted < 0)
        {
            unlockMsg.color = new Color(0.7f, 0.7f, 0.78f);
            unlockMsg.text = "ALREADY UNLOCKED";
            audioMan.PlayTap();
            return;
        }

        selectedCar = granted;
        PlayerPrefs.SetInt("SelectedCar", selectedCar);
        PlayerPrefs.Save();
        StartCarSwap(selectedCar);

        unlockMsg.color = new Color(0.45f, 1f, 0.55f);
        unlockMsg.text = Cars[granted].name + " UNLOCKED";
        audioMan.PlayPowerUp();
        Invoke(nameof(CloseUnlockPrompt), 1.4f);
    }

    void TitleTapped()
    {
        titleTaps++;
        if (titleTaps < TapsToPrompt) return;
        titleTaps = 0;
        OpenUnlockPrompt();
    }

    void OpenUnlockPrompt()
    {
        if (unlockPanel == null) return;
        unlockInput.text = "";
        unlockMsg.text = "";
        unlockPanel.SetActive(true);
        audioMan.PlayPop();
        unlockInput.Select();
        unlockInput.ActivateInputField();
    }

    void CloseUnlockPrompt()
    {
        unlockForAdCheck = false;
        unlockInput.inputType = InputField.InputType.Standard;
        unlockPanel.SetActive(false);
        audioMan.PlayTap();
    }

    void SubmitUnlock()
    {
        string entered = unlockInput.text.Trim();

        // the same box guards the hidden ad check (GameManager.AdCheck.cs)
        if (unlockForAdCheck)
        {
            SubmitAdCheckPassword(entered);
            return;
        }

        // the code that hands out the hidden car, and nothing else
        if (string.Equals(entered, DevCarPassword, System.StringComparison.OrdinalIgnoreCase))
        {
            GrantDevCar();
            return;
        }

        if (entered != UnlockPassword)
        {
            unlockMsg.color = new Color(1f, 0.4f, 0.35f);
            unlockMsg.text = "WRONG PASSWORD";
            unlockInput.text = "";
            audioMan.PlayTap();
            return;
        }

        for (int i = 0; i < Cars.Length; i++)
        {
            if (i != 0) PlayerPrefs.SetInt("CarOwned" + i, 1);
        }
        for (int i = 0; i < RaceMode.TotalLevels; i++)
        {
            PlayerPrefs.SetInt("RaceDone" + i, 1);
        }
        PlayerPrefs.Save();

        unlockMsg.color = new Color(0.45f, 1f, 0.55f);
        unlockMsg.text = "ALL CARS + RACES UNLOCKED";
        RefreshWeeklyMenu();
        audioMan.PlayCoin();
        Invoke(nameof(CloseUnlockPrompt), 1.1f);
    }

    void OpenCredits()
    {
        settingsPanel.SetActive(false);
        creditsPanel.SetActive(true);
        audioMan.PlayTap();
    }

    void CloseCredits()
    {
        creditsPanel.SetActive(false);
        settingsPanel.SetActive(true);
        audioMan.PlayTap();
    }

    void OpenQuests()
    {
        menuPanel.SetActive(false);
        questsPanel.SetActive(true);
        questMultText.text = Loc.F("SCORE MULTIPLIER  X{0}", Mathf.Min(30, 1 + questLevel));
        for (int i = 0; i < 3; i++) questRowTexts[i].text = QuestLine(i);
        RefreshQuestSkip();
        audioMan.PlayTap();
    }

    void CloseQuests()
    {
        questsPanel.SetActive(false);
        menuPanel.SetActive(true);
        audioMan.PlayTap();
    }

    // ------------------------------------------------------------------ shop

    GameObject BuildTireModel(Transform parent, Vector3 pos)
    {
        GameObject prefab = Resources.Load<GameObject>("Tire/tire");
        var root = new GameObject("TireModel");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;
        if (prefab == null) return root;

        GameObject model = Instantiate(prefab, root.transform);
        model.transform.localPosition = Vector3.zero;
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            // black rubber
            if (rubberMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                rubberMat = new Material(sh) { color = new Color(0.05f, 0.05f, 0.06f) };
            }
            foreach (var r in rends) r.sharedMaterial = rubberMat;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float k = 1.0f / Mathf.Max(b.size.x, b.size.y, b.size.z, 0.001f);
            model.transform.localScale = model.transform.localScale * k;
            Vector3 centerLocal = root.transform.InverseTransformPoint(b.center);
            model.transform.localPosition = -centerLocal * k;
            // lie flat: thinnest axis points up
            if (b.size.x <= b.size.y && b.size.x <= b.size.z)
                model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 0f, 90f);
            else if (b.size.z <= b.size.x && b.size.z <= b.size.y)
                model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(90f, 0f, 0f);
        }
        return root;
    }

    /// <summary>
    /// A token, as a flat emblem standing upright in the world. Both box
    /// cameras look down the -z axis at their box, so a face pointing +z is
    /// square to the camera and the art is never mirrored.
    /// </summary>
    GameObject BuildTokenIcon(Currency cur, Transform parent, Vector3 pos, float targetSize)
    {
        return BuildIconObject(TokenIcons[(int)cur], TokenColors[(int)cur],
                               parent, pos, targetSize);
    }

    GameObject BuildIconObject(string iconPath, Color fallback,
                               Transform parent, Vector3 pos, float targetSize)
    {
        var root = new GameObject("Icon");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;

        Texture2D tex = Resources.Load<Texture2D>(iconPath);

        // same reasoning as the locked-car material: ship the shader rather
        // than hoping the stock one survived the build
        Shader sh = Resources.Load<Shader>("Shaders/FlatIcon");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var mat = new Material(sh);
        if (tex != null) mat.mainTexture = tex;
        else mat.color = fallback;
        mat.renderQueue = 3000;

        var quad = new GameObject("Face");
        quad.transform.SetParent(root.transform, false);
        quad.AddComponent<MeshFilter>().sharedMesh = MakeIconMesh(targetSize);
        var mr = quad.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return root;
    }

    /// <summary>A square facing +z, wound by hand so the facing is certain.</summary>
    static Mesh MakeIconMesh(float size)
    {
        float h = size * 0.5f;
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f),
            new Vector3(-h,  h, 0f), new Vector3(h,  h, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void EnsureShopShowcase()
    {
        if (shopShowcaseRoot != null) return;
        shopShowcaseRoot = new GameObject("ShopShowcase");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var redMat = new Material(shader) { color = new Color(0.8f, 0.16f, 0.12f) };
        var darkRedMat = new Material(shader) { color = new Color(0.6f, 0.1f, 0.08f) };
        var grayMat = new Material(shader) { color = new Color(0.35f, 0.35f, 0.38f) };
        var goldMat = new Material(shader) { color = new Color(1f, 0.8f, 0.2f) };

        // --- animated toolbox (hollow: floor + four walls, so prizes rise out)
        Vector3 boxPos = new Vector3(140f, -400f, 0f);
        var boxRoot = new GameObject("Toolbox");
        boxRoot.transform.SetParent(shopShowcaseRoot.transform, false);
        boxRoot.transform.position = boxPos;
        toolboxRoot = boxRoot;

        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, darkRedMat,
            boxPos + new Vector3(0f, 0.03f, 0f), Quaternion.identity, new Vector3(1.5f, 0.06f, 0.8f));
        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, redMat,
            boxPos + new Vector3(0f, 0.3f, 0.37f), Quaternion.identity, new Vector3(1.5f, 0.6f, 0.06f));
        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, redMat,
            boxPos + new Vector3(0f, 0.3f, -0.37f), Quaternion.identity, new Vector3(1.5f, 0.6f, 0.06f));
        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, redMat,
            boxPos + new Vector3(-0.72f, 0.3f, 0f), Quaternion.identity, new Vector3(0.06f, 0.6f, 0.8f));
        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, redMat,
            boxPos + new Vector3(0.72f, 0.3f, 0f), Quaternion.identity, new Vector3(0.06f, 0.6f, 0.8f));
        MakeShowcasePart(boxRoot.transform, PrimitiveType.Cube, goldMat,
            boxPos + new Vector3(0f, 0.3f, 0.41f), Quaternion.identity, new Vector3(0.25f, 0.2f, 0.03f));

        var lidPivot = new GameObject("LidPivot");
        lidPivot.transform.SetParent(boxRoot.transform, false);
        lidPivot.transform.position = boxPos + new Vector3(0f, 0.62f, -0.4f);
        MakeShowcasePart(lidPivot.transform, PrimitiveType.Cube, darkRedMat,
            boxPos + new Vector3(0f, 0.7f, 0f), Quaternion.identity, new Vector3(1.52f, 0.18f, 0.82f));
        MakeShowcasePart(lidPivot.transform, PrimitiveType.Cube, grayMat,
            boxPos + new Vector3(0f, 0.85f, 0f), Quaternion.identity, new Vector3(0.6f, 0.14f, 0.22f));

        toolboxAnim = boxRoot.AddComponent<ToolboxAnimator>();
        toolboxAnim.lidPivot = lidPivot.transform;

        toolboxRT = new RenderTexture(384, 384, 16);
        // camera on the +Z side (clasp faces the player), pulled back and
        // aimed higher so the full lid swing + hovering prize stay in frame
        MakeShowcaseCam(shopShowcaseRoot.transform,
            boxPos + new Vector3(0f, 1.9f, 3.2f), boxPos + Vector3.up * 0.75f, toolboxRT);

        // --- tire stacks for the pack icons (1 / 3 / 6 tires)
        int[] stackCounts = { 1, 3, 6 };
        for (int s = 0; s < 3; s++)
        {
            Vector3 p = new Vector3(180f + s * 40f, -400f, 0f);
            var stack = new GameObject("TireStack" + s);
            stack.transform.SetParent(shopShowcaseRoot.transform, false);
            stack.transform.position = p;
            stack.AddComponent<Coin>().spinSpeed = 30f;

            int count = stackCounts[s];
            for (int i = 0; i < count; i++)
            {
                int col = i / 3;
                int row = i % 3;
                Vector3 offset = count <= 3
                    ? new Vector3(0f, row * 0.34f, 0f)
                    : new Vector3((col - 0.5f) * 1.05f, row * 0.34f, 0f);
                BuildTireModel(stack.transform, p + offset);
            }

            tireStackRTs[s] = new RenderTexture(256, 256, 16);
            // farther back (smaller) and aimed low (tires sit higher in frame)
            MakeShowcaseCam(shopShowcaseRoot.transform,
                p + new Vector3(0.7f, 1.2f, -2.6f), p + Vector3.up * 0.1f, tireStackRTs[s]);
        }

        // --- coin stacks for the coin packs (1 / 3 / 6 coins)
        int[] coinCounts = { 1, 3, 6 };
        for (int s = 0; s < 3; s++)
        {
            Vector3 p = new Vector3(-240f - s * 40f, -400f, 0f);
            var stack = new GameObject("CoinStack" + s);
            stack.transform.SetParent(shopShowcaseRoot.transform, false);
            stack.transform.position = p;
            stack.AddComponent<Coin>().spinSpeed = 30f;

            int count = coinCounts[s];
            for (int i = 0; i < count; i++)
            {
                int col = i / 3;
                int row = i % 3;
                // coins lie flat and sit on top of each other like a till stack
                Vector3 offset = count <= 3
                    ? new Vector3(0f, row * 0.2f, 0f)
                    : new Vector3((col - 0.5f) * 1.15f, row * 0.2f, 0f);

                GameObject c = track.BuildCoinDisplay(p + offset, stack.transform);
                Destroy(c.GetComponent<Coin>());   // the stack spins as one
                // the road version stands upright; lay it down for the pile
                c.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            }

            coinStackRTs[s] = new RenderTexture(256, 256, 16);
            MakeShowcaseCam(shopShowcaseRoot.transform,
                p + new Vector3(0.7f, 1.15f, -3.0f), p + Vector3.up * 0.18f, coinStackRTs[s]);
        }

        // --- blue token box (same build, cooler colours)
        Vector3 tbPos = new Vector3(320f, -400f, 0f);
        var blueMat = new Material(shader) { color = new Color(0.16f, 0.42f, 0.85f) };
        var darkBlueMat = new Material(shader) { color = new Color(0.1f, 0.28f, 0.6f) };
        var tokenBox = new GameObject("TokenBox");
        tokenBox.transform.SetParent(shopShowcaseRoot.transform, false);
        tokenBox.transform.position = tbPos;
        tokenBoxRoot = tokenBox;

        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, darkBlueMat,
            tbPos + new Vector3(0f, 0.03f, 0f), Quaternion.identity, new Vector3(1.5f, 0.06f, 0.8f));
        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, blueMat,
            tbPos + new Vector3(0f, 0.3f, 0.37f), Quaternion.identity, new Vector3(1.5f, 0.6f, 0.06f));
        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, blueMat,
            tbPos + new Vector3(0f, 0.3f, -0.37f), Quaternion.identity, new Vector3(1.5f, 0.6f, 0.06f));
        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, blueMat,
            tbPos + new Vector3(-0.72f, 0.3f, 0f), Quaternion.identity, new Vector3(0.06f, 0.6f, 0.8f));
        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, blueMat,
            tbPos + new Vector3(0.72f, 0.3f, 0f), Quaternion.identity, new Vector3(0.06f, 0.6f, 0.8f));
        MakeShowcasePart(tokenBox.transform, PrimitiveType.Cube, grayMat,
            tbPos + new Vector3(0f, 0.3f, 0.41f), Quaternion.identity, new Vector3(0.25f, 0.2f, 0.03f));

        var tbLid = new GameObject("LidPivot");
        tbLid.transform.SetParent(tokenBox.transform, false);
        tbLid.transform.position = tbPos + new Vector3(0f, 0.62f, -0.4f);
        MakeShowcasePart(tbLid.transform, PrimitiveType.Cube, darkBlueMat,
            tbPos + new Vector3(0f, 0.7f, 0f), Quaternion.identity, new Vector3(1.52f, 0.18f, 0.82f));
        MakeShowcasePart(tbLid.transform, PrimitiveType.Cube, grayMat,
            tbPos + new Vector3(0f, 0.85f, 0f), Quaternion.identity, new Vector3(0.6f, 0.14f, 0.22f));

        tokenBoxAnim = tokenBox.AddComponent<ToolboxAnimator>();
        tokenBoxAnim.lidPivot = tbLid.transform;

        tokenBoxRT = new RenderTexture(384, 384, 16);
        MakeShowcaseCam(shopShowcaseRoot.transform,
            tbPos + new Vector3(0f, 1.9f, 3.2f), tbPos + Vector3.up * 0.75f, tokenBoxRT);

        // Garage prices draw their token straight from the 2D art - no
        // stage, no camera and no render texture needed for a flat image.

        SetLayerRecursively(shopShowcaseRoot, ShowcaseLayer);
        shopShowcaseRoot.SetActive(false);
    }

    GameObject MakeShowcasePart(Transform parent, PrimitiveType prim, Material mat,
        Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(prim);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    void OpenShop()
    {
        menuPanel.SetActive(false);
        shopPanel.SetActive(true);
        var sr = shopPanel.GetComponent<ScrollRect>();
        if (sr != null) sr.verticalNormalizedPosition = 1f; // always open at the top
        boxResultText.text = "";
        if (tokenBoxResultText != null) tokenBoxResultText.text = "";
        EnsureShopShowcase();
        shopShowcaseRoot.SetActive(true);
        toolboxImage.texture = toolboxRT;
        tokenBoxImage.texture = tokenBoxRT;
        for (int i = 0; i < 6; i++) packIcons[i].texture = tireStackRTs[i / 2];
        for (int i = 0; i < 3; i++) coinPackIcons[i].texture = coinStackRTs[i];
        RefreshShopCurrency();
        audioMan.PlayTap();
    }

    void CloseShop()
    {
        shopPanel.SetActive(false);
        if (shopShowcaseRoot != null) shopShowcaseRoot.SetActive(false);
        menuPanel.SetActive(true);
        RefreshWeeklyMenu();
        PlayerPrefs.Save();
        audioMan.PlayTap();
    }

    void RefreshShopCurrency()
    {
        // the amounts themselves are the corner counters, in front of the shop
        RefreshItemUpgrades();
    }

    /// <summary>Redraws the five power-up duration rows.</summary>
    void RefreshItemUpgrades()
    {
        if (itemUpNames[0] == null) return;

        for (int i = 0; i < ItemUpgrades.Count; i++)
        {
            int level = ItemUpgrades.Level(i);
            bool maxed = ItemUpgrades.IsMaxed(i);

            itemUpNames[i].text = ItemUpgrades.Names[i];
            itemUpInfo[i].text = Loc.F("LV {0}/7", level + 1) + "   "
                               + Loc.F("{0}s", ItemUpgrades.Seconds(i).ToString("0.#"))
                               + (maxed ? ""
                                  : "  >  " + Loc.F("{0}s", ItemUpgrades.SecondsAtLevel(i, level + 1)
                                        .ToString("0.#")));

            int cost = ItemUpgrades.Cost(i);
            itemUpButtons[i].text = maxed ? "MAX" : FormatCount(cost);
            itemUpCoins[i].gameObject.SetActive(!maxed);
            // greyed out when it is maxed or you cannot afford it yet
            var img = itemUpButtons[i].transform.parent.GetComponent<Image>();
            if (img != null)
            {
                img.color = maxed ? new Color(0.3f, 0.5f, 0.35f)
                          : totalCoins >= cost ? new Color(0.96f, 0.47f, 0.13f)
                          : new Color(0.42f, 0.36f, 0.5f);
            }
        }
    }

    void BuyItemUpgrade(int item)
    {
        if (ItemUpgrades.IsMaxed(item))
        {
            audioMan.PlayTap();
            return;
        }

        int cost = ItemUpgrades.Cost(item);
        if (totalCoins < cost)
        {
            boxResultText.text = "NOT ENOUGH COINS";
            boxResultText.color = new Color(1f, 0.45f, 0.4f);
            audioMan.PlayTap();
            return;
        }

        totalCoins -= cost;
        PlayerPrefs.SetInt("Coins", totalCoins);
        ItemUpgrades.Buy(item);

        boxResultText.text = Loc.T(ItemUpgrades.Names[item]) + "  "
                           + Loc.F("LV {0}", ItemUpgrades.Level(item) + 1);
        boxResultText.color = new Color(0.6f, 1f, 0.7f);
        audioMan.PlayPowerUp();
        RefreshShopCurrency();
    }

    void BuyMysteryBox()
    {
        if (totalCoins < MysteryBoxCost)
        {
            boxResultText.text = "NOT ENOUGH COINS";
            boxResultText.color = new Color(1f, 0.35f, 0.3f);
            audioMan.PlayTap();
            return;
        }
        totalCoins -= MysteryBoxCost;
        OpenMysteryBox();
    }

    /// <summary>
    /// Draws a mystery box prize and brings the box up full screen - bought in
    /// the shop, or free from the daily reward.
    /// </summary>
    void OpenMysteryBox()
    {
        // the box is the shop's model, filmed by the shop's camera
        EnsureShopShowcase();
        shopShowcaseRoot.SetActive(true);
        pendingBoxCurrency = Currency.Coins;
        pendingBoxTires = false;

        // one draw on the prize table - the same table the ODDS screen shows
        BoxTier tier = MysteryBoxTiers[PickBoxTier(MysteryBoxTiers)];
        int amount = Random.Range(tier.min, tier.max + 1);
        pendingBoxColor = tier.color;
        switch (tier.kind)
        {
            case BoxPrizeKind.Coins:
                totalCoins += amount;
                pendingBoxText = Loc.F(tier.textKey, amount);
                break;

            case BoxPrizeKind.Tires:
                tires += amount;
                pendingBoxText = Loc.F(tier.textKey, amount);
                pendingBoxTires = true;
                break;

            default:
                // rarest prize: brand tokens for the exotic cars, any marque
                pendingBoxCurrency = (Currency)Random.Range(1, 5);
                SetToken(pendingBoxCurrency, GetToken(pendingBoxCurrency) + amount);
                pendingBoxText = Loc.F(tier.textKey, amount, Loc.T(TokenNames[(int)pendingBoxCurrency]));
                pendingBoxColor = TokenColors[(int)pendingBoxCurrency];
                break;
        }

        PlayerPrefs.SetInt("Coins", totalCoins);
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.Save();

        // spawn the actual prize model inside the box so it rises out
        if (boxPrizeGo != null) Destroy(boxPrizeGo);
        pendingBoxIsToken = false;
        bool prizeTires = pendingBoxTires;
        bool prizeToken = pendingBoxCurrency != Currency.Coins && !prizeTires;
        Vector3 inBox = toolboxRoot.transform.position + Vector3.up * 0.25f;
        boxPrizeGo = prizeToken
            ? BuildTokenIcon(pendingBoxCurrency, toolboxRoot.transform, inBox, 0.9f)
            : BuildIconObject(prizeTires ? TireIconPath : CoinIconPath,
                              prizeTires ? Color.white : new Color(1f, 0.82f, 0.1f),
                              toolboxRoot.transform, inBox, 0.9f);
        boxPrizeGo.transform.localScale = Vector3.one * 0.55f;
        SetLayerRecursively(boxPrizeGo, ShowcaseLayer);
        toolboxAnim.prizeItem = boxPrizeGo.transform;

        // the box goes full screen; the player opens it themselves
        boxResultText.text = "";
        ShowBoxFocus(false);
        audioMan.PlayTap();
    }

    // ------------------------------------------------------ box focus screen

    GameObject boxFocusPanel;
    RawImage boxFocusImage;
    Text boxFocusHint;
    bool boxFocusOpened;
    float boxFocusT;

    void BuildBoxFocus(Transform uiRoot)
    {
        boxFocusPanel = MakePanel(uiRoot, "BoxFocusPanel");

        var dimGo = new GameObject("Bright");
        dimGo.transform.SetParent(boxFocusPanel.transform, false);
        boxFocusBack = dimGo.AddComponent<Image>();
        // solid and bright: this screen takes over completely
        boxFocusBack.color = new Color(1f, 0.80f, 0.20f, 1f);
        var dimRt = boxFocusBack.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = new Vector2(-200f, -400f);
        dimRt.offsetMax = new Vector2(200f, 400f);

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(boxFocusPanel.transform, false);
        var glow = glowGo.AddComponent<RawImage>();
        glow.texture = MakeRaysTexture(14);
        glow.color = new Color(1f, 1f, 1f, 0.30f);
        glow.raycastTarget = false;
        var glowRt = glow.rectTransform;
        glowRt.anchorMin = glowRt.anchorMax = glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.anchoredPosition = new Vector2(0f, 80f);
        glowRt.sizeDelta = new Vector2(1500f, 1500f);
        boxFocusGlow = glow;

        var boxGo = new GameObject("BoxView");
        boxGo.transform.SetParent(boxFocusPanel.transform, false);
        boxFocusImage = boxGo.AddComponent<RawImage>();
        boxFocusImage.raycastTarget = false;
        var boxRt = boxFocusImage.rectTransform;
        boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.anchoredPosition = new Vector2(0f, 80f);
        boxRt.sizeDelta = new Vector2(900f, 900f);

        boxFocusHint = MakeText(boxFocusPanel.transform, "BoxHint", 54,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -460f), new Vector2(900f, 100f));
        boxFocusHint.text = "TAP TO OPEN";
        boxFocusHint.color = new Color(0.16f, 0.10f, 0.02f);

        // tapping anywhere opens it
        var btn = boxFocusPanel.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(TapBoxFocus);
        var hit = boxFocusPanel.GetComponent<Image>();
        if (hit == null) hit = boxFocusPanel.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        boxFocusPanel.SetActive(false);
    }

    RawImage boxFocusGlow;
    Image boxFocusBack;

    void ShowBoxFocus(bool tokenBox)
    {
        if (boxFocusPanel == null) return;
        boxFocusImage.texture = tokenBox ? tokenBoxRT : toolboxRT;
        boxFocusBack.color = tokenBox ? new Color(0.20f, 0.62f, 1f, 1f)
                                      : new Color(1f, 0.80f, 0.20f, 1f);
        boxFocusHint.color = tokenBox ? new Color(0.02f, 0.09f, 0.22f)
                                      : new Color(0.16f, 0.10f, 0.02f);
        boxFocusHint.text = "TAP TO OPEN";
        boxFocusOpened = false;
        boxFocusT = 0f;
        boxFocusPanel.SetActive(true);
        // over the top of the shop, so the shop does not have to be closed
        boxFocusPanel.transform.SetAsLastSibling();
    }

    void TapBoxFocus()
    {
        if (boxFocusOpened || boxFocusT < 0.25f) return;
        boxFocusOpened = true;
        boxFocusHint.text = "";
        if (pendingBoxIsToken) tokenBoxAnim.PlayOpen();
        else toolboxAnim.PlayOpen();
        // the lid flies at 0.35s and the prize hovers clear from 0.6s, so the
        // reveal comes in just as it would drop back inside
        boxRevealAt = Time.unscaledTime + 1.3f;
        audioMan.PlayPop();
    }

    void TickBoxFocus()
    {
        if (boxFocusPanel == null || !boxFocusPanel.activeSelf) return;

        boxFocusT += Time.unscaledDeltaTime;
        boxFocusGlow.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, boxFocusT * 14f);

        if (!boxFocusOpened)
        {
            // the box breathes and the prompt pulses while it waits
            float bob = 1f + Mathf.Sin(boxFocusT * 2.4f) * 0.03f;
            boxFocusImage.rectTransform.localScale = Vector3.one * bob;
            Color c = boxFocusHint.color;
            boxFocusHint.color = new Color(c.r, c.g, c.b,
                0.55f + 0.45f * Mathf.Sin(boxFocusT * 4f));
        }
    }

    void CloseBoxFocus()
    {
        if (boxFocusPanel != null) boxFocusPanel.SetActive(false);
    }

    void TickBoxReveal()
    {
        if (boxRevealAt > 0f && Time.unscaledTime >= boxRevealAt)
        {
            boxRevealAt = -1f;
            Text target = pendingBoxIsToken ? tokenBoxResultText : boxResultText;
            target.text = pendingBoxText;
            target.color = pendingBoxColor;
            RefreshShopCurrency();
            audioMan.PlayCoin();
            // which prize it was is known here - it is not read back out of
            // the message, which is only English in English
            bool tokenPrize = pendingBoxCurrency != Currency.Coins;
            ShowPrizeReveal(pendingBoxText, tokenPrize,
                tokenPrize ? TokenIcons[(int)pendingBoxCurrency]
                : pendingBoxTires ? TireIconPath : CoinIconPath);
        }
        TickBoxFocus();
        TickPrizeReveal();
    }

    // -------------------------------------------------- full screen prize reveal

    GameObject revealPanel;
    Image revealBack;
    RawImage revealRays, revealIcon;
    Text revealText, revealHint;
    float revealT = -1f;
    Color revealTint;

    /// <summary>Sunburst used behind a prize.</summary>
    static Texture2D MakeRaysTexture(int spokes)
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float half = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;
                bool lit = ((int)(ang / (360f / (spokes * 2)))) % 2 == 0;
                float fade = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) / 0.75f);
                float a = lit ? fade * 0.85f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void BuildPrizeReveal(Transform uiRoot)
    {
        revealPanel = MakePanel(uiRoot, "PrizeReveal");

        var backGo = new GameObject("Bright");
        backGo.transform.SetParent(revealPanel.transform, false);
        revealBack = backGo.AddComponent<Image>();
        revealBack.color = new Color(1f, 0.78f, 0.12f, 1f);
        var backRt = revealBack.rectTransform;
        backRt.anchorMin = Vector2.zero;
        backRt.anchorMax = Vector2.one;
        backRt.offsetMin = new Vector2(-200f, -400f);
        backRt.offsetMax = new Vector2(200f, 400f);

        var raysGo = new GameObject("Rays");
        raysGo.transform.SetParent(revealPanel.transform, false);
        revealRays = raysGo.AddComponent<RawImage>();
        revealRays.texture = MakeRaysTexture(12);
        revealRays.color = new Color(1f, 1f, 1f, 0.45f);
        revealRays.raycastTarget = false;
        var raysRt = revealRays.rectTransform;
        raysRt.anchorMin = raysRt.anchorMax = raysRt.pivot = new Vector2(0.5f, 0.5f);
        raysRt.anchoredPosition = new Vector2(0f, 120f);
        raysRt.sizeDelta = new Vector2(1900f, 1900f);

        var icoGo = new GameObject("PrizeIcon");
        icoGo.transform.SetParent(revealPanel.transform, false);
        revealIcon = icoGo.AddComponent<RawImage>();
        revealIcon.raycastTarget = false;
        var icoRt = revealIcon.rectTransform;
        icoRt.anchorMin = icoRt.anchorMax = icoRt.pivot = new Vector2(0.5f, 0.5f);
        icoRt.anchoredPosition = new Vector2(0f, 190f);
        icoRt.sizeDelta = new Vector2(460f, 460f);

        // The words: big, upright and dark, straight onto the bright colour.
        // No black outline - on dark letters it only thickens them into a
        // smudge - so they stay crisp against the colour and the rays alike.
        revealText = MakeText(revealPanel.transform, "PrizeText", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -175f), new Vector2(1000f, 220f), true);
        revealText.horizontalOverflow = HorizontalWrapMode.Wrap;
        revealText.lineSpacing = 1.05f;
        Destroy(revealText.GetComponent<Outline>());

        revealHint = MakeText(revealPanel.transform, "PrizeHint", 42, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -410f), new Vector2(900f, 80f), true);
        revealHint.text = "TAP TO CONTINUE";
        Destroy(revealHint.GetComponent<Outline>());

        // the whole screen is the dismiss button
        var btn = revealPanel.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(ClosePrizeReveal);
        var hit = revealPanel.GetComponent<Image>();
        if (hit == null) hit = revealPanel.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastTarget = true;

        revealPanel.SetActive(false);
    }

    /// <summary>Throws the prize up on a bright full screen so it lands.</summary>
    void ShowPrizeReveal(string text, bool tokenBox, string iconPath = null, Texture picture = null)
    {
        if (revealPanel == null) return;

        // the caller names the emblem it just awarded - the message itself
        // cannot be read for it, as it is in the player's language
        if (string.IsNullOrEmpty(iconPath)) iconPath = CoinIconPath;
        bool token = tokenBox;
        revealIcon.texture = picture != null ? picture : Resources.Load<Texture2D>(iconPath);

        // token boxes get the cool blue treatment, coin boxes the gold one
        revealTint = tokenBox || token ? new Color(0.20f, 0.62f, 1f)
                                       : new Color(1f, 0.78f, 0.12f);
        revealBack.color = revealTint;
        revealText.text = text;
        // near-black with a hint of the screen's colour: the strongest
        // contrast there is against the gold and the blue
        revealText.color = tokenBox || token ? new Color(0.02f, 0.06f, 0.16f)
                                             : new Color(0.12f, 0.07f, 0.01f);
        revealHint.color = new Color(revealText.color.r, revealText.color.g,
                                     revealText.color.b, 0f);

        revealT = 0f;
        revealPanel.SetActive(true);
        revealPanel.transform.SetAsLastSibling();   // above the shop and the box
        audioMan.PlayPowerUp();
    }

    void TickPrizeReveal()
    {
        if (revealT < 0f) return;

        revealT += Time.unscaledDeltaTime;

        // rays turn slowly the whole time
        revealRays.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, revealT * 22f);

        // white flash, then the icon punches in and settles
        float flash = Mathf.Clamp01(1f - revealT / 0.35f);
        revealBack.color = Color.Lerp(revealTint, Color.white, flash);

        float p = Mathf.Clamp01(revealT / 0.55f);
        float back = 1f + 2.7f * Mathf.Pow(p - 1f, 3f) + 1.7f * Mathf.Pow(p - 1f, 2f);
        revealIcon.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.2f, 1f, back);
        revealIcon.rectTransform.localRotation =
            Quaternion.Euler(0f, 0f, Mathf.Sin(revealT * 3f) * 4f);

        float tp = Mathf.Clamp01((revealT - 0.30f) / 0.4f);
        revealText.rectTransform.localScale =
            Vector3.one * Mathf.Lerp(0.6f, 1f, 1f - Mathf.Pow(1f - tp, 3f));

        // the hint pulses once the animation has finished
        float ha = revealT < 0.9f ? 0f : 0.65f + 0.3f * Mathf.Sin(revealT * 4f);
        Color hc = revealHint.color;
        revealHint.color = new Color(hc.r, hc.g, hc.b, ha);
    }

    void ClosePrizeReveal()
    {
        if (revealT < 0.6f) return;      // let the punch play out first
        revealT = -1f;
        revealPanel.SetActive(false);
        CloseBoxFocus();     // the box screen goes with it
        // a daily reward's stages go dark again with it (the shop keeps its own)
        if (showcaseRoot != null && (loginPanel == null || !loginPanel.activeSelf))
            showcaseRoot.SetActive(false);
        if (shopShowcaseRoot != null && (shopPanel == null || !shopPanel.activeSelf))
            shopShowcaseRoot.SetActive(false);
        audioMan.PlayTap();
    }

    void BuyTokenBox()
    {
        if (tires < TokenBoxTireCost)
        {
            tokenBoxResultText.text = "NOT ENOUGH TIRES";
            tokenBoxResultText.color = new Color(1f, 0.35f, 0.3f);
            audioMan.PlayTap();
            return;
        }
        tires -= TokenBoxTireCost;
        PlayerPrefs.SetInt("Tires", tires);
        OpenTokenBox();
    }

    /// <summary>Draws a token box prize and brings the box up full screen.</summary>
    void OpenTokenBox()
    {
        EnsureShopShowcase();
        shopShowcaseRoot.SetActive(true);

        // a brand token, 1-5, weighted toward the cheaper marques - but only
        // from the marques that still have a car left to buy. The same pool
        // the ODDS screen lists.
        TokenBoxPool(out List<Currency> pool, out List<float> weights);

        int amount = Random.Range(TokenBoxMin, TokenBoxMax + 1);
        Currency cur = Currency.Coins;
        if (pool.Count > 0)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            float roll = Random.value * total;
            cur = pool[pool.Count - 1];
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f) { cur = pool[i]; break; }
            }
        }

        if (cur == Currency.Coins)
        {
            // every marque collected: the box pays out in coins
            int paid = CoinsInsteadOfTokens(amount);
            totalCoins += paid;
            PlayerPrefs.SetInt("Coins", totalCoins);
            pendingBoxText = Loc.F("+{0} COINS", paid);
        }
        else
        {
            SetToken(cur, GetToken(cur) + amount);
            pendingBoxText = Loc.F("+{0} {1}", amount, Loc.T(TokenNames[(int)cur]));
        }

        pendingBoxCurrency = cur;
        pendingBoxIsToken = cur != Currency.Coins;
        pendingBoxTires = false;
        pendingBoxColor = TokenColors[(int)cur];

        // spawn the token model inside the blue box so it rises out
        if (boxPrizeGo != null) Destroy(boxPrizeGo);
        boxPrizeGo = BuildIconObject(
            cur == Currency.Coins ? CoinIconPath : TokenIcons[(int)cur],
            TokenColors[(int)cur], tokenBoxRoot.transform,
            tokenBoxRoot.transform.position + Vector3.up * 0.25f, 0.9f);
        SetLayerRecursively(boxPrizeGo, ShowcaseLayer);
        tokenBoxAnim.prizeItem = boxPrizeGo.transform;

        tokenBoxResultText.text = "";
        boxResultText.text = "";
        ShowBoxFocus(true);
        RefreshShopCurrency();
        audioMan.PlayTap();
    }

    void BuyCoinPack(int coinsGained, int tireCost)
    {
        if (tires < tireCost)
        {
            boxResultText.text = "NOT ENOUGH TIRES";
            boxResultText.color = new Color(1f, 0.35f, 0.3f);
            audioMan.PlayTap();
            return;
        }
        tires -= tireCost;
        totalCoins += coinsGained;
        PlayerPrefs.SetInt("Coins", totalCoins);
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.Save();
        boxResultText.text = Loc.F("+{0} COINS", coinsGained);
        boxResultText.color = new Color(1f, 0.82f, 0.1f);
        RefreshShopCurrency();
        audioMan.PlayCoin();
    }

    /// <summary>A tire pack button: the store takes it from here (see GrantPurchase).</summary>
    void BuyTirePack(int pack)
    {
        if (audioMan != null) audioMan.PlayTap();
        if (IapStore.Buy(IapStore.TirePackIds[pack])) return;

        // The store has not sent the packs yet (no connection, or still
        // connecting). Never a dead button: the pack says so for a moment -
        // the store is already trying again - then shows its price again.
        if (packLabels[pack] != null)
        {
            // in place of the price, in the price's size, so it fits the button
            packLabels[pack].text = PackTires[pack] + "\n<size=36>" + Loc.T("NOT AVAILABLE") + "</size>";
            CancelInvoke(nameof(RefreshPackPrices));
            Invoke(nameof(RefreshPackPrices), 2.5f);
        }
    }

    /// <summary>
    /// Hands out something paid for. Called by IapStore once the store says
    /// the payment went through - including purchases left unfinished by a
    /// crash, which arrive at the next launch.
    /// </summary>
    void GrantPurchase(string productId)
    {
        int pack = System.Array.IndexOf(IapStore.TirePackIds, productId);
        if (pack < 0)
        {
            Debug.LogWarning("Purchase of unknown product " + productId);
            return;
        }
        int amount = PackTires[pack];
        tires += amount;
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.Save();
        if (boxResultText != null)
        {
            boxResultText.text = Loc.F("+{0} TIRES!", amount);
            boxResultText.color = new Color(0.7f, 0.9f, 1f);
        }
        RefreshShopCurrency();
        if (audioMan != null) audioMan.PlayCoin();
    }

    /// <summary>Writes each pack's tires and price - the store's price once it has sent them.</summary>
    void RefreshPackPrices()
    {
        for (int i = 0; i < packLabels.Length; i++)
        {
            if (packLabels[i] == null) continue;
            string price = IapStore.PriceFor(IapStore.TirePackIds[i], PackFallbackPrices[i]);
            packLabels[i].text = PackTires[i] + "\n<size=" + PackPriceSize + ">" + price + "</size>";
        }
    }

    void OnDestroy()
    {
        // the scene reloads when the language changes; the store lives on,
        // so it must stop talking to this copy of the game
        if (IapStore.GrantHandler == (System.Action<string>)GrantPurchase) IapStore.GrantHandler = null;
        IapStore.PricesChanged -= RefreshPackPrices;
    }

    void OpenGarage()
    {
        menuPanel.SetActive(false);
        garagePanel.SetActive(true);
        shopIndex = selectedCar;
        SetGarageTab(0);      // always opens on the car list
        audioMan.PlayTap();
    }

    // spinning 3D model shown through the open centre of the garage screen
    /// <summary>
    /// The browsed car spins on its own little stage, filmed by a dedicated
    /// camera and shown on a panel. It used to be dropped into the world in
    /// front of the main camera, which now drives off and leaves it behind.
    /// </summary>
    void ShowGaragePreview()
    {
        ClearGaragePreview();
        EnsureGarageStage();

        garagePreview = BuildPreviewModel(shopIndex);
        if (garagePreview == null)
        {
            // model missing/failed to import - show nothing rather than a stall
            Debug.LogWarning("Garage: no renderable model for " + Cars[shopIndex].name +
                             " (path: " + Cars[shopIndex].path + ")");
            return;
        }

        garagePreview.transform.SetParent(garageStage.transform, false);
        garagePreview.transform.position = GarageStagePos;
        SetLayerRecursively(garagePreview, ShowcaseLayer);
        CarPaint.Apply(garagePreview, shopIndex);

        var spin = garagePreview.GetComponent<Coin>();
        if (spin == null) spin = garagePreview.AddComponent<Coin>();
        spin.spinSpeed = 55f;
        spin.enabled = true;

        garageStage.SetActive(true);
        if (garageCarImage != null)
        {
            garageCarImage.texture = garageCarRT;
            garageCarImage.gameObject.SetActive(true);
        }
    }

    // well clear of the other showcase stages - the tire stack sits at -120,
    // and the garage car was being built right on top of it
    static readonly Vector3 GarageStagePos = new Vector3(-600f, -400f, 0f);
    GameObject garageStage;
    RenderTexture garageCarRT;
    RawImage garageCarImage;

    void EnsureGarageStage()
    {
        if (garageStage != null) return;

        garageStage = new GameObject("GarageStage");
        garageStage.transform.position = GarageStagePos;
        SetLayerRecursively(garageStage, ShowcaseLayer);
        if (mainCam != null) mainCam.cullingMask &= ~(1 << ShowcaseLayer);

        garageCarRT = new RenderTexture(640, 640, 16);
        MakeShowcaseCam(garageStage.transform,
            GarageStagePos + new Vector3(2.6f, 1.5f, -4.6f), GarageStagePos, garageCarRT);
    }

    // ------------------------------------------------------- garage tabs

    GameObject garageCarsTab, garagePaintTab;
    Text garageTabCars, garageTabPaint;
    int garageTab;                       // 0 = cars, 1 = paint
    readonly List<Image> paintBodySwatches = new List<Image>();
    readonly List<Image> paintWheelSwatches = new List<Image>();

    GameObject MakeTabGroup(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    void SetGarageTab(int tab)
    {
        garageTab = tab;
        garageCarsTab.SetActive(tab == 0);
        garagePaintTab.SetActive(tab == 1);

        // the selected tab is lit, the other is dimmed
        var onCol = new Color(0.96f, 0.47f, 0.13f);
        var offCol = new Color(0.35f, 0.32f, 0.45f);
        var carsBg = garageTabCars.transform.parent.GetComponent<Image>();
        var paintBg = garageTabPaint.transform.parent.GetComponent<Image>();
        if (carsBg != null) carsBg.color = tab == 0 ? onCol : offCol;
        // the paint tab stays grey while it is locked, so it reads as unavailable
        if (paintBg != null)
        {
            paintBg.color = PaintLocked ? new Color(0.28f, 0.27f, 0.34f)
                          : tab == 1 ? onCol : offCol;
        }
        if (PaintLocked)
        {
            garageTabPaint.color = new Color(0.70f, 0.70f, 0.78f);
        }

        // the paint tab needs room for the palettes, so the car shows smaller
        if (garageCarImage != null)
        {
            var carRt = garageCarImage.rectTransform;
            carRt.sizeDelta = tab == 1 ? new Vector2(620f, 620f) : new Vector2(1080f, 1080f);
            carRt.anchoredPosition = tab == 1 ? new Vector2(0f, 60f) : new Vector2(0f, 30f);
        }

        // paint always previews the car you own, not one you are browsing
        if (tab == 1) shopIndex = selectedCar;
        RefreshShop();
        RefreshPaintTab();
        audioMan.PlayTap();
    }

    void BuildPaintTab(Transform root)
    {
        var pTitle = MakeText(root, "PaintTitle", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(900f, 70f));
        pTitle.text = "BODY";
        pTitle.color = new Color(1f, 0.82f, 0.35f);
        BuildSwatchRow(root, 250f, paintBodySwatches, true);

        var wTitle = MakeText(root, "WheelTitle", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -230f), new Vector2(900f, 70f));
        wTitle.text = "WHEELS";
        wTitle.color = new Color(1f, 0.82f, 0.35f);
        BuildSwatchRow(root, -310f, paintWheelSwatches, false);

        MakeButton(root, "RESET", 38,
            new Vector2(0f, -430f), new Vector2(360f, 96f), ResetPaint,
            new Color(0.35f, 0.32f, 0.45f));

        if (PaintLocked) BuildPaintLockScreen(root);
    }

    /// <summary>Paint is built but not released yet - flip this to open it up.</summary>
    const bool PaintLocked = true;


    /// <summary>
    /// Greys the whole paint tab out and blocks every control underneath it.
    /// </summary>
    void BuildPaintLockScreen(Transform root)
    {
        var go = new GameObject("ComingSoon");
        go.transform.SetParent(root, false);
        var veil = go.AddComponent<Image>();
        veil.color = new Color(0.06f, 0.05f, 0.10f, 0.86f);
        veil.raycastTarget = true;          // swallows taps on the swatches
        var rt = veil.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-200f, -400f);
        rt.offsetMax = new Vector2(200f, 400f);

        var label = MakeText(go.transform, "ComingSoonText", 78, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 140f));
        label.text = "COMING SOON";
        label.color = new Color(0.72f, 0.72f, 0.80f);

        var sub = MakeText(go.transform, "ComingSoonSub", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(900f, 90f));
        sub.text = "PAINT YOUR CAR AND WHEELS";
        sub.color = new Color(0.62f, 0.62f, 0.70f);
    }

    /// <summary>A grid of colour chips. Index 0 puts the model's own paint back.</summary>
    void BuildSwatchRow(Transform root, float y, List<Image> into, bool body)
    {
        const int PerRow = 7;
        const float Size = 108f;
        const float Step = 132f;

        for (int i = 0; i < CarPaint.Count; i++)
        {
            int choice = i;             // capture for the closure
            int row = i / PerRow;
            int col = i % PerRow;
            int inRow = Mathf.Min(PerRow, CarPaint.Count - row * PerRow);
            float x = (col - (inRow - 1) * 0.5f) * Step;

            var go = new GameObject((body ? "Body" : "Wheel") + "Swatch" + i);
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = CarPaint.Swatches[i];
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y - row * Step);
            rt.sizeDelta = new Vector2(Size, Size);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            if (body) btn.onClick.AddListener(() => PickBodyPaint(choice));
            else btn.onClick.AddListener(() => PickWheelPaint(choice));

            if (i == 0)
            {
                var label = MakeText(go.transform, "Stock", 22, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Size, Size));
                label.text = "STOCK";
            }
            into.Add(img);
        }
    }

    void PickBodyPaint(int choice)
    {
        CarPaint.SetBody(selectedCar, choice);
        ApplyPaintEverywhere();
        audioMan.PlayTap();
    }

    void PickWheelPaint(int choice)
    {
        CarPaint.SetWheel(selectedCar, choice);
        ApplyPaintEverywhere();
        audioMan.PlayTap();
    }

    void ResetPaint()
    {
        CarPaint.SetBody(selectedCar, 0);
        CarPaint.SetWheel(selectedCar, 0);
        ApplyPaintEverywhere();
        audioMan.PlayTap();
    }

    /// <summary>Repaints the spinning preview and the car out on the road.</summary>
    void ApplyPaintEverywhere()
    {
        if (garagePreview != null) CarPaint.Apply(garagePreview, selectedCar);
        if (car != null && car.CarModelRoot != null)
        {
            CarPaint.Apply(car.CarModelRoot.gameObject, selectedCar);
        }
        RefreshPaintTab();
    }

    /// <summary>Rings the chosen colours.</summary>
    void RefreshPaintTab()
    {
        int bodyPick = CarPaint.BodyChoice(selectedCar);
        int wheelPick = CarPaint.WheelChoice(selectedCar);

        for (int i = 0; i < paintBodySwatches.Count; i++)
        {
            paintBodySwatches[i].rectTransform.localScale =
                Vector3.one * (i == bodyPick ? 1.18f : 1f);
        }
        for (int i = 0; i < paintWheelSwatches.Count; i++)
        {
            paintWheelSwatches[i].rectTransform.localScale =
                Vector3.one * (i == wheelPick ? 1.18f : 1f);
        }
    }

    void ClearGaragePreview()
    {
        if (garagePreview != null) Destroy(garagePreview);
        if (garageCarImage != null) garageCarImage.gameObject.SetActive(false);
        if (garageStage != null) garageStage.SetActive(false);
    }

    /// <summary>
    /// Every material slot on every renderer, not just the first. Car models
    /// are multi-material meshes - body, glass, lights, rubber - and setting
    /// sharedMaterial only replaces slot zero, which is why the window frames,
    /// headlights and tyres stayed pale.
    /// </summary>
    void BlackOut(Renderer[] rends)
    {
        Material black = LockedCarMaterial();
        foreach (var r in rends)
        {
            if (r == null) continue;
            var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < slots.Length; i++) slots[i] = black;
            r.sharedMaterials = slots;
        }
    }

    Material lockedCarMat;

    Material LockedCarMaterial()
    {
        if (lockedCarMat != null) return lockedCarMat;

        // Loaded from Resources, NOT found by name. A build only ships
        // shaders something references, so Shader.Find for a stock unlit
        // shader works in the editor and comes back null on the phone - and
        // the fallback lit shader still catches a specular sheen on every
        // edge, which is why the locked cars were readable instead of black.
        Shader sh = Resources.Load<Shader>("Shaders/FlatBlack");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");

        lockedCarMat = new Material(sh);
        var flat = new Color(0.035f, 0.035f, 0.05f, 1f);
        lockedCarMat.color = flat;                       // legacy shaders
        if (lockedCarMat.HasProperty("_BaseColor")) lockedCarMat.SetColor("_BaseColor", flat);
        // belt and braces if only a lit shader was available
        if (lockedCarMat.HasProperty("_Metallic")) lockedCarMat.SetFloat("_Metallic", 0f);
        if (lockedCarMat.HasProperty("_Smoothness")) lockedCarMat.SetFloat("_Smoothness", 0f);
        if (lockedCarMat.HasProperty("_Glossiness")) lockedCarMat.SetFloat("_Glossiness", 0f);
        if (lockedCarMat.HasProperty("_SpecColor")) lockedCarMat.SetColor("_SpecColor", Color.black);
        if (lockedCarMat.HasProperty("_EmissionColor"))
        {
            lockedCarMat.DisableKeyword("_EMISSION");
            lockedCarMat.SetColor("_EmissionColor", Color.black);
        }
        return lockedCarMat;
    }

    GameObject BuildPreviewModel(int idx)
    {
        CarDef d = Cars[idx];
        var root = new GameObject("CarPreview");

        if (d.path == null)
        {
            Transform orig = car.GetOriginalVisual();
            if (orig != null)
            {
                var m = Instantiate(orig.gameObject, root.transform);
                m.SetActive(true);
                m.transform.localPosition = Vector3.zero;
            }
        }
        else
        {
            GameObject prefab = Resources.Load<GameObject>(d.path);
            if (prefab != null)
            {
                GameObject m = Instantiate(prefab, root.transform);

                float spinYaw = 0f;
                // a few models are authored nose-backwards
                if (System.Array.IndexOf(BackwardsModels, d.name) >= 0) spinYaw += 180f;
                // the big pack's cars all start showing their tail to the
                // camera - turn them round so a spin starts on the front
                if (d.path != null && d.path.StartsWith("CarsFBX/")) spinYaw += 180f;

                if (Mathf.Abs(spinYaw) > 0.01f)
                {
                    m.transform.localRotation =
                        Quaternion.Euler(0f, spinYaw, 0f) * m.transform.localRotation;
                }
                CarController.BlackenWindows(m);
            }
        }

        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { Destroy(root); return null; }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        // centre on the pivot so it spins in place, then size for display.
        // use the two largest axes so models that import lying on a different
        // axis (common with .blend files) still get sensible scaling.
        foreach (Transform child in root.transform) child.localPosition -= b.center;
        float a = Mathf.Max(b.size.x, b.size.y, b.size.z);
        float c = Mathf.Min(b.size.x, b.size.y, b.size.z);
        float mid = b.size.x + b.size.y + b.size.z - a - c; // the middle axis
        float footprint = Mathf.Max(a, mid, 0.01f);
        float k = 2.2f / footprint;
        root.transform.localScale = Vector3.one * k;

        // Locked cars are pure silhouettes. Tinting the existing materials
        // black was not enough: they keep their metallic sheen and their
        // smoothness, so the highlights still traced out the whole shape, and
        // any part with a bright unlit material - lights, badges, glass -
        // stayed white. An unlit flat black on every renderer leaves nothing
        // for the light to catch.
        if (!OwnedCar(idx)) BlackOut(rends);
        return root;
    }

    void CloseGarage()
    {
        garagePanel.SetActive(false);
        if (shopShowcaseRoot != null) shopShowcaseRoot.SetActive(false);
        ClearGaragePreview();
        FinishCarSwapNow();   // never leave the garage mid-changeover
        menuPanel.SetActive(true);
        RefreshWeeklyMenu();
        PlayerPrefs.Save();
        audioMan.PlayTap();
    }

    void ShopPrev()
    {
        // step past anything that is not on show
        for (int n = 0; n < Cars.Length; n++)
        {
            shopIndex = (shopIndex + Cars.Length - 1) % Cars.Length;
            if (ListedInGarage(shopIndex)) break;
        }
        RefreshShop();
        audioMan.PlayTap();
    }

    void ShopNext()
    {
        for (int n = 0; n < Cars.Length; n++)
        {
            shopIndex = (shopIndex + 1) % Cars.Length;
            if (ListedInGarage(shopIndex)) break;
        }
        RefreshShop();
        audioMan.PlayTap();
    }

    // ------------------------------------------------- garage car changeover

    GameObject swapGhost;
    WheelSpinner swapGhostWheels;
    int swapPhase;              // 0 none, 1 the new car coming up, 2 old car dropping back
    float swapT;
    int swapFromIndex, swapToIndex;
    int displayedCar;

    float swapGhostDistance;
    const float SwapStartGap = 26f;         // how far back it appears
    const float SwapClosingSpeed = 9f;      // how much faster it is running
    const float SwapDropBackSpeed = 8f;     // how fast the old car falls away
    const float SwapLaneOffset = -3.4f;     // it comes up the inside

    /// <summary>
    /// The newly equipped car drives up from behind, draws level and takes
    /// over, and the one it replaced falls away behind.
    /// </summary>
    void StartCarSwap(int newIndex)
    {
        if (newIndex == displayedCar || state != State.Menu)
        {
            EquipSelected();
            return;
        }
        CancelCarSwap();

        swapFromIndex = displayedCar;
        swapToIndex = newIndex;
        swapGhost = BuildCarModel(newIndex);
        if (swapGhost == null)
        {
            EquipSelected();   // no model to animate - just switch
            return;
        }
        swapGhostWheels = WheelSpinner.Attach(swapGhost, swapGhost.transform);
        swapGhostDistance = car.DistanceTraveled - SwapStartGap;
        swapGhostLateral = 0f;
        swapGhostYaw = 0f;
        swapPhase = 1;
        swapT = 0f;
    }

    void CancelCarSwap()
    {
        if (swapGhost != null) Destroy(swapGhost);
        swapGhost = null;
        swapGhostWheels = null;
        swapPhase = 0;
        swapT = 0f;
        if (camFollow != null) camFollow.aimShift = Vector3.zero;
    }

    void TickCarSwap(float dt)
    {
        if (swapPhase == 0) return;
        if (swapGhost == null) { swapPhase = 0; return; }

        swapT += dt;

        // The car is DRIVEN rather than slid along a curve: it runs at the
        // player's speed plus a closing speed, so it tracks the road exactly
        // like any other car on it and simply arrives faster.
        float closing = swapPhase == 1 ? SwapClosingSpeed : -SwapDropBackSpeed;
        swapGhostDistance += (car.CurrentSpeed + closing) * dt;

        float gap = swapGhostDistance - car.DistanceTraveled;   // + = ahead

        // It pulls out of the slipstream as it closes rather than appearing in
        // the next lane already - the lane change is most of what makes an
        // overtake read as one.
        float lateral = swapPhase == 1
            ? Mathf.Lerp(0f, SwapLaneOffset,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-20f, -7f, gap)))
            : 0f;
        PlaceSwapGhost(gap, lateral);

        if (swapPhase == 1)
        {
            // the camera slides across onto the car coming past as it arrives
            if (camFollow != null)
            {
                float blend = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(-7f, -0.5f, gap));
                Vector3 across = swapGhost.transform.position - car.transform.position;
                across.y = 0f;
                camFollow.aimShift = across * blend;
            }
        }

        bool done = swapPhase == 1 ? gap >= 0.4f : gap <= -26f;
        if (!done) return;

        if (swapPhase == 1)
        {
            // The newcomer takes the lead. To keep the camera perfectly
            // continuous, the car being driven is moved into the lane the
            // newcomer was in - so the point the camera is watching does not
            // move at the instant of the swap - and the framing shift is
            // cleared. It drifts back to the middle of the road on its own.
            EquipSelected();
            car.SetLateral(SwapLaneOffset);
            if (camFollow != null) camFollow.aimShift = Vector3.zero;

            Destroy(swapGhost);
            swapGhost = BuildCarModel(swapFromIndex);
            swapGhostWheels = swapGhost != null
                ? WheelSpinner.Attach(swapGhost, swapGhost.transform) : null;
            // the old car carries on from where the new one just was
            swapGhostDistance = car.DistanceTraveled;
            swapPhase = 2;
            swapT = 0f;
            if (swapGhost == null) swapPhase = 0;
        }
        else
        {
            CancelCarSwap();
        }
    }

    void PlaceSwapGhost(float offset, float lateral)
    {
        track.SamplePose(car.DistanceTraveled + offset,
            out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        swapGhost.transform.position = pos + right * lateral;

        // steer into the lane change: how fast it is moving sideways sets how
        // far the body is turned, so it leans out and straightens up again
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float lateralRate = (lateral - swapGhostLateral) / dt;
        swapGhostLateral = lateral;
        float wantYaw = Mathf.Clamp(lateralRate * 2.2f, -18f, 18f);
        swapGhostYaw = Mathf.Lerp(swapGhostYaw, wantYaw, 1f - Mathf.Exp(-7f * dt));

        swapGhost.transform.rotation =
            Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, swapGhostYaw, 0f);

        if (swapGhostWheels != null)
        {
            // a hover car has nothing touching the road - spinning wheels on
            // one as it pulls alongside gives the game away
            int ghostIdx = swapPhase == 1 ? swapToIndex : swapFromIndex;
            bool ghostHovers = ghostIdx >= 0 && ghostIdx < Cars.Length && Cars[ghostIdx].hover;

            swapGhostWheels.speed = ghostHovers ? 0f : car.CurrentSpeed
                + (swapPhase == 1 ? SwapClosingSpeed : -SwapDropBackSpeed);
            swapGhostWheels.steerAngle = swapGhostYaw * 1.4f;
        }
    }

    float swapGhostLateral, swapGhostYaw;

    /// <summary>
    /// Applies a changeover immediately. Called whenever the shot is about to
    /// end - leaving the garage, or starting a run - so the car you picked is
    /// always the one you get, however quickly you move on.
    /// </summary>
    void FinishCarSwapNow()
    {
        if (swapPhase == 0) return;
        CancelCarSwap();
        if (displayedCar != selectedCar) EquipSelected();
        car.SetLateral(0f);
    }

    /// <summary>A standalone, correctly sized and seated car model.</summary>
    GameObject BuildCarModel(int carIdx)
    {
        CarDef d = Cars[carIdx];
        GameObject prefab = d.path != null ? Resources.Load<GameObject>(d.path) : null;
        if (prefab == null) return null;

        float extraFlip = System.Array.IndexOf(BackwardsModels, d.name) >= 0 ? 180f : 0f;

        var root = new GameObject("SwapCar");
        GameObject m = Instantiate(prefab, root.transform);
        m.transform.localPosition = Vector3.zero;
        m.transform.localRotation =
            Quaternion.Euler(0f, d.yaw + racerYawOffset + extraFlip, 0f) * prefab.transform.rotation;
        CarController.BlackenWindows(m);

        var rends = m.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) { Destroy(root); return null; }

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float length = Mathf.Max(b.size.x, b.size.z, 0.01f);
        m.transform.localScale = m.transform.localScale * (4.2f / length);

        // Seat it on its TYRES, measured the same way as the car you drive.
        // The old way below - the lowest point of every part's world box -
        // lifted a few models well clear of the road, because Unity's box for
        // a turned mesh reaches below the mesh itself: half a metre on the
        // VORTEX HATCH, forty centimetres on the AUTEN RX. Those were the
        // rivals floating on the grid.
        if (WheelSpinner.MeasureContactHeight(m, root.transform, root.transform, out float tyres))
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 mid = root.transform.position - b.center;
            m.transform.localPosition += new Vector3(mid.x, 0f, mid.z);
            // centring moved it sideways only, so the tyres are where they were
            m.transform.localPosition += new Vector3(0f, -tyres, 0f);
            m.transform.localPosition += new Vector3(0f, HeightFixFor(d.name), 0f);
            return root;
        }

        // No wheels found: fall back to the bodywork.
        // measure twice: renderer bounds can lag a scale change by a frame
        for (int pass = 0; pass < 2; pass++)
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 off = root.transform.position - b.center;
            m.transform.localPosition += new Vector3(off.x, 0f, off.z);
            // seat it the same way the driven car is seated: on the bulk of the
            // low geometry, so a stray part underneath does not float the car
            m.transform.position += new Vector3(
                0f, root.transform.position.y - RobustFloor(rends), 0f);
        }
        m.transform.localPosition += new Vector3(0f, HeightFixFor(d.name), 0f);

        // RobustFloor deliberately ignores stray low geometry so one dangling
        // part cannot float a car. On a model whose whole underside sits below
        // that robust line - a low splitter, a full floorpan - it seats the
        // car into the ground instead. This is the backstop: measure what is
        // actually lowest, and never let more than a few centimetres of the
        // car end up under the road.
        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float sunk = root.transform.position.y - 0.06f - b.min.y;
        if (sunk > 0f) m.transform.position += new Vector3(0f, sunk, 0f);

        // And the other way. If the lowest point of the whole car is ABOVE the
        // ground line then there is no stray geometry to protect against -
        // the model is simply hovering - so drop it onto the road. This is the
        // half that was missing, and floating rivals were the result.
        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float hover = b.min.y - root.transform.position.y;
        if (hover > 0.02f) m.transform.position -= new Vector3(0f, hover, 0f);

        return root;
    }

    /// <summary>
    /// The height the car actually rests at, ignoring a few parts that hang
    /// below everything else (mud flaps, hidden planes, and the like).
    /// </summary>
    static float RobustFloor(Renderer[] rends)
    {
        var lows = new List<float>(rends.Length);
        float tallest = 0f;
        for (int i = 0; i < rends.Length; i++)
        {
            lows.Add(rends[i].bounds.min.y);
            tallest = Mathf.Max(tallest, rends[i].bounds.size.y);
        }
        lows.Sort();

        int idx = Mathf.Clamp(Mathf.RoundToInt(lows.Count * 0.12f), 1, lows.Count - 1);
        return (lows[idx] - lows[0]) > tallest * 0.12f ? lows[idx] : lows[0];
    }

    void RefreshShop()
    {
        CarDef d = Cars[shopIndex];
        bool owned = OwnedCar(shopIndex);

        // show the currency this car actually costs
        shopCoinsText.text = d.currency == Currency.Coins || owned
            ? Loc.F("COINS  {0}", totalCoins)
            : GetToken(d.currency) + "  " + Loc.T(TokenNames[(int)d.currency]);
        shopCoinsText.color = owned ? TokenColors[0] : TokenColors[(int)d.currency];
        shopCarName.text = d.name;
        shopStats.text = ""; // cars are skins - no stats to show
        ShowGaragePreview();
        if (owned)
        {
            shopPrice.text = shopIndex == selectedCar ? "EQUIPPED" : "OWNED";
            shopPrice.color = new Color(0.4f, 1f, 0.5f);
            shopActionLabel.text = shopIndex == selectedCar ? "EQUIPPED" : "SELECT";
            garagePriceIcon.gameObject.SetActive(false);
        }
        else if (d.cost == -3)
        {
            // only reachable if it somehow gets browsed to without being owned
            shopPrice.text = "NOT AVAILABLE";
            shopPrice.color = new Color(0.7f, 0.7f, 0.78f);
            shopActionLabel.text = "LOCKED";
            garagePriceIcon.gameObject.SetActive(false);
        }
        else if (d.cost == -2)
        {
            shopPrice.text = d.iapPrice;
            shopPrice.color = new Color(0.5f, 1f, 0.6f);
            shopActionLabel.text = Loc.F("BUY {0}", d.iapPrice);
            garagePriceIcon.gameObject.SetActive(false);
        }
        else if (d.cost < 0)
        {
            shopPrice.text = "LOG IN 7 DAYS IN A ROW";
            shopPrice.color = new Color(0.5f, 0.8f, 1f);
            shopActionLabel.text = "LOCKED";
            garagePriceIcon.gameObject.SetActive(false);
        }
        else if (d.currency != Currency.Coins)
        {
            shopPrice.text = FormatCount(d.cost);
            shopPrice.color = TokenColors[(int)d.currency];
            shopActionLabel.text = "BUY";
            // show that brand's token model beside the price
            EnsureShopShowcase();
            if (shopShowcaseRoot != null) shopShowcaseRoot.SetActive(true);
            garagePriceIcon.texture = Resources.Load<Texture2D>(TokenIcons[(int)d.currency]);
            garagePriceIcon.gameObject.SetActive(true);
            IconBeforeText(garagePriceIcon, shopPrice);
        }
        else
        {
            shopPrice.text = FormatCount(d.cost);
            shopPrice.color = new Color(1f, 0.8f, 0.3f);
            shopActionLabel.text = "BUY";
            garagePriceIcon.texture = Resources.Load<Texture2D>(CoinIconPath);
            garagePriceIcon.gameObject.SetActive(true);
            IconBeforeText(garagePriceIcon, shopPrice);
        }

        // green to buy or equip; grey when the car cannot be had from here
        bool locked = !owned && d.cost < 0 && d.cost != -2;
        var actionImg = shopActionLabel.transform.parent.GetComponent<Image>();
        if (actionImg != null) actionImg.color = locked ? LockedGrey : GoGreen;
    }

    void ShopAction()
    {
        CarDef d = Cars[shopIndex];
        bool owned = OwnedCar(shopIndex);

        if (!owned && d.cost == -2)
        {
            // TODO integrate Unity IAP - granted instantly for now so the
            // flow can be tested before store products are configured
            PlayerPrefs.SetInt("CarOwned" + shopIndex, 1);
            selectedCar = shopIndex;
            PlayerPrefs.SetInt("SelectedCar", selectedCar);
            PlayerPrefs.Save();
            StartCarSwap(selectedCar);
            audioMan.PlayCoin();
            RefreshShop();
            return;
        }

        if (!owned && d.cost < 0)
        {
            audioMan.PlayTap(); // reward or code-only car - can't be bought
            return;
        }

        if (!owned)
        {
            int wallet = GetToken(d.currency);
            if (wallet < d.cost)
            {
                shopPrice.text = Loc.F("NOT ENOUGH {0}", Loc.T(TokenNames[(int)d.currency]));
                shopPrice.color = new Color(1f, 0.35f, 0.3f);
                audioMan.PlayTap();
                return;
            }
            SetToken(d.currency, wallet - d.cost);
            PlayerPrefs.SetInt("CarOwned" + shopIndex, 1);
            selectedCar = shopIndex;
            PlayerPrefs.SetInt("SelectedCar", selectedCar);
            StartCarSwap(selectedCar);
            audioMan.PlayCoin();
        }
        else if (shopIndex != selectedCar)
        {
            selectedCar = shopIndex;
            PlayerPrefs.SetInt("SelectedCar", selectedCar);
            StartCarSwap(selectedCar);
            audioMan.PlayTap();
        }
        RefreshShop();
    }

    void OpenSettings()
    {
        settingsFromPause = state == State.Paused;
        // changing language rebuilds the menu, and the tutorial is a drive of
        // its own, so both are offered from the menu only - never in the
        // middle of a run
        if (languageBtnLabel != null)
            languageBtnLabel.transform.parent.gameObject.SetActive(!settingsFromPause);
        if (tutorialBtnLabel != null)
            tutorialBtnLabel.transform.parent.gameObject.SetActive(!settingsFromPause);
        if (settingsFromPause) pausePanel.SetActive(false);
        else menuPanel.SetActive(false);
        settingsPanel.SetActive(true);
        audioMan.PlayTap();
    }

    void CloseSettings()
    {
        settingsPanel.SetActive(false);
        if (settingsFromPause && state == State.Paused) pausePanel.SetActive(true);
        else menuPanel.SetActive(true);
        PlayerPrefs.Save();
        audioMan.PlayTap();
    }

    /// <summary>
    /// The menu backdrop: a clean, dead-straight road with nothing on it, and
    /// no pruning behind the car - the camera looks back down it.
    /// </summary>
    void ApplyLobbyTrack()
    {
        if (track == null) return;
        track.UseFixedLayout(0);      // a fresh road again (the tutorial fixes it)
        track.openingStraight = 0f;
        track.spawnObstacles = false;
        track.spawnTraffic = false;
        track.spawnCoins = false;
        track.spawnPowerUps = false;
        track.spawnTirePickups = false;
        track.spawnBoostPads = false;
        // corners are wanted here - the car drifts through them for the camera
        track.forceStraight = false;
        track.flatTrack = false;
        track.curveTightness = 1f;
        track.straightBias = 1f;
        track.behindDistance = lobbyBehindDistance;
        // the camera looks back down the road from in front of the car, so the
        // world has to already stretch a long way behind the starting point
        track.roadBehindStart = lobbyBehindDistance - 20f;
    }

    /// <summary>Puts everything the lobby switched off back for a real run.</summary>
    void ApplyRunTrack()
    {
        if (track == null) return;
        track.spawnCoins = true;
        track.spawnPowerUps = true;
        track.spawnTirePickups = true;
        track.forceStraight = false;
        // A race asks for a level circuit BEFORE this runs, and this was
        // quietly switching it back on - so race tracks got hills, and rivals
        // up the road appeared to float above the player.
        if (mode != Mode.Race) track.flatTrack = false;
        // The opening flyby looks back past the car, so the world behind has
        // to stay put until the countdown is over - it is pruned back to the
        // normal distance the moment the race actually starts.
        track.behindDistance = 260f;
        track.roadBehindStart = 190f;
    }

    void GoToMenu()
    {
        track.spawnBoostPads = false;
        track.roadWidth = endlessRoadWidth;
        ApplyLobbyTrack();
        track.Init(startPos, startYaw);
        car.ResetRun(track);
        if (camFollow != null) camFollow.SnapToTarget();
        lobbyBiomeShown = 0;     // a fresh lobby road, in the base biome
        EnterMenu();
    }

    void StartRun()
    {
        score = 0f;
        pendingDrift = 0f;
        prevDriftTime = 0f;
        coinsThisRun = 0;
        lastCoinSweepDist = car.DistanceTraveled;
        rewindUsed = false;
        car.GrantMercy(0f);        // drop the lobby's endless immunity
        FinishCarSwapNow();        // and never start a run mid-changeover

        // a burnt car needs its paint back - rebuilding the model is the only
        // thing that restores the materials the scorch replaced
        if (car.IsScorched)
        {
            EquipSelected();
            car.ClearScorch();
        }
        ApplyRunTrack();
        if (mode == Mode.Endless)
        {
            car.baseSpeed = runBaseSpeed;
            car.maxSpeed = runMaxSpeed + Cars[selectedCar].speedBonus;
            car.speedGainPerSecond = runSpeedGain;
            // Every run begins at the run's own starting speed. Left alone the
            // car just kept whatever it had - the lobby's slow cruise on a
            // fresh run, or the speed you died at when you hit RETRY, which is
            // why a retry launched you at full pace.
            car.LaunchAt(runBaseSpeed);
            track.spawnObstacles = true;
            track.spawnTraffic = true;
            track.spawnBoostPads = false;
            track.roadWidth = endlessRoadWidth;
            // the opening stretch stays straight so the countdown and the
            // camera move are not fighting a corner
            track.RequestStraightFor(150f);

            // Nothing on the road while the flyby plays. The spawners come
            // back on at GO, aimed well up the road so nothing pops into view.
            track.spawnObstacles = false;
            track.spawnTraffic = false;
            track.spawnCoins = false;
            track.spawnPowerUps = false;
            track.spawnTirePickups = false;
            track.ClearObstaclesAhead(car.DistanceTraveled, 400f);
        }
        revivesThisRun = 0;
        adRevivesUsed = 0; // 5 ad revives per RUN, not lifetime
        // Endless always starts in daylight forest. A race must NOT be reset
        // here - it has already been built in its own biome, and wiping the
        // blend would turn every later stretch back into forest.
        if (mode == Mode.Endless) ResetBiome();
        invincibleT = doubleCoinsT = magnetT = doubleScoreT = springsT = 0f;
        if (rewindFxGo != null) rewindFxGo.SetActive(false);
        state = State.Playing;
        Time.timeScale = 1f;
        ClearGaragePreview();
        pausePanel.SetActive(false);
        menuPanel.SetActive(false);
        garagePanel.SetActive(false);
        settingsPanel.SetActive(false);
        gameOverPanel.SetActive(false);
        centerText.gameObject.SetActive(false);
        bonusText.gameObject.SetActive(false);
        SetHudVisible(true);
        raceHudText.gameObject.SetActive(mode == Mode.Race);
        audioMan.PlayTap();

        // menu music ducks out, the countdown runs, then the driving theme
        // comes in as the car pulls away
        audioMan.FadeMusicOut();
        raceCountdown = 3f;         // 3 - 2 - 1, then GO on release
        // the car rolls in on rails through the countdown - a thumb on the
        // screen must not be steering it before the flag drops
        car.steeringLocked = true;

        if (mode == Mode.Endless)
        {
            // rolling start: engine running, camera showing the car off, and
            // nothing can hit you until the sweep has finished
            audioMan.StartDriving();
            car.GrantMercy(raceCountdown + 0.3f);
            if (camFollow != null) camFollow.PlayIntro(raceCountdown - 0.2f);
        }
        else if (camFollow != null)
        {
            // races start from the grid in the normal chase view - the lobby's
            // showcase framing has to be dropped, or it would hold all race
            camFollow.CancelIntro();
            camFollow.SetShowcase(false);
            camFollow.aimShift = Vector3.zero;
            camFollow.SnapToTarget();
        }
    }

    // ---------------------------------------------------------------- revive

    void OfferRevive(CarController.TickResult result)
    {
        pendingCrash = result;
        state = State.ReviveOffer;
        Time.timeScale = 0f;
        HideDriftText();
        audioMan.PlayCrash();
        // engine and tyre screech stop the moment you go off - they used to
        // hold on through the whole revive prompt
        audioMan.StopDriving();

        bool canTires = tires >= ReviveTireCost;
        // short and plain - this is read in a hurry, against a five second clock
        reviveTireLabel.text = canTires
            ? Loc.F("REVIVE   {0}", ReviveTireCost)
            : Loc.F("NEED   {0}", ReviveTireCost);
        reviveTireLabel.color = canTires ? Color.white : new Color(1f, 0.55f, 0.5f);
        reviveHaveText.text = Loc.F("YOU HAVE {0}", tires);
        reviveAdPending = false;
        bool canAd = adRevivesUsed < 5;
        reviveAdLabel.text = canAd
            ? Loc.F("WATCH AD ({0} LEFT)", 5 - adRevivesUsed)
            : "NO AD REVIVES LEFT";
        reviveTimer = reviveDecisionTime;
        reviveTimerText.text = Mathf.CeilToInt(reviveTimer).ToString();
        reviveTimerText.color = new Color(1f, 0.8f, 0.25f);
        SetReviveBar(reviveTimer);
        revivePanel.SetActive(true);
        pauseButton.SetActive(false);
    }

    float resumeCountdown;

    /// <summary>
    /// The "3, 2, 1" after a revive. The game is already unfrozen as far as
    /// state goes, but time stays stopped until this finishes - being handed
    /// back a car at 80mph the instant an ad closes is no way to continue a
    /// run you have just paid to keep.
    /// </summary>
    void TickResumeCountdown()
    {
        resumeCountdown -= Time.unscaledDeltaTime;

        int n = Mathf.CeilToInt(resumeCountdown);
        if (n > 0)
        {
            string label = n.ToString();
            if (countdownText.text != label)
            {
                countdownText.text = label;
                countdownText.color = new Color(1f, 0.8f, 0.2f);
                audioMan.PlayCountBeep();
            }
            // the number swells and settles, same as the start of a run
            float frac = resumeCountdown - Mathf.Floor(resumeCountdown);
            float age = 1f - frac;
            countdownText.transform.localScale =
                Vector3.one * (1f + 1.6f * Mathf.Exp(-9f * age));
            countdownText.gameObject.SetActive(true);
            return;
        }

        resumeCountdown = 0f;
        countdownText.gameObject.SetActive(false);
        countdownText.transform.localScale = Vector3.one;
        Time.timeScale = 1f;
        car.steeringLocked = false;
        audioMan.PlayCountGo();
        audioMan.StartDriving();
    }

    /// <summary>Counts the offer down on unscaled time - the game is frozen.</summary>
    void TickReviveTimer()
    {
        if (reviveTimer <= 0f) return;
        // an ad is on screen: the offer must not expire behind it
        if (reviveAdPending) return;

        reviveTimer -= Time.unscaledDeltaTime;
        if (reviveTimer <= 0f)
        {
            reviveTimer = 0f;
            reviveTimerText.text = "0";
            SetReviveBar(0f);
            DeclineRevive();          // out of time - the run is over
            return;
        }

        int shown = Mathf.CeilToInt(reviveTimer);
        reviveTimerText.text = shown.ToString();
        // beats faster and turns red as it runs out
        float frac = reviveTimer - Mathf.Floor(reviveTimer);
        float pulse = 1f + Mathf.Clamp01(1f - frac) * (reviveTimer < 3f ? 0.45f : 0.2f);
        reviveTimerText.transform.localScale = Vector3.one * pulse;
        reviveTimerText.color = reviveTimer < 3f
            ? Color.Lerp(new Color(1f, 0.35f, 0.3f), new Color(1f, 0.8f, 0.25f), reviveTimer / 3f)
            : new Color(1f, 0.8f, 0.25f);
        SetReviveBar(reviveTimer);
    }

    void ReviveWithTires()
    {
        if (tires < ReviveTireCost) { audioMan.PlayTap(); return; }
        tires -= ReviveTireCost;
        SaveQuests();
        DoRevive();
    }

    void ReviveWithAd()
    {
        if (adRevivesUsed >= 5) { audioMan.PlayTap(); return; }

        // The revive clock is paused while the ad is up, and the game is
        // already at timeScale 0 here - the countdown runs on unscaled time,
        // so it is stopped explicitly rather than ticking away behind the ad.
        reviveAdPending = true;
        reviveAdLabel.text = "LOADING AD...";

        AdManager.ShowRewarded("revive", () =>
        {
            reviveAdPending = false;
            adRevivesUsed++;
            SaveQuests();
            DoRevive();
        },
        () =>
        {
            // no ad to show: leave the player on the offer with the tyre
            // option still open rather than eating their revive
            reviveAdPending = false;
            reviveAdLabel.text = "NO AD AVAILABLE";
        });
    }

    void DoRevive()
    {
        reviveTimer = 0f;                 // stop the clock
        reviveTimerText.transform.localScale = Vector3.one;
        revivesThisRun++;
        revivePanel.SetActive(false);
        // Time stays stopped until the count-in finishes - see
        // TickResumeCountdown, which restarts it.
        Time.timeScale = 0f;
        resumeCountdown = 3f;
        car.steeringLocked = true;
        state = State.Playing;
        pauseButton.SetActive(true);
        car.CenterLane();                                  // back to the middle
        track.ClearObstaclesAhead(car.DistanceTraveled, 80f); // clean runway

        // The count-in is for reading the road you are about to drive, not for
        // watching the wreck you just made. Clear the fireball, put the car
        // back on its wheels and snap the camera to the normal driving view so
        // the three seconds are spent looking at what is coming.
        foreach (var blast in FindObjectsByType<CrashExplosion>())
        {
            Destroy(blast.gameObject);
        }
        if (camFollow != null)
        {
            camFollow.CancelIntro();
            camFollow.SetShowcase(false);
            camFollow.SnapToTarget();
        }
        // a revive un-burns the car: it is back on the road, not a wreck
        if (car.IsScorched)
        {
            EquipSelected();
            car.ClearScorch();
        }
        car.GrantMercy(2f);   // mercy window, but keep the speed you had
        // A boost still queued when you crashed would otherwise cash itself
        // in the instant you came back, firing the car straight to its
        // ceiling - and the ceiling never drops again on its own.
        car.ClearBoost();
        // the engine comes back with the count, not before it
        FlashBonus("REVIVED!", new Color(0.4f, 1f, 0.5f));
        audioMan.PlayCoin();
    }

    void DeclineRevive()
    {
        reviveTimer = 0f;
        reviveTimerText.transform.localScale = Vector3.one;
        revivePanel.SetActive(false);
        Time.timeScale = 1f;
        audioMan.PlayTap();
        CrashRun(pendingCrash);
    }

    void CrashRun(CarController.TickResult result)
    {
        state = State.GameOver;
        // quest progress that settles at the end of a run
        UpdateQuest(0, Mathf.RoundToInt(car.DistanceTraveled), true);
        UpdateQuest(1, Mathf.RoundToInt(car.DistanceTraveled), false);
        UpdateQuest(7, 1, false);
        pendingDrift = 0f; // unbanked combo dies with you
        HideDriftText();   // and so does its multiplier on screen
        if (camFollow != null) camFollow.Shake(0.8f);
        audioMan.PlayCrash();

        // the run goes out with a bang - at the body, not at the road surface
        Vector3 blastAt = car.CarModelRoot != null
            ? car.CarModelRoot.position + Vector3.up * 0.4f
            : car.transform.position + Vector3.up * 0.7f;
        CrashExplosion.Play(blastAt, 1.45f);
        audioMan.PlayExplosion();
        car.Scorch();      // and it stays burnt on the results screen
        // the camera pulls back to watch it go up, the car is thrown off its
        // wheels, and the wreck is left burning behind the results
        StartWreck(blastAt);
        SetHudVisible(false);   // nothing in front of the fireball
        audioMan.StopDriving();
        audioMan.PlayMenuMusic();

        int finalScore = Mathf.RoundToInt(score);
        bool raceRun = mode == Mode.Race;
        bool newBest = !raceRun && finalScore > best;   // a race never sets the high score
        if (newBest)
        {
            best = finalScore;
            PlayerPrefs.SetInt("HighScore", best);
        }
        // the week's ranking (endless runs only); it is shown over the results
        if (mode == Mode.Endless) LbRecordRun(finalScore);
        // performance bonus: skilled runs pay far better than long ones,
        // since score already rewards drifting and near misses
        int bonusCoins = raceRun ? 0 : Mathf.RoundToInt(score / Mathf.Max(1f, scorePerBonusCoin)
                                          * Mathf.Pow(1f - revivePenalty, revivesThisRun)
                                          * RaceMode.CoinMultiplier());
        int runTotal = coinsThisRun + bonusCoins;
        totalCoins += runTotal;
        PlayerPrefs.SetInt("Coins", totalCoins);
        PlayerPrefs.Save();

        string cause = result == CarController.TickResult.CrashedOffRoad
            ? "OFF THE ROAD" : "CRASHED";
        EndTutorialHints();
        centerText.gameObject.SetActive(false);
        pauseButton.SetActive(false);

        // a race shows where you were, not points
        if (raceRun)
        {
            int place = race != null ? race.PlayerPlace(car.DistanceTraveled) : RaceMode.Opponents + 1;
            float togo = Mathf.Max(0f, raceFinishDistance - car.DistanceTraveled);
            ShowGameOver(cause, 0, false, coinsThisRun, 0);
            ShowRaceCrashLines(place, togo);
            return;
        }
        // a new best gets its own screen too, once MENU is pressed
        ShowGameOver(cause, finalScore, newBest, coinsThisRun, bonusCoins);
    }

    void RestartRun()
    {
        if (mode == Mode.Race) { StartRace(raceLevel); return; }
        track.roadWidth = endlessRoadWidth;
        ApplyRunTrack();          // road behind the start line, before it is built
        track.Init(startPos, startYaw);
        car.ResetRun(track);
        if (camFollow != null) camFollow.SnapToTarget();
        StartRun();
    }

    // ------------------------------------------------------------------ update

    void Update()
    {
        // the opening title sequence owns the screen until it finishes
        if (introRunning)
        {
            var intro = FindAnyObjectByType<TitleIntro>();
            if (intro != null) introSeen = true;

            // the intro is created part way through the logo screen, so until
            // it shows up the splash still counts as "opening in progress"
            bool openingUp = intro != null ||
                (!introSeen && FindAnyObjectByType<SplashScreen>() != null);

            if (!openingUp)
            {
                introRunning = false;
                if (menuTitle != null) menuTitle.gameObject.SetActive(true);
                if (menuSubTitle != null) menuSubTitle.gameObject.SetActive(true);
                SetMenuTitleAlpha(1f);
                // the login popup waited for the intro - show it now
                if (state == State.Menu && loginPending) EnterMenu();
            }
            else
            {
                // The intro's name lands exactly on the lobby title, so the
                // real title switches on underneath while the intro is still
                // drawn. Before that moment the lobby stays hidden.
                bool showTitle = intro != null && intro.HandingOver;
                if (menuTitle != null && menuTitle.gameObject.activeSelf != showTitle)
                {
                    menuTitle.gameObject.SetActive(showTitle);
                    titleFadeT = 0f;
                }
                if (menuSubTitle != null && menuSubTitle.gameObject.activeSelf != showTitle)
                {
                    menuSubTitle.gameObject.SetActive(showTitle);
                }
                // fades up as the intro's own letters fade down, so the swap
                // is a cross-fade instead of one popping off the other
                if (showTitle) FadeInMenuTitle();
                if (loginPanel != null && loginPanel.activeSelf) CloseLoginPopup();
                // Skipping is handled by the intro's own full-screen button.
                // Reading raw input here as well would fire on the press while
                // the UI fires on the release, and anything under the intro
                // would see the same tap.
            }
        }

        AnimateBonus();
        TickBoxReveal();
        TickCoach();          // the tutorial (GameManager.Tutorial.cs)

        // corner currency counters stay current in every state
        if (persistentCoinsText != null) RefreshCornerCounters();


        switch (state)
        {
            case State.Menu:
                TickLobbyDrive();
                TickWheel();
                if (menuPanel.activeSelf)
                {
                    AnimateSpinButton();
                    AnimateLoginButton();
                }
                TickLoginPopup();
                break;

            case State.Playing:
                if (Input.GetKeyDown(KeyCode.Escape)) { PauseGame(); break; }
                // coming back from a revive: hold everything until the count
                // is done, so nobody is dropped straight back into traffic
                if (resumeCountdown > 0f) { TickResumeCountdown(); break; }
                TickPlaying();
                break;

            case State.Paused:
                if (Input.GetKeyDown(KeyCode.Escape)) ResumeGame();
                break;

            case State.Rewinding:
                TickRewinding();
                break;

            case State.ReviveOffer:
                TickReviveTimer();
                break;

            case State.GameOver:
                TickFinishCinematic();
                break;
        }
    }

    /// <summary>
    /// Keeps the car rolling gently behind the menu. No input is read, so it
    /// just tracks the road while the camera shows it off.
    /// </summary>
    void TickLobbyDrive()
    {
        // runs during the logo and title screens too, so the car is already
        // rolling by the time the lobby is revealed
        if (car == null || track == null) return;

        float dt = Time.deltaTime;
        car.TickIdle(dt);
        TickCarSwap(dt);      // the garage's changeover rides along with it

        // The biome deliberately does NOT advance here. This tick runs on the
        // title screen and in the lobby, and the world changes on a clock, so
        // leaving it running meant a player who sat in the menu for a minute
        // watched the showcase car drive off into sunset, then the city, then
        // the snow. The lobby is always the base biome.
    }

    void TickItems(float dt)
    {
        invincibleT = Mathf.Max(0f, invincibleT - dt);
        doubleCoinsT = Mathf.Max(0f, doubleCoinsT - dt);
        magnetT = Mathf.Max(0f, magnetT - dt);
        doubleScoreT = Mathf.Max(0f, doubleScoreT - dt);
        springsT = Mathf.Max(0f, springsT - dt);
        car.itemInvincible = invincibleT > 0f;
        car.springsActive = springsT > 0f;
    }

    /// <summary>Draws one draining bar per active item, stacked from the bottom.</summary>
    void UpdateItemBars()
    {
        float[] remaining = { invincibleT, doubleCoinsT, magnetT, doubleScoreT, springsT };

        int slot = 0;
        for (int i = 0; i < ItemSlots; i++)
        {
            if (remaining[i] <= 0f) continue;

            GameObject bar = itemBarRoots[slot];
            if (!bar.activeSelf) bar.SetActive(true);
            var rt = (RectTransform)bar.transform;
            rt.anchoredPosition = new Vector2(0f, 60f + slot * 62f);

            float frac = Mathf.Clamp01(remaining[i] / ItemDurations[i]);
            itemBarFills[slot].fillAmount = frac;
            // flash the bar when it is about to expire
            Color c = ItemColors[i];
            if (remaining[i] < 3f)
            {
                c = Color.Lerp(c, Color.white, Mathf.PingPong(Time.time * 5f, 1f) * 0.6f);
            }
            itemBarFills[slot].color = c;
            itemBarLabels[slot].text = Loc.T(ItemNames[i]) + "   " + Mathf.CeilToInt(remaining[i]);
            slot++;
        }

        for (int i = slot; i < ItemSlots; i++)
        {
            if (itemBarRoots[i].activeSelf) itemBarRoots[i].SetActive(false);
        }
    }

    void HideItemBars()
    {
        for (int i = 0; i < ItemSlots; i++)
        {
            if (itemBarRoots[i] != null && itemBarRoots[i].activeSelf) itemBarRoots[i].SetActive(false);
        }
    }

    void ActivateItem(TrackGenerator.PowerUpType type)
    {
        // duration comes from the shop upgrade for that item
        float seconds = ItemUpgrades.Seconds(ItemUpgrades.IndexOf(type));

        switch (type)
        {
            case TrackGenerator.PowerUpType.Invincible:
                invincibleT = seconds;
                FlashBonus("SHIELD UP! STAY ON THE ROAD", new Color(0.45f, 0.8f, 1f));
                break;
            case TrackGenerator.PowerUpType.DoubleCoins:
                doubleCoinsT = seconds;
                FlashBonus("DOUBLE COINS!", new Color(1f, 0.8f, 0.15f));
                break;
            case TrackGenerator.PowerUpType.Magnet:
                magnetT = seconds;
                FlashBonus("COIN MAGNET!", new Color(0.35f, 0.6f, 1f));
                break;
            case TrackGenerator.PowerUpType.DoubleScore:
                doubleScoreT = seconds;
                FlashBonus("DOUBLE SCORE!", new Color(0.8f, 0.4f, 1f));
                break;
            case TrackGenerator.PowerUpType.Springs:
                springsT = seconds;
                // the jump is a swipe now, not a tap - the old wording had
                // players pressing the screen and wondering why nothing hopped
                FlashBonus("SPRINGS! SWIPE UP TO JUMP", new Color(0.35f, 1f, 0.45f));
                break;
        }
        // one recognisable "you picked something up" chime for every item
        audioMan.PlayPowerUp();
        CoachOnItem(type);
    }

    void TickPlaying()
    {
        float dt = Time.deltaTime;
        TickGoFlash();

        if (TickCountdown(dt))
        {
            // Endless gets a rolling start: the car is already driving while
            // the camera sweeps around it and the numbers count down. Races
            // keep their standing start on the grid.
            if (mode == Mode.Endless)
            {
                track.TickTraffic(dt, car.DistanceTraveled);
                car.Tick(dt);          // crashes can't happen - mercy is active
                if (!coachDrive) TickBiome(dt);
            }
            UpdateHud();
            return;
        }

        if (mode == Mode.Race) TickRace(dt);
        else if (!coachDrive) TickBiome(dt);   // the tutorial stays in the forest
        TickTutorial(dt);
        TickItems(dt);
        track.TickTraffic(dt, car.DistanceTraveled);
        CarController.TickResult result = car.Tick(dt);

        // springs: one boing per jump
        if (car.ConsumeJumpStarted()) { audioMan.PlayBoing(); CoachOnJump(); }

        // shield: obstacles get punted off the road instead of passed through
        if (invincibleT > 0f &&
            track.TryKnockAside(car.DistanceTraveled, car.LateralOffset,
                                car.carRadius, car.CurrentSpeed))
        {
            audioMan.PlaySmash();
            if (camFollow != null) camFollow.Shake(0.22f);
            FlashBonus("SMASH!", new Color(0.45f, 0.8f, 1f));
        }

        score += car.CurrentSpeed * dt * pointsPerMeter * carPointMult * ScoreMultiplier;

        // power-ups
        if (track.TryCollectPowerUp(car.DistanceTraveled, car.LateralOffset, car.carRadius,
            out TrackGenerator.PowerUpType put))
        {
            ActivateItem(put);
        }

        // rare tire pickups
        if (track.TryCollectTire(car.DistanceTraveled, car.LateralOffset, car.carRadius))
        {
            tires++;
            PlayerPrefs.SetInt("Tires", tires);
            FlashBonus("+1 TIRE!", new Color(0.7f, 0.9f, 1f));
            audioMan.PlayCoin();
        }

        // magnet visibly drags nearby coins toward the car
        if (magnetT > 0f)
        {
            track.AttractCoins(car.DistanceTraveled, car.LateralOffset, 22f, dt);
        }

        // coins (magnet widens pickup, double-coins doubles the haul).
        // sweep from last frame's position so none can be skipped at speed
        float coinRadius = car.carRadius + (magnetT > 0f ? 3.5f : 0f);
        int got = track.CollectCoins(lastCoinSweepDist, car.DistanceTraveled,
                                     car.LateralOffset, coinRadius);
        lastCoinSweepDist = car.DistanceTraveled;
        if (got > 0)
        {
            int earned = Mathf.RoundToInt(got * (doubleCoinsT > 0f ? 2 : 1) * RaceMode.CoinMultiplier());
            coinsThisRun += earned;
            audioMan.PlayCoin();
            UpdateQuest(2, coinsThisRun, true);
            UpdateQuest(3, earned, false);
        }

        // drift combo: points build up while the chain is alive, and are only
        // banked into the score when you end the drift cleanly - crash and
        // the whole combo is lost
        // (a race is about the place, not points: no combo there)
        if (car.IsDrifting && mode == Mode.Endless) pendingDrift += driftPointsPerSecond * DriftMultiplier() * dt * carPointMult * ScoreMultiplier;

        if (prevDriftTime > 0f && car.DriftTime <= 0f && pendingDrift > 0f)
        {
            int banked = Mathf.RoundToInt(pendingDrift);
            score += banked;
            if (banked >= DriftFlashMin)
            {
                FlashBonus(Loc.F("+{0} DRIFT COMBO", banked), new Color(0.35f, 0.85f, 1f));
                // A short surge that drains back to the normal climb (see
                // CarController). It used to stay, every combo stacking on the
                // last, and drifting through corners was quietly a second,
                // much faster accelerator: most players were near top speed
                // by the sunset. Now it is a feel-good whoosh, nothing more.
                car.Boost(Mathf.Min(2f, banked / 300f));
            }
            UpdateQuest(5, banked, true);
            pendingDrift = 0f;
        }
        prevDriftTime = car.DriftTime;

        int nearMisses = track.ConsumeNearMisses(car.DistanceTraveled, car.LateralOffset, car.carRadius);
        if (nearMisses > 0)
        {
            int bonus = Mathf.RoundToInt(nearMisses * nearMissBonus * carPointMult) * ScoreMultiplier;
            score += bonus;
            FlashBonus(Loc.F("+{0} NEAR MISS", bonus), new Color(0.4f, 1f, 0.5f));
            audioMan.PlayNearMiss();
            UpdateQuest(4, nearMisses, false);
        }

        if (result == CarController.TickResult.HitOil)
        {
            FlashBonus("OIL SPILL!", new Color(1f, 0.6f, 0.15f));
            audioMan.PlayOilSlip();
            UpdateQuest(6, 1, false);
        }

        UpdateHud();

        if (result == CarController.TickResult.CrashedObstacle ||
            result == CarController.TickResult.CrashedOffRoad)
        {
            // DocLorean powerup: the first impact sends you back in time instead
            if (result == CarController.TickResult.CrashedObstacle &&
                Cars[selectedCar].hover && !rewindUsed)
            {
                StartRewind();
                return;
            }
            // a tutorial replayed from Settings just ends - no revive offer,
            // no results screen
            if (coachDrive)
            {
                CoachCrash();
                return;
            }
            // revive offer: tires or ad, before the run truly ends
            if (tires >= ReviveTireCost || adRevivesUsed < 5)
            {
                OfferRevive(result);
                return;
            }
            CrashRun(result);
        }
    }

    void UpdateHud()
    {
        scoreText.text = Mathf.RoundToInt(score).ToString();
        bestText.text = Loc.F("BEST  {0}", best);
        speedText.text = SpeedLabel(car.CurrentSpeed);
        multHudText.text = "X" + ScoreMultiplier;
        TickRivalHud();

        UpdateItemBars();

        // drift multiplier floats next to the car
        bool showDrift = state == State.Playing && car.IsDrifting && mode == Mode.Endless;
        if (driftText.gameObject.activeSelf != showDrift) driftText.gameObject.SetActive(showDrift);
        if (showDrift && mainCam != null)
        {
            int mult = DriftMultiplier();
            driftText.text = Loc.F("DRIFT X{0}", mult) + " +" + Mathf.RoundToInt(pendingDrift);
            driftText.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.1f), (mult - 1) / 7f);

            Vector3 sp = mainCam.WorldToScreenPoint(car.transform.position + Vector3.up * 1.4f);
            if (sp.z > 0f)
            {
                // aggressive shake, harder as the multiplier climbs
                float amp = Screen.height * 0.004f * (1f + 0.35f * mult);
                Vector2 jitter = Random.insideUnitCircle * amp;
                driftText.rectTransform.position = new Vector3(
                    sp.x + Screen.width * 0.17f + jitter.x, sp.y + jitter.y, 0f);
            }
        }

    }

    /// <summary>
    /// Takes the floating DRIFT multiplier off the screen. It is only updated
    /// while driving, so when driving stops - a crash, a pause - it would
    /// otherwise hang there, frozen where it was.
    /// </summary>
    void HideDriftText()
    {
        if (driftText != null) driftText.gameObject.SetActive(false);
    }

    void AnimateBonus()
    {
        if (bonusText == null || !bonusText.gameObject.activeSelf) return;

        float t = (Time.unscaledTime - bonusShownAt) / BonusDuration;
        if (t >= 1f)
        {
            bonusText.gameObject.SetActive(false);
            return;
        }

        // rise and fade
        bonusText.rectTransform.anchoredPosition = bonusBasePos + new Vector2(0f, 160f * t);
        Color c = bonusText.color;
        c.a = 1f - t * t; // stays readable, then falls away
        bonusText.color = c;
    }

    int DriftMultiplier()
    {
        // +1 for every half second of sustained drift, capped at x8
        return 1 + Mathf.Min(7, (int)(car.DriftTime / 0.5f));
    }

    void FlashBonus(string message, Color color)
    {
        bonusText.text = message;
        color.a = 1f;
        bonusText.color = color;
        bonusText.rectTransform.anchoredPosition = bonusBasePos;
        bonusText.gameObject.SetActive(true);
        bonusShownAt = Time.unscaledTime;
    }

    // ---------------------------------------------------------------- UI build

    /// <summary>
    /// The UI is laid out on a 1080-wide portrait screen, and every screen
    /// needs about 2160 of height to breathe: phones from the iPhone X on are
    /// that tall or taller once they are 1080 wide, which is what it was tuned
    /// on. Fitting the width alone left wider screens short - an iPad came out
    /// only 1440 tall, so screens were cut off top and bottom and the corner
    /// counters sat on top of titles. Expand fits the whole 1080 x 2160 on
    /// every screen: tall phones are exactly as before (width decides), and
    /// iPads and 16:9 phones get the full height with extra room at the sides.
    /// </summary>
    public static void ApplyUiScaling(CanvasScaler scaler)
    {
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, UiLayoutHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }

    /// <summary>The height every screen is laid out to fit in.</summary>
    public const float UiLayoutHeight = 2160f;

    RectTransform uiCanvasRt;

    /// <summary>
    /// How much wider the UI is than the 1080 it was laid out at: 1 on a phone,
    /// about 1.5 on an iPad. Anything that slides off the side of the screen
    /// travels this much further, so it really does leave a wide screen.
    /// </summary>
    float UiWidthScale()
    {
        if (uiCanvasRt == null) return 1f;
        return Mathf.Max(1f, uiCanvasRt.rect.width / 1080f);
    }

    void BuildUi()
    {
        // drop any TTF at Assets/Resources/Fonts/GameFont.ttf and every text
        // element will use it automatically (racing fonts are often
        // uppercase-only, which is why all UI text is uppercase)
        uiFont = Resources.Load<Font>("Fonts/GameFont");
        usingCustomFont = uiFont != null;
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("GameUI");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        uiCanvasRt = (RectTransform)canvasGo.transform;
        ApplyUiScaling(scaler);

        // everything lives inside the device safe area (iPhone notch etc.)
        var safeGo = new GameObject("SafeArea");
        safeGo.transform.SetParent(canvas.transform, false);
        safeGo.AddComponent<RectTransform>();
        safeGo.AddComponent<SafeAreaFitter>();
        Transform uiRoot = safeGo.transform;

        scoreText = MakeText(uiRoot, "Score", 92, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0f, -128f), new Vector2(700f, 130f));
        bestText = MakeText(uiRoot, "Best", 40, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0f, -246f), new Vector2(700f, 60f));
        speedText = MakeText(uiRoot, "Speed", 40, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(-40f, -200f), new Vector2(400f, 60f));
        // the weekly rival to beat, under BEST (GameManager.Leaderboard.cs)
        BuildRivalHud(uiRoot);
        // the race rivals' gamertags (GameManager.RaceExtras.cs)
        BuildRaceTags(uiRoot);

        // always-visible currency counters, one per top corner, each on its
        // own dark pill so it reads over the bright sky and road behind it
        persistentCoinsText = MakeCornerCounter(uiRoot, false,
            new Color(1f, 0.85f, 0.25f), out coinPillRt);
        persistentTiresText = MakeCornerCounter(uiRoot, true,
            new Color(1f, 0.68f, 0.62f), out tirePillRt);

        // pause button, top-right under the tire counter: the classic II
        pauseButton = MakePauseButton(uiRoot);
        pauseButton.SetActive(false);
        coinHudText = MakeText(uiRoot, "CoinHud", 36, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(62f, -96f), new Vector2(420f, 55f));
        coinHudText.color = new Color(1f, 0.9f, 0.45f);
        multHudText = MakeText(uiRoot, "MultHud", 34, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(62f, -96f), new Vector2(400f, 50f));
        multHudText.color = new Color(0.7f, 0.9f, 1f);
        itemHudText = MakeText(uiRoot, "ItemHud", 28, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(62f, -218f), new Vector2(500f, 260f));
        itemHudText.color = new Color(1f, 1f, 1f, 0.9f);
        itemHudText.gameObject.SetActive(false); // replaced by the bars below

        // --- active item timer bars, stacked at the bottom of the screen
        for (int i = 0; i < ItemSlots; i++)
        {
            var rootGo = new GameObject("ItemBar" + i);
            rootGo.transform.SetParent(uiRoot, false);
            var rt = rootGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 60f + i * 62f);
            rt.sizeDelta = new Vector2(520f, 52f);

            var bg = new GameObject("BG");
            bg.transform.SetParent(rootGo.transform, false);
            var bgImg = bg.AddComponent<Image>();
            bgImg.sprite = GetRoundedSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);
            bgImg.raycastTarget = false;
            var bgRt = bgImg.rectTransform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(rootGo.transform, false);
            var fillImg = fill.AddComponent<Image>();
            fillImg.sprite = GetRoundedSprite();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0;
            fillImg.raycastTarget = false;
            var fRt = fillImg.rectTransform;
            fRt.anchorMin = new Vector2(0f, 0f);
            fRt.anchorMax = new Vector2(1f, 1f);
            fRt.offsetMin = new Vector2(5f, 5f);
            fRt.offsetMax = new Vector2(-5f, -5f);
            itemBarFills[i] = fillImg;

            var label = MakeText(rootGo.transform, "Label", 28, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 52f));
            label.color = Color.white;
            itemBarLabels[i] = label;

            itemBarRoots[i] = rootGo;
            rootGo.SetActive(false);
        }
        driftText = MakeText(uiRoot, "Drift", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -420f), new Vector2(500f, 70f));
        driftText.text = "DRIFT x1";
        driftText.color = Color.white;
        driftText.gameObject.SetActive(false);
        bonusText = MakeText(uiRoot, "Bonus", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(800f, 80f));
        bonusText.color = new Color(0.4f, 1f, 0.5f);
        bonusBasePos = bonusText.rectTransform.anchoredPosition;
        bonusText.gameObject.SetActive(false);
        centerText = MakeText(uiRoot, "Center", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), new Vector2(950f, 600f));

        // --- interactive panels need a raycaster + an EventSystem
        uiRootCanvas = canvasGo.transform;
        canvasGo.AddComponent<GraphicRaycaster>();
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // --- main menu
        menuPanel = MakePanel(uiRoot, "MenuPanel");
        var title = MakeText(menuPanel.transform, "Title", 96, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 420f), new Vector2(1040f, 150f));
        title.text = "DRIFTLINE";
        KeepGameFont(title, 96);
        menuTitle = title;
        title.color = new Color(1f, 0.72f, 0.12f); // sunny arcade orange
        title.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f); // playful tilt

        // second line of the name, sitting under the big word
        menuSubTitle = MakeText(menuPanel.transform, "TitleSub", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(1040f, 80f));
        menuSubTitle.text = "ETERNAL";
        KeepGameFont(menuSubTitle, 50);
        menuSubTitle.color = new Color(1f, 0.86f, 0.45f);
        menuSubTitle.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        // hidden dev unlock: tap the title 15 times for a password prompt.
        // TODO remove (or keep as an easter egg) before shipping!
        title.raycastTarget = true;
        var titleBtn = title.gameObject.AddComponent<Button>();
        titleBtn.transition = Selectable.Transition.None;
        titleBtn.onClick.AddListener(TitleTapped);
        menuBestText = MakeText(menuPanel.transform, "MenuBest", 52, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(700f, 80f));
        menuBestText.color = new Color(1f, 0.8f, 0.3f);
        // the week's best, under the all-time one - tap it for the ranking
        // (the coins already have their own counter in the corner)
        BuildWeeklyMenuButton(menuPanel.transform);
        // PLAY stands alone; everything else sits in a tidy two-column block
        // underneath, so the menu is not one long stack of identical bars.
        playButtonGo = MakeButton(menuPanel.transform, "PLAY", 64,
            new Vector2(0f, 30f), new Vector2(600f, 150f), PlayPressed, GoGreen)
            .transform.parent.gameObject;

        // Build number, bottom right. The panel bleeds past the safe area, so
        // a corner-anchored label has to come back in by the same amount or it
        // sits off the edge of the screen.
        var version = MakeText(menuPanel.transform, "Version", 44, TextAnchor.LowerRight,
            new Vector2(1f, 0f),
            new Vector2(-40f - PanelBleed.x, 34f + PanelBleed.y),
            new Vector2(340f, 70f), true);
        version.text = GameVersion;
        version.color = new Color(1f, 1f, 1f, 0.28f);
        HookAdCheck(version);   // five taps opens the hidden ad check

        // RACE is the second big button, straight under PLAY, on a
        // chequered flag
        raceButtonGo = ButtonOf(MakeRaceButton(menuPanel.transform, new Vector2(0f, -137f),
            new Vector2(600f, 140f)).transform);

        const float ColX = 152f;
        const float RowTop = -289f;
        const float RowStep = 128f;
        Vector2 small = new Vector2(292f, 112f);
        // the game's own lettering is wide, so it is set smaller than the
        // other scripts' fonts to sit in these buttons with room to spare
        int smallText = Loc.DisplayFontFor(Loc.Current) == null ? 38 : 44;

        spinMenuLabel = MakeButton(menuPanel.transform, "SPIN", smallText,
            new Vector2(-ColX, RowTop), small, OpenWheel,
            new Color(1f, 0.78f, 0.12f));
        spinBadge = MakeBadge(spinMenuLabel.transform.parent);
        garageButtonGo = MakeButton(menuPanel.transform, "GARAGE", smallText,
            new Vector2(ColX, RowTop), small, OpenGarage).transform.parent.gameObject;

        shopButtonGo = MakeButton(menuPanel.transform, "SHOP", smallText,
            new Vector2(-ColX, RowTop - RowStep), small, OpenShop).transform.parent.gameObject;
        questsButtonGo = MakeButton(menuPanel.transform, "QUESTS", smallText,
            new Vector2(ColX, RowTop - RowStep), small, OpenQuests).transform.parent.gameObject;
        // SPIN is drawn after its neighbours: when it bounces, its red dot
        // swings a little over GARAGE and should pass over it, not under it
        spinMenuLabel.transform.parent.SetAsLastSibling();

        // settings: a gear in the bottom-left corner, mirroring the version
        // number in the bottom-right (and brought back in by the panel's
        // overspill the same way)
        settingsButtonGo = MakeIconButton(menuPanel.transform, SettingsIconPath, "SETTINGS",
            new Vector2(0f, 0f), new Vector2(40f + PanelBleed.x, 34f + PanelBleed.y),
            new Vector2(128f, 128f), OpenSettings).gameObject;
        // LOGIN STREAK: a third row, as wide as the two buttons above it and
        // as tall as one, yellow and bouncing with a red dot like SPIN while
        // today's reward is still to collect
        loginMenuLabel = MakeButton(menuPanel.transform, "LOGIN STREAK", smallText,
            new Vector2(0f, RowTop - 2f * RowStep), new Vector2(ColX * 2f + small.x, small.y),
            OpenLoginStreak, LoginYellow);
        loginButtonGo = loginMenuLabel.transform.parent.gameObject;
        loginButtonRt = loginButtonGo.transform as RectTransform;
        loginButtonHome = loginButtonRt.anchoredPosition;
        loginBadge = MakeBadge(loginButtonGo.transform);
        // SPIN stays on top: its dot swings over its neighbours when it bounces
        spinMenuLabel.transform.parent.SetAsLastSibling();

        // --- settings
        settingsPanel = MakePanel(uiRoot, "SettingsPanel");
        MakeCard(settingsPanel.transform, new Vector2(0f, 30f), new Vector2(1010f, 1580f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var sTitle = MakeText(settingsPanel.transform, "SettingsTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 640f), new Vector2(800f, 120f));
        sTitle.text = "SETTINGS";
        sTitle.color = new Color(1f, 0.72f, 0.12f);

        // three sections rather than one long column of sliders
        // four tabs across the card; a long word shrinks to fit its tab
        const float TabW = 226f, TabStep = 238f;
        settingsTabAudio = MakeButton(settingsPanel.transform, "AUDIO", 34,
            new Vector2(-1.5f * TabStep, 512f), new Vector2(TabW, 88f), () => SetSettingsTab(0));
        settingsTabGeneral = MakeButton(settingsPanel.transform, "GENERAL", 34,
            new Vector2(-0.5f * TabStep, 512f), new Vector2(TabW, 88f), () => SetSettingsTab(1));
        settingsTabCredits = MakeButton(settingsPanel.transform, "CREDITS", 34,
            new Vector2(0.5f * TabStep, 512f), new Vector2(TabW, 88f), () => SetSettingsTab(2));
        settingsTabContact = MakeButton(settingsPanel.transform, "CONTACT", 34,
            new Vector2(1.5f * TabStep, 512f), new Vector2(TabW, 88f), () => SetSettingsTab(3));
        foreach (var tabLabel in new[] { settingsTabAudio, settingsTabGeneral, settingsTabCredits, settingsTabContact })
        {
            tabLabel.rectTransform.sizeDelta = new Vector2(TabW - 22f, 80f);
            tabLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            tabLabel.verticalOverflow = VerticalWrapMode.Truncate;
            tabLabel.resizeTextForBestFit = true;
            tabLabel.resizeTextMinSize = 10;
            tabLabel.resizeTextMaxSize = tabLabel.fontSize;
        }

        settingsAudioTab = MakeTabGroup(settingsPanel.transform, "AudioTab");
        settingsGeneralTab = MakeTabGroup(settingsPanel.transform, "GeneralTab");
        settingsCreditsTab = MakeTabGroup(settingsPanel.transform, "CreditsTab");
        settingsContactTab = MakeTabGroup(settingsPanel.transform, "ContactTab");

        // --- audio
        AddSliderRow(settingsAudioTab.transform, "MASTER VOLUME", 340f, volumeSetting, SetVolume);
        AddSliderRow(settingsAudioTab.transform, "MUSIC", 190f, volMusic, SetMusicVol);
        AddSliderRow(settingsAudioTab.transform, "SOUND EFFECTS", 40f, volSfx, SetSfxVol);
        AddSliderRow(settingsAudioTab.transform, "ENGINE", -110f, volEngine, SetEngineVol);
        AddSliderRow(settingsAudioTab.transform, "DRIFT", -260f, volDrift, SetDriftVol);
        AddSliderRow(settingsAudioTab.transform, "COINS", -410f, volCoins, SetCoinVol);

        // --- general
        AddSliderRow(settingsGeneralTab.transform, "STEERING SENSITIVITY", 340f,
            sensSetting, SetSensitivity);
        invertBtnLabel = MakeButton(settingsGeneralTab.transform, InvertLabel(), 38,
            new Vector2(0f, 170f), new Vector2(640f, 100f), ToggleInvert);
        unitsBtnLabel = MakeButton(settingsGeneralTab.transform, UnitsLabel(), 38,
            new Vector2(0f, 40f), new Vector2(640f, 100f), ToggleUnits);
        languageBtnLabel = MakeButton(settingsGeneralTab.transform,
            Loc.F("LANGUAGE: {0}", Loc.NativeName(Loc.Current)), 38,
            new Vector2(0f, -90f), new Vector2(640f, 100f), OpenLanguages);
        BuildLanguagePanel(uiRoot);
        // plays the tutorial again, on its own
        tutorialBtnLabel = MakeButton(settingsGeneralTab.transform, "TUTORIAL", 38,
            new Vector2(0f, -220f), new Vector2(640f, 100f), ReplayTutorial);
        // the name on the weekly ranking
        BuildUsernameButton(settingsGeneralTab.transform);

        // --- credits
        var credLine = MakeText(settingsCreditsTab.transform, "CreditsBody", 28,
            TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -40f), new Vector2(880f, 960f), true);
        credLine.text = CreditsText;
        credLine.color = new Color(1f, 1f, 1f, 0.92f);
        credLine.lineSpacing = 1.15f;

        BuildContactTab(settingsContactTab.transform);

        SetSettingsTab(0);

        // inside the card (it ends at -760), well under the last slider
        MakeCloseButton(settingsPanel.transform, new Vector2(0f, -645f), CloseSettings);

        // --- credits
        creditsPanel = MakePanel(uiRoot, "CreditsPanel");
        Transform credScroll = MakeScrollArea(creditsPanel, 1300f);
        MakeCard(credScroll, new Vector2(0f, -40f), new Vector2(1010f, 1200f),
            new Color(0.13f, 0.09f, 0.2f, 0.94f));
        var cTitle = MakeText(credScroll, "CreditsTitle", 76, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 470f), new Vector2(900f, 110f));
        cTitle.text = "CREDITS";
        cTitle.color = new Color(1f, 0.72f, 0.12f);

        string credits = CreditsText;
        // plain font: readable at small sizes, unlike the display font
        var cBody = MakeText(credScroll, "CreditsBody", 30, TextAnchor.UpperCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(880f, 1000f), true);
        cBody.text = credits;
        cBody.color = new Color(1f, 1f, 1f, 0.92f);
        cBody.lineSpacing = 1.15f;

        // at the foot of the card itself (it ends at -640)
        MakeCloseButton(credScroll, new Vector2(0f, -525f), CloseCredits);

        // --- garage / car shop
        garagePanel = MakePanel(uiRoot, "GaragePanel");
        // full-screen backdrop: the garage is its own screen, not a window
        // over the road running behind the menu
        var gBackGo = new GameObject("Backdrop");
        gBackGo.transform.SetParent(garagePanel.transform, false);
        var gBack = gBackGo.AddComponent<Image>();
        // darkened, not solid: the road keeps running behind the garage
        gBack.color = new Color(0.05f, 0.04f, 0.09f, 0.72f);
        var gBackRt = gBack.rectTransform;
        gBackRt.anchorMin = new Vector2(0f, 0f);
        gBackRt.anchorMax = new Vector2(1f, 1f);
        // stretched well past the safe area so notches and rounded corners
        // are covered too
        gBackRt.offsetMin = new Vector2(-200f, -400f);
        gBackRt.offsetMax = new Vector2(200f, 400f);

        var gTitle = MakeText(garagePanel.transform, "GarageTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 560f), new Vector2(800f, 120f));
        gTitle.text = "GARAGE";
        gTitle.color = new Color(1f, 0.72f, 0.12f);

        // --- tabs
        garageTabCars = MakeButton(garagePanel.transform, "CARS", 40,
            new Vector2(-170f, 452f), new Vector2(320f, 92f), () => SetGarageTab(0));
        garageTabPaint = MakeButton(garagePanel.transform, "PAINT", 40,
            new Vector2(170f, 452f), new Vector2(320f, 92f), () => SetGarageTab(1));

        // everything that belongs to one tab or the other lives in these
        garageCarsTab = MakeTabGroup(garagePanel.transform, "CarsTab");
        garagePaintTab = MakeTabGroup(garagePanel.transform, "PaintTab");
        BuildPaintTab(garagePaintTab.transform);
        shopCoinsText = MakeText(garagePanel.transform, "ShopCoins", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 370f), new Vector2(800f, 70f), true);
        shopCoinsText.color = new Color(1f, 0.82f, 0.1f);
        shopCarName = MakeText(garageCarsTab.transform, "ShopCarName", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(460f, 100f));
        shopCarName.horizontalOverflow = HorizontalWrapMode.Wrap; // never spill onto the arrows
        MakeButton(garageCarsTab.transform, "PREV", 36,
            new Vector2(-380f, 250f), new Vector2(220f, 100f), ShopPrev);
        MakeButton(garageCarsTab.transform, "NEXT", 36,
            new Vector2(380f, 250f), new Vector2(220f, 100f), ShopNext);
        // the car spins on its own stage and is shown here, so it is not mixed
        // in with the road running behind the menu
        var gCarGo = new GameObject("GarageCarView");
        gCarGo.transform.SetParent(garagePanel.transform, false);
        garageCarImage = gCarGo.AddComponent<RawImage>();
        garageCarImage.raycastTarget = false;
        var gCarRt = garageCarImage.rectTransform;
        gCarRt.anchorMin = gCarRt.anchorMax = gCarRt.pivot = new Vector2(0.5f, 0.5f);
        gCarRt.anchoredPosition = new Vector2(0f, 30f);
        gCarRt.sizeDelta = new Vector2(1080f, 1080f);
        garageCarImage.gameObject.SetActive(false);
        // sits directly on the backdrop, underneath every tab - so the paint
        // tab's grey veil covers the car too
        gCarGo.transform.SetSiblingIndex(1);
        shopStats = MakeText(garageCarsTab.transform, "ShopStats", 38, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(950f, 70f));
        shopPrice = MakeText(garageCarsTab.transform, "ShopPrice", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(860f, 70f), true);
        garagePriceIcon = MakeCurrencyIcon(garageCarsTab.transform, new Vector2(0.5f, 0.5f),
            new Vector2(-235f, -250f), 52f, false);
        shopActionLabel = MakeButton(garageCarsTab.transform, "BUY", 48,
            new Vector2(0f, -370f), new Vector2(480f, 120f), ShopAction, GoGreen);
        // below the BUY button and below the paint tab's last row of swatches
        MakeCloseButton(garagePanel.transform, new Vector2(0f, -600f), CloseGarage);

        // --- pause menu
        pausePanel = MakePanel(uiRoot, "PausePanel");
        // taller than it was, to give the quests the room the quests window gives them
        MakeCard(pausePanel.transform, new Vector2(0f, 162.5f), new Vector2(950f, 1115f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var pTitle = MakeText(pausePanel.transform, "PauseTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 625f), new Vector2(800f, 120f));
        pTitle.text = "PAUSED";
        pTitle.color = new Color(1f, 0.72f, 0.12f);
        var qTitle = MakeText(pausePanel.transform, "QuestsTitle", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 515f), new Vector2(700f, 70f));
        qTitle.text = "QUESTS";
        // one card per quest, with the quests window's text size
        for (int i = 0; i < 3; i++)
        {
            float y = 390f - i * 160f;
            MakeCard(pausePanel.transform, new Vector2(0f, y), new Vector2(890f, 150f),
                new Color(0.22f, 0.17f, 0.33f, 0.95f));
            pauseQuestRows[i] = MakeText(pausePanel.transform, "PauseQuest" + i, 50, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(860f, 146f), true);
            pauseQuestRows[i].lineSpacing = 1.15f;
            pauseQuestRows[i].horizontalOverflow = HorizontalWrapMode.Wrap;
        }
        MakeButton(pausePanel.transform, "RESUME", 56,
            new Vector2(0f, -115f), new Vector2(560f, 140f), ResumeGame);
        MakeButton(pausePanel.transform, "MENU", 40,
            new Vector2(0f, -285f), new Vector2(560f, 100f), QuitToMenu);
        // settings: the same gear as the menu's, in the same corner
        MakeIconButton(pausePanel.transform, SettingsIconPath, "SETTINGS",
            new Vector2(0f, 0f), new Vector2(40f + PanelBleed.x, 34f + PanelBleed.y),
            new Vector2(128f, 128f), OpenSettings);

        // --- daily login reward popup
        loginPanel = MakePanel(uiRoot, "LoginPanel");
        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(loginPanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.85f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero; // blocks clicks behind

        // everything except the dimmer lives in one container so it can be
        // scaled as a single unit when the popup springs in
        var loginBodyGo = new GameObject("Body");
        loginBodyGo.transform.SetParent(loginPanel.transform, false);
        var loginBodyRt = loginBodyGo.AddComponent<RectTransform>();
        loginBodyRt.anchorMin = Vector2.zero;
        loginBodyRt.anchorMax = Vector2.one;
        loginBodyRt.offsetMin = loginBodyRt.offsetMax = Vector2.zero;
        Transform loginBody = loginBodyGo.transform;
        var loginPop = loginBodyGo.AddComponent<PopIn>();
        loginPop.dimmer = dim;
        loginPop.dimmerAlpha = 0.85f;

        // a solid card - nothing of the lobby shows through the week
        MakeCard(loginBody, new Vector2(0f, 15f), new Vector2(1020f, 1550f),
            new Color(0.13f, 0.09f, 0.2f, 1f));
        var lTitle = MakeText(loginBody, "LoginTitle", 76, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 720f), new Vector2(960f, 100f));
        lTitle.text = "LOGIN STREAK";
        lTitle.color = new Color(1f, 0.72f, 0.12f);
        loginDayText = MakeText(loginBody, "LoginDay", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 632f), new Vector2(900f, 70f));
        loginDayText.color = new Color(1f, 0.8f, 0.3f);

        // days 1-6: two rows of three big cells. Today's gets a gold frame,
        // drawn behind it (so it is made first).
        loginCellFrame = MakeCard(loginBody, Vector2.zero, LoginCellSize + new Vector2(20f, 20f),
            new Color(1f, 0.86f, 0.25f));
        for (int i = 0; i < 6; i++)
        {
            Vector2 at = new Vector2(-325f + (i % 3) * 325f, i < 3 ? 430f : 115f);
            Image cell = MakeCard(loginBody, at, LoginCellSize, Color.white);

            // plain lettering: it has to read at a glance
            var dayLabel = MakeText(cell.transform, "Day", 34, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 116f), new Vector2(290f, 50f), true);
            dayLabel.text = Loc.F("DAY {0}", i + 1);

            loginCellIcons[i] = MakeLoginPicture(cell.transform, new Vector2(0f, 6f), 158f);
            if (LoginPrizes[i] == LoginPrize.Spin)
            {
                // the wheel gets its pointer, so it reads as the wheel
                var ptrGo = new GameObject("Pointer");
                ptrGo.transform.SetParent(cell.transform, false);
                var ptr = ptrGo.AddComponent<RawImage>();
                ptr.texture = MakePointerTexture();
                ptr.raycastTarget = false;
                var pRt = ptr.rectTransform;
                pRt.anchorMin = pRt.anchorMax = pRt.pivot = new Vector2(0.5f, 0.5f);
                pRt.anchoredPosition = new Vector2(0f, 6f + 79f - 6f);
                pRt.sizeDelta = new Vector2(38f, 44f);
            }

            var cellText = MakeText(cell.transform, "CellText", 36, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -106f), new Vector2(290f, 56f), true);
            cellText.text = LoginCellCaption(i);
            loginCells[i] = cell;
            loginCellTexts[i] = cellText;
        }

        // day 7, the car: a whole row to itself, twice the size of the rest,
        // with a gold edge and the car turning on rays of light
        Vector2 carAt = new Vector2(0f, -225f);
        Vector2 carSize = new Vector2(960f, 350f);
        loginCarEdge = MakeCard(loginBody, carAt, carSize + new Vector2(20f, 20f),
            new Color(1f, 0.86f, 0.25f));
        Image carCell = MakeCard(loginBody, carAt, carSize, Color.white);
        var raysGo = new GameObject("Rays");
        raysGo.transform.SetParent(carCell.transform, false);
        loginCarRays = raysGo.AddComponent<RawImage>();
        loginCarRays.texture = MakeRaysTexture(12);
        loginCarRays.color = new Color(1f, 0.92f, 0.55f, 0.35f);
        loginCarRays.raycastTarget = false;
        var raysRt = loginCarRays.rectTransform;
        raysRt.anchorMin = raysRt.anchorMax = raysRt.pivot = new Vector2(0.5f, 0.5f);
        raysRt.anchoredPosition = new Vector2(-255f, 0f);
        raysRt.sizeDelta = new Vector2(330f, 330f);
        loginCellIcons[6] = MakeLoginPicture(carCell.transform, new Vector2(-255f, 0f), 340f);
        var carDay = MakeText(carCell.transform, "Day", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(170f, 118f), new Vector2(560f, 56f), true);
        carDay.text = Loc.F("DAY {0}", 7);
        var carText = MakeText(carCell.transform, "CellText", 66, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(170f, -8f), new Vector2(560f, 190f));
        carText.lineSpacing = 1.1f;
        loginCells[6] = carCell;
        loginCellTexts[6] = carText;

        loginRewardText = MakeText(loginBody, "LoginReward", 62, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -478f), new Vector2(960f, 110f));
        loginRewardText.color = new Color(1f, 0.82f, 0.1f);
        claimBtnLabel = MakeButton(loginBody, "CLAIM", 58,
            new Vector2(0f, -640f), new Vector2(560f, 140f), ClaimLogin, GoGreen);

        BuildUnlockPanel(uiRoot);
        BuildWheelPanel(uiRoot);
        BuildBoxFocus(uiRoot);
        BuildPrizeReveal(uiRoot);
        int finishFrom = uiRoot.childCount;
        BuildFinishCinematic(uiRoot);
        var finishParts = new List<Transform>();
        for (int i = finishFrom; i < uiRoot.childCount; i++) finishParts.Add(uiRoot.GetChild(i));

        // --- race select: one Grand Prix per biome, flicked through like
        // the garage's cars (GameManager.RaceSelect.cs)
        BuildRaceSelect(uiRoot);
        BuildUpgradeAdPopup();

        // race HUD: position and distance remaining
        raceHudText = MakeText(uiRoot, "RaceHud", 76, TextAnchor.UpperCenter,
            new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(600f, 170f));
        raceHudText.color = new Color(1f, 0.85f, 0.35f);
        raceHudText.gameObject.SetActive(false);

        countdownText = MakeText(uiRoot, "Countdown", 170, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(700f, 300f));
        countdownText.gameObject.SetActive(false);

        // --- quests panel
        questsPanel = MakePanel(uiRoot, "QuestsPanel");
        MakeCard(questsPanel.transform, new Vector2(0f, 30f), new Vector2(1010f, 1350f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var qpTitle = MakeText(questsPanel.transform, "QPTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 550f), new Vector2(800f, 120f));
        qpTitle.text = "QUESTS";
        qpTitle.color = new Color(1f, 0.72f, 0.12f);
        // the multiplier, upright and plain; the tire count is the corner
        // counter's job, so the window does not repeat it
        questMultText = MakeText(questsPanel.transform, "QPMult", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 405f), new Vector2(900f, 70f), true);
        questMultText.color = new Color(0.72f, 0.92f, 1f);
        for (int i = 0; i < 3; i++)
        {
            MakeCard(questsPanel.transform, new Vector2(0f, 190f - i * 220f),
                new Vector2(920f, 190f), new Color(0.22f, 0.17f, 0.33f, 0.95f));
            // big enough to read at a glance; a long one (in some languages)
            // wraps onto a second line rather than running off the card
            questRowTexts[i] = MakeText(questsPanel.transform, "QRow" + i, 50, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 190f - i * 220f), new Vector2(870f, 184f), true);
            questRowTexts[i].lineSpacing = 1.15f;
            questRowTexts[i].horizontalOverflow = HorizontalWrapMode.Wrap;
        }
        var qpHint = MakeText(questsPanel.transform, "QPHint", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -410f), new Vector2(920f, 90f), true);
        qpHint.text = "COMPLETE ALL 3\n+1 MULTIPLIER   +6 TIRES";
        qpHint.color = new Color(1f, 1f, 1f, 0.9f);
        questHint = qpHint;
        // inside the card (it ends at -645), under the hint
        MakeCloseButton(questsPanel.transform, new Vector2(0f, -565f), CloseQuests);
        // two done, one to go: finish it with an ad or 5 tires
        BuildQuestSkip(questsPanel.transform);

        // --- shop (tires for money, mystery boxes for coins)
        shopPanel = MakePanel(uiRoot, "ShopPanel");
        // content runs from about +635 (title) down to -2150 (last upgrade row).
        // The shop has no coin and tire lines of its own: the corner counters
        // are drawn in front of the windows, so they show here as everywhere.
        Color shopWindow = new Color(0.13f, 0.09f, 0.2f, 0.92f);
        Transform shopScroll = MakeScrollArea(shopPanel, 2835f, -757.5f);
        // carries on past the end of the content so it runs into the footer
        MakeCard(shopScroll, new Vector2(0f, -802.5f), new Vector2(1010f, 2905f), shopWindow);
        var shTitle = MakeText(shopScroll, "ShopTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 575f), new Vector2(800f, 120f));
        shTitle.text = "SHOP";
        shTitle.color = new Color(1f, 0.72f, 0.12f);

        var boxLabel = MakeText(shopScroll, "BoxLabel", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 460f), new Vector2(700f, 70f));
        boxLabel.text = "MYSTERY BOX";

        // live view of the animated 3D toolbox
        var tbGo = new GameObject("ToolboxView");
        tbGo.transform.SetParent(shopScroll, false);
        toolboxImage = tbGo.AddComponent<RawImage>();
        toolboxImage.raycastTarget = false;
        var tbRt = toolboxImage.rectTransform;
        tbRt.anchorMin = tbRt.anchorMax = tbRt.pivot = new Vector2(0.5f, 0.5f);
        tbRt.anchoredPosition = new Vector2(0f, 285f);
        tbRt.sizeDelta = new Vector2(290f, 290f);

        // what's inside, and how likely - beside the box, before buying it
        MakeOddsButton(shopScroll, new Vector2(335f, 285f), new Vector2(220f, 92f),
            OddsFor.MysteryBox);

        var boxBtnLabel = MakeButton(shopScroll, Loc.F("OPEN BOX  {0}", MysteryBoxCost), 48,
            new Vector2(0f, 90f), new Vector2(700f, 116f), BuyMysteryBox, null, true);
        IconAfterText(MakeCurrencyIcon(boxBtnLabel.transform.parent, new Vector2(0.5f, 0.5f),
            Vector2.zero, 62f, false), boxBtnLabel);
        boxResultText = MakeText(shopScroll, "BoxResult", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -5f), new Vector2(900f, 70f), true);

        // --- blue token box
        var tokLabel = MakeText(shopScroll, "TokenBoxLabel", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -95f), new Vector2(800f, 70f));
        tokLabel.text = "TOKEN BOX";
        tokLabel.color = new Color(0.45f, 0.75f, 1f);

        var tbGo2 = new GameObject("TokenBoxView");
        tbGo2.transform.SetParent(shopScroll, false);
        tokenBoxImage = tbGo2.AddComponent<RawImage>();
        tokenBoxImage.raycastTarget = false;
        var tb2Rt = tokenBoxImage.rectTransform;
        tb2Rt.anchorMin = tb2Rt.anchorMax = tb2Rt.pivot = new Vector2(0.5f, 0.5f);
        tb2Rt.anchoredPosition = new Vector2(0f, -265f);
        tb2Rt.sizeDelta = new Vector2(255f, 255f);

        MakeOddsButton(shopScroll, new Vector2(335f, -265f), new Vector2(220f, 92f),
            OddsFor.TokenBox);

        var tokBtn = MakeButton(shopScroll, Loc.F("OPEN  {0}", TokenBoxTireCost), 48,
            new Vector2(0f, -430f), new Vector2(700f, 116f), BuyTokenBox,
            new Color(0.16f, 0.45f, 0.9f), true);
        IconAfterText(MakeCurrencyIcon(tokBtn.transform.parent, new Vector2(0.5f, 0.5f),
            Vector2.zero, 62f, true), tokBtn);
        tokenBoxResultText = MakeText(shopScroll, "TokenBoxResult", 44,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -520f), new Vector2(900f, 70f), true);

        var tiresLabel = MakeText(shopScroll, "TiresLabel", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -610f), new Vector2(700f, 70f));
        tiresLabel.text = "TIRES";
        // bigger packs give progressively more bonus tires (base: 10 per $0.99)
        for (int i = 0; i < 6; i++)
        {
            int pack = i; // capture for the closure
            float x = i % 2 == 0 ? -235f : 235f;
            float y = -710f - (i / 2) * 140f;
            // the amount big, the price under it nearly as big
            Text label = MakeButton(shopScroll, "", 52,
                new Vector2(x, y), new Vector2(440f, 132f), () => BuyTirePack(pack),
                null, true);
            // Unity's own font here: it falls back to the phone's fonts, so
            // any currency sign the store sends (EUR, JPY, INR, KRW...) shows
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            packLabels[i] = label;
            // nudge text right to make room for the tire-stack icon
            label.rectTransform.anchoredPosition = new Vector2(45f, 0f);
            label.rectTransform.sizeDelta = new Vector2(330f, 150f);

            var iconGo = new GameObject("PackIcon");
            iconGo.transform.SetParent(label.transform.parent, false);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            var iRt = icon.rectTransform;
            iRt.anchorMin = iRt.anchorMax = iRt.pivot = new Vector2(0.5f, 0.5f);
            iRt.anchoredPosition = new Vector2(-143f, 4f);
            iRt.sizeDelta = new Vector2(96f, 96f);
            packIcons[i] = icon;
        }
        RefreshPackPrices();

        // coins for tires: three exchange packs
        var coinsLabel = MakeText(shopScroll, "CoinsLabel", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -1105f), new Vector2(700f, 70f));
        coinsLabel.text = "COINS";
        int[] coinPackCoins = { 1500, 4500, 12000 };
        int[] coinPackCost = { 10, 25, 60 };
        for (int i = 0; i < 3; i++)
        {
            int cCoins = coinPackCoins[i];
            int cCost = coinPackCost[i];
            Text label = MakeButton(shopScroll,
                FormatCount(cCoins) + "\n<size=" + PackPriceSize + ">" + cCost + "</size>", 46,
                new Vector2(-330f + i * 330f, -1205f), new Vector2(305f, 155f),
                () => BuyCoinPack(cCoins, cCost), null, true);
            label.rectTransform.anchoredPosition = new Vector2(40f, 0f);
            label.rectTransform.sizeDelta = new Vector2(205f, 155f);

            // spinning coin stack, matching the tire packs above
            var cIconGo = new GameObject("CoinPackIcon");
            cIconGo.transform.SetParent(label.transform.parent, false);
            var cIcon = cIconGo.AddComponent<RawImage>();
            cIcon.raycastTarget = false;
            var cRt = cIcon.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0.5f, 0.5f);
            cRt.anchoredPosition = new Vector2(-74f, 0f);
            cRt.sizeDelta = new Vector2(92f, 92f);
            coinPackIcons[i] = cIcon;

            // tyre tucked right up against the price
            MakeCurrencyIcon(label.transform.parent, new Vector2(0.5f, 0.5f),
                new Vector2(88f, -21f), 42f, true);
        }

        // --- permanent power-up duration upgrades, 7 levels each
        var itemLabel = MakeText(shopScroll, "ItemUpLabel", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -1330f), new Vector2(800f, 70f));
        itemLabel.text = "POWER UP DURATION";
        itemLabel.color = new Color(0.6f, 1f, 0.7f);

        for (int i = 0; i < ItemUpgrades.Count; i++)
        {
            int item = i;                       // capture for the closure
            float y = -1440f - i * 150f;

            MakeCard(shopScroll, new Vector2(0f, y), new Vector2(940f, 132f),
                new Color(0.2f, 0.16f, 0.3f, 0.9f));

            itemUpNames[i] = MakeText(shopScroll, "ItemUpName" + i, 42,
                TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
                new Vector2(-215f, y + 24f), new Vector2(480f, 56f), true);
            itemUpInfo[i] = MakeText(shopScroll, "ItemUpInfo" + i, 34,
                TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
                new Vector2(-215f, y - 26f), new Vector2(480f, 50f), true);
            itemUpInfo[i].color = new Color(1f, 1f, 1f, 0.92f);

            itemUpButtons[i] = MakeButton(shopScroll, "", 46,
                new Vector2(270f, y), new Vector2(330f, 108f),
                () => BuyItemUpgrade(item), null, true);
            // coin icon sits to the left of the price
            itemUpCoins[i] = MakeCurrencyIcon(itemUpButtons[i].transform.parent,
                new Vector2(0.5f, 0.5f), new Vector2(-100f, 0f), 54f, false);
            itemUpButtons[i].rectTransform.anchoredPosition = new Vector2(32f, 0f);
        }

        MakeScrollFooter(shopPanel, shopWindow, CloseShop);

        // --- revive offer
        revivePanel = MakePanel(uiRoot, "RevivePanel");
        var rDimGo = new GameObject("Dim");
        rDimGo.transform.SetParent(revivePanel.transform, false);
        var rDim = rDimGo.AddComponent<Image>();
        rDim.color = new Color(0f, 0f, 0f, 0.7f);
        var rDimRt = rDim.rectTransform;
        rDimRt.anchorMin = Vector2.zero;
        rDimRt.anchorMax = Vector2.one;
        rDimRt.offsetMin = rDimRt.offsetMax = Vector2.zero;
        MakeCard(revivePanel.transform, new Vector2(0f, 0f), new Vector2(950f, 900f),
            new Color(0.13f, 0.09f, 0.2f, 0.95f));
        var rTitle = MakeText(revivePanel.transform, "RTitle", 76, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 335f), new Vector2(900f, 110f));
        rTitle.text = "CRASHED!";
        rTitle.color = new Color(1f, 0.4f, 0.3f);
        var rSub = MakeText(revivePanel.transform, "RSub", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(900f, 70f));
        rSub.text = "KEEP GOING?";
        // ticking clock - the offer expires
        reviveTimerText = MakeText(revivePanel.transform, "RTimer", 66, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 178f), new Vector2(900f, 90f));
        reviveTimerText.color = new Color(1f, 0.8f, 0.25f);
        // and a bar that runs out with it
        var rBarBack = MakeCard(revivePanel.transform, new Vector2(0f, 118f),
            new Vector2(ReviveBarWidth + 14f, 36f), new Color(0f, 0f, 0f, 0.55f));
        reviveBarFill = MakeCard(rBarBack.transform, Vector2.zero,
            new Vector2(ReviveBarWidth, 22f), new Color(1f, 0.8f, 0.25f));
        var rbf = reviveBarFill.rectTransform;
        rbf.anchorMin = rbf.anchorMax = rbf.pivot = new Vector2(0f, 0.5f);
        rbf.anchoredPosition = new Vector2(7f, 0f);
        reviveTireLabel = MakeButton(revivePanel.transform, "TIRES", 44,
            new Vector2(0f, 28f), new Vector2(760f, 120f), ReviveWithTires, null, true);
        // the tyre reads as part of the price, so it sits just after the number
        MakeCurrencyIcon(reviveTireLabel.transform.parent, new Vector2(0.5f, 0.5f),
            new Vector2(150f, 0f), 62f, true);
        reviveTireLabel.rectTransform.anchoredPosition = new Vector2(-34f, 0f);
        reviveHaveText = MakeText(revivePanel.transform, "RHave", 38,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -62f), new Vector2(700f, 50f), true);
        reviveHaveText.color = new Color(1f, 1f, 1f, 0.9f);
        // green: the free way back in
        reviveAdLabel = MakeButton(revivePanel.transform, "AD", 38,
            new Vector2(0f, -160f), new Vector2(760f, 120f), ReviveWithAd, GoGreen, true);
        // grey: the way out, so it does not compete with the two revives
        MakeButton(revivePanel.transform, "GIVE UP", 40,
            new Vector2(0f, -318f), new Vector2(560f, 110f), DeclineRevive, GiveUpGrey);

        // The coin and tire counters were made first, so every window drew
        // over them. They come to the front now, above the menu, the shop,
        // the race screen and every other window and popup; the race finish
        // goes back above them (as do game over and the best-run screen,
        // made below, and the box and prize screens, which lift themselves).
        coinPillRt.SetAsLastSibling();
        tirePillRt.SetAsLastSibling();
        foreach (Transform part in finishParts) part.SetAsLastSibling();

        BuildGameOverPanel(uiRoot);
        BuildBestPanel(uiRoot);
        BuildLeaderboardPanel(uiRoot);
        BuildNamePanel(uiRoot);
        BuildTutorial(uiRoot);
        BuildCoach(uiRoot);
    }

    // ------------------------------------------------------------- tutorial

    const string TutorialKey = "TutorialSeen";
    GameObject tutorialPanel;
    Text tutorialHint;
    int tutorialStep = -1;          // -1 = not coaching
    float tutorialStepT;
    bool tutorialRun;               // this run is the very first one
    bool tutorialOnly;              // replayed from Settings: the drive ends with it
    Text tutorialBtnLabel;

    static bool TutorialSeen { get { return PlayerPrefs.GetInt(TutorialKey, 0) == 1; } }

    void BuildTutorial(Transform uiRoot)
    {
        tutorialPanel = MakePanel(uiRoot, "TutorialPanel");

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(tutorialPanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0.02f, 0.01f, 0.06f, 0.88f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

        MakeCard(tutorialPanel.transform, new Vector2(0f, 40f), new Vector2(1000f, 1320f),
            new Color(0.13f, 0.09f, 0.2f, 0.97f));

        var title = MakeText(tutorialPanel.transform, "TutTitle", 76, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 560f), new Vector2(900f, 110f));
        title.text = "HOW TO PLAY";
        title.color = new Color(1f, 0.72f, 0.12f);

        // Each line is a heading and a plain-font explanation underneath, so
        // the important word is readable at a glance and the detail is there
        // for anyone who wants it.
        string[] heads = { "SLIDE TO STEER", "HOLD IT TO DRIFT", "SWIPE TO JUMP", "KEEP IT CLEAN" };
        string[] bodies =
        {
            "Slide your thumb anywhere\non screen. No buttons.",
            "Stay on the slide through a\ncorner and the tail steps out.",
            "With springs, flick your\nthumb up to hop over things.",
            "Drifts and near misses build\nyour combo. Crashing ends it.",
        };

        for (int i = 0; i < heads.Length; i++)
        {
            float y = 380f - i * 195f;
            var h = MakeText(tutorialPanel.transform, "TutHead" + i, 42, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(900f, 60f));
            h.text = heads[i];
            h.color = new Color(0.55f, 0.95f, 1f);

            var b = MakeText(tutorialPanel.transform, "TutBody" + i, 28, TextAnchor.UpperCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, y - 78f), new Vector2(860f, 110f), true);
            b.horizontalOverflow = HorizontalWrapMode.Wrap;   // never run off the card
            b.text = bodies[i];
            b.color = new Color(1f, 1f, 1f, 0.85f);
            b.lineSpacing = 1.15f;
        }

        MakeButton(tutorialPanel.transform, "LET'S DRIVE", 46,
            new Vector2(0f, -540f), new Vector2(660f, 130f), CloseTutorial,
            new Color(0.16f, 0.62f, 0.32f));

        // the coaching line that runs during the first drive
        tutorialHint = MakeText(uiRoot, "TutHint", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(1000f, 90f));
        tutorialHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        tutorialHint.color = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>
    /// PLAY. The tutorial's first stop is PLAY (and the very first PLAY ever
    /// starts it); its last stop is PLAY too, which starts a real run.
    /// </summary>
    void PlayPressed()
    {
        if (coachPhase == CoachPhase.MenuStart ||
            (coachPhase == CoachPhase.None && !TutorialSeen))
        {
            audioMan.PlayTap();
            StartCoachDrive();
            return;
        }
        if (coachPhase == CoachPhase.Tour) CoachFinish();
        StartRun();
    }

    void CloseTutorial()
    {
        tutorialPanel.SetActive(false);
        PlayerPrefs.SetInt(TutorialKey, 1);
        PlayerPrefs.Save();

        // the prompts carry on into the drive itself - reading about a drift
        // and being told to do one as the corner arrives are different things
        tutorialRun = true;
        tutorialStep = 0;
        tutorialStepT = 0f;
        StartRun();
    }

    // Short. These are read at speed, in a display font that is wider than it
    // looks, and MakeText does not wrap - a long line simply runs off the
    // screen at both ends.
    static readonly string[] TutorialPrompts =
    {
        "SLIDE TO STEER",
        "HOLD IT THROUGH THE CORNER",
        "NICE - THAT'S A DRIFT",
        "GRAB COINS, DODGE THE REST",
    };

    /// <summary>
    /// Walks the player through the controls on their first drive. Each step
    /// waits for the thing it asked for rather than running on a timer, so
    /// nobody is told "nice drift" before they have done one.
    /// </summary>
    void TickTutorial(float dt)
    {
        if (!tutorialRun || tutorialStep < 0) return;

        tutorialStepT += dt;
        bool advance = false;

        switch (tutorialStep)
        {
            case 0:   // waiting for any steering at all
                advance = Mathf.Abs(car.SteerInput) > 0.25f && tutorialStepT > 0.6f;
                break;
            case 1:   // waiting for a real drift
                advance = car.IsDrifting && tutorialStepT > 0.4f;
                break;
            case 2:   // the well done, on a timer
                advance = tutorialStepT > 1.8f;
                break;
            default:  // the last line, then out of the way
                advance = tutorialStepT > 4f;
                break;
        }

        // Never nag: if a step goes unanswered for long enough, move on. The
        // player is clearly driving, which was the point.
        if (!advance && tutorialStep < 2 && tutorialStepT > 14f) advance = true;

        int shown = Mathf.Min(tutorialStep, TutorialPrompts.Length - 1);
        tutorialHint.text = TutorialPrompts[shown];

        // fades up, holds, and fades out as the step ends
        float a = Mathf.Clamp01(tutorialStepT / 0.35f);
        tutorialHint.color = new Color(1f, 1f, 1f, a * (0.75f + 0.25f * Mathf.Sin(tutorialStepT * 3f)));

        if (!advance) return;

        tutorialStep++;
        tutorialStepT = 0f;
        if (tutorialStep >= TutorialPrompts.Length)
        {
            tutorialStep = -1;
            tutorialRun = false;
            tutorialHint.color = new Color(1f, 1f, 1f, 0f);
            // replayed from Settings: that was the whole tutorial
            if (tutorialOnly) Invoke(nameof(FinishTutorialOnly), 0.5f);
        }
        else
        {
            audioMan.PlayPop();
        }
    }

    /// <summary>A crash during a tutorial replay: the bang, then back to the menu.</summary>
    void TutorialOnlyCrash()
    {
        state = State.GameOver;
        HideDriftText();
        EndTutorialHints();
        centerText.gameObject.SetActive(false);
        pauseButton.SetActive(false);
        if (camFollow != null) camFollow.Shake(0.8f);
        audioMan.PlayCrash();
        Vector3 blastAt = car.CarModelRoot != null
            ? car.CarModelRoot.position + Vector3.up * 0.4f
            : car.transform.position + Vector3.up * 0.7f;
        CrashExplosion.Play(blastAt, 1.15f);
        audioMan.PlayExplosion();
        audioMan.StopDriving();
        Invoke(nameof(FinishTutorialOnly), 1.2f);
    }

    void FinishTutorialOnly()
    {
        tutorialOnly = false;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
        audioMan.StopDriving();
        GoToMenu();
    }

    /// <summary>Takes the coaching off the screen when the drive ends.</summary>
    void EndTutorialHints()
    {
        tutorialRun = false;
        tutorialStep = -1;
        if (tutorialHint != null) tutorialHint.color = new Color(1f, 1f, 1f, 0f);
    }

    // the death screen and the wreck: GameManager.CrashScreen.cs

    /// <summary>
    /// Wraps a panel in a vertical ScrollRect. Returns the content transform
    /// to parent children to; they keep using centred anchored positions.
    /// </summary>
    /// <summary>
    /// Scrollable panel. <paramref name="contentCenter"/> is the midpoint of the
    /// content's design coordinates - pass it when a panel's content is not
    /// centred on y=0, or the scroll range will not line up with what's in it.
    /// </summary>
    Transform MakeScrollArea(GameObject panel, float contentHeight, float contentCenter = 0f)
    {
        // the panel itself needs a graphic so drags register on empty space
        var panelImg = panel.GetComponent<Image>();
        if (panelImg == null) panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0f);
        panelImg.raycastTarget = true;

        var viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(panel.transform, false);
        var vpRt = viewportGo.AddComponent<RectTransform>();
        // RectMask2D clips by rectangle - no stencil or alpha-cutoff pitfalls
        viewportGo.AddComponent<RectMask2D>();
        vpRt.anchorMin = Vector2.zero;
        vpRt.anchorMax = Vector2.one;
        // measured from the panel's bled edges, so the visible window is the
        // same as it was before panels started overspilling the safe area
        // the bottom strip is kept for the window's footer (MakeScrollFooter)
        vpRt.offsetMin = new Vector2(PanelBleed.x, ScrollFooterHeight + PanelBleed.y);
        vpRt.offsetMax = new Vector2(-PanelBleed.x, -PanelBleed.y);

        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.AddComponent<RectTransform>();
        // centred anchors + pivot: children keep using y=0-centred positions
        // exactly as they did before the panel became scrollable
        contentRt.anchorMin = new Vector2(0.5f, 0.5f);
        contentRt.anchorMax = new Vector2(0.5f, 0.5f);
        contentRt.pivot = new Vector2(0.5f, 0.5f);
        contentRt.sizeDelta = new Vector2(1080f, contentHeight);
        contentRt.anchoredPosition = Vector2.zero;

        var scroll = panel.AddComponent<ScrollRect>();
        scroll.content = contentRt;
        scroll.viewport = vpRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;

        scroll.verticalNormalizedPosition = 1f; // start at the top

        // Children keep using the panel's own y coordinates; this inner layer
        // shifts them so the used range fills the scrollable content exactly.
        var innerGo = new GameObject("Inner");
        innerGo.transform.SetParent(contentGo.transform, false);
        var innerRt = innerGo.AddComponent<RectTransform>();
        innerRt.anchorMin = innerRt.anchorMax = innerRt.pivot = new Vector2(0.5f, 0.5f);
        innerRt.sizeDelta = Vector2.zero;
        innerRt.anchoredPosition = new Vector2(0f, -contentCenter);
        return innerGo.transform;
    }

    /// <summary>
    /// A full-screen panel. It deliberately overspills the safe area on every
    /// side: the UI lives inside the notch inset, so a panel that stopped at
    /// the safe area would leave strips of the game visible along the edges
    /// behind its backdrop. The overspill is symmetric, so anything centred
    /// inside it stays exactly where it was.
    /// </summary>
    GameObject MakePanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-PanelBleed.x, -PanelBleed.y);
        rt.offsetMax = new Vector2(PanelBleed.x, PanelBleed.y);
        go.SetActive(false);
        return go;
    }

    /// <summary>How far panels reach past the safe area, in canvas units.</summary>
    static readonly Vector2 PanelBleed = new Vector2(260f, 460f);

    void AddSliderRow(Transform parent, string label, float y, float value,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        var text = MakeText(parent, label + "Label", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, y + 62f), new Vector2(800f, 54f), true);
        text.text = label;
        MakeSlider(parent, new Vector2(0f, y), value, onChanged);
    }

    Slider MakeSlider(Transform parent, Vector2 offset, float initialValue,
        UnityEngine.Events.UnityAction<float> onChanged)
    {
        var go = new GameObject("Slider");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(640f, 70f);

        // background track
        var bgGo = new GameObject("Background");
        bgGo.transform.SetParent(go.transform, false);
        var bg = bgGo.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.12f);
        var bgRt = bg.rectTransform;
        bgRt.anchorMin = new Vector2(0f, 0.32f);
        bgRt.anchorMax = new Vector2(1f, 0.68f);
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // fill
        var fillAreaGo = new GameObject("Fill Area");
        fillAreaGo.transform.SetParent(go.transform, false);
        var faRt = fillAreaGo.AddComponent<RectTransform>();
        faRt.anchorMin = new Vector2(0f, 0.32f);
        faRt.anchorMax = new Vector2(1f, 0.68f);
        faRt.offsetMin = new Vector2(6f, 0f);
        faRt.offsetMax = new Vector2(-6f, 0f);

        var fillGo = new GameObject("Fill");
        fillGo.transform.SetParent(fillAreaGo.transform, false);
        var fill = fillGo.AddComponent<Image>();
        fill.color = new Color(1f, 0.55f, 0.1f, 0.9f);
        var fillRt = fill.rectTransform;
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;

        // handle
        var handleAreaGo = new GameObject("Handle Slide Area");
        handleAreaGo.transform.SetParent(go.transform, false);
        var haRt = handleAreaGo.AddComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero;
        haRt.anchorMax = Vector2.one;
        haRt.offsetMin = new Vector2(20f, 0f);
        haRt.offsetMax = new Vector2(-20f, 0f);

        var handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(handleAreaGo.transform, false);
        var handle = handleGo.AddComponent<Image>();
        handle.color = Color.white;
        var hRt = handle.rectTransform;
        hRt.sizeDelta = new Vector2(34f, 70f);

        var slider = go.AddComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handle;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = initialValue;
        slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    // ------------------------------------------------- playful UI styling

    static Sprite roundedSprite;

    // rounded rect with a baked border + bottom shading, drawn in code.
    // white, so Image.color tints it to any button colour.
    static Sprite GetRoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;

        const int S = 64;
        const float R = 18f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - (S - 1) * 0.5f) - ((S - 1) * 0.5f - R), 0f);
                float dy = Mathf.Max(Mathf.Abs(y - (S - 1) * 0.5f) - ((S - 1) * 0.5f - R), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(R - d + 0.5f);

                float shade = Mathf.Clamp01(Mathf.Lerp(0.7f, 1.08f, y / (float)(S - 1)));
                if (d > R - 5f) shade *= 0.55f; // darker rim
                tex.SetPixel(x, y, new Color(shade, shade, shade, a));
            }
        }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        roundedSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(24f, 24f, 24f, 24f));
        return roundedSprite;
    }

    // ------------------------------------------------- raised buttons

    /// <summary>How tall a button's side (the lip under its face) is, in canvas units.</summary>
    const int ButtonLip = 10;
    /// <summary>How far a button's face sinks while it is held down.</summary>
    const int ButtonPressTravel = 7;
    static Sprite buttonSpriteUp, buttonSpriteDown;

    /// <summary>
    /// The buttons' look: a rounded face sitting on a darker side that shows
    /// below it, with a light bevel along the top, so each one reads as a
    /// raised key. The pressed version has the face pushed down onto its side.
    /// White, like the rounded sprite, so Image.color tints it to any colour.
    /// </summary>
    static Sprite GetButtonSprite(bool pressed)
    {
        Sprite cached = pressed ? buttonSpriteDown : buttonSpriteUp;
        if (cached != null) return cached;

        const int W = 64;
        const int Face = 64;
        const float R = 18f;
        int h = Face + ButtonLip;
        int faceBottom = pressed ? ButtonLip - ButtonPressTravel : ButtonLip;
        var tex = new Texture2D(W, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < W; x++)
            {
                // the side, seen below the face
                float lipEdge;
                float lipA = RoundedCoverage(x, y, W, Face, R, out lipEdge);
                float lip = 0.30f + 0.10f * (y / (float)(Face - 1));

                // the face
                int fy = y - faceBottom;
                float faceEdge;
                float faceA = RoundedCoverage(x, fy, W, Face, R, out faceEdge);
                float face = 0.84f + 0.12f * (fy / (float)(Face - 1));
                if (fy >= Face - 8 && fy < Face - 3 && faceEdge < R - 3f) face = 1f;  // bevel
                if (faceEdge > R - 3f) face *= 0.72f;                                   // rim

                float a = faceA + lipA * (1f - faceA);
                float v = a > 0f ? (face * faceA + lip * lipA * (1f - faceA)) / a : 1f;
                v = Mathf.Min(v, 1f);
                tex.SetPixel(x, y, new Color(v, v, v, a));
            }
        }
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        // left, bottom, right, top: the corners, the side and the bevel keep
        // their size on any button, only the middle stretches
        var sprite = Sprite.Create(tex, new Rect(0, 0, W, h), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect,
            new Vector4(24f, 24f + ButtonLip, 24f, 24f + ButtonPressTravel));
        if (pressed) buttonSpriteDown = sprite;
        else buttonSpriteUp = sprite;
        return sprite;
    }

    /// <summary>How much of pixel (x, y) a w x h rounded rectangle covers, 0-1.</summary>
    static float RoundedCoverage(int x, int y, int w, int h, float r, out float edge)
    {
        edge = 0f;
        if (y < 0 || y >= h) return 0f;
        float dx = Mathf.Max(Mathf.Abs(x - (w - 1) * 0.5f) - ((w - 1) * 0.5f - r), 0f);
        float dy = Mathf.Max(Mathf.Abs(y - (h - 1) * 0.5f) - ((h - 1) * 0.5f - r), 0f);
        edge = Mathf.Sqrt(dx * dx + dy * dy);
        return Mathf.Clamp01(r - edge + 0.5f);
    }

    /// <summary>Gives a button the raised look and a physical press.</summary>
    static void AddButtonDepth(Image img)
    {
        img.sprite = GetButtonSprite(false);
        img.type = Image.Type.Sliced;
        var depth = img.gameObject.AddComponent<ButtonDepth>();
        depth.image = img;
        depth.upSprite = GetButtonSprite(false);
        depth.downSprite = GetButtonSprite(true);
        depth.lift = ButtonLip * 0.5f;
        depth.travel = ButtonPressTravel;
    }

    // ------------------------------------------------- round close buttons

    static Sprite closeSpriteUp, closeSpriteDown;

    /// <summary>
    /// The round close button: a red disc with a white rim and a white X,
    /// lit from above with a gloss across the top, standing on a darker side
    /// with a soft shadow under it. The pressed version has the face pushed
    /// down onto its side. Drawn in code, in full colour.
    /// </summary>
    static Sprite GetCloseSprite(bool pressed)
    {
        Sprite cached = pressed ? closeSpriteDown : closeSpriteUp;
        if (cached != null) return cached;

        const int W = 160, H = 178;
        const float CX = 79.5f, FaceY = 101.5f, R = 72f, Rim = 7f;
        const float Depth = 12f, Travel = 8f, Arm = 25f, Thick = 7f;
        float fy = FaceY - (pressed ? Travel : 0f);
        float sideY = FaceY - Depth;
        const float Diagonal = Mathf.PI / 4f;

        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var pixels = new Color[W * H];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                Color c = new Color(1f, 1f, 1f, 0f);
                // soft shadow under the whole button
                float sd = Mathf.Sqrt((x - CX) * (x - CX) + (y - (FaceY - Depth - 3f)) * (y - (FaceY - Depth - 3f)));
                c = Over(c, Color.black, 0.32f * Mathf.Clamp01((R + 9f - sd) / 12f));
                // the side: a paler rim over dark red, showing below the face
                c = Over(c, new Color(0.74f, 0.74f, 0.80f), Disc(x, y, CX, sideY, R));
                c = Over(c, new Color(0.46f, 0.04f, 0.06f), Disc(x, y, CX, sideY, R - Rim));
                // the face: white rim, red disc lit from the top
                c = Over(c, Color.white, Disc(x, y, CX, fy, R));
                float inner = Disc(x, y, CX, fy, R - Rim);
                float t = Mathf.Clamp01((y - (fy - R)) / (2f * R));
                c = Over(c, new Color(0.78f + 0.22f * t, 0.07f + 0.25f * t, 0.09f + 0.20f * t), inner);
                // gloss across the top
                float gloss = Disc(x, y, CX, fy + R * 0.55f, R * 0.78f) * inner
                            * Mathf.Clamp01((y - fy) / (R * 0.35f));
                c = Over(c, Color.white, 0.22f * gloss);
                // the X, with a faint shadow just below it
                float shade = Mathf.Max(Capsule(x, y, CX, fy - 2.5f, Diagonal, Arm, Thick),
                                        Capsule(x, y, CX, fy - 2.5f, -Diagonal, Arm, Thick));
                c = Over(c, new Color(0.35f, 0f, 0.02f), 0.45f * shade);
                float cross = Mathf.Max(Capsule(x, y, CX, fy, Diagonal, Arm, Thick),
                                        Capsule(x, y, CX, fy, -Diagonal, Arm, Thick));
                c = Over(c, Color.white, cross);
                pixels[y * W + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        if (pressed) closeSpriteDown = sprite;
        else closeSpriteUp = sprite;
        return sprite;
    }

    /// <summary>How much of pixel (x, y) a disc covers, 0-1.</summary>
    static float Disc(float x, float y, float cx, float cy, float r)
    {
        float dx = x - cx, dy = y - cy;
        return Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
    }

    /// <summary>How much of pixel (x, y) a rounded bar covers, 0-1.</summary>
    static float Capsule(float x, float y, float cx, float cy, float angle,
                         float half, float thick)
    {
        float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
        float dx = x - cx, dy = y - cy;
        float u = Mathf.Clamp(dx * ca + dy * sa, -half, half);
        float ex = dx - u * ca, ey = dy - u * sa;
        return Mathf.Clamp01(thick - Mathf.Sqrt(ex * ex + ey * ey) + 0.5f);
    }

    /// <summary>Paints colour src at coverage a over dst.</summary>
    static Color Over(Color dst, Color src, float a)
    {
        if (a <= 0f) return dst;
        float oa = a + dst.a * (1f - a);
        if (oa <= 0f) return new Color(0f, 0f, 0f, 0f);
        float k = dst.a * (1f - a);
        return new Color((src.r * a + dst.r * k) / oa,
                         (src.g * a + dst.g * k) / oa,
                         (src.b * a + dst.b * k) / oa, oa);
    }

    /// <summary>
    /// The round red X that closes a screen - it takes the place of the old
    /// BACK buttons. Presses down like the other buttons. Returns its
    /// RectTransform so a screen can anchor it where it wants.
    /// </summary>
    RectTransform MakeCloseButton(Transform parent, Vector2 offset,
                                  UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("CloseBtn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetCloseSprite(false);
        img.preserveAspect = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = new Vector2(CloseSize, CloseSize * 178f / 160f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // the press: the face sinks onto its side (nothing on it to move -
        // the X is part of the picture)
        var depth = go.AddComponent<ButtonDepth>();
        depth.image = img;
        depth.upSprite = GetCloseSprite(false);
        depth.downSprite = GetCloseSprite(true);
        depth.lift = 0f;
        depth.travel = 0f;
        return rt;
    }

    /// <summary>Width of the round close buttons, in canvas units.</summary>
    const float CloseSize = 136f;

    static Sprite badgeSprite;

    /// <summary>
    /// A notification dot: a plain, flat red circle with a smooth edge.
    /// Drawn in code.
    /// </summary>
    static Sprite GetBadgeSprite()
    {
        if (badgeSprite != null) return badgeSprite;

        const int S = 64;
        const float CX = 31.5f, CY = 31.5f, R = 31f;   // fills the texture
        var red = new Color(0.90f, 0.11f, 0.13f);
        // with mipmaps, so it stays a clean round dot however small the
        // screen shows it (without them a scaled-down game view makes the
        // edge jagged)
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        var pixels = new Color[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // only the edge is blended, so it is smooth rather than jagged
                pixels[y * S + x] = new Color(red.r, red.g, red.b, Disc(x, y, CX, CY, R));
            }
        }
        tex.SetPixels(pixels);
        tex.Apply(true);
        tex.filterMode = FilterMode.Trilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        badgeSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return badgeSprite;
    }

    /// <summary>
    /// Puts a red dot in the top-right corner of a button. It is a child of
    /// the button, so it rides along with it and sinks with the face when the
    /// button is pressed.
    /// </summary>
    GameObject MakeBadge(Transform button)
    {
        var go = new GameObject("Badge");
        go.transform.SetParent(button, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetBadgeSprite();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        // centred on the button's rounded corner itself: 5.3 in from the top
        // and right edges is where the corner's curve crosses the diagonal
        // (ButtonDepth lifts everything on the face by 5, hence the -10.3)
        rt.anchoredPosition = new Vector2(-5.3f, -10.3f);
        rt.sizeDelta = new Vector2(28f, 28f);
        return go;
    }

    /// <summary>Room kept under a scrolling window for its footer.</summary>
    const float ScrollFooterHeight = 250f;

    /// <summary>Space between a footer and the bottom of the screen.</summary>
    const float ScrollFooterGap = 30f;

    /// <summary>
    /// The fixed bottom of a scrolling window. It is a strip in the window's
    /// own colour, square on top so the scrolling card runs straight into it
    /// and rounded underneath, with the close button in the middle. The
    /// content scrolls away above it, so nothing slides under the button.
    /// </summary>
    void MakeScrollFooter(GameObject panel, Color color,
                          UnityEngine.Events.UnityAction onClose)
    {
        float height = ScrollFooterHeight - ScrollFooterGap;

        var footGo = new GameObject("Footer");
        footGo.transform.SetParent(panel.transform, false);
        var footRt = footGo.AddComponent<RectTransform>();
        footRt.anchorMin = footRt.anchorMax = footRt.pivot = new Vector2(0.5f, 0f);
        footRt.anchoredPosition = new Vector2(0f, ScrollFooterGap + PanelBleed.y);
        footRt.sizeDelta = new Vector2(1010f, height);
        // cuts the rounded top corners off the card inside, so only its
        // rounded bottom shows
        footGo.AddComponent<RectMask2D>();

        var card = MakeCard(footGo.transform, Vector2.zero,
            new Vector2(1010f, height + 100f), color);
        var cardRt = card.rectTransform;
        cardRt.anchorMin = cardRt.anchorMax = cardRt.pivot = new Vector2(0.5f, 0f);
        cardRt.anchoredPosition = Vector2.zero;

        // a faint line where the scrolling part ends
        var lineGo = new GameObject("Divider");
        lineGo.transform.SetParent(footGo.transform, false);
        var line = lineGo.AddComponent<Image>();
        line.color = new Color(1f, 1f, 1f, 0.10f);
        line.raycastTarget = false;
        var lineRt = line.rectTransform;
        lineRt.anchorMin = lineRt.anchorMax = lineRt.pivot = new Vector2(0.5f, 1f);
        lineRt.anchoredPosition = Vector2.zero;
        lineRt.sizeDelta = new Vector2(930f, 3f);

        // the button sits on the panel, not inside the mask, centred in the strip
        var close = MakeCloseButton(panel.transform, Vector2.zero, onClose);
        close.anchorMin = close.anchorMax = new Vector2(0.5f, 0f);
        close.pivot = new Vector2(0.5f, 0.5f);
        close.anchoredPosition = new Vector2(0f, ScrollFooterGap + PanelBleed.y + height * 0.5f);
    }

    Image MakeCard(Transform parent, Vector2 offset, Vector2 size, Color color)
    {
        var go = new GameObject("Card");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
        return img;
    }

    /// <summary>The green of the go buttons: PLAY, and BUY / SELECT in the garage.</summary>
    static readonly Color GoGreen = new Color(0.22f, 0.70f, 0.30f);

    /// <summary>A button that cannot do anything right now.</summary>
    static readonly Color LockedGrey = new Color(0.36f, 0.34f, 0.44f);

    // returns the label so callers can update its text later
    /// <param name="plainFont">
    /// Use the plain font instead of the display one. The racing font is
    /// heavily slanted and loses legibility at small sizes, so anything with
    /// numbers packed into a small button reads far better in plain text.
    /// </param>
    Text MakeButton(Transform parent, string label, int fontSize,
        Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction onClick,
        Color? tint = null, bool plainFont = false)
    {
        var go = new GameObject(label + "Btn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = tint ?? new Color(0.96f, 0.47f, 0.13f); // warm arcade orange
        AddButtonDepth(img);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        // Button labels are set a size up from the rest of the text - players
        // found them small and hard to read - and shrink only as far as it
        // takes to stay inside the button, in every language.
        var text = MakeText(go.transform, "Label", Mathf.RoundToInt(fontSize * ButtonTextScale),
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size, plainFont);
        if (text is LocText fit) fit.fitInEnglish = true;
        text.text = label;
        return text;
    }

    /// <summary>How much larger a button's label is than the size it asks for.</summary>
    const float ButtonTextScale = 1.25f;

    /// <summary>Size of the price line on the shop's tire and coin packs.</summary>
    const int PackPriceSize = 44;

    const string SettingsIconPath = "UI/icon_settings";

    /// <summary>
    /// Puts a currency icon just after a centred label's text (the label's
    /// own width, so it fits every language and every amount).
    /// </summary>
    static void IconAfterText(RawImage icon, Text label)
    {
        var rt = icon.rectTransform;
        const float Gap = 14f;
        float w = rt.sizeDelta.x;
        // the text and its icon are centred on the button together
        var lRt = label.rectTransform;
        lRt.anchoredPosition = new Vector2(-(Gap + w) * 0.5f, lRt.anchoredPosition.y);
        float half = label.preferredWidth * 0.5f;
        rt.anchoredPosition = new Vector2(lRt.anchoredPosition.x + half + Gap + w * 0.5f, 0f);
    }

    /// <summary>Puts a currency icon just before a centred label's text.</summary>
    static void IconBeforeText(RawImage icon, Text label)
    {
        var rt = icon.rectTransform;
        Vector2 at = label.rectTransform.anchoredPosition;
        float half = label.preferredWidth * 0.5f;
        rt.anchoredPosition = new Vector2(at.x - half - 16f - rt.sizeDelta.x * 0.5f, at.y);
    }

    /// <summary>
    /// The in-run pause button: two white bars on a dark raised square, lined
    /// up under the tire counter. It reaches a little past its square on
    /// every side, so it is easy to hit without having to look.
    /// </summary>
    GameObject MakePauseButton(Transform parent)
    {
        var go = new GameObject("PauseBtn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.20f, 0.18f, 0.28f, 0.94f);
        AddButtonDepth(img);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-CounterEdge, -94f);
        rt.sizeDelta = new Vector2(96f, 96f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(PauseGame);

        // an invisible margin around it that still counts as the button
        var hitGo = new GameObject("HitArea");
        hitGo.transform.SetParent(go.transform, false);
        var hit = hitGo.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var hitRt = hit.rectTransform;
        hitRt.anchorMin = hitRt.anchorMax = hitRt.pivot = new Vector2(0.5f, 0.5f);
        hitRt.sizeDelta = new Vector2(136f, 128f);

        // the two bars
        for (int i = 0; i < 2; i++)
        {
            var barGo = new GameObject(i == 0 ? "BarLeft" : "BarRight");
            barGo.transform.SetParent(go.transform, false);
            var bar = barGo.AddComponent<Image>();
            bar.color = Color.white;
            bar.raycastTarget = false;
            var bRt = bar.rectTransform;
            bRt.anchorMin = bRt.anchorMax = bRt.pivot = new Vector2(0.5f, 0.5f);
            bRt.anchoredPosition = new Vector2(i == 0 ? -15f : 15f, 0f);
            bRt.sizeDelta = new Vector2(16f, 46f);
        }
        return go;
    }

    /// <summary>
    /// A square button showing a white icon on a dark rounded tile. If the
    /// icon cannot be found it falls back to a text label, so the button is
    /// never blank.
    /// </summary>
    Button MakeIconButton(Transform parent, string iconPath, string fallbackLabel,
        Vector2 anchor, Vector2 offset, Vector2 size,
        UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(fallbackLabel + "Btn");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        // slate rather than near-black, so the raised edge still shows
        img.color = new Color(0.26f, 0.24f, 0.34f, 0.94f);
        AddButtonDepth(img);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        Texture2D icon = Resources.Load<Texture2D>(iconPath);
        if (icon != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            var raw = iconGo.AddComponent<RawImage>();
            raw.texture = icon;
            raw.raycastTarget = false;
            var iRt = raw.rectTransform;
            iRt.anchorMin = iRt.anchorMax = iRt.pivot = new Vector2(0.5f, 0.5f);
            iRt.anchoredPosition = Vector2.zero;
            iRt.sizeDelta = size * 0.66f;
        }
        else
        {
            var label = MakeText(go.transform, "Label", 22, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, size, true);
            label.text = fallbackLabel;
        }
        return btn;
    }

    static Texture2D checkerTexture;

    /// <summary>A black-and-white chequer tile, drawn in code (2x2 squares).</summary>
    static Texture2D GetCheckerTexture()
    {
        if (checkerTexture != null) return checkerTexture;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        Color dark = new Color(0.07f, 0.07f, 0.08f, 1f);
        Color light = new Color(0.96f, 0.96f, 0.96f, 1f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, y, ((x < S / 2) == (y < S / 2)) ? dark : light);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        checkerTexture = tex;
        return tex;
    }

    /// <summary>
    /// The big RACE button: a black frame around a chequered flag, with the
    /// word in white over a dark wash so it stays readable against the black
    /// and white squares.
    /// </summary>
    Text MakeRaceButton(Transform parent, Vector2 offset, Vector2 size)
    {
        // black frame - this is what the button tints when pressed. Not quite
        // pure black, so its raised edge still shows against the dark flag.
        var go = new GameObject("RACESBtn");
        go.transform.SetParent(parent, false);
        var frame = go.AddComponent<Image>();
        frame.color = new Color(0.10f, 0.10f, 0.11f);
        AddButtonDepth(frame);
        var rt = frame.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = frame;
        btn.onClick.AddListener(OpenRaces);

        // the flag, clipped to a rounded face just inside the frame
        var faceGo = new GameObject("Flag");
        faceGo.transform.SetParent(go.transform, false);
        var face = faceGo.AddComponent<Image>();
        face.sprite = GetRoundedSprite();
        face.type = Image.Type.Sliced;
        face.color = Color.black;
        face.raycastTarget = false;
        var fRt = face.rectTransform;
        fRt.anchorMin = Vector2.zero;
        fRt.anchorMax = Vector2.one;
        // inset 8 all round inside the raised face. (Everything on a button
        // is lifted by half the lip once it is built - see ButtonDepth - so
        // this is set that much lower here to end up level with the face.)
        fRt.offsetMin = new Vector2(8f, 8f + ButtonLip * 0.5f);
        fRt.offsetMax = new Vector2(-8f, -8f - ButtonLip * 0.5f);
        faceGo.AddComponent<Mask>().showMaskGraphic = true;

        // chequers, tipped a few degrees so it reads as a flag in the wind
        const float Square = 30f;
        var checkGo = new GameObject("Chequers");
        checkGo.transform.SetParent(faceGo.transform, false);
        var check = checkGo.AddComponent<RawImage>();
        check.texture = GetCheckerTexture();
        check.raycastTarget = false;
        Vector2 cover = new Vector2(size.x + 160f, size.y + 260f);
        check.uvRect = new Rect(0f, 0f, cover.x / (Square * 2f), cover.y / (Square * 2f));
        var cRt = check.rectTransform;
        cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0.5f, 0.5f);
        cRt.anchoredPosition = Vector2.zero;
        cRt.sizeDelta = cover;
        cRt.localRotation = Quaternion.Euler(0f, 0f, -7f);

        // the dark wash over the whole flag - the chequers show through it
        // evenly, and the white word (with its black outline) stands out
        // anywhere on it
        var bandGo = new GameObject("Shade");
        bandGo.transform.SetParent(faceGo.transform, false);
        var band = bandGo.AddComponent<Image>();
        band.color = new Color(0f, 0f, 0f, 0.80f);
        band.raycastTarget = false;
        var bRt = band.rectTransform;
        bRt.anchorMin = Vector2.zero;
        bRt.anchorMax = Vector2.one;
        bRt.offsetMin = bRt.offsetMax = Vector2.zero;

        var label = MakeText(faceGo.transform, "Label", 64, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, size);
        label.text = "RACE";
        label.color = Color.white;
        return label;
    }

    /// <summary>
    /// Hidden unlock prompt: 15 taps on the title opens it, the right password
    /// grants every car and race.
    /// </summary>
    void BuildUnlockPanel(Transform uiRoot)
    {
        unlockPanel = MakePanel(uiRoot, "UnlockPanel");

        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(unlockPanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.7f);
        var dimRt = dim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(unlockPanel.transform, false);
        var bodyRt = bodyGo.AddComponent<RectTransform>();
        bodyRt.anchorMin = Vector2.zero;
        bodyRt.anchorMax = Vector2.one;
        bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;
        Transform body = bodyGo.transform;
        var pop = bodyGo.AddComponent<PopIn>();
        pop.dimmer = dim;
        pop.dimmerAlpha = 0.7f;

        MakeCard(body, new Vector2(0f, 0f), new Vector2(940f, 620f),
            new Color(0.13f, 0.09f, 0.2f, 0.96f));

        var uTitle = MakeText(body, "UnlockTitle", 64, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(880f, 100f));
        uTitle.text = "ENTER PASSWORD";
        uTitle.color = new Color(1f, 0.72f, 0.12f);

        // --- the field itself (built by hand so it can use the plain font)
        var fieldGo = new GameObject("Field");
        fieldGo.transform.SetParent(body, false);
        var fieldBg = fieldGo.AddComponent<Image>();
        fieldBg.sprite = GetRoundedSprite();
        fieldBg.type = Image.Type.Sliced;
        fieldBg.color = new Color(0.05f, 0.04f, 0.09f, 0.95f);
        var fieldRt = fieldBg.rectTransform;
        fieldRt.anchorMin = fieldRt.anchorMax = fieldRt.pivot = new Vector2(0.5f, 0.5f);
        fieldRt.anchoredPosition = new Vector2(0f, 70f);
        fieldRt.sizeDelta = new Vector2(760f, 120f);

        Font plain = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var placeGo = new GameObject("Placeholder");
        placeGo.transform.SetParent(fieldGo.transform, false);
        var place = placeGo.AddComponent<Text>();
        place.font = plain;
        place.fontSize = 34;
        place.alignment = TextAnchor.MiddleCenter;
        place.color = new Color(1f, 1f, 1f, 0.35f);
        place.text = "password";
        var placeRt = place.rectTransform;
        placeRt.anchorMin = Vector2.zero;
        placeRt.anchorMax = Vector2.one;
        placeRt.offsetMin = new Vector2(24f, 8f);
        placeRt.offsetMax = new Vector2(-24f, -8f);

        var inTextGo = new GameObject("Text");
        inTextGo.transform.SetParent(fieldGo.transform, false);
        var inText = inTextGo.AddComponent<Text>();
        inText.font = plain;
        inText.fontSize = 34;
        inText.alignment = TextAnchor.MiddleCenter;
        inText.color = Color.white;
        inText.supportRichText = false;
        var inTextRt = inText.rectTransform;
        inTextRt.anchorMin = Vector2.zero;
        inTextRt.anchorMax = Vector2.one;
        inTextRt.offsetMin = new Vector2(24f, 8f);
        inTextRt.offsetMax = new Vector2(-24f, -8f);

        unlockInput = fieldGo.AddComponent<InputField>();
        unlockInput.textComponent = inText;
        unlockInput.placeholder = place;
        unlockInput.targetGraphic = fieldBg;
        unlockInput.lineType = InputField.LineType.SingleLine;
        unlockInput.characterLimit = 24;
        // the on-screen keyboard's Done key submits, but an empty field just
        // closes the keyboard instead of reporting a wrong password
        unlockInput.onEndEdit.AddListener(delegate (string value)
        {
            if (!string.IsNullOrEmpty(value)) SubmitUnlock();
        });

        unlockMsg = MakeText(body, "UnlockMsg", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(880f, 70f));
        unlockMsg.text = "";

        MakeButton(body, "UNLOCK", 46,
            new Vector2(-190f, -140f), new Vector2(340f, 110f), SubmitUnlock);
        MakeButton(body, "CANCEL", 46,
            new Vector2(190f, -140f), new Vector2(340f, 110f), CloseUnlockPrompt,
            new Color(0.35f, 0.32f, 0.45f));

        unlockPanel.SetActive(false);
    }

    Text MakeText(Transform parent, string name, int size, TextAnchor anchor,
        Vector2 anchorPoint, Vector2 offset, Vector2 sizeDelta, bool plainFont = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        // LocText: translates whatever is written to it, and lays out
        // Arabic and Hindi properly (see Loc)
        var text = go.AddComponent<LocText>();
        // languages the game font cannot write bring their own font
        Font langFont = plainFont ? Loc.PlainFontFor(Loc.Current) : Loc.DisplayFontFor(Loc.Current);
        text.font = langFont != null ? langFont
                  : plainFont ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                  : uiFont;
        // The plain font is used where legibility matters most - small,
        // number-heavy labels - so it is set a little larger and bold to hold
        // its own against the display font around it.
        text.fontSize = Mathf.RoundToInt(size * FontScale * (plainFont ? 1.2f : 1f)
                                         * Loc.SizeScale(Loc.Current));
        // custom racing fonts are usually already slanted; the fallback font
        // gets bold+italic to fake the same look. The language fonts are
        // already bold, so they are left as drawn.
        text.fontStyle = langFont != null ? FontStyle.Normal
                       : plainFont ? FontStyle.Bold
                       : usingCustomFont ? FontStyle.Bold : FontStyle.BoldAndItalic;
        text.alignment = anchor;
        text.color = Color.white;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        // Unity's Outline is really four offset copies of the text stacked
        // behind it, so the distance has to stay small - push it out and the
        // copies separate visibly, and it stops looking like an outline and
        // starts looking like a second label behind the first. Opaque and
        // tight reads far heavier than wide and translucent.
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        var rt = text.rectTransform;
        rt.anchorMin = anchorPoint;
        rt.anchorMax = anchorPoint;
        rt.pivot = anchorPoint;
        rt.anchoredPosition = offset;
        rt.sizeDelta = sizeDelta;

        return text;
    }
}
