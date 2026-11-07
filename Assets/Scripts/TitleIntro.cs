using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The opening title, shot like the cold open of a film over the real game.
///
/// The lobby car is already out on the road. Letterboxed, the camera picks it
/// up low behind the back wheels, then whips round its flank to the nose -
/// speed lines tearing past - and as it lands two streaks of light shoot
/// across the screen and the name slams down along them letter by letter.
/// The gold is struck white-hot, sparks fly and a shockwave rings out; ETERNAL
/// wipes in underneath, and a light runs over the lettering. Then the
/// letterbox opens, the camera settles into the lobby's own framing and the
/// name flies to exactly where the lobby title sits, so the handover to the
/// menu is invisible. The theme that starts here keeps looping as the lobby
/// music.
///
/// Everything drawn is worked out from the clock alone (see Pose), so a tap
/// can skip ahead and the frame is still right. CameraFollow is handed the
/// same clock every frame (TitleShot) for the same reason.
/// </summary>
public class TitleIntro : MonoBehaviour
{
    // ---------------------------------------------------------------- timeline
    // seconds; CameraFollow's moves for the intro use the public ones too

    public const float CurtainUp = 0.15f;      // the black lifts off the road...
    public const float CurtainOpen = 0.65f;    // ...and is gone
    public const float WhipStart = 1.50f;      // the camera whips round the car
    public const float WhipEnd = 2.05f;        // and lands on the nose
    const float TrailsAt = 1.78f;              // two streaks of light shoot across
    const float SlamStart = 1.98f;             // the first letter starts to fall
    const float LetterGap = 0.035f;            // each one just after the last
    const float LetterFall = 0.12f;            // and takes this long to land
    const float SubAt = 2.55f;                 // ETERNAL wipes in
    const float SubWipe = 0.40f;
    const float ShineAt = 3.05f;               // a light runs over the gold
    public const float SettleStart = 3.90f;    // everything heads for the lobby
    public const float MoveEnd = 4.95f;        // the name is on the lobby title
    const float RevealFrom = 4.20f;            // the menu fades up from here
    const float Total = 5.20f;                 // a short cross-fade, then gone

    // where the lobby title sits, if the lobby cannot be asked
    const float LobbyY = 420f;
    const int LobbyFontSize = 60;              // menu title 96 * FontScale
    const float LobbySubY = 345f;
    const int LobbySubFontSize = 31;           // menu subtitle 50 * FontScale
    const float LobbyTilt = 2.5f;

    // the big name, before it goes to the lobby
    static readonly Vector2 TitleHome = new Vector2(0f, 330f);
    const float TitleRoom = 980f;              // the widest the name may be
    const float BarHeight = 230f;              // letterbox, from each screen edge

    static readonly Color CurtainColour = new Color(0.02f, 0.02f, 0.035f, 1f);
    static readonly Color LobbyColor = new Color(1f, 0.72f, 0.12f);
    static readonly Color LobbySubColor = new Color(1f, 0.86f, 0.45f);
    static readonly Color TrailColour = new Color(1f, 0.84f, 0.42f);

    public string gameName = "DRIFTLINE";
    public string subName = "ETERNAL";

    public bool IsFinished { get { return finished; } }

    /// <summary>
    /// True once the name is sitting exactly where the lobby title goes. The
    /// menu can safely show its own title from this moment.
    /// </summary>
    public bool HandingOver { get { return handingOver; } }

    /// <summary>
    /// How far the lobby's own screen should be faded up, 0 to 1. The game's
    /// UI stays hidden behind the cinematic and comes up as the name settles.
    /// </summary>
    public float LobbyReveal { get { return Smooth((t - RevealFrom) / (MoveEnd - RevealFrom)); } }

    /// <summary>
    /// Jumps to just before the hand-over. Not all the way: the last stretch is
    /// the cross-fade onto the lobby title, and cutting that out would make the
    /// name pop rather than settle.
    /// </summary>
    public void Skip()
    {
        t = Mathf.Max(t, MoveEnd - 0.3f);
    }

