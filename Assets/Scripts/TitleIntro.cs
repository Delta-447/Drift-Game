using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opening title: a tyre comes screaming in from the left, drifts across the
/// screen laying a black skid mark and a cloud of smoke, and the game's name
/// is revealed in its wake. The word then settles exactly where the lobby
/// title sits, so the handover to the menu is invisible. The theme that starts
/// here keeps looping as the lobby music.
/// </summary>
public class TitleIntro : MonoBehaviour
{
    // --- timeline (seconds)
    const float TyreIn = 0.28f;      // the tyre arrives
    const float TyreOut = 1.85f;     // and leaves the far side
    const float SubIn = 1.90f;       // the second line drops in
    const float HoldEnd = 3.60f;     // the name holds while the smoke drifts
    const float MoveEnd = 4.70f;     // then travels to the lobby position
    const float Total = 4.95f;       // short overlap, then the canvas is gone

    // where the lobby title sits, so the two line up exactly
    const float LobbyY = 420f;
    const int LobbyFontSize = 60;          // menu title 96 * FontScale
    const float LobbySubY = 345f;
    const int LobbySubFontSize = 31;       // menu subtitle 50 * FontScale
    const float LobbyTilt = 2.5f;
    static readonly Color LobbyColor = new Color(1f, 0.72f, 0.12f);
    static readonly Color LobbySubColor = new Color(1f, 0.86f, 0.45f);

    // where the tyre runs, in canvas units
    const float TyreFromX = -760f;
    const float TyreToX = 780f;
    const float TyreY = 120f;
    const float TyreSize = 240f;

    public string gameName = "DRIFTLINE";
    public string subName = "ETERNAL";
    float bigScaleFactor = 0.5f;   // big size -> lobby size
    Vector2 subHome;               // where the second line sits while big

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

    GameObject ownCanvas;
    Image backdrop;
    Text[] letters;
    RectTransform[] letterRts;
    Vector2[] letterHome;      // final offsets within the word
    float[] letterShown;       // -1 until the tyre uncovers it
    Text subtitle;

    RawImage tyre;
    RectTransform tyreRt;
    RawImage skid;
    RectTransform skidRt;
    Transform smokeRoot;
    Texture2D puffTex;
    readonly List<Puff> puffs = new List<Puff>();
    float nextPuff;

    class Puff
    {
        public RawImage img;
        public float life, maxLife, size, drift, spin;
    }

    AudioSource music, sfx;
    AudioClip slamClip, boomClip, riserClip, swooshClip;
    bool risen, boomed, swooshed;

    float t;
    int frames;
    bool finished;
    bool handingOver;
    AudioManager audioMan;

    public bool IsFinished { get { return finished; } }

    /// <summary>
    /// True once the name is sitting exactly where the lobby title goes. The
    /// menu can safely show its own title from this moment.
    /// </summary>
    public bool HandingOver { get { return handingOver; } }

    /// <summary>
    /// Jumps to the hand-over. Not straight to the end: the last stretch is
    /// the cross-fade onto the lobby title, and cutting that out would make
    /// the name pop rather than settle.
    /// </summary>
    public void Skip()
    {
        // never backwards, and not all the way to the end: the last stretch
        // is the cross-fade onto the lobby title, and cutting that out makes
        // the name pop instead of settle
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
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;        // match the game's UI scaling

        var go = new GameObject("TitleIntro");
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        // Tap anywhere to skip. This blocker is a raycast target on the
        // intro's own canvas, which sorts above the menu, so the tap is
        // swallowed here and never reaches whatever button happens to be
        // underneath it. It stays alive through the hand-over, so the release
        // of that same tap cannot land on the lobby either.
        var skipGo = new GameObject("SkipCatcher");
        skipGo.transform.SetParent(go.transform, false);
        var skipImg = skipGo.AddComponent<Image>();
        skipImg.color = new Color(0f, 0f, 0f, 0f);
        skipImg.raycastTarget = true;
        var skipRt = skipImg.rectTransform;
        skipRt.anchorMin = Vector2.zero;
        skipRt.anchorMax = Vector2.one;
        skipRt.offsetMin = skipRt.offsetMax = Vector2.zero;

        var intro = go.AddComponent<TitleIntro>();
        intro.ownCanvas = canvasGo;

        var skipBtn = skipGo.AddComponent<Button>();
        skipBtn.transition = Selectable.Transition.None;
        skipBtn.targetGraphic = skipImg;
        skipBtn.onClick.AddListener(intro.Skip);

        intro.Build();
        return intro;
    }

