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
public class GameManager : MonoBehaviour
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
    static readonly string[] HeightFixNames = { "VORTEX HATCH" };
    static readonly float[] HeightFixValues = { -0.32f };

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
    // 35 mph, in m/s. The HUD can read in either unit, but the tuning is
    // quoted in mph because that is how it was asked for.
    const float RunBaseSpeed = 15.65f;
    /// <summary>Top speed of an endless run before car bonuses.</summary>
    const float RunMaxSpeed = 42.92f;   // 96 mph
    /// <summary>m/s gained per second: 8 m/s over about six minutes.</summary>
    const float RunSpeedGain = 0.0649f;  // 35 -> 96 mph over seven minutes


    /// <summary>Score per metre travelled, before multipliers.</summary>
    const float PointsPerMetre = 0.09f;

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
    float gameOverAt;
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
    Text menuBestText, menuCoinsText, coinHudText;
    Text shopCarName, shopStats, shopPrice, shopActionLabel, shopCoinsText;
    float volumeSetting, sensSetting;
    float volMusic, volEngine, volDrift, volCoins, volSfx = 1f;
    bool useMph = true;
    float baseSteerAccel;
    int invertSteer;
    Text invertBtnLabel, unitsBtnLabel;

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
    static readonly int[] LoginRewards = { 100, 150, 200, 250, 300, 400, 500 };
    bool loginPending;
    bool loginUnlocksCar;
    int loginStreakNew;
    int loginRewardCoins;
    GameObject loginPanel;
    Text loginDayText, loginRewardText, loginCarText;
    readonly Image[] loginCells = new Image[7];
    readonly Text[] loginCellTexts = new Text[7];
    readonly RawImage[] loginCellIcons = new RawImage[7];
    Text claimBtnLabel;
    bool loginAlreadyClaimed;

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
    const string SaveResetToken = "reset-2026-08-21-cars";

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

    void ComputeDailyLogin()
    {
        string today = System.DateTime.Now.ToString("yyyyMMdd");
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
        int day = (loginStreakNew - 1) % 7 + 1;
        loginRewardCoins = LoginRewards[day - 1];
        loginUnlocksCar = day == 7 && !OwnedCar(RewardCarIndex);
        loginPending = true;
    }

    void ClaimLogin()
    {
        if (!loginAlreadyClaimed)
        {
            totalCoins += loginRewardCoins;
            PlayerPrefs.SetInt("Coins", totalCoins);
            PlayerPrefs.SetString("LastLogin", System.DateTime.Now.ToString("yyyyMMdd"));
            PlayerPrefs.SetInt("LoginStreak", loginStreakNew);
            if (loginUnlocksCar) PlayerPrefs.SetInt("CarOwned" + RewardCarIndex, 1);
            PlayerPrefs.Save();
            audioMan.PlayCoin();
        }
        else
        {
            audioMan.PlayTap();
        }

        loginPending = false;
        loginPanel.SetActive(false);
        if (showcaseRoot != null) showcaseRoot.SetActive(false);
        menuCoinsText.text = "COINS  " + totalCoins;
    }

    void Start()
    {
        car = FindFirstObjectByType<CarController>();
        if (car == null)
        {
            Debug.LogError("GameManager: no CarController found in the scene. " +
                           "Add the CarController component to your PlayerCar object.");
            enabled = false;
            return;
        }

        track = FindFirstObjectByType<TrackGenerator>();
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

        // the old hand-made menu is replaced by the runtime UI
        GameObject oldMenu = GameObject.Find("MainMenuPanel");
        if (oldMenu != null) oldMenu.SetActive(false);

        audioMan = FindFirstObjectByType<AudioManager>();
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

        // Speed and scoring are forced from code (see the constants above);
        // everything about how the car handles is left to the CarController.
        Debug.Log("[TUNING] speed " + runBaseSpeed + "->" + runMaxSpeed +
                  " gain " + runSpeedGain + " | points/m " + pointsPerMeter +
                  " | drift thr " + car.driftThreshold +
                  " angle " + car.driftAngle);
        baseSteerAccel = car.steerAcceleration;
        ApplySettings();
        EquipSelected();
        AssignCurrencyIcons();
        EnterMenu();
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
    string bestPendingCause;
    int bestPendingScore, bestPendingCoins, bestPendingBonus;

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
    /// new number down in its wake. The results screen waits behind it.
    /// </summary>
    void ShowBestRun(string cause, int finalScore, int collected, int bonus)
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

        bestPendingCause = cause;
        bestPendingScore = finalScore;
        bestPendingCoins = collected;
        bestPendingBonus = bonus;

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
        ShowGameOver(bestPendingCause, bestPendingScore, true,
                     bestPendingCoins, bestPendingBonus);
    }

    void LateUpdate()
    {
        TickBestRun();
        TickGameOver();
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
        car.baseSpeed = runBaseSpeed;
        car.speedGainPerSecond = runSpeedGain;
        car.maxSpeed = baseMaxSpeed + d.speedBonus;
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
        menuPanel.SetActive(true);
        settingsPanel.SetActive(false);
        garagePanel.SetActive(false);
        gameOverPanel.SetActive(false);
        pausePanel.SetActive(false);
        questsPanel.SetActive(false);
        revivePanel.SetActive(false);
        shopPanel.SetActive(false);
        creditsPanel.SetActive(false);
        if (unlockPanel != null) unlockPanel.SetActive(false);
        if (wheelPanel != null) wheelPanel.SetActive(false);
        if (racePanel != null) racePanel.SetActive(false);
        RefreshSpinBadge();
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
        menuCoinsText.text = "COINS  " + totalCoins;

        // the daily reward waits until the opening sequence has finished
        if (loginPending && !introRunning)
        {
            BuildLoginShowcase();
            showcaseRoot.SetActive(true);

            int day = (loginStreakNew - 1) % 7 + 1;
            loginDayText.text = "DAY " + day + (loginStreakNew > 7 ? "  (STREAK " + loginStreakNew + ")" : "");
            loginRewardText.fontSize = Mathf.RoundToInt((loginAlreadyClaimed ? 36 : 56) * FontScale);
            loginRewardText.text = loginAlreadyClaimed ? "COME BACK TOMORROW!"
                                                       : "+" + loginRewardCoins + " COINS";
            loginCarText.gameObject.SetActive(loginUnlocksCar);
            if (loginUnlocksCar) loginCarText.text = "NEW CAR!\n" + Cars[RewardCarIndex].name;
            claimBtnLabel.text = loginAlreadyClaimed ? "OK" : "CLAIM";

            bool rewardOwned = OwnedCar(RewardCarIndex);
            for (int i = 0; i < 7; i++)
            {
                // on a claimed day, today counts as done (green)
                bool current = i + 1 == day && !loginAlreadyClaimed;
                bool past = i + 1 < day || (i + 1 == day && loginAlreadyClaimed);
                loginCells[i].color = current ? new Color(1f, 0.6f, 0.15f)
                                    : past ? new Color(0.3f, 0.62f, 0.35f)
                                    : new Color(0.24f, 0.19f, 0.36f);
                loginCellTexts[i].color = current ? Color.white
                                        : new Color(1f, 1f, 1f, past ? 0.75f : 0.55f);
                loginCellIcons[i].texture = (i == 6 && !rewardOwned)
                    ? (Texture)rewardCarRT : Resources.Load<Texture2D>(CoinIconPath);
            }
            loginCellTexts[6].text = rewardOwned ? "+500" : "CAR";

            loginPanel.SetActive(true);
            if (audioMan != null) audioMan.PlayPop();
        }
        else if (showcaseRoot != null)
        {
            showcaseRoot.SetActive(false);
        }
        centerText.gameObject.SetActive(false);
        bonusText.gameObject.SetActive(false);
        driftText.gameObject.SetActive(false);
        SetHudVisible(false);
        menuBestText.text = "BEST  " + best;
        // back to the lobby theme (the intro owns the music until it hands over)
        if (audioMan != null && !introRunning) audioMan.PlayMenuMusic();
    }

    void SetHudVisible(bool visible)
    {
        bool endless = mode == Mode.Endless;
        scoreText.gameObject.SetActive(visible && endless);
        bestText.gameObject.SetActive(visible && endless);
        speedText.gameObject.SetActive(visible);
        coinHudText.gameObject.SetActive(visible);
        multHudText.gameObject.SetActive(visible);
        pauseButton.SetActive(visible);
        if (raceHudText != null) raceHudText.gameObject.SetActive(visible && mode == Mode.Race);
        if (!visible) HideItemBars();
    }

    // ---------------------------------------------------------------- pause

    void PauseGame()
    {
        if (state != State.Playing) return;
        state = State.Paused;
        Time.timeScale = 0f;
        // compact single-line-per-quest version (strip the big size tags)
        pauseQuestText.text =
            CompactQuestLine(0) + "\n" + CompactQuestLine(1) + "\n" + CompactQuestLine(2);
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
        GoToMenu();
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

    GameObject settingsAudioTab, settingsGeneralTab, settingsCreditsTab;
    Text settingsTabAudio, settingsTabGeneral, settingsTabCredits;

    const string CreditsText =
        "DRIFTLINE ETERNAL\nby MEIND GAMES\n\n" +
        "3D MODELS\n\n" +
        "Midnight Black Horse - Sketchfab\n" +
        "Lightning Bolt - Sketchfab\n" +
        "Keys - Sketchfab\n" +
        "Charging Bull - Sketchfab\n\n" +
        "Nature Pack - Quaternius\n" +
        "Low Poly Car Pack - Designersoup\n\n" +
        "AUDIO\n\n" +
        "Music and sound effects\ncreated for this game\n\n" +
        "All models used under their\nrespective licenses.";

    void SetSettingsTab(int tab)
    {
        settingsAudioTab.SetActive(tab == 0);
        settingsGeneralTab.SetActive(tab == 1);
        settingsCreditsTab.SetActive(tab == 2);

        var on = new Color(1f, 0.78f, 0.2f);
        var off = new Color(1f, 1f, 1f, 0.55f);
        settingsTabAudio.color = tab == 0 ? on : off;
        settingsTabGeneral.color = tab == 1 ? on : off;
        settingsTabCredits.color = tab == 2 ? on : off;
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
        return "STEERING: " + (invertSteer == 1 ? "INVERTED" : "NORMAL");
    }

    void SetVolume(float v) { volumeSetting = v; PlayerPrefs.SetFloat("Volume", v); ApplySettings(); }
    void SetMusicVol(float v) { volMusic = v; PlayerPrefs.SetFloat("VolMusic", v); ApplySettings(); }
    void SetEngineVol(float v) { volEngine = v; PlayerPrefs.SetFloat("VolEngine", v); ApplySettings(); }
    void SetDriftVol(float v) { volDrift = v; PlayerPrefs.SetFloat("VolDrift", v); ApplySettings(); }
    void SetCoinVol(float v) { volCoins = v; PlayerPrefs.SetFloat("VolCoins", v); ApplySettings(); }
    void SetSensitivity(float v) { sensSetting = v; PlayerPrefs.SetFloat("Sensitivity", v); ApplySettings(); }
    void SetSfxVol(float v) { volSfx = v; PlayerPrefs.SetFloat("VolSfx", v); ApplySettings(); }

    /// <summary>Speed in whichever unit the player picked, with its label.</summary>
    string SpeedLabel(float metresPerSecond)
    {
        return useMph
            ? Mathf.RoundToInt(metresPerSecond * 2.23694f) + " MPH"
            : Mathf.RoundToInt(metresPerSecond * 3.6f) + " KM/H";
    }

    void ToggleUnits()
    {
        useMph = !useMph;
        PlayerPrefs.SetInt("UseMph", useMph ? 1 : 0);
        unitsBtnLabel.text = UnitsLabel();
        audioMan.PlayTap();
    }

    string UnitsLabel() { return "SPEED: " + (useMph ? "MPH" : "KM/H"); }

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
    Text storeCoinsText, storeTiresText, boxResultText;
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
    string pendingBoxText;
    Color pendingBoxColor;
    Currency pendingBoxCurrency = Currency.Coins;
    float boxRevealAt = -1f;
    bool pendingBoxIsToken;
    Material rubberMat;
    Text persistentCoinsText, persistentTiresText;
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
    Text questMultText, questTiresText, pauseQuestText, itemHudText, multHudText;
    Text reviveTireLabel, reviveAdLabel, reviveTimerText, reviveHaveText;

    [Tooltip("Seconds the player has to decide whether to revive.")]
    public float reviveDecisionTime = 5f;
    float reviveTimer;

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
        string line = string.Format(QuestNames[questType[i]], questTarget[i])
               + "\n<size=24>" + Mathf.Min(questProgress[i], questTarget[i]) + " / " + questTarget[i]
               + (isDone ? "  DONE!" : "") + "</size>";
        return isDone ? "<color=#66E28C>" + line + "</color>" : line;
    }

    string CompactQuestLine(int i)
    {
        return QuestLine(i)
            .Replace("<size=24>", "")
            .Replace("</size>", "")
            .Replace("\n", "   ");
    }

    void UpdateQuest(int type, int amount, bool bestOf)
    {
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
        if (allDone)
        {
            questLevel = Mathf.Min(questLevel + 1, 29);
            tires += 6;
            FlashBonus("QUESTS DONE! X" + Mathf.Min(30, 1 + questLevel) + "  +6 TIRES",
                new Color(0.6f, 0.9f, 1f));
            audioMan.PlayCoin();
            GenerateQuests();
        }
        SaveQuests();
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
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
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
    readonly Text[] raceLevelLabels = new Text[RaceMode.TotalLevels];
    readonly Text[] upgradeLabels = new Text[3];

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
            upgradeLabels[(int)u].text = "NOT ENOUGH\nCOINS";
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
                     : u == RaceMode.Upgrade.Speed ? "+" + Mathf.RoundToInt(RaceMode.SpeedBonus() * (useMph ? 2.23694f : 3.6f)) + (useMph ? " MPH" : " KM/H")
                     : "+" + Mathf.RoundToInt(RaceMode.Strength() * 100f) + "%";
        string cost = RaceMode.IsMaxed(u) ? "MAX" : RaceMode.CostOf(u).ToString();
        // the picture carries the meaning, so the text just states the numbers
        return "<size=17>LV " + lv + "</size>\n<size=16>" + value
               + "</size>\n<size=16>" + cost + "</size>";
    }

    void OpenRaces()
    {
        menuPanel.SetActive(false);
        racePanel.SetActive(true);
        RefreshRacePanel();
        audioMan.PlayTap();
    }

    void CloseRaces()
    {
        racePanel.SetActive(false);
        menuPanel.SetActive(true);
        audioMan.PlayTap();
    }

    void RefreshRacePanel()
    {
        for (int u = 0; u < 3; u++)
        {
            upgradeLabels[u].text = UpgradeLine((RaceMode.Upgrade)u);
        }
        for (int i = 0; i < RaceMode.TotalLevels; i++)
        {
            bool unlocked = RaceMode.IsUnlocked(i);
            bool done = RaceMode.IsCompleted(i);
            int stage = RaceMode.StageOf(i) + 1;
            raceLevelLabels[i].text = unlocked ? (done ? stage + "\n<size=16>WON</size>" : stage.ToString()) : "X";
            raceLevelLabels[i].color = !unlocked ? new Color(1f, 1f, 1f, 0.35f)
                                     : done ? new Color(0.45f, 1f, 0.55f) : Color.white;
        }
    }

    void StartRace(int level)
    {
        if (!RaceMode.IsUnlocked(level)) { audioMan.PlayTap(); return; }

        EnsureRace();
        mode = Mode.Race;
        raceLevel = level;
        raceFinished = false;

        racePanel.SetActive(false);

        // clean racing surface: no obstacles or traffic, boost pads instead
        track.spawnObstacles = false;
        track.spawnTraffic = false;
        track.spawnBoostPads = true;
        track.roadWidth = raceRoadWidth;     // three proper lanes to fight over
        track.roadBehindStart = 8f;          // no empty road visible behind the grid
        track.flatTrack = true;              // a level circuit, whatever the biome

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

        // a race is a straight fight: no coins, power-ups, tires or obstacles
        // scattered around, just the track and the field
        track.spawnCoins = false;
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
        ApplyUpgrades();
        race.stockSpeedReference = raceBaseSpeed;
        race.BuildField(level, car.DistanceTraveled, raceBaseSpeed, MakeRacerVisual);
        race.PlaceAll();         // rivals sit on the grid while it counts down
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

        CarDef d = Cars[carIdx];
        // a few models in the pack are authored nose-backwards
        float extraFlip = System.Array.IndexOf(BackwardsModels, d.name) >= 0 ? 180f : 0f;
        GameObject prefab = d.path != null ? Resources.Load<GameObject>(d.path) : null;
        if (prefab == null) return null;

        var root = new GameObject("RacerCar");
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
        float k = 4.2f / length;
        m.transform.localScale = m.transform.localScale * k;

        // measure twice: renderer bounds can lag a scale change by a frame
        for (int pass = 0; pass < 2; pass++)
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            Vector3 offset = root.transform.position - b.center;
            m.transform.localPosition += new Vector3(
                offset.x, root.transform.position.y - b.min.y, offset.z);
        }
        return root;
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
    Text wheelSpinsText, wheelResultText, spinMenuLabel, wheelSpinLabel, wheelAdLabel;
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
        wheelSpinsText.text = left > 0 ? left + "  SPINS LEFT" : "BUY A SPIN BELOW";
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
        RefreshSpinBadge();
    }

    /// <summary>Marks the menu button while there are spins waiting.</summary>
    void RefreshSpinBadge()
    {
        if (spinMenuLabel == null) return;
        int left = SpinsLeft;
        spinMenuLabel.text = left > 0 ? "SPIN  " + left : "SPIN";

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

    /// <summary>
    /// Nudges the spin button about while a spin is unclaimed - a small,
    /// constant motion in the corner of the eye is what actually gets it
    /// noticed among six identical buttons.
    /// </summary>
    void AnimateSpinButton()
    {
        if (spinButtonRt == null || SpinsLeft <= 0) return;

        float t = Time.unscaledTime;
        // a slow breath with a quick double-bounce every few seconds
        float beat = Mathf.Repeat(t, 2.6f);
        float pop = beat < 0.5f ? Mathf.Sin(beat / 0.5f * Mathf.PI * 2f) : 0f;

        spinButtonRt.anchoredPosition = spinButtonHome
            + new Vector2(0f, Mathf.Sin(t * 2.2f) * 4f + pop * 9f);
        spinButtonRt.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.2f) * 0.015f
                                                    + Mathf.Abs(pop) * 0.05f);
        spinButtonRt.localRotation = Quaternion.Euler(0f, 0f, pop * 2.5f);
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

        // TODO show a real rewarded ad here and only grant on completion
        PlayerPrefs.SetString("SpinAdDay", System.DateTime.Now.ToString("yyyyMMdd"));
        SpinsLeft = SpinsLeft + 1;

        wheelResultText.text = "+1 SPIN";
        wheelResultText.color = new Color(0.55f, 1f, 0.65f);
        RefreshWheel();
        audioMan.PlayCoin();
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
    /// Chooses the slice. Mostly a straight weighted draw, but every few spins
    /// it is steered onto a neighbour of the jackpot so the wheel visibly stops
    /// a hair away from the car.
    /// </summary>
    /// <param name="frac">
    /// Where inside the slice to stop, 0 to 1. Never dead centre, and on a
    /// teased spin it stops hard against the jackpot's edge.
    /// </param>
    int PickWheelSlot(out float frac)
    {
        int spun = PlayerPrefs.GetInt("SpinsSinceTease", 0) + 1;
        int teaseAt = PlayerPrefs.GetInt("SpinTeaseAt", 0);
        if (teaseAt <= 0) teaseAt = Random.Range(4, 7);

        int carSlot = -1;
        for (int i = 0; i < Wheel.Length; i++)
        {
            if (Wheel[i].kind == SpinPrize.Car) { carSlot = i; break; }
        }

        if (spun >= teaseAt && carSlot >= 0)
        {
            PlayerPrefs.SetInt("SpinsSinceTease", 0);
            PlayerPrefs.SetInt("SpinTeaseAt", Random.Range(4, 7));
            PlayerPrefs.Save();

            // land on a neighbour, right up against the shared edge, so the
            // pointer sits a hair outside the jackpot
            int step = Random.value < 0.5f ? -1 : 1;
            frac = step == -1 ? Random.Range(0.90f, 0.975f)
                              : Random.Range(0.025f, 0.10f);
            return (carSlot + step + Wheel.Length) % Wheel.Length;
        }

        PlayerPrefs.SetInt("SpinsSinceTease", spun);
        PlayerPrefs.SetInt("SpinTeaseAt", teaseAt);
        PlayerPrefs.Save();

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
                msg = "+" + s.amount + " COINS";
                break;

            case SpinPrize.Tires:
                tires += s.amount;
                PlayerPrefs.SetInt("Tires", tires);
                msg = "+" + s.amount + " TIRES";
                break;

            case SpinPrize.Tokens:
            {
                Currency cur = s.currency == Currency.Coins
                    ? (Currency)Random.Range(1, 5) : s.currency;
                SetToken(cur, GetToken(cur) + s.amount);
                msg = "+" + s.amount + " " + TokenNames[(int)cur];
                col = TokenColors[(int)cur];
                break;
            }

            default:
            {
                int car = PickJackpotCar();
                if (car < 0)
                {
                    // every coin car already owned - pay out instead
                    totalCoins += 25000;
                    PlayerPrefs.SetInt("Coins", totalCoins);
                    msg = "JACKPOT!  +25000 COINS";
                }
                else
                {
                    PlayerPrefs.SetInt("CarOwned" + car, 1);
                    msg = "JACKPOT!  " + Cars[car].name;
                }
                break;
            }
        }

        PlayerPrefs.Save();
        wheelResultText.text = msg;
        wheelResultText.color = col;
        menuCoinsText.text = "COINS  " + totalCoins;
        // the wheel gets the same full-screen celebration as the boxes
        ShowPrizeReveal(msg, s.kind == SpinPrize.Tokens, WheelIconPath(s));
    }

    /// <summary>A coin-priced car the player does not own yet.</summary>
    int PickJackpotCar()
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

        wheelSpinsText = MakeText(wheelPanel.transform, "WheelSpins", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 610f), new Vector2(900f, 70f));
        wheelSpinsText.color = new Color(0.8f, 0.85f, 1f);

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
        faceImg.texture = MakeWheelTexture(Wheel.Length);
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

            var label = MakeText(discGo.transform, "Slice" + i, 36, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 70f), true);
            label.text = Wheel[i].label;
            label.color = Color.white;
            label.rectTransform.anchoredPosition = at * 152f;
            label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -ang);
        }

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
            new Vector2(0.5f, 0.5f), new Vector2(0f, -280f), new Vector2(960f, 90f));

        wheelSpinLabel = MakeButton(wheelPanel.transform, "SPIN", 54,
            new Vector2(0f, -390f), new Vector2(520f, 124f), SpinWheel,
            new Color(0.55f, 0.30f, 0.85f));

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

        MakeButton(wheelPanel.transform, "BACK", 44,
            new Vector2(0f, -650f), new Vector2(420f, 100f), CloseWheel);

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

        finishBarTop.rectTransform.sizeDelta = new Vector2(400f, h);
        finishBarBottom.rectTransform.sizeDelta = new Vector2(400f, h);
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
        return place == 1 ? "st" : place == 2 ? "nd" : place == 3 ? "rd" : "th";
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
            float h = Mathf.Lerp(BarHeight, 1200f, shut);
            finishBarTop.rectTransform.sizeDelta = new Vector2(400f, h);
            finishBarBottom.rectTransform.sizeDelta = new Vector2(400f, h);
        }
        finishBlackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01(finishAnimT / 0.3f) * 0.96f);

        // the position slides in once the screen is dark
        float p = Mathf.Clamp01((finishAnimT - 0.3f) / 0.45f);
        float eased = 1f - Mathf.Pow(1f - p, 3f);
        finishPlaceRt.anchoredPosition = new Vector2(Mathf.Lerp(900f, 0f, eased), 0f);
        finishPlaceRt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, eased);
        float a = Mathf.Clamp01(p * 2f);
        finishPlaceText.color = new Color(1f, 0.82f, 0.12f, a);
        finishPlaceSuffix.color = new Color(1f, 0.82f, 0.12f, a);

        if (finishAnimT >= 4.2f) EndFinishCinematic();
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
        raceHudText.text = "P" + place + "/" + (RaceMode.Opponents + 1)
                           + "\n<size=24>" + Mathf.RoundToInt(togo) + "M</size>";

        UpdateFinishTension();
        if (car.DistanceTraveled >= raceFinishDistance) FinishRace(place);
    }

    void FinishRace(int place)
    {
        raceFinished = true;
        RaceMode.RecordResult(raceLevel, place);

        int reward = place == 1 ? RaceMode.Reward(raceLevel)
                   : place <= 3 ? RaceMode.Reward(raceLevel) / 3 : 0;
        if (reward > 0)
        {
            totalCoins += Mathf.RoundToInt(reward * RaceMode.CoinMultiplier());
            PlayerPrefs.SetInt("Coins", totalCoins);
            PlayerPrefs.Save();
        }

        state = State.GameOver;
        PlayFinishCinematic(place);
        centerText.text = (place == 1 ? "WINNER!" : "FINISHED P" + place) +
            "\n<size=30>" + (place == 1 ? "NEXT RACE UNLOCKED" : "1ST PLACE UNLOCKS THE NEXT RACE") + "</size>" +
            (reward > 0 ? "\n<size=34>+" + reward + " COINS</size>" : "");
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
        unlockPanel.SetActive(false);
        audioMan.PlayTap();
    }

    void SubmitUnlock()
    {
        string entered = unlockInput.text.Trim();

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
        menuCoinsText.text = "COINS  " + totalCoins;
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
        questMultText.text = "SCORE MULTIPLIER  X" + Mathf.Min(30, 1 + questLevel);
        questTiresText.text = "TIRES  " + tires;
        for (int i = 0; i < 3; i++) questRowTexts[i].text = QuestLine(i);
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

        Shader sh = Shader.Find("Sprites/Default");
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
        menuCoinsText.text = "COINS  " + totalCoins;
        PlayerPrefs.Save();
        audioMan.PlayTap();
    }

    void RefreshShopCurrency()
    {
        storeCoinsText.text = "COINS  " + totalCoins;
        storeTiresText.text = "TIRES  " + tires;
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
            itemUpInfo[i].text = "LV " + (level + 1) + "/7   "
                               + ItemUpgrades.Seconds(i).ToString("0.#") + "s"
                               + (maxed ? ""
                                  : "  >  " + ItemUpgrades.SecondsAtLevel(i, level + 1)
                                        .ToString("0.#") + "s");

            int cost = ItemUpgrades.Cost(i);
            itemUpButtons[i].text = maxed ? "MAX" : cost.ToString();
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

        boxResultText.text = ItemUpgrades.Names[item] + "  LV "
                           + (ItemUpgrades.Level(item) + 1);
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
        pendingBoxCurrency = Currency.Coins;

        // rarity table - TODO: add car skins as prizes when new skins exist
        float roll = Random.value;
        if (roll < 0.40f)
        {
            int c = Random.Range(200, 451);
            totalCoins += c;
            pendingBoxText = "+" + c + " COINS";
            pendingBoxColor = new Color(1f, 0.82f, 0.1f);
        }
        else if (roll < 0.70f)
        {
            int c = Random.Range(500, 901);
            totalCoins += c;
            pendingBoxText = "+" + c + " COINS!";
            pendingBoxColor = new Color(1f, 0.82f, 0.1f);
        }
        else if (roll < 0.85f)
        {
            int t = Random.Range(1, 4);
            tires += t;
            pendingBoxText = "+" + t + " TIRES!";
            pendingBoxColor = new Color(0.7f, 0.9f, 1f);
        }
        else if (roll < 0.95f)
        {
            int c = Random.Range(1200, 2001);
            totalCoins += c;
            pendingBoxText = "RARE!  +" + c + " COINS!";
            pendingBoxColor = new Color(0.8f, 0.4f, 1f);
        }
        else if (roll < 0.98f)
        {
            int t = Random.Range(5, 9);
            tires += t;
            pendingBoxText = "JACKPOT!  +" + t + " TIRES!";
            pendingBoxColor = new Color(0.35f, 1f, 0.45f);
        }
        else
        {
            // rarest prize: brand tokens for the exotic cars
            pendingBoxCurrency = (Currency)Random.Range(1, 5);
            int amount = Random.Range(1, 6);
            SetToken(pendingBoxCurrency, GetToken(pendingBoxCurrency) + amount);
            pendingBoxText = "+" + amount + " " + TokenNames[(int)pendingBoxCurrency] + "!";
            pendingBoxColor = TokenColors[(int)pendingBoxCurrency];
        }

        PlayerPrefs.SetInt("Coins", totalCoins);
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.Save();

        // spawn the actual prize model inside the box so it rises out
        if (boxPrizeGo != null) Destroy(boxPrizeGo);
        pendingBoxIsToken = false;
        bool prizeTires = pendingBoxText.Contains("TIRE");
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
            ShowPrizeReveal(pendingBoxText, pendingBoxIsToken,
                pendingBoxIsToken ? TokenIcons[(int)pendingBoxCurrency] : null);
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

        revealText = MakeText(revealPanel.transform, "PrizeText", 80, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1000f, 220f));
        revealText.color = new Color(0.12f, 0.08f, 0.02f);
        revealText.horizontalOverflow = HorizontalWrapMode.Wrap;

        revealHint = MakeText(revealPanel.transform, "PrizeHint", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -420f), new Vector2(900f, 80f));
        revealHint.text = "TAP TO CONTINUE";
        revealHint.color = new Color(0.15f, 0.10f, 0.03f, 0.75f);

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
    void ShowPrizeReveal(string text, bool tokenBox, string iconPath = null)
    {
        if (revealPanel == null) return;

        bool tires = text.Contains("TIRE");
        bool token = text.Contains("TOKEN");
        bool car = text.Contains("CAR");

        // the caller can name the exact emblem it just awarded; otherwise the
        // prize is worked out from the message
        if (string.IsNullOrEmpty(iconPath))
        {
            iconPath = car ? "UI/wheel_car" : token ? "UI/wheel_token"
                     : tires ? "UI/wheel_tires" : "UI/wheel_coins";
        }
        revealIcon.texture = Resources.Load<Texture2D>(iconPath);

        // token boxes get the cool blue treatment, coin boxes the gold one
        revealTint = tokenBox || token ? new Color(0.20f, 0.62f, 1f)
                                       : new Color(1f, 0.78f, 0.12f);
        revealBack.color = revealTint;
        revealText.text = text;
        revealText.color = tokenBox || token ? new Color(0.02f, 0.08f, 0.20f)
                                             : new Color(0.14f, 0.09f, 0.02f);
        revealHint.color = new Color(revealText.color.r, revealText.color.g,
                                     revealText.color.b, 0.7f);

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
        float ha = revealT < 0.9f ? 0f : 0.45f + 0.35f * Mathf.Sin(revealT * 4f);
        Color hc = revealHint.color;
        revealHint.color = new Color(hc.r, hc.g, hc.b, ha);
    }

    void ClosePrizeReveal()
    {
        if (revealT < 0.6f) return;      // let the punch play out first
        revealT = -1f;
        revealPanel.SetActive(false);
        CloseBoxFocus();     // the box screen goes with it
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

        // always a brand token, 1-5, weighted toward the cheaper marques
        float r = Random.value;
        Currency cur = r < 0.34f ? Currency.Caldera
                     : r < 0.64f ? Currency.Cyber
                     : r < 0.87f ? Currency.Tempasta
                     : Currency.Vettura;
        int amount = Random.Range(1, 6);
        SetToken(cur, GetToken(cur) + amount);

        pendingBoxCurrency = cur;
        pendingBoxIsToken = true;
        pendingBoxText = "+" + amount + " " + TokenNames[(int)cur];
        pendingBoxColor = TokenColors[(int)cur];

        // spawn the token model inside the blue box so it rises out
        if (boxPrizeGo != null) Destroy(boxPrizeGo);
        boxPrizeGo = BuildTokenIcon(cur, tokenBoxRoot.transform,
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
        boxResultText.text = "+" + coinsGained + " COINS";
        boxResultText.color = new Color(1f, 0.82f, 0.1f);
        RefreshShopCurrency();
        audioMan.PlayCoin();
    }

    void BuyTirePack(int amount)
    {
        // TODO integrate Unity IAP / store purchases here. Until the store is
        // wired up, the pack is granted instantly so the flow can be tested.
        tires += amount;
        PlayerPrefs.SetInt("Tires", tires);
        PlayerPrefs.Save();
        boxResultText.text = "+" + amount + " TIRES!";
        boxResultText.color = new Color(0.7f, 0.9f, 1f);
        RefreshShopCurrency();
        audioMan.PlayCoin();
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

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
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
        menuCoinsText.text = "COINS  " + totalCoins;
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
            swapGhostWheels.speed = car.CurrentSpeed
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
            ? "COINS  " + totalCoins
            : GetToken(d.currency) + "  " + TokenNames[(int)d.currency];
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
            shopActionLabel.text = "BUY " + d.iapPrice;
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
            shopPrice.text = d.cost.ToString();
            shopPrice.color = TokenColors[(int)d.currency];
            shopActionLabel.text = "BUY";
            // show that brand's token model beside the price
            EnsureShopShowcase();
            if (shopShowcaseRoot != null) shopShowcaseRoot.SetActive(true);
            garagePriceIcon.texture = Resources.Load<Texture2D>(TokenIcons[(int)d.currency]);
            garagePriceIcon.gameObject.SetActive(true);
        }
        else
        {
            shopPrice.text = d.cost.ToString();
            shopPrice.color = new Color(1f, 0.8f, 0.3f);
            shopActionLabel.text = "BUY";
            garagePriceIcon.texture = Resources.Load<Texture2D>(CoinIconPath);
            garagePriceIcon.gameObject.SetActive(true);
        }
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
                shopPrice.text = "NOT ENOUGH " + TokenNames[(int)d.currency];
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
        track.spawnObstacles = false;
        track.spawnTraffic = false;
        track.spawnCoins = false;
        track.spawnPowerUps = false;
        track.spawnTirePickups = false;
        track.spawnBoostPads = false;
        // corners are wanted here - the car drifts through them for the camera
        track.forceStraight = false;
        track.flatTrack = false;
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
        track.flatTrack = false;
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
        ApplyRunTrack();
        if (mode == Mode.Endless)
        {
            car.baseSpeed = runBaseSpeed;
            car.maxSpeed = runMaxSpeed + Cars[selectedCar].speedBonus;
            car.speedGainPerSecond = runSpeedGain;
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
        audioMan.PlayCrash();
        // engine and tyre screech stop the moment you go off - they used to
        // hold on through the whole revive prompt
        audioMan.StopDriving();

        bool canTires = tires >= ReviveTireCost;
        // short and plain - this is read in a hurry, against a five second clock
        reviveTireLabel.text = canTires
            ? "REVIVE   " + ReviveTireCost
            : "NEED   " + ReviveTireCost;
        reviveTireLabel.color = canTires ? Color.white : new Color(1f, 0.55f, 0.5f);
        reviveHaveText.text = "YOU HAVE " + tires;
        bool canAd = adRevivesUsed < 5;
        reviveAdLabel.text = canAd
            ? "WATCH AD (" + (5 - adRevivesUsed) + " LEFT)"
            : "NO AD REVIVES LEFT";
        reviveTimer = reviveDecisionTime;
        reviveTimerText.text = Mathf.CeilToInt(reviveTimer).ToString();
        revivePanel.SetActive(true);
        pauseButton.SetActive(false);
    }

    /// <summary>Counts the offer down on unscaled time - the game is frozen.</summary>
    void TickReviveTimer()
    {
        if (reviveTimer <= 0f) return;

        reviveTimer -= Time.unscaledDeltaTime;
        if (reviveTimer <= 0f)
        {
            reviveTimer = 0f;
            reviveTimerText.text = "0";
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
        // TODO integrate a real ad SDK (Unity Ads / AdMob) here - for now the
        // reward is granted immediately so the flow can be tested
        adRevivesUsed++;
        SaveQuests();
        DoRevive();
    }

    void DoRevive()
    {
        reviveTimer = 0f;                 // stop the clock
        reviveTimerText.transform.localScale = Vector3.one;
        revivesThisRun++;
        revivePanel.SetActive(false);
        Time.timeScale = 1f;
        state = State.Playing;
        pauseButton.SetActive(true);
        car.CenterLane();                                  // back to the middle
        track.ClearObstaclesAhead(car.DistanceTraveled, 80f); // clean runway
        car.GrantMercy(2f);   // mercy window, but keep the speed you had
        // A boost still queued when you crashed would otherwise cash itself
        // in the instant you came back, firing the car straight to its
        // ceiling - and the ceiling never drops again on its own.
        car.ClearBoost();
        audioMan.StartDriving();   // engine and tyres come back with you
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
        gameOverAt = Time.unscaledTime;
        pendingDrift = 0f; // unbanked combo dies with you
        if (camFollow != null) camFollow.Shake(0.55f);
        audioMan.PlayCrash();
        audioMan.StopDriving();
        audioMan.PlayMenuMusic();

        int finalScore = Mathf.RoundToInt(score);
        bool newBest = finalScore > best;
        if (newBest)
        {
            best = finalScore;
            PlayerPrefs.SetInt("HighScore", best);
        }
        // performance bonus: skilled runs pay far better than long ones,
        // since score already rewards drifting and near misses
        int bonusCoins = Mathf.RoundToInt(score / Mathf.Max(1f, scorePerBonusCoin)
                                          * Mathf.Pow(1f - revivePenalty, revivesThisRun)
                                          * RaceMode.CoinMultiplier());
        int runTotal = coinsThisRun + bonusCoins;
        totalCoins += runTotal;
        PlayerPrefs.SetInt("Coins", totalCoins);
        PlayerPrefs.Save();

        string cause = result == CarController.TickResult.CrashedOffRoad
            ? "OFF THE ROAD" : "CRASHED";
        centerText.gameObject.SetActive(false);
        pauseButton.SetActive(false);

        // beating your best earns its own screen; the results wait behind it
        if (newBest) ShowBestRun(cause, finalScore, coinsThisRun, bonusCoins);
        else ShowGameOver(cause, finalScore, false, coinsThisRun, bonusCoins);
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
            var intro = FindFirstObjectByType<TitleIntro>();
            if (intro != null) introSeen = true;

            // the intro is created part way through the logo screen, so until
            // it shows up the splash still counts as "opening in progress"
            bool openingUp = intro != null ||
                (!introSeen && FindFirstObjectByType<SplashScreen>() != null);

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
                if (loginPanel != null && loginPanel.activeSelf)
                {
                    loginPanel.SetActive(false);
                    if (showcaseRoot != null) showcaseRoot.SetActive(false);
                }
                // Skipping is handled by the intro's own full-screen button.
                // Reading raw input here as well would fire on the press while
                // the UI fires on the release, and anything under the intro
                // would see the same tap.
            }
        }

        AnimateBonus();
        TickBoxReveal();

        // corner currency counters stay current in every state
        if (persistentCoinsText != null)
        {
            persistentCoinsText.text = totalCoins.ToString();
            persistentTiresText.text = tires.ToString();
        }


        switch (state)
        {
            case State.Menu:
                TickLobbyDrive();
                TickWheel();
                if (menuPanel.activeSelf) AnimateSpinButton();
                break;

            case State.Playing:
                if (Input.GetKeyDown(KeyCode.Escape)) { PauseGame(); break; }
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
                // space bar = quick retry while testing in the editor
                if (Time.unscaledTime - gameOverAt > 0.6f && Input.GetKeyDown(KeyCode.Space))
                {
                    RestartRun();
                }
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
            itemBarLabels[slot].text = ItemNames[i] + "   " + Mathf.CeilToInt(remaining[i]);
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
                FlashBonus("SPRINGS! TAP TO JUMP", new Color(0.35f, 1f, 0.45f));
                break;
        }
        // one recognisable "you picked something up" chime for every item
        audioMan.PlayPowerUp();
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
                TickBiome(dt);
            }
            UpdateHud();
            return;
        }

        if (mode == Mode.Race) TickRace(dt);
        else TickBiome(dt);
        TickItems(dt);
        track.TickTraffic(dt, car.DistanceTraveled);
        CarController.TickResult result = car.Tick(dt);

        // springs: one boing per jump
        if (car.ConsumeJumpStarted()) audioMan.PlayBoing();

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
        if (car.IsDrifting) pendingDrift += driftPointsPerSecond * DriftMultiplier() * dt * carPointMult * ScoreMultiplier;

        if (prevDriftTime > 0f && car.DriftTime <= 0f && pendingDrift > 0f)
        {
            int banked = Mathf.RoundToInt(pendingDrift);
            score += banked;
            if (banked >= 50)
            {
                FlashBonus("+" + banked + " DRIFT COMBO", new Color(0.35f, 0.85f, 1f));
                // banking a good combo gives a satisfying little surge
                car.Boost(Mathf.Min(3f, banked / 250f));
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
            FlashBonus("+" + bonus + " NEAR MISS", new Color(0.4f, 1f, 0.5f));
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
        bestText.text = "BEST " + best;
        speedText.text = SpeedLabel(car.CurrentSpeed);
        coinHudText.text = "RUN COINS " + coinsThisRun;
        multHudText.text = "X" + ScoreMultiplier;

        UpdateItemBars();

        // drift multiplier floats next to the car
        bool showDrift = state == State.Playing && car.IsDrifting;
        if (driftText.gameObject.activeSelf != showDrift) driftText.gameObject.SetActive(showDrift);
        if (showDrift && mainCam != null)
        {
            int mult = DriftMultiplier();
            driftText.text = "DRIFT X" + mult + " +" + Mathf.RoundToInt(pendingDrift);
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
        // +1 for every second of sustained drift, capped at x8
        return 1 + Mathf.Min(7, (int)car.DriftTime);
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
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        // match width: on narrow screens (e.g. 860x1862) the UI scales to fit
        // horizontally and gains breathing room vertically instead of clipping
        scaler.matchWidthOrHeight = 0f;

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
            new Vector2(1f, 1f), new Vector2(-40f, -196f), new Vector2(400f, 60f));

        // always-visible currency counters with model icons, one per top corner
        MakeCurrencyIcon(uiRoot, new Vector2(0f, 1f), new Vector2(62f, -10f), 52f, false);
        persistentCoinsText = MakeText(uiRoot, "TotalCoins", 38, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(124f, -28f), new Vector2(400f, 55f));
        persistentCoinsText.color = new Color(1f, 0.82f, 0.1f);
        MakeCurrencyIcon(uiRoot, new Vector2(1f, 1f), new Vector2(-62f, -12f), 52f, true);
        persistentTiresText = MakeText(uiRoot, "TotalTires", 38, TextAnchor.UpperRight,
            new Vector2(1f, 1f), new Vector2(-124f, -28f), new Vector2(400f, 55f));
        persistentTiresText.color = new Color(1f, 0.34f, 0.30f);

        // pause button, top-right (below the tire counter)
        var pauseLabel = MakeButton(uiRoot, "PAUSE", 30,
            Vector2.zero, new Vector2(220f, 90f), PauseGame);
        pauseButton = pauseLabel.transform.parent.gameObject;
        var pbRt = pauseButton.GetComponent<RectTransform>();
        pbRt.anchorMin = pbRt.anchorMax = pbRt.pivot = new Vector2(1f, 1f);
        pbRt.anchoredPosition = new Vector2(-52f, -96f);
        pauseButton.SetActive(false);
        coinHudText = MakeText(uiRoot, "CoinHud", 36, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(62f, -96f), new Vector2(420f, 55f));
        coinHudText.color = new Color(1f, 0.9f, 0.45f);
        multHudText = MakeText(uiRoot, "MultHud", 34, TextAnchor.UpperLeft,
            new Vector2(0f, 1f), new Vector2(62f, -158f), new Vector2(400f, 50f));
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
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
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
        menuTitle = title;
        title.color = new Color(1f, 0.72f, 0.12f); // sunny arcade orange
        title.transform.localRotation = Quaternion.Euler(0f, 0f, 2.5f); // playful tilt

        // second line of the name, sitting under the big word
        menuSubTitle = MakeText(menuPanel.transform, "TitleSub", 50, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 345f), new Vector2(1040f, 80f));
        menuSubTitle.text = "ETERNAL";
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
        menuCoinsText = MakeText(menuPanel.transform, "MenuCoins", 42, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 175f), new Vector2(700f, 70f));
        menuCoinsText.color = new Color(1f, 0.82f, 0.1f);
        // PLAY stands alone; everything else sits in a tidy two-column block
        // underneath, so the menu is not one long stack of identical bars.
        MakeButton(menuPanel.transform, "PLAY", 64,
            new Vector2(0f, 30f), new Vector2(600f, 150f), StartRun);

        const float ColX = 152f;
        const float RowTop = -150f;
        const float RowStep = 128f;
        Vector2 small = new Vector2(292f, 112f);

        spinMenuLabel = MakeButton(menuPanel.transform, "SPIN", 34,
            new Vector2(-ColX, RowTop), small, OpenWheel,
            new Color(1f, 0.78f, 0.12f));
        MakeButton(menuPanel.transform, "RACES", 34,
            new Vector2(ColX, RowTop), small, OpenRaces);

        MakeButton(menuPanel.transform, "GARAGE", 34,
            new Vector2(-ColX, RowTop - RowStep), small, OpenGarage);
        MakeButton(menuPanel.transform, "SHOP", 34,
            new Vector2(ColX, RowTop - RowStep), small, OpenShop);

        MakeButton(menuPanel.transform, "QUESTS", 34,
            new Vector2(-ColX, RowTop - RowStep * 2f), small, OpenQuests);
        MakeButton(menuPanel.transform, "SETTINGS", 34,
            new Vector2(ColX, RowTop - RowStep * 2f), small, OpenSettings);
        var hint = MakeText(menuPanel.transform, "Hint", 30, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -560f), new Vector2(900f, 60f));
        hint.text = "SLIDE THUMB TO STEER";
        hint.color = new Color(1f, 1f, 1f, 0.75f);

        // --- settings
        settingsPanel = MakePanel(uiRoot, "SettingsPanel");
        MakeCard(settingsPanel.transform, new Vector2(0f, 30f), new Vector2(1010f, 1580f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var sTitle = MakeText(settingsPanel.transform, "SettingsTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 640f), new Vector2(800f, 120f));
        sTitle.text = "SETTINGS";
        sTitle.color = new Color(1f, 0.72f, 0.12f);

        // three sections rather than one long column of sliders
        settingsTabAudio = MakeButton(settingsPanel.transform, "AUDIO", 34,
            new Vector2(-320f, 512f), new Vector2(300f, 88f), () => SetSettingsTab(0));
        settingsTabGeneral = MakeButton(settingsPanel.transform, "GENERAL", 34,
            new Vector2(0f, 512f), new Vector2(300f, 88f), () => SetSettingsTab(1));
        settingsTabCredits = MakeButton(settingsPanel.transform, "CREDITS", 34,
            new Vector2(320f, 512f), new Vector2(300f, 88f), () => SetSettingsTab(2));

        settingsAudioTab = MakeTabGroup(settingsPanel.transform, "AudioTab");
        settingsGeneralTab = MakeTabGroup(settingsPanel.transform, "GeneralTab");
        settingsCreditsTab = MakeTabGroup(settingsPanel.transform, "CreditsTab");

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

        // --- credits
        var credLine = MakeText(settingsCreditsTab.transform, "CreditsBody", 28,
            TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -40f), new Vector2(880f, 960f), true);
        credLine.text = CreditsText;
        credLine.color = new Color(1f, 1f, 1f, 0.92f);
        credLine.lineSpacing = 1.15f;

        SetSettingsTab(0);

        MakeButton(settingsPanel.transform, "BACK", 44,
            new Vector2(0f, -720f), new Vector2(560f, 110f), CloseSettings);

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

        var credBackLabel = MakeButton(creditsPanel.transform, "BACK", 44,
            Vector2.zero, new Vector2(560f, 110f), CloseCredits);
        var credBackRt = credBackLabel.transform.parent.GetComponent<RectTransform>();
        credBackRt.anchorMin = credBackRt.anchorMax = credBackRt.pivot = new Vector2(0.5f, 0f);
        // panels now bleed past the safe area, so bottom-anchored buttons have
        // to come back up by the same amount to sit where they used to
        credBackRt.anchoredPosition = new Vector2(0f, 30f + PanelBleed.y);

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
        shopCoinsText = MakeText(garagePanel.transform, "ShopCoins", 42, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 370f), new Vector2(700f, 70f));
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
        shopPrice = MakeText(garageCarsTab.transform, "ShopPrice", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(800f, 70f));
        garagePriceIcon = MakeCurrencyIcon(garageCarsTab.transform, new Vector2(0.5f, 0.5f),
            new Vector2(-235f, -250f), 52f, false);
        shopActionLabel = MakeButton(garageCarsTab.transform, "BUY", 48,
            new Vector2(0f, -370f), new Vector2(480f, 120f), ShopAction);
        MakeButton(garagePanel.transform, "BACK", 44,
            new Vector2(0f, -520f), new Vector2(560f, 110f), CloseGarage);

        // --- pause menu
        pausePanel = MakePanel(uiRoot, "PausePanel");
        MakeCard(pausePanel.transform, new Vector2(0f, 10f), new Vector2(950f, 1090f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var pTitle = MakeText(pausePanel.transform, "PauseTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 430f), new Vector2(800f, 120f));
        pTitle.text = "PAUSED";
        pTitle.color = new Color(1f, 0.72f, 0.12f);
        var qTitle = MakeText(pausePanel.transform, "QuestsTitle", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(700f, 70f));
        qTitle.text = "QUESTS";
        pauseQuestText = MakeText(pausePanel.transform, "QuestsBody", 22, TextAnchor.UpperCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(820f, 210f));
        pauseQuestText.color = new Color(1f, 1f, 1f, 0.85f);
        pauseQuestText.horizontalOverflow = HorizontalWrapMode.Wrap;
        pauseQuestText.verticalOverflow = VerticalWrapMode.Truncate;
        pauseQuestText.lineSpacing = 1.25f;
        MakeButton(pausePanel.transform, "RESUME", 56,
            new Vector2(0f, -40f), new Vector2(560f, 140f), ResumeGame);
        MakeButton(pausePanel.transform, "SETTINGS", 44,
            new Vector2(0f, -220f), new Vector2(560f, 110f), OpenSettings);
        MakeButton(pausePanel.transform, "MENU", 40,
            new Vector2(0f, -370f), new Vector2(560f, 100f), QuitToMenu);

        // --- daily login reward popup
        loginPanel = MakePanel(uiRoot, "LoginPanel");
        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(loginPanel.transform, false);
        var dim = dimGo.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
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

        MakeCard(loginBody, new Vector2(0f, 20f), new Vector2(1020f, 950f),
            new Color(0.13f, 0.09f, 0.2f, 0.95f));
        var lTitle = MakeText(loginBody, "LoginTitle", 72, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 360f), new Vector2(900f, 100f));
        lTitle.text = "LOGIN STREAK";
        lTitle.color = new Color(1f, 0.72f, 0.12f);
        loginDayText = MakeText(loginBody, "LoginDay", 44, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 265f), new Vector2(800f, 70f));
        loginDayText.color = new Color(1f, 0.8f, 0.3f);

        // all 7 days visible in a row, current day highlighted at show time,
        // each with a live spinning 3D model
        for (int i = 0; i < 7; i++)
        {
            Image cell = MakeCard(loginBody,
                new Vector2(-435f + i * 145f, 120f), new Vector2(135f, 185f), Color.white);

            var dayLabel = MakeText(cell.transform, "Day", 22, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 72f), new Vector2(135f, 40f));
            dayLabel.text = "DAY " + (i + 1);

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(cell.transform, false);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            var iconRt = icon.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0f, 5f);
            iconRt.sizeDelta = new Vector2(96f, 96f);
            loginCellIcons[i] = icon;

            var cellText = MakeText(cell.transform, "CellText", 24, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -62f), new Vector2(135f, 50f));
            cellText.text = i == 6 ? "CAR" : "+" + LoginRewards[i];
            loginCells[i] = cell;
            loginCellTexts[i] = cellText;
        }

        loginRewardText = MakeText(loginBody, "LoginReward", 56, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(800f, 90f));
        loginRewardText.color = new Color(1f, 0.82f, 0.1f);
        loginCarText = MakeText(loginBody, "LoginCar", 42, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(900f, 70f));
        loginCarText.color = new Color(0.5f, 0.9f, 1f);
        loginCarText.gameObject.SetActive(false);
        claimBtnLabel = MakeButton(loginBody, "CLAIM", 54,
            new Vector2(0f, -310f), new Vector2(520f, 130f), ClaimLogin);

        BuildUnlockPanel(uiRoot);
        BuildWheelPanel(uiRoot);
        BuildBoxFocus(uiRoot);
        BuildPrizeReveal(uiRoot);
        BuildFinishCinematic(uiRoot);

        // --- race level select: 4 biomes x 5 levels
        racePanel = MakePanel(uiRoot, "RacePanel");
        Transform raceScroll = MakeScrollArea(racePanel, 2100f);
        MakeCard(raceScroll, new Vector2(0f, -100f), new Vector2(1010f, 1980f),
            new Color(0.13f, 0.09f, 0.2f, 0.93f));
        var rTitle2 = MakeText(raceScroll, "RaceTitle", 80, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 800f), new Vector2(900f, 110f));
        rTitle2.text = "RACES";
        rTitle2.color = new Color(1f, 0.72f, 0.12f);

        // --- three upgrades, shown above the level grid
        var upgHeader = MakeText(raceScroll, "UpgHeader", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 595f), new Vector2(900f, 60f));
        upgHeader.text = "UPGRADES";
        upgHeader.color = new Color(1f, 0.8f, 0.35f);
        // each upgrade is shown with a picture of what it does: a speedometer
        // for top speed, a car shunting another aside for chassis strength,
        // and a growing stack of coins for the coin bonus
        string[] upgIcons = { "UI/icon_engine", "UI/icon_chassis", "UI/icon_coinbonus" };
        for (int u = 0; u < 3; u++)
        {
            RaceMode.Upgrade up = (RaceMode.Upgrade)u;
            upgradeLabels[u] = MakeButton(raceScroll, "Upg" + u, 20,
                new Vector2(-320f + u * 320f, 470f), new Vector2(300f, 196f),
                () => BuyUpgrade(up), null, true);
            // text sits under the picture
            upgradeLabels[u].rectTransform.anchoredPosition = new Vector2(0f, -46f);
            upgradeLabels[u].rectTransform.sizeDelta = new Vector2(290f, 100f);

            var icoGo = new GameObject("UpgIcon" + u);
            icoGo.transform.SetParent(upgradeLabels[u].transform.parent, false);
            var ico = icoGo.AddComponent<RawImage>();
            ico.raycastTarget = false;
            ico.texture = Resources.Load<Texture2D>(upgIcons[u]);
            var icoRt = ico.rectTransform;
            icoRt.anchorMin = icoRt.anchorMax = icoRt.pivot = new Vector2(0.5f, 0.5f);
            icoRt.anchoredPosition = new Vector2(0f, 54f);
            icoRt.sizeDelta = new Vector2(92f, 92f);
        }

        for (int b = 0; b < RaceMode.BiomeCount; b++)
        {
            float rowY = 330f - b * 330f;
            var bLabel = MakeText(raceScroll, "RaceBiome" + b, 38, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, rowY), new Vector2(900f, 60f));
            bLabel.text = RaceMode.BiomeNames[b];
            bLabel.color = new Color(0.7f, 0.9f, 1f);

            for (int s2 = 0; s2 < RaceMode.LevelsPerBiome; s2++)
            {
                int level = b * RaceMode.LevelsPerBiome + s2;
                float x = -380f + s2 * 190f;
                Text lbl = MakeButton(raceScroll, "L" + level, 40,
                    new Vector2(x, rowY - 110f), new Vector2(165f, 140f),
                    () => StartRace(level));
                raceLevelLabels[level] = lbl;
            }
        }
        var raceBackLabel = MakeButton(racePanel.transform, "BACK", 44,
            Vector2.zero, new Vector2(560f, 110f), CloseRaces);
        var raceBackRt = raceBackLabel.transform.parent.GetComponent<RectTransform>();
        raceBackRt.anchorMin = raceBackRt.anchorMax = raceBackRt.pivot = new Vector2(0.5f, 0f);
        raceBackRt.anchoredPosition = new Vector2(0f, 30f + PanelBleed.y);

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
        questMultText = MakeText(questsPanel.transform, "QPMult", 36, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 430f), new Vector2(900f, 70f));
        questMultText.color = new Color(0.7f, 0.9f, 1f);
        questTiresText = MakeText(questsPanel.transform, "QPTires", 32, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 350f), new Vector2(900f, 60f));
        questTiresText.color = new Color(1f, 0.34f, 0.30f);
        for (int i = 0; i < 3; i++)
        {
            MakeCard(questsPanel.transform, new Vector2(0f, 190f - i * 220f),
                new Vector2(920f, 190f), new Color(0.22f, 0.17f, 0.33f, 0.95f));
            questRowTexts[i] = MakeText(questsPanel.transform, "QRow" + i, 30, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 190f - i * 220f), new Vector2(860f, 180f), true);
            questRowTexts[i].lineSpacing = 1.25f;
        }
        var qpHint = MakeText(questsPanel.transform, "QPHint", 26, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -450f), new Vector2(920f, 60f));
        qpHint.text = "COMPLETE ALL 3\n+1 MULTIPLIER   +6 TIRES";
        qpHint.color = new Color(1f, 1f, 1f, 0.6f);
        MakeButton(questsPanel.transform, "BACK", 44,
            new Vector2(0f, -560f), new Vector2(560f, 110f), CloseQuests);

        // --- shop (tires for money, mystery boxes for coins)
        shopPanel = MakePanel(uiRoot, "ShopPanel");
        // content runs from about +700 (title) down to -2150 (last upgrade row)
        Transform shopScroll = MakeScrollArea(shopPanel, 2900f, -725f);
        MakeCard(shopScroll, new Vector2(0f, -725f), new Vector2(1010f, 2880f),
            new Color(0.13f, 0.09f, 0.2f, 0.92f));
        var shTitle = MakeText(shopScroll, "ShopTitle", 84, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 640f), new Vector2(800f, 120f));
        shTitle.text = "SHOP";
        shTitle.color = new Color(1f, 0.72f, 0.12f);
        storeCoinsText = MakeText(shopScroll, "StoreCoins", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(-220f, 540f), new Vector2(440f, 60f));
        storeCoinsText.color = new Color(1f, 0.82f, 0.1f);
        storeTiresText = MakeText(shopScroll, "StoreTires", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(220f, 540f), new Vector2(440f, 60f));
        storeTiresText.color = new Color(1f, 0.34f, 0.30f);

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

        var boxBtnLabel = MakeButton(shopScroll, "OPEN BOX  " + MysteryBoxCost, 34,
            new Vector2(0f, 90f), new Vector2(700f, 110f), BuyMysteryBox, null, true);
        MakeCurrencyIcon(boxBtnLabel.transform.parent, new Vector2(0.5f, 0.5f),
            new Vector2(196f, 0f), 55f, false);
        boxResultText = MakeText(shopScroll, "BoxResult", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -5f), new Vector2(900f, 70f));

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

        var tokBtn = MakeButton(shopScroll, "OPEN  " + TokenBoxTireCost, 34,
            new Vector2(0f, -430f), new Vector2(700f, 110f), BuyTokenBox,
            new Color(0.16f, 0.45f, 0.9f), true);
        MakeCurrencyIcon(tokBtn.transform.parent, new Vector2(0.5f, 0.5f),
            new Vector2(158f, 0f), 55f, true);
        tokenBoxResultText = MakeText(shopScroll, "TokenBoxResult", 44,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -520f), new Vector2(900f, 70f));

        var tiresLabel = MakeText(shopScroll, "TiresLabel", 46, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, -610f), new Vector2(700f, 70f));
        tiresLabel.text = "TIRES";
        // bigger packs give progressively more bonus tires (base: 10 per $0.99)
        string[] packPrices = { "$0.99", "$4.99", "$9.99", "$24.99", "$49.99", "$99.99" };
        int[] packTires = { 10, 55, 120, 325, 700, 1500 };
        for (int i = 0; i < 6; i++)
        {
            int amount = packTires[i]; // capture for the closure
            float x = i % 2 == 0 ? -235f : 235f;
            float y = -710f - (i / 2) * 140f;
            Text label = MakeButton(shopScroll,
                packTires[i] + "\n\n<size=24>" + packPrices[i] + "</size>", 34,
                new Vector2(x, y), new Vector2(440f, 132f), () => BuyTirePack(amount),
                null, true);
            // nudge text right to make room for the tire-stack icon
            label.rectTransform.anchoredPosition = new Vector2(45f, 0f);
            label.rectTransform.sizeDelta = new Vector2(330f, 150f);

            var iconGo = new GameObject("PackIcon");
            iconGo.transform.SetParent(label.transform.parent, false);
            var icon = iconGo.AddComponent<RawImage>();
            icon.raycastTarget = false;
            var iRt = icon.rectTransform;
            iRt.anchorMin = iRt.anchorMax = iRt.pivot = new Vector2(0.5f, 0.5f);
            iRt.anchoredPosition = new Vector2(-143f, 18f);
            iRt.sizeDelta = new Vector2(88f, 88f);
            packIcons[i] = icon;
        }

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
                cCoins + "\n\n<size=26>" + cCost + "</size>", 32,
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
                new Vector2(84f, -27f), 34f, true);
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

            itemUpNames[i] = MakeText(shopScroll, "ItemUpName" + i, 34,
                TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
                new Vector2(-215f, y + 22f), new Vector2(480f, 50f), true);
            itemUpInfo[i] = MakeText(shopScroll, "ItemUpInfo" + i, 28,
                TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
                new Vector2(-215f, y - 24f), new Vector2(480f, 46f), true);
            itemUpInfo[i].color = new Color(1f, 1f, 1f, 0.75f);

            itemUpButtons[i] = MakeButton(shopScroll, "", 32,
                new Vector2(270f, y), new Vector2(330f, 104f),
                () => BuyItemUpgrade(item), null, true);
            // coin icon sits to the left of the price
            itemUpCoins[i] = MakeCurrencyIcon(itemUpButtons[i].transform.parent,
                new Vector2(0.5f, 0.5f), new Vector2(-105f, 0f), 46f, false);
            itemUpButtons[i].rectTransform.anchoredPosition = new Vector2(28f, 0f);
        }

        var shopBackLabel = MakeButton(shopPanel.transform, "BACK", 44,
            Vector2.zero, new Vector2(560f, 110f), CloseShop);
        var shopBackRt = shopBackLabel.transform.parent.GetComponent<RectTransform>();
        shopBackRt.anchorMin = shopBackRt.anchorMax = shopBackRt.pivot = new Vector2(0.5f, 0f);
        shopBackRt.anchoredPosition = new Vector2(0f, 30f + PanelBleed.y);

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
            new Vector2(0.5f, 0.5f), new Vector2(0f, 310f), new Vector2(900f, 110f));
        rTitle.text = "CRASHED!";
        rTitle.color = new Color(1f, 0.4f, 0.3f);
        var rSub = MakeText(revivePanel.transform, "RSub", 40, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 210f), new Vector2(900f, 70f));
        rSub.text = "KEEP GOING?";
        // ticking clock - the offer expires
        reviveTimerText = MakeText(revivePanel.transform, "RTimer", 66, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 145f), new Vector2(900f, 90f));
        reviveTimerText.color = new Color(1f, 0.8f, 0.25f);
        reviveTireLabel = MakeButton(revivePanel.transform, "TIRES", 44,
            new Vector2(0f, 60f), new Vector2(760f, 120f), ReviveWithTires, null, true);
        // the tyre reads as part of the price, so it sits just after the number
        MakeCurrencyIcon(reviveTireLabel.transform.parent, new Vector2(0.5f, 0.5f),
            new Vector2(150f, 0f), 62f, true);
        reviveTireLabel.rectTransform.anchoredPosition = new Vector2(-34f, 0f);
        reviveHaveText = MakeText(revivePanel.transform, "RHave", 30,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0f, -28f), new Vector2(700f, 40f), true);
        reviveHaveText.color = new Color(1f, 1f, 1f, 0.65f);
        reviveAdLabel = MakeButton(revivePanel.transform, "AD", 38,
            new Vector2(0f, -132f), new Vector2(760f, 120f), ReviveWithAd, null, true);
        MakeButton(revivePanel.transform, "GIVE UP", 40,
            new Vector2(0f, -300f), new Vector2(560f, 110f), DeclineRevive);

        BuildGameOverPanel(uiRoot);
        BuildBestPanel(uiRoot);
    }

    // ------------------------------------------------------- the death screen

    RectTransform goCard;
    Image goDim;
    Text goTitle, goScore, goBestLine, goRunLabel;
    readonly Text[] goStatValue = new Text[3];
    float goAnimT = -1f;
    int goTargetScore;
    bool goWasBest;

    void BuildGameOverPanel(Transform uiRoot)
    {
        gameOverPanel = MakePanel(uiRoot, "GameOverPanel");

        // full-bleed dim so the world behind reads as a backdrop, not clutter
        var dimGo = new GameObject("Dim");
        dimGo.transform.SetParent(gameOverPanel.transform, false);
        goDim = dimGo.AddComponent<Image>();
        goDim.color = new Color(0.62f, 0.05f, 0.08f, 0.62f);
        var dimRt = goDim.rectTransform;
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = dimRt.offsetMax = Vector2.zero;

        // everything below hangs off one holder so the whole screen can
        // scale and slide in as a single piece
        var holder = new GameObject("Holder");
        holder.transform.SetParent(gameOverPanel.transform, false);
        goCard = holder.AddComponent<RectTransform>();
        goCard.anchorMin = goCard.anchorMax = goCard.pivot = new Vector2(0.5f, 0.5f);
        goCard.anchoredPosition = Vector2.zero;
        goCard.sizeDelta = new Vector2(1000f, 1400f);

        MakeCard(goCard, new Vector2(0f, -40f), new Vector2(980f, 1120f),
            new Color(0.10f, 0.06f, 0.12f, 0.93f));

        goTitle = MakeText(goCard, "GOTitle", 92, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 400f), new Vector2(950f, 130f));
        goTitle.text = "CRASHED";
        goTitle.color = new Color(1f, 0.34f, 0.28f);

        goRunLabel = MakeText(goCard, "GORunLabel", 32, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(950f, 60f));
        goRunLabel.text = "SCORE";
        goRunLabel.color = new Color(1f, 1f, 1f, 0.55f);

        goScore = MakeText(goCard, "GOScore", 130, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(950f, 190f));

        goBestLine = MakeText(goCard, "GOBest", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, 105f), new Vector2(950f, 60f));
        goBestLine.color = new Color(1f, 1f, 1f, 0.6f);

        // Three stat rows, label left and value right. The offsets are the
        // CENTRE of each text box, so a left-aligned label that starts at
        // x = -360 has to sit at -360 + width/2.
        string[] labels = { "DISTANCE", "COINS COLLECTED", "BONUS" };
        for (int i = 0; i < 3; i++)
        {
            float y = 10f - i * 78f;
            var lab = MakeText(goCard, "GOStatL" + i, 30, TextAnchor.MiddleLeft,
                new Vector2(0.5f, 0.5f), new Vector2(-160f, y), new Vector2(400f, 60f), true);
            lab.text = labels[i];
            lab.color = new Color(1f, 1f, 1f, 0.6f);
            goStatValue[i] = MakeText(goCard, "GOStatV" + i, 32, TextAnchor.MiddleRight,
                new Vector2(0.5f, 0.5f), new Vector2(160f, y), new Vector2(400f, 60f), true);
        }

        MakeButton(goCard, "RETRY", 58,
            new Vector2(0f, -320f), new Vector2(700f, 140f), RestartRun,
            new Color(0.16f, 0.62f, 0.32f));
        MakeButton(goCard, "MENU", 40,
            new Vector2(0f, -470f), new Vector2(700f, 110f), GoToMenu);
    }

    /// <summary>Fills in the death screen and starts its entrance.</summary>
    void ShowGameOver(string cause, int finalScore, bool newBest,
                      int collected, int bonus)
    {
        goTitle.text = cause;
        // "OFF THE ROAD" is far wider than "CRASHED", so the headline is
        // sized to the words rather than fixed
        goTitle.fontSize = Mathf.RoundToInt((cause.Length > 8 ? 58 : 92) * FontScale);
        goTargetScore = finalScore;
        goWasBest = newBest;
        goScore.text = "0";
        goBestLine.text = newBest ? "NEW PERSONAL BEST" : "BEST  " + best;
        goBestLine.color = newBest ? new Color(1f, 0.82f, 0.15f)
                                   : new Color(1f, 1f, 1f, 0.6f);
        goStatValue[0].text = Mathf.RoundToInt(car.DistanceTraveled) + " M";
        goStatValue[1].text = "+" + collected;
        goStatValue[2].text = "+" + bonus;
        goAnimT = 0f;
        gameOverPanel.SetActive(true);
        gameOverPanel.transform.SetAsLastSibling();
    }

    void TickGameOver()
    {
        if (goAnimT < 0f || gameOverPanel == null || !gameOverPanel.activeSelf) return;
        goAnimT += Time.unscaledDeltaTime;

        // card drops in and settles
        float p = Mathf.Clamp01(goAnimT / 0.35f);
        float ease = 1f - Mathf.Pow(1f - p, 3f);
        goCard.localScale = Vector3.one * Mathf.Lerp(0.86f, 1f, ease);
        goCard.anchoredPosition = new Vector2(0f, Mathf.Lerp(90f, 0f, ease));

        // the score reels up rather than just appearing
        float c = Mathf.Clamp01((goAnimT - 0.2f) / 0.9f);
        int shown = Mathf.RoundToInt(Mathf.Lerp(0f, goTargetScore,
            1f - Mathf.Pow(1f - c, 3f)));
        goScore.text = shown.ToString();
        goScore.color = goWasBest
            ? Color.Lerp(Color.white, new Color(1f, 0.85f, 0.2f),
                         0.5f + 0.5f * Mathf.Sin(goAnimT * 3.5f))
            : Color.white;

        // the red wash floods in on impact, then settles
        float wash = Mathf.Lerp(0.8f, 0.62f, Mathf.Clamp01(goAnimT / 0.8f));
        goDim.color = new Color(0.62f, 0.05f, 0.08f, wash);
    }

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
        vpRt.offsetMin = new Vector2(PanelBleed.x, 150f + PanelBleed.y);
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
        var text = MakeText(parent, label + "Label", 34, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0f, y + 62f), new Vector2(800f, 50f));
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
        img.sprite = GetRoundedSprite();
        img.type = Image.Type.Sliced;
        img.color = tint ?? new Color(0.96f, 0.47f, 0.13f); // warm arcade orange
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var text = MakeText(go.transform, "Label", fontSize, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), Vector2.zero, size, plainFont);
        text.text = label;
        return text;
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
        var text = go.AddComponent<Text>();
        text.font = plainFont
            ? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            : uiFont;
        // The plain font is used where legibility matters most - small,
        // number-heavy labels - so it is set a little larger and bold to hold
        // its own against the display font around it.
        text.fontSize = Mathf.RoundToInt(size * FontScale * (plainFont ? 1.2f : 1f));
        // custom racing fonts are usually already slanted; the fallback font
        // gets bold+italic to fake the same look
        text.fontStyle = plainFont ? FontStyle.Bold
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