    public static TitleIntro Create()
    {
        var canvasGo = new GameObject("TitleIntroCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;           // above the game UI, below the logo
        canvasGo.AddComponent<GraphicRaycaster>();
        // the intro can be up before the game has built its own UI
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        GameManager.ApplyUiScaling(scaler);    // match the game's UI scaling

        var go = new GameObject("TitleIntro");
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.AddComponent<RectTransform>();
        Stretch(rt);

        // Tap anywhere to skip. This blocker is a raycast target on the
        // intro's own canvas, which sorts above the menu, so the tap is
        // swallowed here and never reaches whatever button happens to be
        // underneath it. It stays alive through the hand-over, so the release
        // of that same tap cannot land on the lobby either. Nothing else in
        // the intro takes taps, so they all come here.
        var skipGo = new GameObject("SkipCatcher");
        skipGo.transform.SetParent(go.transform, false);
        var skipImg = skipGo.AddComponent<Image>();
        skipImg.color = new Color(0f, 0f, 0f, 0f);
        skipImg.raycastTarget = true;
        Stretch(skipImg.rectTransform);

        var intro = go.AddComponent<TitleIntro>();
        intro.ownCanvas = canvasGo;

        var skipBtn = skipGo.AddComponent<Button>();
        skipBtn.transition = Selectable.Transition.None;
        skipBtn.targetGraphic = skipImg;
        skipBtn.onClick.AddListener(intro.Skip);

        intro.Build();
        return intro;
    }

    // ------------------------------------------------------------------ state

    GameObject ownCanvas;
    RectTransform root;
    CameraFollow camFollow;
    SplashScreen splash;

    float t;
    int frames;
    bool started;
    bool finished;
    bool handingOver;

    // the name
    RectTransform titleRt;
    Text[] letters;
    RectTransform[] letterRts;
    TitleGleam[] letterGleams;
    Vector2[] letterHome;
    float wordWidth;
    int bigSize;
    float bigScale;                // the lobby's size over the big size
    float boomAt;                  // when the last letter lands

    RectTransform subRt;
    Text subtitle;
    TitleGleam subGleam;
    Vector2 subHome;
    float subWidth;
    Image subEdge;                 // the bright edge of the wipe

    // where it all ends up
    bool targetsFound;
    Vector2 lobbyTitlePos, lobbySubPos;

    // light and lines
    RawImage[] streaks;            // the two shooting across
    RawImage[] streakLines;        // the faint lines they leave
    RawImage[] speedLines;
    float[] speedY, speedLen, speedRate, speedPhase;

    // the hit
    RectTransform fxRoot;
    RawImage ring;
    Texture2D streakTex;
    readonly List<Spark> sparks = new List<Spark>();

    class Spark
    {
        public RawImage img;
        public Vector2 pos, vel;
        public float age, life;
    }

    // framing
    RawImage vignette;
    Image barTop, barBottom, flashImg, curtain;

    // sound
    AudioSource music, sfx;
    AudioClip slamClip, boomClip, riserClip, swooshClip;
    AudioManager audioMan;

    // one-off moments: fired once each, and only heard if not skipped past
    float[] cueAt;
    bool[] cueDone;
    const int CueRiser = 0, CueWhip = 1, CueBoom = 2, CueSub = 3, CueSettle = 4, CueLetters = 5;

    // ------------------------------------------------------------------ build

    void Build()
    {
        root = (RectTransform)transform;
        PlayMusic();     // the theme starts with the intro
        LoadSfx();
        camFollow = FindCamera();
        splash = FindAnyObjectByType<SplashScreen>();

        Font font = Resources.Load<Font>("Fonts/GameFont");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        streakTex = MakeStreakTexture();

        // a dark edge round the picture while it is a film
        vignette = MakeRaw("Vignette", root, MakeVignetteTexture(), new Color(0f, 0f, 0f, 0f));
        Stretch(vignette.rectTransform);

        BuildSpeedLines();
        BuildTitle(font);
        BuildSubtitle(font);

        fxRoot = MakeRect("Effects", root);
        ring = MakeRaw("Shockwave", fxRoot, MakeRingTexture(), new Color(1f, 0.95f, 0.8f, 0f));
        ring.rectTransform.sizeDelta = new Vector2(420f, 420f);

        barTop = MakeBar("LetterboxTop", 1f);
        barBottom = MakeBar("LetterboxBottom", 0f);

        flashImg = MakeImage("Flash", root, new Color(1f, 0.95f, 0.85f, 0f));
        Stretch(flashImg.rectTransform);

        // starts black, exactly the colour the logo screen leaves behind
        curtain = MakeImage("Curtain", root, CurtainColour);
        Stretch(curtain.rectTransform);

        int last = letters.Length - 1;
        boomAt = SlamStart + last * LetterGap + LetterFall;
        cueAt = new[] { 0f, WhipStart, boomAt, SubAt, SettleStart };
        cueDone = new bool[cueAt.Length + letters.Length];

        Pose(0f);
    }

    void BuildTitle(Font font)
    {
        // sized from the lobby title, as large as the screen allows
        Text lobby = GameManager.LobbyTitleText;
        int lobbySize = lobby != null && lobby.fontSize > 0 ? lobby.fontSize : LobbyFontSize;
        float lobbyWidth = WordWidth(font, lobbySize, gameName);
        float grow = Mathf.Clamp(TitleRoom / Mathf.Max(1f, lobbyWidth), 1f, 1.3f);
        bigSize = Mathf.RoundToInt(lobbySize * grow);
        bigScale = lobbySize / (float)bigSize;

        titleRt = MakeRect("Title", root);
        titleRt.anchoredPosition = TitleHome;
        titleRt.localEulerAngles = new Vector3(0f, 0f, LobbyTilt);

        // measure each glyph so the spacing matches how the lobby's own text
        // lays the word out, instead of assuming a fixed width per letter
        float[] widths = new float[gameName.Length];
        wordWidth = 0f;
        for (int i = 0; i < gameName.Length; i++)
        {
            widths[i] = MeasureGlyph(font, bigSize, gameName[i]);
            wordWidth += widths[i];
        }

        // the faint lines the streaks leave, behind the letters
        streaks = new RawImage[2];
        streakLines = new RawImage[2];
        for (int j = 0; j < 2; j++)
        {
            streakLines[j] = MakeRaw("StreakLine" + j, titleRt, MakeTaperTexture(), Faded(TrailColour, 0f));
            var lrt = streakLines[j].rectTransform;
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(-wordWidth * 0.5f - 160f, StreakY(j));
            lrt.sizeDelta = new Vector2(0f, j == 0 ? 6f : 4f);
        }

        letters = new Text[gameName.Length];
        letterRts = new RectTransform[gameName.Length];
        letterGleams = new TitleGleam[gameName.Length];
        letterHome = new Vector2[gameName.Length];
        float penX = -wordWidth * 0.5f;
        for (int i = 0; i < gameName.Length; i++)
        {
            var text = MakeText("Letter" + i, titleRt, font, bigSize, gameName[i].ToString());
            letterGleams[i] = AddGleam(text, LobbyColor,
                new Color(1f, 0.94f, 0.58f), new Color(1f, 0.55f, 0.05f), 3.4f);
            var lrt = text.rectTransform;
            lrt.sizeDelta = new Vector2(widths[i] * 1.4f + 20f, bigSize * 1.6f);
            letterHome[i] = new Vector2(penX + widths[i] * 0.5f, 0f);
            penX += widths[i];
            lrt.anchoredPosition = letterHome[i];
            letters[i] = text;
            letterRts[i] = lrt;
        }

        // the streaks themselves, in front: they are what the letters land on
        for (int j = 0; j < 2; j++)
        {
            streaks[j] = MakeRaw("Streak" + j, titleRt, streakTex, Faded(Color.white, 0f));
            var srt = streaks[j].rectTransform;
            srt.pivot = new Vector2(1f, 0.5f);      // the head is the bright end
            srt.sizeDelta = new Vector2(760f, j == 0 ? 16f : 11f);
        }
    }

    float StreakY(int j)
    {
        return j == 0 ? -bigSize * 0.08f : -bigSize * 0.30f;
    }

    void BuildSubtitle(Font font)
    {
        // the same gap below the name as in the lobby, scaled up
        Text lobbyTitle = GameManager.LobbyTitleText;
        Text lobbySub = GameManager.LobbySubTitleText;
        float gap = LobbyY - LobbySubY;
        if (lobbyTitle != null && lobbySub != null)
        {
            float measured = lobbyTitle.rectTransform.localPosition.y - lobbySub.rectTransform.localPosition.y;
            if (measured > 1f) gap = measured;
        }
        int lobbySubSize = lobbySub != null && lobbySub.fontSize > 0 ? lobbySub.fontSize : LobbySubFontSize;
        int subSize = Mathf.RoundToInt(lobbySubSize / bigScale);

        subRt = MakeRect("SubTitle", root);
        subHome = TitleHome - new Vector2(0f, gap / bigScale);
        subRt.anchoredPosition = subHome;
        subRt.localEulerAngles = new Vector3(0f, 0f, LobbyTilt);

        subtitle = MakeText("SubName", subRt, font, subSize, subName);
        subGleam = AddGleam(subtitle, LobbySubColor,
            new Color(1f, 0.97f, 0.80f), new Color(1f, 0.74f, 0.30f), 3f);
        subtitle.rectTransform.sizeDelta = new Vector2(1200f, subSize * 1.8f);
        subWidth = WordWidth(font, subSize, subName);

        // the bright edge that runs along the wipe
        subEdge = MakeImage("WipeEdge", subRt, new Color(1f, 0.95f, 0.75f, 0f));
        subEdge.rectTransform.sizeDelta = new Vector2(6f, subSize * 1.5f);
    }

    void BuildSpeedLines()
    {
        const int Count = 16;
        speedLines = new RawImage[Count];
        speedY = new float[Count];
        speedLen = new float[Count];
        speedRate = new float[Count];
        speedPhase = new float[Count];
        var rng = new System.Random(7);   // the same lines every time
        for (int i = 0; i < Count; i++)
        {
            speedY[i] = Mathf.Lerp(-1150f, 1150f, (float)rng.NextDouble());
            speedLen[i] = Mathf.Lerp(260f, 820f, (float)rng.NextDouble());
            speedRate[i] = Mathf.Lerp(6000f, 11000f, (float)rng.NextDouble());
            speedPhase[i] = Mathf.Lerp(0f, 3200f, (float)rng.NextDouble());
            var img = MakeRaw("SpeedLine" + i, root, streakTex, Faded(Color.white, 0f));
            img.uvRect = new Rect(1f, 0f, -1f, 1f);   // racing leftwards: bright end first
            img.rectTransform.sizeDelta = new Vector2(speedLen[i], Mathf.Lerp(2f, 6f, (float)rng.NextDouble()));
            img.enabled = false;
            speedLines[i] = img;
        }
    }

    // ----------------------------------------------------------------- update

    void Update()
    {
        if (finished) return;

        // Held at the very start while the logo screen is still up: it covers
        // the screen, and the film should not start behind it.
        bool held = splash != null && !splash.IsFinished;

        // clamp the first frames - a load spike would skip the whole intro
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (frames < 2 || held) step = 0f;
        frames++;
        t += step;
        if (!held) started = true;

        if (camFollow == null && frames % 15 == 0) camFollow = FindCamera();
        if (camFollow != null) camFollow.TitleShot(t);

        if (started) FireCues();
        TickSparks(step);
        Pose(t);

        if (t >= MoveEnd) handingOver = true;
        if (t >= Total) Finish();
    }

    /// <summary>The moments with a sound (and the hit with its sparks).</summary>
    void FireCues()
    {
        for (int c = 0; c < cueAt.Length; c++)
        {
            if (cueDone[c] || t < cueAt[c]) continue;
            cueDone[c] = true;
            if (t - cueAt[c] > 0.25f) continue;      // skipped past: stay quiet
            switch (c)
            {
                case CueRiser: Play(riserClip, 0.45f, 1f); break;
                case CueWhip: Play(swooshClip, 0.6f, 1.05f); break;
                case CueBoom:
                    Play(boomClip, 0.9f, 1f);
                    BurstSparks();
                    if (camFollow != null) camFollow.Shake(0.1f);
                    break;
                case CueSub: Play(swooshClip, 0.35f, 1.5f); break;
                case CueSettle: Play(swooshClip, 0.5f, 1f); break;
            }
        }
        // each letter hits with its own slam, rising in pitch along the word
        for (int i = 0; i < letters.Length; i++)
        {
            int k = CueLetters + i;
            float land = SlamStart + i * LetterGap + LetterFall;
            if (cueDone[k] || t < land) continue;
            cueDone[k] = true;
            if (t - land <= 0.25f) Play(slamClip, 0.42f, 0.95f + i * 0.04f);
        }
    }

    /// <summary>
    /// Puts every part of the picture where it is <paramref name="time"/>
    /// seconds in. Nothing here depends on the frame before.
    /// </summary>
    void Pose(float time)
    {
        // the black lifts to show the road
        float lift = Smooth((time - CurtainUp) / (CurtainOpen - CurtainUp));
        curtain.color = Faded(CurtainColour, 1f - lift);
        curtain.enabled = lift < 1f;

        // the letterbox and the dark edges belong to the film, and go as it settles
        float settle = Smooth((time - SettleStart) / 0.6f);
        SetBar(barTop, BarHeight * (1f - settle));
        SetBar(barBottom, BarHeight * (1f - settle));
        vignette.color = new Color(0f, 0f, 0f, 0.55f * (1f - Smooth((time - SettleStart) / 0.9f)));

        PoseSpeedLines(time);

        float boom = time - boomAt;
        flashImg.color = new Color(1f, 0.95f, 0.85f, boom >= 0f ? 0.35f * Mathf.Exp(-14f * boom) : 0f);
        flashImg.enabled = boom >= 0f && boom < 0.6f;

        // the trip to the lobby
        float go = EaseInOut((time - SettleStart) / (MoveEnd - SettleStart));
        if (time >= SettleStart) FindLobbyTargets();
        float fadeOut = time >= MoveEnd ? 1f - Mathf.Clamp01((time - MoveEnd) / (Total - MoveEnd)) : 1f;

        // the whole name jolts when the last letter lands
        Vector2 jolt = Vector2.zero;
        if (boom >= 0f && boom < 0.5f)
        {
            float k = Mathf.Exp(-12f * boom) * 16f;
            float step = Mathf.Floor(time * 40f);
            jolt = new Vector2(Hash(step) - 0.5f, Hash(step + 9f) - 0.5f) * 2f * k;
        }
        Vector2 titleTo = targetsFound ? lobbyTitlePos : TitleHome;
        titleRt.anchoredPosition = Vector2.Lerp(TitleHome, titleTo, go) + jolt * (1f - go);
        titleRt.localScale = Vector3.one * Mathf.Lerp(1f, bigScale, go);

        PoseLetters(time, go, fadeOut, boom);
        PoseStreaks(time, go);
        PoseSubtitle(time, go, fadeOut);
        PoseRing(boom);
    }

    void PoseLetters(float time, float go, float fadeOut, float boom)
    {
        // the band of light running over the gold
        float sk = (time - ShineAt) / 0.6f;
        float shineX = Mathf.Lerp(-wordWidth * 0.5f - 150f, wordWidth * 0.5f + 150f, Mathf.Clamp01(sk));
        float shine = sk > 0f && sk < 1f ? 0.85f : 0f;
        float white = boom >= 0f ? Mathf.Exp(-8f * boom) : 0f;

        for (int i = 0; i < letters.Length; i++)
        {
            // each letter is thrown at the screen: huge and close, then hits
            float start = SlamStart + i * LetterGap;
            float s = (time - start) / LetterFall;
            float scale, alpha, drop;
            if (s < 0f)
            {
                scale = 2.6f;
                alpha = 0f;
                drop = 60f;
            }
            else if (s < 1f)
            {
                scale = Mathf.Lerp(2.6f, 1f, s * s);       // faster as it comes
                alpha = Mathf.Clamp01(s * 3f);
                drop = (1f - s) * (1f - s) * 60f;
            }
            else
            {
                float since = time - start - LetterFall;   // a punch on landing
                scale = 1f + Mathf.Exp(-14f * since) * Mathf.Sin(since * 30f) * 0.12f;
                alpha = 1f;
                drop = 0f;
            }
            letterRts[i].anchoredPosition = letterHome[i] + new Vector2(0f, drop);
            letterRts[i].localScale = Vector3.one * scale;
            letters[i].color = new Color(1f, 1f, 1f, alpha * fadeOut);

            // white-hot for a moment as it lands, and all of them at the end
            float land = start + LetterFall;
            float hit = time >= land ? Mathf.Exp(-18f * (time - land)) * 0.9f : 0f;
            // stays gold all the way home: the lobby title is gold too
            letterGleams[i].Apply(letterHome[i].x, 0f, shineX, shine,
                Mathf.Clamp01(hit + white), 100000f);
        }
    }

    /// <summary>
    /// Two streaks of light race across where the name is about to land,
    /// leaving a faint line each that stays until the name heads home.
    /// </summary>
    void PoseStreaks(float time, float go)
    {
        float left = -wordWidth * 0.5f - 160f;
        float lineWidth = wordWidth + 320f;
        float fade = 1f - Smooth((time - SettleStart) / 0.4f);
        for (int j = 0; j < 2; j++)
        {
            float k = (time - TrailsAt - j * 0.07f) / 0.32f;
            float head = Mathf.Lerp(-1300f, 1300f, Mathf.Clamp01(k));
            var srt = streaks[j].rectTransform;
            srt.anchoredPosition = new Vector2(head, StreakY(j));
            float a = k > 0f && k < 1f ? 1f : 0f;
            streaks[j].color = new Color(1f, 0.97f, 0.85f, a);
            streaks[j].enabled = a > 0f;

            var line = streakLines[j];
            float shown = Mathf.Clamp(head - left, 0f, lineWidth);
            line.rectTransform.sizeDelta = new Vector2(shown, line.rectTransform.sizeDelta.y);
            line.color = Faded(TrailColour, (j == 0 ? 0.55f : 0.35f) * fade);
            line.enabled = shown > 0f && fade > 0f;
        }
    }

    void PoseSubtitle(float time, float go, float fadeOut)
    {
        Vector2 to = targetsFound ? lobbySubPos : subHome;
        subRt.anchoredPosition = Vector2.Lerp(subHome, to, go);
        subRt.localScale = Vector3.one * Mathf.Lerp(1f, bigScale, go);

        // wiped in from the left, with a bright edge running along the wipe
        float w = (time - SubAt) / SubWipe;
        float half = subWidth * 0.5f;
        float revealTo = w <= 0f ? -100000f : Mathf.Lerp(-half - 50f, half + 50f, Smooth(w));
        if (w >= 1f) revealTo = 100000f;
        subtitle.color = new Color(1f, 1f, 1f, fadeOut);

        float sk = (time - ShineAt - 0.15f) / 0.5f;
        float shineX = Mathf.Lerp(-half - 120f, half + 120f, Mathf.Clamp01(sk));
        float shine = sk > 0f && sk < 1f ? 0.8f : 0f;
        subGleam.Apply(0f, 0f, shineX, shine, 0f, revealTo);

        bool wiping = w > 0f && w < 1f;
        subEdge.enabled = wiping;
        if (wiping)
        {
            subEdge.rectTransform.anchoredPosition = new Vector2(revealTo, 0f);
            subEdge.color = new Color(1f, 0.95f, 0.75f, Mathf.Sin(w * Mathf.PI));
        }
    }

    /// <summary>Lines tearing past while the camera whips round.</summary>
    void PoseSpeedLines(float time)
    {
        float k = (time - (WhipStart - 0.05f)) / (WhipEnd - WhipStart + 0.15f);
        float env = k > 0f && k < 1f ? Mathf.Sin(k * Mathf.PI) : 0f;
        for (int i = 0; i < speedLines.Length; i++)
        {
            var img = speedLines[i];
            img.enabled = env > 0f;
            if (env <= 0f) continue;
            float x = Mathf.Repeat(speedPhase[i] - speedRate[i] * time, 3200f) - 1600f;
            img.rectTransform.anchoredPosition = new Vector2(x, speedY[i]);
            img.color = new Color(1f, 1f, 1f, 0.32f * env);
        }
    }

    /// <summary>The shockwave, flattened as if it ran along the road.</summary>
    void PoseRing(float boom)
    {
        float k = boom / 0.5f;
        bool on = k >= 0f && k < 1f;
        ring.enabled = on;
        if (!on) return;
        ring.rectTransform.anchoredPosition = titleRt.anchoredPosition;
        float s = Mathf.Lerp(0.3f, 3.2f, 1f - Mathf.Pow(1f - k, 3f));
        ring.rectTransform.localScale = new Vector3(s, s * 0.45f, 1f);
        ring.color = new Color(1f, 0.95f, 0.8f, 0.7f * (1f - k) * (1f - k));
    }

    // ----------------------------------------------------------------- sparks

    void BurstSparks()
    {
        const int Count = 36;
        Vector2 centre = titleRt.anchoredPosition;
        for (int i = 0; i < Count; i++)
        {
            var img = MakeRaw("Spark", fxRoot, streakTex, Color.white);
            var sp = new Spark
            {
                img = img,
                pos = centre + new Vector2(Random.Range(-wordWidth * 0.5f, wordWidth * 0.5f),
                                           -bigSize * 0.3f),
                life = Random.Range(0.45f, 0.85f),
            };
            float side = sp.pos.x >= centre.x ? 1f : -1f;
            sp.vel = new Vector2(side * Random.Range(100f, 800f), Random.Range(250f, 1200f));
            sparks.Add(sp);
        }
    }

    void TickSparks(float dt)
    {
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            Spark sp = sparks[i];
            sp.age += dt;
            float f = sp.age / sp.life;
            if (f >= 1f || sp.img == null)
            {
                if (sp.img != null) Destroy(sp.img.gameObject);
                sparks.RemoveAt(i);
                continue;
            }
            sp.vel += new Vector2(0f, -2600f) * dt;
            sp.pos += sp.vel * dt;
            var rt = sp.img.rectTransform;
            rt.anchoredPosition = sp.pos;
            // stretched along the way it is flying, hot white cooling to orange
            float speed = sp.vel.magnitude;
            rt.sizeDelta = new Vector2(18f + speed * 0.03f, 7f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(sp.vel.y, sp.vel.x) * Mathf.Rad2Deg);
            Color c = Color.Lerp(new Color(1f, 0.97f, 0.7f), new Color(1f, 0.45f, 0.05f), f);
            c.a = 1f - f * f;
            sp.img.color = c;
        }
    }

