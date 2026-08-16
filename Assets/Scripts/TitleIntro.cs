using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ten second opening that plays straight after the studio logo: speed lines
/// streak past, the game name slams in letter by letter, holds, then flies
/// back and settles exactly where the lobby title sits so the handover to the
/// menu is invisible. The theme keeps looping as the lobby music.
/// </summary>
public class TitleIntro : MonoBehaviour
{
    // --- timeline (seconds)
    const float LinesIn = 0.45f;
    const float LettersIn = 1.5f;    // letters finish arriving
    const float HoldEnd = 3.5f;      // title holds
    const float MoveEnd = 4.7f;      // title travels to the lobby position
    const float Total = 4.95f;       // short overlap, then the canvas is gone

    // letter arrival, tuned to land the last one on LettersIn
    const float FirstLetter = 0.22f;
    const float LetterGap = 0.085f;
    const float LetterFly = 0.26f;

    // where the lobby title sits, so the two line up exactly
    const float LobbyY = 420f;
    const int LobbyFontSize = 60;          // menu title 96 * FontScale
    const float LobbySubY = 345f;
    const int LobbySubFontSize = 31;       // menu subtitle 50 * FontScale
    const float LobbyTilt = 2.5f;
    static readonly Color LobbyColor = new Color(1f, 0.72f, 0.12f);
    static readonly Color LobbySubColor = new Color(1f, 0.86f, 0.45f);

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
    Text subtitle;
    RawImage[] speedLines;
    AudioSource music;

    // --- sound effects, played from the intro's own source so they work
    // whether or not the AudioManager has come up yet
    AudioSource sfx, sweeps;
    AudioClip slamClip, boomClip, riserClip, swooshClip;
    int slammed;               // letters that have already thumped
    bool boomed, swooshed, risen;

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

    public static TitleIntro Create()
    {
        var canvasGo = new GameObject("TitleIntroCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31000;           // above the game UI, below the logo
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

        var intro = go.AddComponent<TitleIntro>();
        intro.ownCanvas = canvasGo;
        intro.Build();
        return intro;
    }

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

        // speed lines streaking past
        var lineTex = MakeLineTexture();
        speedLines = new RawImage[26];
        for (int i = 0; i < speedLines.Length; i++)
        {
            var lgo = new GameObject("Line" + i);
            lgo.transform.SetParent(transform, false);
            var img = lgo.AddComponent<RawImage>();
            img.texture = lineTex;
            img.raycastTarget = false;
            img.color = new Color(1f, 0.85f, 0.5f, 0f);
            var lrt = img.rectTransform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(Random.Range(160f, 520f), Random.Range(3f, 7f));
            lrt.anchoredPosition = new Vector2(Random.Range(-700f, 700f), Random.Range(-900f, 900f));
            speedLines[i] = img;
        }

        // one Text per letter so they can arrive separately
        letters = new Text[gameName.Length];
        letterRts = new RectTransform[gameName.Length];
        letterHome = new Vector2[gameName.Length];

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
        float tracking = bigSize * 0.06f;                    // small gap between letters
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
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            var outline = lgo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(4f, -4f);

            var lrt = text.rectTransform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(widths[i] * 1.4f + 20f, bigSize * 1.6f);

            // pen advances by each glyph's own width
            letterHome[i] = new Vector2(penX + widths[i] * 0.5f, 120f);
            penX += widths[i] + tracking;
            lrt.anchoredPosition = letterHome[i];

            letters[i] = text;
            letterRts[i] = lrt;
        }

        // second line of the name - the gap between the two lines is the same
        // one the lobby uses, scaled up, so both fly home together
        float subSize = LobbySubFontSize / bigScaleFactor;
        subHome = new Vector2(0f, 120f - (LobbyY - LobbySubY) / bigScaleFactor);

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

