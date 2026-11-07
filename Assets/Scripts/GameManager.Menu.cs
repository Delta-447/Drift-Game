using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lobby's look and its motion.
///
/// The menu is laid out round the car: the camera aims a little below it
/// (<see cref="CameraFollow.lobbyLift"/>) so it rides up into the gap between
/// the name at the top and the buttons at the bottom. Shade runs down from
/// the top of the screen and up from the bottom, so the gold name and the
/// buttons stand out against the sky and the road.
///
/// PLAY glows and breathes, with a band of light sweeping over it every few
/// seconds; the name catches the same light now and then. The other screens
/// are a row of picture tiles. Every time the menu comes up, PLAY and RACE
/// pop in and the tiles rise into place one after another.
/// </summary>
public partial class GameManager
{
    // the layout, in canvas units from the middle of the safe area
    const float MenuTitleY = 830f;
    const float MenuBestY = 650f;
    const float MenuWeeklyY = 545f;
    const float MenuBestRoom = 530f;           // widest the high score may be
    const float MenuPlayY = -300f;
    static readonly Vector2 MenuPlaySize = new Vector2(660f, 170f);
    const float MenuRaceY = -482f;
    const float MenuTileY = -705f;
    const int MenuTileCount = 5;
    const float MenuTileStep = 206f;
    static readonly Vector2 MenuTileSize = new Vector2(194f, 216f);
    const int MenuTileText = 34;

    /// <summary>
    /// How far below the car the lobby camera aims, in metres. It lifts the
    /// car up the screen, into the gap between the name and the buttons.
    /// </summary>
    const float MenuCarLift = 0.38f;

    /// <summary>How long the menu takes to come in, in seconds.</summary>
    const float MenuEnterTime = 1.2f;

    const string TrophyIconPath = "UI/icon_trophy";

    static readonly Color GarageTileBlue = new Color(0.20f, 0.47f, 0.90f);
    static readonly Color ShopTilePurple = new Color(0.56f, 0.30f, 0.90f);
    static readonly Color QuestTileGreen = new Color(0.18f, 0.64f, 0.34f);
    static readonly Color LoginTileLive = new Color(0.95f, 0.36f, 0.36f);

    TitleGleam menuTitleGleam, menuSubGleam;
    float menuTitleHalf;
    RectTransform menuBestPill;
    readonly RectTransform[] menuTileSlots = new RectTransform[MenuTileCount];
    readonly CanvasGroup[] menuTileGroups = new CanvasGroup[MenuTileCount];
    readonly RectTransform[] menuTileIcons = new RectTransform[MenuTileCount];
    Image playGlow;
    RectTransform playGlowRt;
    readonly RectTransform[] playGlints = new RectTransform[2];
    float menuEnterT = -1f;
    bool menuShownLast;
    int menuTickFrame = -10;

    // ------------------------------------------------------------- building