    // ---------------------------------------------------------------- handover

    /// <summary>
    /// Asks the lobby where its title actually is, so the name lands on it
    /// exactly on every screen shape - the lobby lays out inside the safe
    /// area, this canvas does not.
    /// </summary>
    void FindLobbyTargets()
    {
        if (targetsFound) return;
        targetsFound = true;
        lobbyTitlePos = new Vector2(0f, LobbyY);
        lobbySubPos = new Vector2(0f, LobbySubY);
        Text title = GameManager.LobbyTitleText;
        Text sub = GameManager.LobbySubTitleText;
        if (title != null) lobbyTitlePos = root.InverseTransformPoint(title.rectTransform.position);
        if (sub != null) lobbySubPos = root.InverseTransformPoint(sub.rectTransform.position);
    }

    void Finish()
    {
        finished = true;
        if (camFollow != null) camFollow.EndTitleShot();

        // hand the fallback source over to the AudioManager if one exists now
        if (music != null && audioMan == null)
        {
            audioMan = FindAnyObjectByType<AudioManager>();
            if (audioMan != null)
            {
                music.Stop();
                audioMan.RestartMusic();
            }
        }
        if (ownCanvas != null) Destroy(ownCanvas);
    }

    void OnDestroy()
    {
        if (camFollow != null) camFollow.EndTitleShot();
    }