    // ---------------------------------------------------------------- build

    void Build()
    {
        PlayMusic();     // theme starts with the first frame of the intro
        LoadSfx();

        Font font = Resources.Load<Font>("Fonts/GameFont");
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // dark backdrop
        var bgGo = new GameObject("Backdrop");
        bgGo.transform.SetParent(transform, false);
        backdrop = bgGo.AddComponent<Image>();
        backdrop.color = new Color(0.02f, 0.02f, 0.035f, 1f);
        var bgRt = backdrop.rectTransform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // the black line the tyre burns into the screen
        var skidGo = new GameObject("SkidMark");
        skidGo.transform.SetParent(transform, false);
        skid = skidGo.AddComponent<RawImage>();
        skid.texture = MakeSkidTexture();
        skid.color = new Color(0f, 0f, 0f, 0f);
        skid.raycastTarget = false;
        skidRt = skid.rectTransform;
        skidRt.anchorMin = skidRt.anchorMax = new Vector2(0.5f, 0.5f);
        skidRt.pivot = new Vector2(0f, 0.5f);     // grows rightward from the start
        skidRt.anchoredPosition = new Vector2(TyreFromX, TyreY - TyreSize * 0.34f);
        skidRt.sizeDelta = new Vector2(0f, TyreSize * 0.30f);

        // smoke lives under its own node so it draws behind the letters
        var smokeGo = new GameObject("Smoke");
        smokeGo.transform.SetParent(transform, false);
        var smokeRt = smokeGo.AddComponent<RectTransform>();
        smokeRt.anchorMin = smokeRt.anchorMax = smokeRt.pivot = new Vector2(0.5f, 0.5f);
        smokeRt.sizeDelta = Vector2.zero;
        smokeRoot = smokeGo.transform;
        puffTex = MakePuffTexture();

        BuildLetters(font);

        // the tyre itself, on top of everything it is uncovering
        var tyreGo = new GameObject("Tyre");
        tyreGo.transform.SetParent(transform, false);
        tyre = tyreGo.AddComponent<RawImage>();
        tyre.texture = MakeTyreTexture();
        tyre.raycastTarget = false;
        tyreRt = tyre.rectTransform;
        tyreRt.anchorMin = tyreRt.anchorMax = tyreRt.pivot = new Vector2(0.5f, 0.5f);
        tyreRt.anchoredPosition = new Vector2(TyreFromX, TyreY);
        tyreRt.sizeDelta = new Vector2(TyreSize, TyreSize);
        tyre.color = new Color(1f, 1f, 1f, 0f);
    }

    void BuildLetters(Font font)
    {
        letters = new Text[gameName.Length];
        letterRts = new RectTransform[gameName.Length];
        letterHome = new Vector2[gameName.Length];
        letterShown = new float[gameName.Length];
        for (int i = 0; i < letterShown.Length; i++) letterShown[i] = -1f;

        float bigSize = LobbyFontSize * 1.15f;
        bigScaleFactor = LobbyFontSize / bigSize;

        // measure each glyph so the spacing matches how the word actually
        // renders, instead of assuming a fixed fraction of the font size
        float[] widths = new float[gameName.Length];
        float totalWidth = 0f;
        for (int i = 0; i < gameName.Length; i++)
        {
            widths[i] = MeasureGlyph(font, Mathf.RoundToInt(bigSize), gameName[i]);
            totalWidth += widths[i];
        }
        float tracking = 0f;   // the lobby title has no extra spacing either
        totalWidth += tracking * (gameName.Length - 1);

        float penX = -totalWidth * 0.5f;
        for (int i = 0; i < gameName.Length; i++)
        {
            var lgo = new GameObject("Letter" + i);
            lgo.transform.SetParent(transform, false);
            var text = lgo.AddComponent<Text>();
            text.font = font;
            text.text = gameName[i].ToString();
            text.fontSize = Mathf.RoundToInt(bigSize);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 1f, 1f, 0f);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = lgo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(3.4f, -3.4f);

            var lrt = text.rectTransform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(widths[i] * 1.4f + 20f, bigSize * 1.6f);

            letterHome[i] = new Vector2(penX + widths[i] * 0.5f, TyreY);
            penX += widths[i] + tracking;
            lrt.anchoredPosition = letterHome[i];

            letters[i] = text;
            letterRts[i] = lrt;
        }