    void LoadSfx()
    {
        // the slams are re-pitched per letter, so they get their own source -
        // changing pitch would otherwise bend the riser that is still playing
        sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        sweeps = gameObject.AddComponent<AudioSource>();
        sweeps.playOnAwake = false;
        sweeps.spatialBlend = 0f;
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

    void PlaySweep(AudioClip clip, float volume)
    {
        if (sweeps == null || clip == null) return;
        sweeps.PlayOneShot(clip, volume);
    }

    void PlayMusic()
    {
        // the theme plays through the game's own music source, so it keeps
        // looping straight into the lobby without a gap or a second copy
        audioMan = FindFirstObjectByType<AudioManager>();
        if (audioMan != null && audioMan.RestartMusic())
        {
            return;
        }

        // no AudioManager yet, or it had no clip - play the theme here
        AudioClip clip = Resources.Load<AudioClip>("Audio/intro_music");
        if (clip == null)
        {
            Debug.LogWarning("TitleIntro: Resources/Audio/intro_music not found.");
            return;
        }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        music = gameObject.AddComponent<AudioSource>();
        music.clip = clip;
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.volume = 0.8f;
        music.Play();
    }

    static Texture2D MakeLineTexture()
    {
        const int W = 32, H = 2;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int x = 0; x < W; x++)
        {
            float p = x / (float)(W - 1);
            float a = Mathf.Sin(p * Mathf.PI);          // fades at both ends
            for (int y = 0; y < H; y++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    void Update()
    {
        if (finished) return;

        // clamp the first frames - a load spike would skip the whole intro
        float step = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        if (frames < 2) step = 0f;
        frames++;
        t += step;

        UpdateSpeedLines();

        // riser builds under the speed lines before the first letter lands
        if (!risen && t > 0f)
        {
            risen = true;
            PlaySweep(riserClip, 0.5f);
        }

        // --- letters slam in one after another
        float bigScale = 1f;
        for (int i = 0; i < letters.Length; i++)
        {
            float delay = FirstLetter + i * LetterGap;
            float p = Mathf.Clamp01((t - delay) / LetterFly);
            float eased = 1f - Mathf.Pow(1f - p, 5f);   // very fast in, hard stop

            // alternate sides so the word assembles from both directions
            float from = (i % 2 == 0 ? -1f : 1f) * 1800f;
            Vector2 home = letterHome[i];
            letterRts[i].anchoredPosition = new Vector2(
                Mathf.Lerp(home.x + from, home.x, eased), home.y);
            letters[i].color = new Color(1f, 1f, 1f, eased);
            letterRts[i].localScale = Vector3.one * (1f + (1f - eased) * 0.8f);

            // one thump per letter, each a touch higher than the last
            if (i == slammed && p >= 1f)
            {
                slammed++;
                Play(slamClip, 0.55f, 0.92f + i * 0.05f);
                if (slammed >= letters.Length && !boomed)
                {
                    boomed = true;
                    Play(boomClip, 0.75f, 1f);
                }
            }
        }

        // --- the second line drops in right after the last letter lands
        if (t < HoldEnd)
        {
            float sp = Mathf.Clamp01((t - (LettersIn - 0.15f)) / 0.35f);
            float sEased = 1f - Mathf.Pow(1f - sp, 4f);
            subtitle.color = new Color(1f, 1f, 1f, sEased);
            subtitle.rectTransform.anchoredPosition =
                new Vector2(subHome.x, subHome.y - (1f - sEased) * 60f);
            subtitle.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, sEased);
        }

        // --- hold: the whole name breathes
        if (t > LettersIn && t < HoldEnd)
        {
            float p = (t - LettersIn) / (HoldEnd - LettersIn);
            bigScale = 1f + Mathf.Sin(p * Mathf.PI * 2f) * 0.01f;
            for (int i = 0; i < letterRts.Length; i++)
            {
                letterRts[i].localScale = Vector3.one * bigScale;
            }
        }

        // --- the word shrinks and flies to the lobby title position
        if (t >= HoldEnd)
        {
            float p = Mathf.Clamp01((t - HoldEnd) / (MoveEnd - HoldEnd));
            float eased = 1f - Mathf.Pow(1f - p, 3f);

            if (!swooshed)
            {
                swooshed = true;
                PlaySweep(swooshClip, 0.6f);
            }

            float scale = Mathf.Lerp(1f, bigScaleFactor, eased);

            // the second line travels with the word to its own lobby slot
            subtitle.rectTransform.anchoredPosition =
                Vector2.Lerp(subHome, new Vector2(0f, LobbySubY), eased);
            subtitle.rectTransform.localScale = Vector3.one * scale;
            subtitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, LobbyTilt * eased);
            subtitle.color = Color.Lerp(Color.white, LobbySubColor, eased);

            for (int i = 0; i < letterRts.Length; i++)
            {
                // same word, scaled down to lobby size and moved into place
                Vector2 target = new Vector2(letterHome[i].x * bigScaleFactor, LobbyY);
                letterRts[i].anchoredPosition = Vector2.Lerp(letterHome[i], target, eased);
                letterRts[i].localScale = Vector3.one * scale;
                letterRts[i].localRotation = Quaternion.Euler(0f, 0f, LobbyTilt * eased);
                letters[i].color = Color.Lerp(Color.white, LobbyColor, eased);
            }

            // reveal the menu behind
            float bgFade = Mathf.Clamp01((t - HoldEnd - 0.3f) / 1.0f);
            backdrop.color = new Color(0.02f, 0.02f, 0.035f, 1f - bgFade);
            for (int i = 0; i < speedLines.Length; i++)
            {
                Color c = speedLines[i].color;
                speedLines[i].color = new Color(c.r, c.g, c.b, c.a * (1f - bgFade));
            }
        }

        // Once the name is home it stays fully visible. GameManager switches
        // the real lobby title on underneath while this is still drawn, then
        // this canvas goes away - so the title is never missing for a frame.
        if (t >= MoveEnd) handingOver = true;

        if (t >= Total) Finish();
    }

    void UpdateSpeedLines()
    {
        float visible = Mathf.Clamp01(t / LinesIn) * Mathf.Clamp01((HoldEnd + 0.6f - t) / 0.6f);
        for (int i = 0; i < speedLines.Length; i++)
        {
            var rt = speedLines[i].rectTransform;
            Vector2 p = rt.anchoredPosition;
            p.x -= (1500f + (i % 5) * 420f) * Time.unscaledDeltaTime;
            if (p.x < -1100f) p.x = 1100f;
            rt.anchoredPosition = p;

            Color c = speedLines[i].color;
            speedLines[i].color = new Color(c.r, c.g, c.b, 0.30f * visible);
        }
    }

    /// <summary>Skip straight to the handover.</summary>
    public void Skip()
    {
        t = Mathf.Max(t, MoveEnd - 0.3f);
    }

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
