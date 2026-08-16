using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Studio logo shown when the game boots: the mark drops in, settles with a
/// bounce, a light sweeps across it, then the whole thing fades into the menu.
/// Built entirely in code - GameManager creates it, no scene setup needed.
/// </summary>
public class SplashScreen : MonoBehaviour
{
    public float fadeInTime = 0.55f;
    public float holdTime = 1.6f;
    public float fadeOutTime = 0.6f;
    [Tooltip("Paints the backdrop red and logs progress, for debugging only.")]
    public bool diagnosticMode = false;

    Image backdrop;
    RawImage logo;
    RectTransform logoRt;
    Image barFill;
    RawImage glow;
    RectTransform glowRt;

    float t;
    int frames;
    bool finished;
    bool introStarted;
    AudioSource sting;

    public bool IsFinished { get { return finished; } }

    /// <summary>
    /// Runs automatically when the game starts - no scene setup, no dependency
    /// on any other script having initialised first.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Create(null, null);
    }

    /// <summary>Builds the splash on its own canvas, above everything else.</summary>
    public static SplashScreen Create(Transform unused, Sprite roundedSprite)
    {
        var canvasGo = new GameObject("SplashCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;                 // on top of all game UI
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        var go = new GameObject("SplashScreen");
        go.transform.SetParent(canvasGo.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var splash = go.AddComponent<SplashScreen>();
        splash.ownCanvas = canvasGo;
        splash.Build(roundedSprite);
        splash.PlaySting();
        return splash;
    }

    GameObject ownCanvas;

    void Build(Sprite roundedSprite)
    {
        // solid black backdrop
        var bgGo = new GameObject("Backdrop");
        bgGo.transform.SetParent(transform, false);
        backdrop = bgGo.AddComponent<Image>();
        // the logo art sits on solid black, so the backdrop matches it and
        // the square edge of the image disappears completely
        backdrop.color = diagnosticMode
            ? new Color(0.8f, 0f, 0f, 1f)
            : new Color(0.02f, 0.02f, 0.035f, 1f);
        var bgRt = backdrop.rectTransform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // soft glow behind the mark so it reads instantly on black
        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(transform, false);
        glow = glowGo.AddComponent<RawImage>();
        glow.texture = MakeGlowTexture();
        glow.raycastTarget = false;
        glow.color = new Color(0.55f, 0.75f, 1f, 0f);
        glowRt = glow.rectTransform;
        glowRt.anchorMin = glowRt.anchorMax = glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.anchoredPosition = new Vector2(0f, 60f);
        glowRt.sizeDelta = new Vector2(1150f, 1150f);

        // the studio mark
        var logoGo = new GameObject("Logo");
        logoGo.transform.SetParent(transform, false);
        logo = logoGo.AddComponent<RawImage>();
        // the cut-out version has a transparent background, so the mark sits
        // on the backdrop instead of inside a black square
        var logoTex = Resources.Load<Texture2D>("UI/studio_logo_cut");
        if (logoTex == null) logoTex = Resources.Load<Texture2D>("UI/studio_logo");
        logo.texture = logoTex;
        if (logoTex == null)
        {
            Debug.LogWarning("SplashScreen: no logo texture found in Resources/UI.");
        }
        logo.raycastTarget = false;
        logo.color = new Color(1f, 1f, 1f, 0f);
        logoRt = logo.rectTransform;
        logoRt.anchorMin = logoRt.anchorMax = logoRt.pivot = new Vector2(0.5f, 0.5f);
        logoRt.anchoredPosition = new Vector2(0f, 60f);
        logoRt.sizeDelta = new Vector2(860f, 860f);

        // thin loading bar underneath
        var barBgGo = new GameObject("BarBG");
        barBgGo.transform.SetParent(transform, false);
        var barBg = barBgGo.AddComponent<Image>();
        if (roundedSprite != null) { barBg.sprite = roundedSprite; barBg.type = Image.Type.Sliced; }
        barBg.color = new Color(1f, 1f, 1f, 0.16f);
        var barBgRt = barBg.rectTransform;
        barBgRt.anchorMin = barBgRt.anchorMax = barBgRt.pivot = new Vector2(0.5f, 0.5f);
        barBgRt.anchoredPosition = new Vector2(0f, -420f);
        barBgRt.sizeDelta = new Vector2(460f, 12f);

        var barFillGo = new GameObject("BarFill");
        barFillGo.transform.SetParent(barBgGo.transform, false);
        barFill = barFillGo.AddComponent<Image>();
        if (roundedSprite != null) { barFill.sprite = roundedSprite; barFill.type = Image.Type.Sliced; }
        barFill.color = new Color(1f, 0.72f, 0.12f);
        var fillRt = barFill.rectTransform;
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(0f, 1f);
        fillRt.pivot = new Vector2(0f, 0.5f);
        fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        fillRt.sizeDelta = new Vector2(0f, 0f);
    }

    void PlaySting()
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/logo_sting");
        if (clip == null) return;
        sting = gameObject.AddComponent<AudioSource>();
        sting.clip = clip;
        sting.playOnAwake = false;
        sting.spatialBlend = 0f;
        sting.volume = 0.85f;
        sting.Play();
    }

    /// <summary>Radial glow that sits behind the mark.</summary>
    static Texture2D MakeGlowTexture()
    {
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - (S - 1) * 0.5f) / ((S - 1) * 0.5f);
                float dy = (y - (S - 1) * 0.5f) / ((S - 1) * 0.5f);
                float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 3.5f)));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void Update()
    {
        if (finished) return;

        // The first frame after load can report several seconds of real time
        // while Unity finishes starting up. Feeding that straight in would
        // fast-forward the whole splash before it ever draws, so clamp it.
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (frames < 2) step = 0f;      // let the canvas render at least once

        frames++;
        float prev = t;
        t += step;
        float total = fadeInTime + holdTime + fadeOutTime;

        if (diagnosticMode && Mathf.FloorToInt(prev) != Mathf.FloorToInt(t))
        {
            Debug.Log("[SplashScreen] t=" + t.ToString("0.0"));
        }

        // --- logo drops in and settles with a small bounce
        if (t < fadeInTime)
        {
            float p = t / fadeInTime;
            float eased = 1f - Mathf.Pow(1f - p, 3f);
            logo.color = new Color(1f, 1f, 1f, eased);
            float overshoot = Mathf.Sin(p * Mathf.PI) * 0.12f;
            logoRt.localScale = Vector3.one * (0.82f + eased * 0.18f + overshoot);
            logoRt.anchoredPosition = new Vector2(0f, 60f + (1f - eased) * 90f);
            glow.color = new Color(0.55f, 0.75f, 1f, eased * 0.28f);
            glowRt.localScale = Vector3.one * (0.7f + eased * 0.3f);
        }
        else if (t < fadeInTime + holdTime)
        {
            float p = (t - fadeInTime) / holdTime;
            logo.color = Color.white;
            logoRt.localScale = Vector3.one * (1f + Mathf.Sin(p * Mathf.PI * 2f) * 0.012f);
            logoRt.anchoredPosition = new Vector2(0f, 60f);
            glow.color = new Color(0.55f, 0.75f, 1f,
                0.24f + Mathf.Sin(p * Mathf.PI * 2f) * 0.06f);
        }
        else
        {
            float p = (t - fadeInTime - holdTime) / fadeOutTime;
            float a = 1f - p;
            logo.color = new Color(1f, 1f, 1f, a);
            // backdrop stays solid - the intro's identical backdrop is already
            // behind it, so the handover is invisible
            if (diagnosticMode) backdrop.color = new Color(0.8f, 0f, 0f, a);

            // bring the intro up early, while we still cover the screen
            if (!introStarted && p > 0.15f)
            {
                introStarted = true;
                TitleIntro.Create();
            }
            barFill.color = new Color(1f, 0.72f, 0.12f, a);
            glow.color = new Color(0.55f, 0.75f, 1f, 0.24f * a);
            logoRt.localScale = Vector3.one * (1f + p * 0.08f);
            if (sting != null) sting.volume = 0.85f * a;
        }

        // loading bar fills across the whole sequence
        float progress = Mathf.Clamp01(t / (fadeInTime + holdTime));
        var fillRt = barFill.rectTransform;
        fillRt.anchorMax = new Vector2(progress, 1f);

        if (t >= total)
        {
            finished = true;
            if (!introStarted) { introStarted = true; TitleIntro.Create(); }
            if (ownCanvas != null) Destroy(ownCanvas);
            else gameObject.SetActive(false);
        }
    }

    /// <summary>Skip on tap.</summary>
    public void Skip()
    {
        t = Mathf.Max(t, fadeInTime + holdTime);
    }

    void OnDisable()
    {
        if (sting != null) sting.Stop();
    }
}