        // second line of the name - the gap between the two lines is the same
        // one the lobby uses, scaled up, so both fly home together
        float subSize = LobbySubFontSize / bigScaleFactor;
        subHome = new Vector2(0f, TyreY - (LobbyY - LobbySubY) / bigScaleFactor);

        var subGo = new GameObject("SubName");
        subGo.transform.SetParent(transform, false);
        subtitle = subGo.AddComponent<Text>();
        subtitle.font = font;
        subtitle.text = subName;
        subtitle.fontSize = Mathf.RoundToInt(subSize);
        subtitle.fontStyle = FontStyle.Bold;
        subtitle.alignment = TextAnchor.MiddleCenter;
        subtitle.color = new Color(1f, 1f, 1f, 0f);
        subtitle.raycastTarget = false;
        subtitle.horizontalOverflow = HorizontalWrapMode.Overflow;
        subtitle.verticalOverflow = VerticalWrapMode.Overflow;
        var subOutline = subGo.AddComponent<Outline>();
        subOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        subOutline.effectDistance = new Vector2(3f, -3f);
        var srt = subtitle.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.anchoredPosition = subHome;
        srt.sizeDelta = new Vector2(1000f, subSize * 1.8f);
    }

    // --------------------------------------------------------------- assets

    /// <summary>A tyre seen side on: black wall, treaded edge, pale rim.</summary>
    static Texture2D MakeTyreTexture()
    {
        const int S = 256;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float half = (S - 1) * 0.5f;

        Color rubber = new Color(0.10f, 0.10f, 0.12f);
        Color tread = new Color(0.05f, 0.05f, 0.06f);
        Color rim = new Color(0.72f, 0.75f, 0.82f);
        Color hub = new Color(0.30f, 0.32f, 0.38f);

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r > 1f) { tex.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }

                float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 180f;
                Color c;
                if (r > 0.86f)
                {
                    // tread blocks around the outside
                    c = ((int)(ang / 9f)) % 2 == 0 ? tread : rubber;
                }
                else if (r > 0.58f) c = rubber;
                else if (r > 0.50f) c = Color.Lerp(rubber, rim, 0.5f);
                else if (r > 0.20f)
                {
                    // five spokes
                    bool spoke = ((int)(ang / 36f)) % 2 == 0;
                    c = spoke ? rim : hub;
                }
                else c = hub;

                // a little shading so it does not read as a flat disc
                c *= 0.82f + 0.28f * Mathf.Clamp01(1f - (dy + 1f) * 0.5f);
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, 1f));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>Soft round blob used for each puff of smoke.</summary>
    static Texture2D MakePuffTexture()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float half = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.2f)));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    /// <summary>Dark band, softened top and bottom, for the rubber laid down.</summary>
    static Texture2D MakeSkidTexture()
    {
        const int W = 8, H = 32;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int y = 0; y < H; y++)
        {
            float f = Mathf.Abs(y - (H - 1) * 0.5f) / ((H - 1) * 0.5f);
            float a = Mathf.Pow(1f - f, 1.5f);
            for (int x = 0; x < W; x++) tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    // ---------------------------------------------------------------- audio

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
        audioMan = FindFirstObjectByType<AudioManager>();
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

    // --------------------------------------------------------------- update

    void Update()
    {
        if (finished) return;

        // clamp the first frames - a load spike would skip the whole intro
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (frames < 2) step = 0f;
        frames++;
        t += step;

        if (!risen && t > 0f)
        {
            risen = true;
            if (sfx != null && riserClip != null) sfx.PlayOneShot(riserClip, 0.45f);
        }

        UpdateTyre(step);
        UpdateSmoke(step);
        UpdateLetters(step);
        UpdateSubtitle();
        UpdateHandover();

        if (t >= Total) Finish();
    }

    void UpdateTyre(float dt)
    {
        float p = Mathf.InverseLerp(TyreIn, TyreOut, t);

        if (t < TyreIn)
        {
            tyre.color = new Color(1f, 1f, 1f, 0f);
            return;
        }

        // fast in, easing as it crosses, so it reads as a controlled drift
        float travel = 1f - Mathf.Pow(1f - Mathf.Clamp01(p), 2.2f);
        float x = Mathf.Lerp(TyreFromX, TyreToX, travel);
        float bob = Mathf.Sin(travel * Mathf.PI * 2f) * 14f;

        tyreRt.anchoredPosition = new Vector2(x, TyreY + bob);
        tyreRt.localRotation = Quaternion.Euler(0f, 0f, -travel * 1500f);
        tyre.color = new Color(1f, 1f, 1f, p < 1f ? 1f : Mathf.Clamp01(1f - (t - TyreOut) * 6f));

        // the skid mark it leaves behind
        float width = Mathf.Max(0f, x - TyreFromX);
        skidRt.sizeDelta = new Vector2(width, TyreSize * 0.30f);
        skid.color = new Color(0f, 0f, 0f, 0.55f);

        if (p >= 1f && !boomed)
        {
            boomed = true;
            Play(boomClip, 0.8f, 1f);
        }

        // puffs pour off the contact patch while it is crossing
        if (p > 0f && p < 1f)
        {
            nextPuff -= dt;
            if (nextPuff <= 0f)
            {
                nextPuff = 0.035f;
                SpawnPuff(new Vector2(x - 40f, TyreY + bob - TyreSize * 0.28f));
            }
        }
    }

    void SpawnPuff(Vector2 at)
    {
        var go = new GameObject("Puff");
        go.transform.SetParent(smokeRoot, false);
        var img = go.AddComponent<RawImage>();
        img.texture = puffTex;
        img.raycastTarget = false;

        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = at + new Vector2(Random.Range(-18f, 18f), Random.Range(-12f, 12f));

        var p = new Puff
        {
            img = img,
            maxLife = Random.Range(1.1f, 1.9f),
            size = Random.Range(150f, 260f),
            drift = Random.Range(18f, 52f),
            spin = Random.Range(-40f, 40f),
        };
        rt.sizeDelta = Vector2.one * p.size * 0.45f;
        puffs.Add(p);
    }

    void UpdateSmoke(float dt)
    {
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            Puff p = puffs[i];
            if (p.img == null) { puffs.RemoveAt(i); continue; }

            p.life += dt;
            float f = Mathf.Clamp01(p.life / p.maxLife);

            var rt = p.img.rectTransform;
            rt.sizeDelta = Vector2.one * Mathf.Lerp(p.size * 0.45f, p.size * 1.5f, f);
            rt.anchoredPosition += new Vector2(-p.drift * 0.35f, p.drift) * dt;
            rt.localRotation = Quaternion.Euler(0f, 0f, p.spin * p.life);

            // grey, thick at first, gone by the end
            float a = 0.5f * (1f - f * f);
            p.img.color = new Color(0.82f, 0.82f, 0.86f, a);

            if (f >= 1f)
            {
                Destroy(p.img.gameObject);
                puffs.RemoveAt(i);
            }
        }
    }

    /// <summary>Each letter appears as the tyre sweeps over it.</summary>
    void UpdateLetters(float dt)
    {
        float tyreX = tyreRt.anchoredPosition.x;

        for (int i = 0; i < letters.Length; i++)
        {
            if (letterShown[i] < 0f)
            {
                if (t < TyreIn || tyreX < letterHome[i].x + 20f) continue;
                letterShown[i] = 0f;
                Play(slamClip, 0.5f, 0.95f + i * 0.04f);
                continue;
            }

            letterShown[i] += dt;
            float f = Mathf.Clamp01(letterShown[i] / 0.32f);
            float eased = 1f - Mathf.Pow(1f - f, 3f);

            letters[i].color = new Color(1f, 1f, 1f, eased);
            if (t < HoldEnd)
            {
                // punches out of the smoke, then settles
                letterRts[i].localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, eased);
                letterRts[i].anchoredPosition = letterHome[i];
            }
        }
    }

    void UpdateSubtitle()
    {
        if (t >= HoldEnd) return;

        float f = Mathf.Clamp01((t - SubIn) / 0.4f);
        float eased = 1f - Mathf.Pow(1f - f, 3f);
        subtitle.color = new Color(1f, 1f, 1f, eased);
        subtitle.rectTransform.anchoredPosition =
            new Vector2(subHome.x, subHome.y - (1f - eased) * 40f);
        subtitle.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, eased);
    }

    /// <summary>The name shrinks and flies to where the lobby title sits.</summary>
    void UpdateHandover()
    {
        if (t < HoldEnd) return;

        float p = Mathf.Clamp01((t - HoldEnd) / (MoveEnd - HoldEnd));
        float eased = 1f - Mathf.Pow(1f - p, 3f);

        if (!swooshed)
        {
            swooshed = true;
            if (sfx != null && swooshClip != null) sfx.PlayOneShot(swooshClip, 0.55f);
        }

        float scale = Mathf.Lerp(1f, bigScaleFactor, eased);

        for (int i = 0; i < letterRts.Length; i++)
        {
            Vector2 target = new Vector2(letterHome[i].x * bigScaleFactor, LobbyY);
            letterRts[i].anchoredPosition = Vector2.Lerp(letterHome[i], target, eased);
            letterRts[i].localScale = Vector3.one * scale;
            letterRts[i].localRotation = Quaternion.Euler(0f, 0f, LobbyTilt * eased);
            letters[i].color = Color.Lerp(Color.white, LobbyColor, eased);
        }

        subtitle.rectTransform.anchoredPosition =
            Vector2.Lerp(subHome, new Vector2(0f, LobbySubY), eased);
        subtitle.rectTransform.localScale = Vector3.one * scale;
        subtitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, LobbyTilt * eased);
        subtitle.color = Color.Lerp(Color.white, LobbySubColor, eased);

        // the backdrop, skid mark and smoke clear to reveal the menu
        float fade = Mathf.Clamp01((t - HoldEnd - 0.2f) / 0.9f);
        backdrop.color = new Color(0.02f, 0.02f, 0.035f, 1f - fade);
        skid.color = new Color(0f, 0f, 0f, 0.55f * (1f - fade));
        for (int i = 0; i < puffs.Count; i++)
        {
            if (puffs[i].img == null) continue;
            Color c = puffs[i].img.color;
            puffs[i].img.color = new Color(c.r, c.g, c.b, c.a * (1f - fade));
        }

        if (t < MoveEnd) return;

        handingOver = true;

        // The menu switches its own title on now. These fade out over the same
        // quarter second, so the two are cross-faded rather than one popping
        // off and revealing the other sitting behind it.
        float over = Mathf.Clamp01((t - MoveEnd) / (Total - MoveEnd));
        float a = 1f - over;
        for (int i = 0; i < letters.Length; i++)
        {
            Color lc = letters[i].color;
            letters[i].color = new Color(lc.r, lc.g, lc.b, a);
        }
        Color sc = subtitle.color;
        subtitle.color = new Color(sc.r, sc.g, sc.b, a);
    }

    /// <summary>Skip straight to the handover.</summary>
    void Finish()
    {
        finished = true;

        // hand the fallback source over to the AudioManager if one exists now
        if (music != null && audioMan == null)
        {
            audioMan = FindFirstObjectByType<AudioManager>();
            if (audioMan != null)
            {
                music.Stop();
                audioMan.RestartMusic();
            }
        }
        if (ownCanvas != null) Destroy(ownCanvas);
    }
}