    /// <summary>
    /// A dark wash from one edge of the screen, fading out towards the middle.
    /// It reaches past the safe area with the panel, under notches and the
    /// home bar.
    /// </summary>
    void MakeMenuShade(Transform panel, bool top)
    {
        var go = new GameObject(top ? "ShadeTop" : "ShadeBottom");
        go.transform.SetParent(panel, false);
        var img = go.AddComponent<RawImage>();
        img.texture = GetShadeTexture();
        // the texture is dark at its bottom: turned upside down for the top
        img.uvRect = top ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        img.color = new Color(0.03f, 0.02f, 0.07f, top ? 0.55f : 0.8f);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, PanelBleed.y + (top ? 620f : 1060f));
    }

    static Texture2D shadeTexture;

    /// <summary>Solid along the bottom fifth, then easing away to nothing at the top.</summary>
    static Texture2D GetShadeTexture()
    {
        if (shadeTexture != null) return shadeTexture;
        const int H = 256;
        var tex = new Texture2D(4, H, TextureFormat.RGBA32, false);
        var px = new Color32[4 * H];
        for (int y = 0; y < H; y++)
        {
            float k = Mathf.Clamp01((y / (float)(H - 1) - 0.2f) / 0.8f);
            float a = 1f - k * k * (3f - 2f * k);
            byte b = (byte)Mathf.RoundToInt(a * 255f);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color32(255, 255, 255, b);
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        shadeTexture = tex;
        return tex;
    }

    /// <summary>A picture from Resources/UI, centred on a point.</summary>
    RawImage MakeMenuIcon(Transform parent, string path, Vector2 at, float size)
    {
        var go = new GameObject("Icon");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<RawImage>();
        img.texture = Resources.Load<Texture2D>(path);
        img.raycastTarget = false;
        if (img.texture == null) img.color = new Color(1f, 1f, 1f, 0f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = at;
        rt.sizeDelta = new Vector2(size, size);
        return img;
    }

    /// <summary>
    /// Paints a line of the lobby title gold like the opening title, which
    /// lands exactly on it. The gold has to go on before the black outline,
    /// or the outline is painted gold with it, so the outline is put back
    /// after it.
    /// </summary>
    TitleGleam AddTitleGleam(Text text, Color top, Color bottom, float outline)
    {
        var old = text.GetComponent<Outline>();
        if (old != null) DestroyImmediate(old);
        var gleam = text.gameObject.AddComponent<TitleGleam>();
        gleam.top = top;
        gleam.bottom = bottom;
        gleam.flat = text.color;
        var line = text.gameObject.AddComponent<Outline>();
        line.effectColor = new Color(0f, 0f, 0f, 0.9f);
        line.effectDistance = new Vector2(outline, -outline);
        return gleam;
    }

    /// <summary>
    /// PLAY's glow, behind it, and two glints that twinkle at its corners in turn - the same four-pointed
    /// sparkle as on the icons.
    /// </summary>
    void DressPlayButton(GameObject button)
    {
        var rt = (RectTransform)button.transform;
        playGlow = MakeButtonGlow(rt, new Color(0.45f, 1f, 0.5f, 0.45f));
        playGlowRt = playGlow.rectTransform;
        for (int i = 0; i < playGlints.Length; i++)
        {
            var go = new GameObject("Glint" + i);
            go.transform.SetParent(rt, false);
            var img = go.AddComponent<RawImage>();
            img.texture = GetGlintTexture();
            img.raycastTarget = false;
            var g = img.rectTransform;
            g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
            g.anchoredPosition = i == 0
                ? new Vector2(-rt.sizeDelta.x * 0.5f + 34f, rt.sizeDelta.y * 0.5f - 26f)
                : new Vector2(rt.sizeDelta.x * 0.5f - 40f, -rt.sizeDelta.y * 0.5f + 34f);
            g.sizeDelta = new Vector2(84f, 84f);
            g.localScale = Vector3.zero;
            playGlints[i] = g;
        }
    }

    /// <summary>A soft glow round a button, just behind it, in the given colour.</summary>
    Image MakeButtonGlow(RectTransform button, Color colour)
    {
        var glowGo = new GameObject(button.name + "Glow");
        glowGo.transform.SetParent(button.parent, false);
        glowGo.transform.SetSiblingIndex(button.GetSiblingIndex());
        var glow = glowGo.AddComponent<Image>();
        glow.sprite = GetGlowSprite();
        glow.type = Image.Type.Sliced;
        glow.color = colour;
        glow.raycastTarget = false;
        var g = glow.rectTransform;
        g.anchorMin = g.anchorMax = g.pivot = new Vector2(0.5f, 0.5f);
        g.anchoredPosition = button.anchoredPosition;
        // the glow's solid middle starts a little inside the button's edge
        g.sizeDelta = button.sizeDelta + Vector2.one * (GlowReach - 14f) * 2f;
        return glow;
    }

    /// <summary>
    /// A soft white sheen over the top half of a button's face, like the
    /// gloss on the icons. It is its own little sprite, rounded along the
    /// top like the face, so it needs no mask. Under the label.
    /// </summary>
    static void MakeButtonGloss(RectTransform button)
    {
        var go = new GameObject("Gloss");
        go.transform.SetParent(button, false);
        go.transform.SetSiblingIndex(0);
        var img = go.AddComponent<Image>();
        img.sprite = GetGlossSprite();
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.3f);
        img.raycastTarget = false;
        var g = img.rectTransform;
        g.anchorMin = new Vector2(0f, 0.5f);
        g.anchorMax = Vector2.one;
        // just inside the face's rim, along its top. (Everything on a button
        // is lifted by half the lip once it is built - see ButtonDepth - so
        // this is set that much lower here to end up level with the face.)
        g.offsetMin = new Vector2(4f, 0f);
        g.offsetMax = new Vector2(-4f, -4f - ButtonLip * 0.5f);
    }

    static Sprite glossSprite;

    /// <summary>White, rounded at the top corners, fading out downwards.</summary>
    static Sprite GetGlossSprite()
    {
        if (glossSprite != null) return glossSprite;
        const int S = 64;
        const float R = 15f;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // only the top corners are rounded
                float dx = Mathf.Max(Mathf.Abs(x - (S - 1) * 0.5f) - ((S - 1) * 0.5f - R), 0f);
                float dy = Mathf.Max(y - (S - 1 - R), 0f);
                float cover = Mathf.Clamp01(R - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                float k = y / (float)(S - 1);
                float fade = k * k * (3f - 2f * k);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(cover * fade * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        glossSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(24f, 0f, 24f, 24f));
        return glossSprite;
    }

    static Texture2D glintTexture;

    /// <summary>A four-pointed sparkle with a soft bloom, white, drawn in code.</summary>
    static Texture2D GetGlintTexture()
    {
        if (glintTexture != null) return glintTexture;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = Mathf.Abs((x + 0.5f) / S - 0.5f) * 2f, v = Mathf.Abs((y + 0.5f) / S - 0.5f) * 2f;
                // concave arms, sharp at the tips
                float arm = Mathf.Max(Mathf.Clamp01((1f - u - Mathf.Sqrt(v) * 1.15f) * 6f),
                                      Mathf.Clamp01((1f - v - Mathf.Sqrt(u) * 1.15f) * 6f));
                float bloom = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 1.6f);
                float a = Mathf.Max(arm, bloom * bloom * 0.55f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        glintTexture = tex;
        return tex;
    }

    /// <summary>How far the glow sprite's soft edge reaches, in canvas units.</summary>
    const float GlowReach = 48f;
    static Sprite glowSprite;

    /// <summary>
    /// A soft-edged glow: a small solid middle fading smoothly to nothing
    /// over <see cref="GlowReach"/>. Sliced, so it fits round any button.
    /// </summary>
    static Sprite GetGlowSprite()
    {
        if (glowSprite != null) return glowSprite;
        const int S = 128;
        const float Inner = 16f;
        float c = (S - 1) * 0.5f;
        float reach = c - Inner;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x - c) - Inner, 0f);
                float dy = Mathf.Max(Mathf.Abs(y - c) - Inner, 0f);
                float k = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / reach);
                float a = k * k * (3f - 2f * k);
                px[y * S + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float border = c - Inner + 0.5f;
        glowSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return glowSprite;
    }

    /// <summary>
    /// One of the picture tiles along the bottom of the menu: a raised square
    /// in its own colour, the picture on it and the word under the picture.
    /// The tile sits in a slot of its own; the menu's entrance moves the slot
    /// and the "something is waiting" bounce moves the tile inside it, so the
    /// two never fight. Returns the label, like <see cref="MakeButton"/>.
    /// </summary>
    Text MakeMenuTile(int index, string label, string iconPath, Color tint,
                      UnityEngine.Events.UnityAction onClick)
    {
        var slotGo = new GameObject("TileSlot" + index);
        slotGo.transform.SetParent(menuPanel.transform, false);
        var slot = slotGo.AddComponent<RectTransform>();
        slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0.5f, 0.5f);
        slot.anchoredPosition = new Vector2(MenuTileX(index), MenuTileY);
        slot.sizeDelta = MenuTileSize;
        menuTileSlots[index] = slot;
        menuTileGroups[index] = slotGo.AddComponent<CanvasGroup>();

        // the game's own lettering is wide, so it is set smaller than the
        // other scripts' fonts
        int askSize = Loc.DisplayFontFor(Loc.Current) == null ? MenuTileText : MenuTileText + 6;
        Text text = MakeButton(slot, label, askSize, Vector2.zero, MenuTileSize, onClick, tint);
        Transform tile = text.transform.parent;

        // the word along the bottom, wrapping onto a second line if it must.
        // (A button label shrinks itself to fit on one line - LOGIN STREAK
        // would end up tiny - so it is switched to wrapping first and given
        // its full size back, and only then allowed to shrink, if even two
        // lines will not fit.)
        var trt = text.rectTransform;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        trt.anchoredPosition = new Vector2(0f, -66f);
        trt.sizeDelta = new Vector2(MenuTileSize.x - 14f, 72f);
        int size = TextSize(Mathf.RoundToInt(askSize * ButtonTextScale), false, text.font);
        text.fontSize = size;
        text.lineSpacing = 0.85f;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 10;
        text.resizeTextMaxSize = size;

        // the picture above it
        menuTileIcons[index] = MakeMenuIcon(tile, iconPath, new Vector2(0f, 34f), 122f).rectTransform;
        return text;
    }

    static float MenuTileX(int index)
    {
        return (index - (MenuTileCount - 1) * 0.5f) * MenuTileStep;
    }

    // ------------------------------------------------------------- the other screens

    readonly List<Text> screenTitles = new List<Text>();
    readonly List<TitleGleam> screenTitleGleams = new List<TitleGleam>();

    /// <summary>
    /// Gives a screen's title the lobby title's look - gold, with a light
    /// running over it now and then - and the screen's own picture from the
    /// menu beside it, so every screen reads as part of the same game. The
    /// title and its picture are centred together.
    /// </summary>
    void StyleScreenTitle(Text title, string iconPath)
    {
        screenTitleGleams.Add(AddTitleGleam(title, new Color(1f, 0.94f, 0.58f), new Color(1f, 0.55f, 0.05f), 2.4f));
        screenTitles.Add(title);
        if (iconPath == null) return;
        float size = Mathf.Round(title.fontSize * 1.9f);
        const float Gap = 16f;
        float w = title.preferredWidth;
        MakeMenuIcon(title.transform, iconPath, new Vector2(-(w * 0.5f + Gap + size * 0.5f), 2f), size);
        title.rectTransform.anchoredPosition += new Vector2((size + Gap) * 0.5f, 0f);
    }

    /// <summary>The light across whichever screen title is showing, every few seconds.</summary>
    void TickScreenTitles()
    {
        float s = (Mathf.Repeat(Time.unscaledTime, 6f) - 0.6f) / 0.9f;
        bool on = s >= 0f && s < 1f;
        for (int i = 0; i < screenTitles.Count; i++)
        {
            var title = screenTitles[i];
            if (title == null || !title.isActiveAndEnabled) continue;
            float x = -100000f;
            if (on)
            {
                float half = title.preferredWidth * 0.5f + 160f;
                x = Mathf.Lerp(-half, half, s);
            }
            screenTitleGleams[i].Apply(0f, 0f, x, on ? 0.8f : 0f, 0f, 100000f);
        }
    }

    // ------------------------------------------------------------- motion

    /// <summary>
    /// Runs every frame while the lobby is up: keeps the car lifted, brings
    /// the menu in when it appears, and keeps it moving gently after that.
    /// </summary>
    void TickMenu()
    {
        if (menuPanel == null) return;
        if (camFollow != null) camFollow.lobbyLift = menuPanel.activeSelf ? MenuCarLift : 0f;

        // it "appears" when it is switched on and the opening title has let
        // the UI show
        bool shown = menuPanel.activeInHierarchy && (uiGroup == null || uiGroup.alpha > 0.02f);
        // this only runs in the lobby: coming back from anywhere else (a run)
        // counts as the menu appearing again
        if (Time.frameCount - menuTickFrame > 1) menuShownLast = false;
        menuTickFrame = Time.frameCount;
        if (shown && !menuShownLast) menuEnterT = 0f;
        menuShownLast = shown;
        if (!shown) return;

        float t = Time.unscaledTime;
        if (menuEnterT >= 0f)
        {
            menuEnterT += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (menuEnterT > MenuEnterTime) menuEnterT = -1f;
        }
        float enter = menuEnterT < 0f ? MenuEnterTime : menuEnterT;

        // the tiles rise into place one after another, and their pictures sway
        for (int i = 0; i < MenuTileCount; i++)
        {
            var slot = menuTileSlots[i];
            if (slot == null) continue;
            float k = Mathf.Clamp01((enter - 0.22f - i * 0.06f) / 0.45f);
            float e = EaseOutBack(k);
            slot.anchoredPosition = new Vector2(MenuTileX(i), MenuTileY - 150f * (1f - e));
            slot.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, e);
            if (menuTileGroups[i] != null) menuTileGroups[i].alpha = Mathf.Clamp01(k * 2.5f);

            var icon = menuTileIcons[i];
            if (icon != null)
                icon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.6f + i * 1.7f) * 4f);
        }

        // the high score and PLAY pop in, then RACE; PLAY keeps breathing
        float bestPop = EaseOutBack(Mathf.Clamp01(enter / 0.4f));
        float playPop = EaseOutBack(Mathf.Clamp01((enter - 0.05f) / 0.4f));
        float racePop = EaseOutBack(Mathf.Clamp01((enter - 0.13f) / 0.4f));
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.1f);
        if (menuBestPill != null)
            menuBestPill.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, bestPop);
        if (playButtonGo != null)
            playButtonGo.transform.localScale = Vector3.one
                * Mathf.LerpUnclamped(0.7f, 1f, playPop) * (1f + 0.022f * (pulse - 0.5f));
        if (raceButtonGo != null)
            raceButtonGo.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.7f, 1f, racePop);
        if (playGlow != null)
        {
            playGlow.color = new Color(0.45f, 1f, 0.5f, (0.25f + 0.3f * pulse) * Mathf.Clamp01(playPop));
            playGlowRt.localScale = Vector3.one
                * Mathf.LerpUnclamped(0.7f, 1f, playPop) * (1f + 0.035f * pulse);
        }

        // the glints on PLAY's corners, one and then the other
        for (int i = 0; i < playGlints.Length; i++)
        {
            var g = playGlints[i];
            if (g == null) continue;
            float k = (Mathf.Repeat(t + i * 1.4f, 2.8f) - 0.2f) / 0.7f;
            float pop = k > 0f && k < 1f ? Mathf.Sin(k * Mathf.PI) : 0f;
            g.localScale = Vector3.one * pop;
            g.localRotation = Quaternion.Euler(0f, 0f, k * 90f);
        }

        // RACE glows a beat after PLAY, and its flags wave
        if (raceGlow != null)
        {
            float rp = 0.5f + 0.5f * Mathf.Sin(t * 3.1f - 1.6f);
            raceGlow.color = new Color(1f, 0.45f, 0.25f, (0.18f + 0.22f * rp) * Mathf.Clamp01(racePop));
            raceGlow.rectTransform.localScale = Vector3.one
                * Mathf.LerpUnclamped(0.7f, 1f, racePop) * (1f + 0.03f * rp);
        }
        if (raceFlags != null)
            raceFlags.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 2.3f) * 6f);

        // and across the name, less often (the opening title paints it
        // itself until it has finished)
        if (!introRunning && menuTitleGleam != null)
        {
            if (menuTitleHalf <= 0f) menuTitleHalf = menuTitle.preferredWidth * 0.5f + 160f;
            float s = (Mathf.Repeat(t, 7f) - 1f) / 0.9f;
            bool on = s >= 0f && s < 1f;
            float x = on ? Mathf.Lerp(-menuTitleHalf, menuTitleHalf, s) : -100000f;
            menuTitleGleam.Apply(0f, 0f, x, on ? 0.85f : 0f, 0f, 100000f);
            if (menuSubGleam != null)
            {
                // the line underneath catches it a moment later
                float s2 = s - 0.12f;
                bool on2 = s2 >= 0f && s2 < 1f;
                float x2 = on2 ? Mathf.Lerp(-menuTitleHalf, menuTitleHalf, s2) : -100000f;
                menuSubGleam.Apply(0f, 0f, x2, on2 ? 0.7f : 0f, 0f, 100000f);
            }
        }
    }
}