    static CameraFollow FindCamera()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.GetComponent<CameraFollow>() : null;
    }

    // ------------------------------------------------------------------ audio

    void LoadSfx()
    {
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        slamClip = Resources.Load<AudioClip>("Audio/intro_slam");
        boomClip = Resources.Load<AudioClip>("Audio/intro_boom");
        riserClip = Resources.Load<AudioClip>("Audio/intro_riser");
        swooshClip = Resources.Load<AudioClip>("Audio/intro_swoosh");
    }

    void Play(AudioClip clip, float volume, float pitch)
    {
        if (sfx == null || clip == null) return;
        sfx.pitch = pitch;
        sfx.PlayOneShot(clip, volume);
    }

    void PlayMusic()
    {
        // the theme plays through the game's own music source, so it keeps
        // looping straight into the lobby without a gap or a second copy
        audioMan = FindAnyObjectByType<AudioManager>();
        if (audioMan != null && audioMan.RestartMusic()) return;

        AudioClip clip = Resources.Load<AudioClip>("Audio/intro_music");
        if (clip == null) return;
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        music = gameObject.AddComponent<AudioSource>();
        music.clip = clip;
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.volume = 0.8f;
        music.Play();
    }

    // ------------------------------------------------------------------ pieces

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    static RawImage MakeRaw(string name, Transform parent, Texture tex, Color colour)
    {
        var img = MakeRect(name, parent).gameObject.AddComponent<RawImage>();
        img.texture = tex;
        img.color = colour;
        img.raycastTarget = false;
        return img;
    }

    static Image MakeImage(string name, Transform parent, Color colour)
    {
        var img = MakeRect(name, parent).gameObject.AddComponent<Image>();
        img.color = colour;
        img.raycastTarget = false;
        return img;
    }

    Image MakeBar(string name, float edge)
    {
        var img = MakeImage(name, root, Color.black);
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, edge);
        rt.anchorMax = new Vector2(1f, edge);
        rt.pivot = new Vector2(0.5f, edge);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, BarHeight);
        return img;
    }

    static void SetBar(Image bar, float height)
    {
        bool on = height > 0.5f;
        if (bar.enabled != on) bar.enabled = on;
        if (on) bar.rectTransform.sizeDelta = new Vector2(0f, height);
    }

    static Text MakeText(string name, Transform parent, Font font, int size, string text)
    {
        var rt = MakeRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 1f, 1f, 0f);
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    /// <summary>The gold paint, then a black outline round it (in that order).</summary>
    static TitleGleam AddGleam(Text text, Color flat, Color top, Color bottom, float outlineWidth)
    {
        var gleam = text.gameObject.AddComponent<TitleGleam>();
        gleam.flat = flat;
        gleam.top = top;
        gleam.bottom = bottom;
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(outlineWidth, -outlineWidth);
        return gleam;
    }

    /// <summary>Width one character occupies at a given font size.</summary>
    static float MeasureGlyph(Font font, int size, char c)
    {
        font.RequestCharactersInTexture(c.ToString(), size, FontStyle.Bold);
        if (font.GetCharacterInfo(c, out CharacterInfo info, size, FontStyle.Bold))
        {
            return info.advance;
        }
        return size * 0.62f;
    }

    static float WordWidth(Font font, int size, string word)
    {
        float w = 0f;
        foreach (char c in word) w += MeasureGlyph(font, size, c);
        return w;
    }

    static Color Faded(Color c, float alpha)
    {
        c.a = alpha;
        return c;
    }

    static float Smooth(float p)
    {
        p = Mathf.Clamp01(p);
        return p * p * (3f - 2f * p);
    }

    static float EaseInOut(float p)
    {
        p = Mathf.Clamp01(p);
        return p < 0.5f ? 4f * p * p * p : 1f - Mathf.Pow(-2f * p + 2f, 3f) / 2f;
    }

    static float Hash(float step)
    {
        return Mathf.Repeat(Mathf.Sin(step * 12.9898f) * 43758.5453f, 1f);
    }

    // ---------------------------------------------------------------- textures

    /// <summary>A streak of light: a bright head trailing off to nothing.</summary>
    static Texture2D MakeStreakTexture()
    {
        const int W = 128, H = 16;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = Mathf.Abs(y - (H - 1) * 0.5f) / ((H - 1) * 0.5f);
            float across = Mathf.Pow(1f - v, 1.6f);
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                float a = Mathf.Pow(u, 2.2f) * across;
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>A line that fades away at both ends.</summary>
    static Texture2D MakeTaperTexture()
    {
        const int W = 64, H = 8;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float v = Mathf.Abs(y - (H - 1) * 0.5f) / ((H - 1) * 0.5f);
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                float a = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.7f) * (1f - v * v);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>A thin soft ring, for the shockwave.</summary>
    static Texture2D MakeRingTexture()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        float half = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float k = (d - 0.82f) / 0.09f;
                float a = d > 1f ? 0f : Mathf.Exp(-k * k);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Clear in the middle, dark towards the edges.</summary>
    static Texture2D MakeVignetteTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f;
                float v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Smooth(Mathf.InverseLerp(0.5f, 1.3f, d));
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, true);
        return tex;
    }
}
