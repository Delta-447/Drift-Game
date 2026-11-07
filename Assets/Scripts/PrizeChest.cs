using UnityEngine;

/// <summary>
/// The two prize chests in the shop, built from Unity's simple shapes so
/// they need no model files.
///
/// - The MYSTERY BOX is a pit-lane crate: charcoal, with armoured gold
///   corners, a hazard-stripe band, the game's double-chevron badge and an
///   amber light inside.
/// - The TOKEN BOX is a vault: navy and chrome with cyan neon along its
///   edges and a glowing hexagon emblem like the token art.
///
/// Every part is listed below (tables made from the design, in metres, with
/// the chest's bottom centre at 0,0,0 and +z facing the camera). A cylinder's
/// y scale is half its length, as with Unity's own cylinder. Parts marked lid
/// hang off the hinge and swing open with it.
///
/// The glowing parts - neon, the light inside, the emblem - use the game's
/// own unlit shader (Driftline/FlatIcon, shipped in Resources), so they show
/// their full colour on every device whatever the lighting, and the opening
/// animation (ToolboxAnimator) can flare them by changing their colour.
/// </summary>
public static class PrizeChest
{
    public enum Style { Crate, Vault }

    /// <summary>What the animator needs from a chest once it is built.</summary>
    public class Rig
    {
        public GameObject root;
        public Transform lidPivot;
        public Material glow, inner, emblem, beam;   // the parts that light up
        public Color glowColour, innerColour, emblemColour, beamColour;
        public Transform beamQuad;                    // the shaft of light from inside
    }

    /// <summary>The lid turns about this line (along x), at the back of the top.</summary>
    public static readonly Vector3 Hinge = new Vector3(0f, 0.625f, -0.44f);

    enum Slot { Body, BodyTop, Trim, Dark, Glow, Inner, Stripes, Badge, BadgeCore, Emblem }

    struct Part
    {
        public PrimitiveType shape;
        public Slot slot;
        public Vector3 pos, scale, rot;
        public bool lid;

        public Part(PrimitiveType shape, Slot slot, Vector3 pos, Vector3 scale, Vector3 rot, bool lid)
        {
            this.shape = shape;
            this.slot = slot;
            this.pos = pos;
            this.scale = scale;
            this.rot = rot;
            this.lid = lid;
        }
    }

    /// <summary>How one material slot looks.</summary>
    struct Finish
    {
        public Color colour;
        public float metal, smooth;
        public bool glows;        // unlit and full brightness
        public bool hazard;       // yellow and black stripes

        public Finish(Color colour, float metal, float smooth, bool glows, bool hazard)
        {
            this.colour = colour;
            this.metal = metal;
            this.smooth = smooth;
            this.glows = glows;
            this.hazard = hazard;
        }
    }

    public static Rig Build(Style style, Transform parent, Vector3 at)
    {
        Part[] parts = style == Style.Crate ? CrateParts : VaultParts;
        Finish[] look = style == Style.Crate ? CrateLook : VaultLook;
        var mats = new Material[look.Length];
        for (int i = 0; i < look.Length; i++) mats[i] = MakeMaterial(look[i]);

        var root = new GameObject(style == Style.Crate ? "PrizeCrate" : "PrizeVault");
        root.transform.SetParent(parent, false);
        root.transform.position = at;

        var pivot = new GameObject("LidPivot");
        pivot.transform.SetParent(root.transform, false);
        pivot.transform.localPosition = Hinge;

        foreach (Part p in parts)
        {
            GameObject go = GameObject.CreatePrimitive(p.shape);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.name = p.slot.ToString();
            Transform t = go.transform;
            t.SetParent(p.lid ? pivot.transform : root.transform, false);
            t.localPosition = p.lid ? p.pos - Hinge : p.pos;
            t.localRotation = Quaternion.Euler(p.rot);
            t.localScale = p.scale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mats[(int)p.slot];
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        var rig = new Rig
        {
            root = root,
            lidPivot = pivot.transform,
            glow = mats[(int)Slot.Glow],
            inner = mats[(int)Slot.Inner],
            emblem = mats[(int)Slot.Emblem],
            glowColour = look[(int)Slot.Glow].colour,
            innerColour = look[(int)Slot.Inner].colour,
            emblemColour = look[(int)Slot.Emblem].colour,
        };

        // A shaft of light that pours out of the open chest. It stands behind
        // the front wall, so only the part above the rim is ever seen.
        rig.beamColour = Color.Lerp(rig.innerColour, Color.white, 0.35f);
        rig.beam = MakeGlowMaterial(rig.beamColour, BeamTexture(), 3000);
        var beam = new GameObject("LightShaft");
        beam.transform.SetParent(root.transform, false);
        beam.transform.localPosition = new Vector3(0f, 0.06f, -0.12f);
        beam.AddComponent<MeshFilter>().sharedMesh = ShaftMesh();
        var br = beam.AddComponent<MeshRenderer>();
        br.sharedMaterial = rig.beam;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        br.receiveShadows = false;
        beam.SetActive(false);
        rig.beamQuad = beam.transform;
        return rig;
    }

    // ------------------------------------------------------------- materials

    static Material MakeMaterial(Finish f)
    {
        if (f.glows) return MakeGlowMaterial(f.colour, null, 2450);

        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        Color c = f.colour;
        c.a = 1f;
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", f.metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", f.smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", f.smooth);
        if (f.hazard)
        {
            Texture2D tex = HazardTexture();
            var tiling = new Vector2(2f, 1f);         // the band is long and thin
            m.mainTexture = tex;
            m.mainTextureScale = tiling;
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", tex);
                m.SetTextureScale("_BaseMap", tiling);
            }
        }
        return m;
    }

    /// <summary>
    /// Unlit, at full colour. Drawn just after the solid parts (queue 2450)
    /// so it is hidden behind them, and before the prize and the light shaft.
    /// </summary>
    static Material MakeGlowMaterial(Color colour, Texture tex, int queue)
    {
        Shader sh = Resources.Load<Shader>("Shaders/FlatIcon");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh);
        if (tex != null) m.mainTexture = tex;
        colour.a = 1f;
        m.color = colour;
        m.renderQueue = queue;
        return m;
    }

    static Texture2D hazardTex, beamTex;
    static Mesh shaftMesh;

    /// <summary>Yellow and black diagonal stripes, with smooth edges.</summary>
    static Texture2D HazardTexture()
    {
        if (hazardTex != null) return hazardTex;
        const int W = 256, H = 64;
        const float Period = 48f;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        var px = new Color32[W * H];
        Color yellow = new Color(1f, 0.76f, 0.10f), black = new Color(0.08f, 0.08f, 0.09f);
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                // distance along the stripe pattern; a stripe is half a period wide
                float u = Mathf.Repeat(x + y * 0.75f, Period);
                float d = Mathf.Min(Mathf.Abs(u - Period * 0.25f), Period - Mathf.Abs(u - Period * 0.25f));
                float k = Mathf.Clamp01(Period * 0.25f - d + 0.5f);
                px[y * W + x] = Color.Lerp(yellow, black, k);
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply(true);
        hazardTex = tex;
        return tex;
    }

    /// <summary>Bright at the bottom, fading upward and towards the sides.</summary>
    static Texture2D BeamTexture()
    {
        if (beamTex != null) return beamTex;
        const int W = 64, H = 128;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
        {
            float up = y / (float)(H - 1);
            for (int x = 0; x < W; x++)
            {
                float across = Mathf.Abs(x / (float)(W - 1) - 0.5f) * 2f;
                float a = Mathf.Pow(1f - up, 1.6f) * Mathf.Pow(Mathf.Clamp01(1f - across), 1.3f);
                px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 200f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply(false, true);
        beamTex = tex;
        return tex;
    }

    /// <summary>A 1 x 1 square standing on its bottom edge, facing +z.</summary>
    static Mesh ShaftMesh()
    {
        if (shaftMesh != null) return shaftMesh;
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        shaftMesh = mesh;
        return mesh;
    }

    // ----------------------------------------------------------------- parts
    // made from the chest designs; edit the design and regenerate rather
    // than nudging single numbers here

    static readonly Part[] CrateParts =
    {
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.05f, 0f), new Vector3(1.28f, 0.04f, 0.76f), new Vector3(0f, 0f, 0f), false),  // floorGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.32f, -0.374f), new Vector3(1.27f, 0.52f, 0.01f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(-0.634f, 0.32f, 0f), new Vector3(0.01f, 0.52f, 0.75f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0.634f, 0.32f, 0f), new Vector3(0.01f, 0.52f, 0.75f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.32f, 0.374f), new Vector3(1.27f, 0.52f, 0.01f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.03f, 0f), new Vector3(1.4f, 0.06f, 0.88f), new Vector3(0f, 0f, 0f), false),  // floor
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.3f, 0.41f), new Vector3(1.4f, 0.6f, 0.06f), new Vector3(0f, 0f, 0f), false),  // front
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.3f, -0.41f), new Vector3(1.4f, 0.6f, 0.06f), new Vector3(0f, 0f, 0f), false),  // back
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(-0.67f, 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.88f), new Vector3(0f, 0f, 0f), false),  // side
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0.67f, 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.88f), new Vector3(0f, 0f, 0f), false),  // side
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.045f, 0f), new Vector3(1.48f, 0.09f, 0.96f), new Vector3(0f, 0f, 0f), false),  // plinth
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.575f, 0.445f), new Vector3(1.44f, 0.05f, 0.05f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.575f, -0.445f), new Vector3(1.44f, 0.05f, 0.05f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.705f, 0.575f, 0f), new Vector3(0.05f, 0.05f, 0.92f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.705f, 0.575f, 0f), new Vector3(0.05f, 0.05f, 0.92f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.32f, -0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.32f, 0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Sphere, Slot.Dark, new Vector3(-0.68f, 0.16f, 0.496f), new Vector3(0.045f, 0.045f, 0.045f), new Vector3(0f, 0f, 0f), false),  // rivet
        new Part(PrimitiveType.Sphere, Slot.Dark, new Vector3(-0.68f, 0.48f, 0.496f), new Vector3(0.045f, 0.045f, 0.045f), new Vector3(0f, 0f, 0f), false),  // rivet
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.32f, -0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.32f, 0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Sphere, Slot.Dark, new Vector3(0.68f, 0.16f, 0.496f), new Vector3(0.045f, 0.045f, 0.045f), new Vector3(0f, 0f, 0f), false),  // rivet
        new Part(PrimitiveType.Sphere, Slot.Dark, new Vector3(0.68f, 0.48f, 0.496f), new Vector3(0.045f, 0.045f, 0.045f), new Vector3(0f, 0f, 0f), false),  // rivet
        new Part(PrimitiveType.Cube, Slot.Stripes, new Vector3(0f, 0.17f, 0.452f), new Vector3(1.08f, 0.13f, 0.03f), new Vector3(0f, 0f, 0f), false),  // hazard
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0f, 0.606f, 0.46f), new Vector3(1.1f, 0.014f, 0.02f), new Vector3(0f, 0f, 0f), false),  // seam
        new Part(PrimitiveType.Cylinder, Slot.Dark, new Vector3(-0.765f, 0.38f, 0f), new Vector3(0.045f, 0.17f, 0.045f), new Vector3(90f, 0f, 0f), false),  // handle
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(-0.735f, 0.38f, -0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(-0.735f, 0.38f, 0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cylinder, Slot.Dark, new Vector3(0.765f, 0.38f, 0f), new Vector3(0.045f, 0.17f, 0.045f), new Vector3(90f, 0f, 0f), false),  // handle
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0.735f, 0.38f, -0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0.735f, 0.38f, 0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cylinder, Slot.Badge, new Vector3(0f, 0.4f, 0.47f), new Vector3(0.34f, 0.02f, 0.34f), new Vector3(90f, 0f, 0f), false),  // badge
        new Part(PrimitiveType.Cylinder, Slot.BadgeCore, new Vector3(0f, 0.4f, 0.482f), new Vector3(0.27f, 0.02f, 0.27f), new Vector3(90f, 0f, 0f), false),  // badgeCore
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(-0.0454f, 0.435f, 0.5f), new Vector3(0.13f, 0.039f, 0.02f), new Vector3(0f, 0f, 38f), false),  // chevron
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(0.0454f, 0.435f, 0.5f), new Vector3(0.13f, 0.039f, 0.02f), new Vector3(0f, 0f, -38f), false),  // chevron
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(-0.0454f, 0.365f, 0.5f), new Vector3(0.13f, 0.039f, 0.02f), new Vector3(0f, 0f, 38f), false),  // chevron
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(0.0454f, 0.365f, 0.5f), new Vector3(0.13f, 0.039f, 0.02f), new Vector3(0f, 0f, -38f), false),  // chevron
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.635f, 0f), new Vector3(1.46f, 0.04f, 0.94f), new Vector3(0f, 0f, 0f), true),  // lidLip
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0f, 0.611f, 0f), new Vector3(1.34f, 0.008f, 0.82f), new Vector3(0f, 0f, 0f), true),  // lidLiner
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.71f, 0f), new Vector3(1.42f, 0.11f, 0.9f), new Vector3(0f, 0f, 0f), true),  // lid
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.8f, 0f), new Vector3(1.18f, 0.07f, 0.64f), new Vector3(0f, 0f, 0f), true),  // lidTop
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.777f, -0.34f), new Vector3(1.18f, 0.05f, 0.12f), new Vector3(-30f, 0f, 0f), true),  // lidBevel
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.777f, 0.34f), new Vector3(1.18f, 0.05f, 0.12f), new Vector3(30f, 0f, 0f), true),  // lidBevel
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.69f, -0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.69f, 0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.69f, -0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.69f, 0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.84f, 0f), new Vector3(0.22f, 0.02f, 0.66f), new Vector3(0f, 0f, 0f), true),  // strap
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.853f, 0f), new Vector3(0.08f, 0.012f, 0.58f), new Vector3(0f, 0f, 0f), true),  // strapLine
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.61f, 0.485f), new Vector3(0.24f, 0.16f, 0.04f), new Vector3(0f, 0f, 0f), true),  // latch
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0f, 0.6f, 0.508f), new Vector3(0.09f, 0.045f, 0.012f), new Vector3(0f, 0f, 0f), true),  // latchLight
    };

    static readonly Part[] VaultParts =
    {
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.05f, 0f), new Vector3(1.28f, 0.04f, 0.76f), new Vector3(0f, 0f, 0f), false),  // floorGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.32f, -0.374f), new Vector3(1.27f, 0.52f, 0.01f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(-0.634f, 0.32f, 0f), new Vector3(0.01f, 0.52f, 0.75f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0.634f, 0.32f, 0f), new Vector3(0.01f, 0.52f, 0.75f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Inner, new Vector3(0f, 0.32f, 0.374f), new Vector3(1.27f, 0.52f, 0.01f), new Vector3(0f, 0f, 0f), false),  // innerGlow
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.03f, 0f), new Vector3(1.4f, 0.06f, 0.88f), new Vector3(0f, 0f, 0f), false),  // floor
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.3f, 0.41f), new Vector3(1.4f, 0.6f, 0.06f), new Vector3(0f, 0f, 0f), false),  // front
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.3f, -0.41f), new Vector3(1.4f, 0.6f, 0.06f), new Vector3(0f, 0f, 0f), false),  // back
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(-0.67f, 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.88f), new Vector3(0f, 0f, 0f), false),  // side
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0.67f, 0.3f, 0f), new Vector3(0.06f, 0.6f, 0.88f), new Vector3(0f, 0f, 0f), false),  // side
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.045f, 0f), new Vector3(1.48f, 0.09f, 0.96f), new Vector3(0f, 0f, 0f), false),  // plinth
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.575f, 0.445f), new Vector3(1.44f, 0.05f, 0.05f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.575f, -0.445f), new Vector3(1.44f, 0.05f, 0.05f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.705f, 0.575f, 0f), new Vector3(0.05f, 0.05f, 0.92f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.705f, 0.575f, 0f), new Vector3(0.05f, 0.05f, 0.92f), new Vector3(0f, 0f, 0f), false),  // rim
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.32f, -0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.32f, 0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(-0.68f, 0.32f, 0.498f), new Vector3(0.03f, 0.46f, 0.012f), new Vector3(0f, 0f, 0f), false),  // neonEdge
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.32f, -0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.32f, 0.42f), new Vector3(0.15f, 0.64f, 0.15f), new Vector3(0f, 0f, 0f), false),  // corner
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0.68f, 0.32f, 0.498f), new Vector3(0.03f, 0.46f, 0.012f), new Vector3(0f, 0f, 0f), false),  // neonEdge
        new Part(PrimitiveType.Cube, Slot.Stripes, new Vector3(0f, 0.13f, 0.45f), new Vector3(1.08f, 0.022f, 0.025f), new Vector3(0f, 0f, 0f), false),  // neon
        new Part(PrimitiveType.Cube, Slot.Stripes, new Vector3(0f, 0.21f, 0.45f), new Vector3(1.08f, 0.022f, 0.025f), new Vector3(0f, 0f, 0f), false),  // neon
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0f, 0.606f, 0.46f), new Vector3(1.1f, 0.014f, 0.02f), new Vector3(0f, 0f, 0f), false),  // seam
        new Part(PrimitiveType.Cylinder, Slot.Dark, new Vector3(-0.765f, 0.38f, 0f), new Vector3(0.045f, 0.17f, 0.045f), new Vector3(90f, 0f, 0f), false),  // handle
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(-0.735f, 0.38f, -0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(-0.735f, 0.38f, 0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cylinder, Slot.Dark, new Vector3(0.765f, 0.38f, 0f), new Vector3(0.045f, 0.17f, 0.045f), new Vector3(90f, 0f, 0f), false),  // handle
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0.735f, 0.38f, -0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0.735f, 0.38f, 0.17f), new Vector3(0.07f, 0.06f, 0.05f), new Vector3(0f, 0f, 0f), false),  // handlePost
        new Part(PrimitiveType.Cube, Slot.Badge, new Vector3(0f, 0.4f, 0.47f), new Vector3(0.3291f, 0.19f, 0.04f), new Vector3(0f, 0f, 0f), false),  // badge
        new Part(PrimitiveType.Cube, Slot.Badge, new Vector3(0f, 0.4f, 0.47f), new Vector3(0.3291f, 0.19f, 0.04f), new Vector3(0f, 0f, 60f), false),  // badge
        new Part(PrimitiveType.Cube, Slot.Badge, new Vector3(0f, 0.4f, 0.47f), new Vector3(0.3291f, 0.19f, 0.04f), new Vector3(0f, 0f, 120f), false),  // badge
        new Part(PrimitiveType.Cube, Slot.BadgeCore, new Vector3(0f, 0.4f, 0.485f), new Vector3(0.2685f, 0.155f, 0.04f), new Vector3(0f, 0f, 0f), false),  // badgeCore
        new Part(PrimitiveType.Cube, Slot.BadgeCore, new Vector3(0f, 0.4f, 0.485f), new Vector3(0.2685f, 0.155f, 0.04f), new Vector3(0f, 0f, 60f), false),  // badgeCore
        new Part(PrimitiveType.Cube, Slot.BadgeCore, new Vector3(0f, 0.4f, 0.485f), new Vector3(0.2685f, 0.155f, 0.04f), new Vector3(0f, 0f, 120f), false),  // badgeCore
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(0f, 0.4f, 0.5f), new Vector3(0.1992f, 0.115f, 0.04f), new Vector3(0f, 0f, 0f), false),  // emblemHex
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(0f, 0.4f, 0.5f), new Vector3(0.1992f, 0.115f, 0.04f), new Vector3(0f, 0f, 60f), false),  // emblemHex
        new Part(PrimitiveType.Cube, Slot.Emblem, new Vector3(0f, 0.4f, 0.5f), new Vector3(0.1992f, 0.115f, 0.04f), new Vector3(0f, 0f, 120f), false),  // emblemHex
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.4f, 0.522f), new Vector3(0.085f, 0.085f, 0.02f), new Vector3(0f, 0f, 45f), false),  // diamond
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.635f, 0f), new Vector3(1.46f, 0.04f, 0.94f), new Vector3(0f, 0f, 0f), true),  // lidLip
        new Part(PrimitiveType.Cube, Slot.Dark, new Vector3(0f, 0.611f, 0f), new Vector3(1.34f, 0.008f, 0.82f), new Vector3(0f, 0f, 0f), true),  // lidLiner
        new Part(PrimitiveType.Cube, Slot.Body, new Vector3(0f, 0.71f, 0f), new Vector3(1.42f, 0.11f, 0.9f), new Vector3(0f, 0f, 0f), true),  // lid
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.8f, 0f), new Vector3(1.18f, 0.07f, 0.64f), new Vector3(0f, 0f, 0f), true),  // lidTop
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.777f, -0.34f), new Vector3(1.18f, 0.05f, 0.12f), new Vector3(-30f, 0f, 0f), true),  // lidBevel
        new Part(PrimitiveType.Cube, Slot.BodyTop, new Vector3(0f, 0.777f, 0.34f), new Vector3(1.18f, 0.05f, 0.12f), new Vector3(30f, 0f, 0f), true),  // lidBevel
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.69f, -0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(-0.68f, 0.69f, 0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.69f, -0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0.68f, 0.69f, 0.42f), new Vector3(0.16f, 0.15f, 0.16f), new Vector3(0f, 0f, 0f), true),  // lidCorner
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.84f, 0f), new Vector3(0.22f, 0.02f, 0.66f), new Vector3(0f, 0f, 0f), true),  // strap
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0f, 0.853f, 0f), new Vector3(0.08f, 0.012f, 0.58f), new Vector3(0f, 0f, 0f), true),  // strapLine
        new Part(PrimitiveType.Cube, Slot.Trim, new Vector3(0f, 0.61f, 0.485f), new Vector3(0.24f, 0.16f, 0.04f), new Vector3(0f, 0f, 0f), true),  // latch
        new Part(PrimitiveType.Cube, Slot.Glow, new Vector3(0f, 0.6f, 0.508f), new Vector3(0.09f, 0.045f, 0.012f), new Vector3(0f, 0f, 0f), true),  // latchLight
    };

    static readonly Finish[] CrateLook =
    {
        new Finish(new Color(0.15f, 0.15f, 0.17f), 0.15f, 0.4f, false, false),  // body
        new Finish(new Color(0.13f, 0.13f, 0.15f), 0.15f, 0.45f, false, false),  // bodyTop
        new Finish(new Color(1f, 0.72f, 0.16f), 0.45f, 0.65f, false, false),  // trim
        new Finish(new Color(0.06f, 0.06f, 0.07f), 0.1f, 0.3f, false, false),  // dark
        new Finish(new Color(1f, 0.58f, 0.1f), 0f, 0.3f, true, false),  // glow
        new Finish(new Color(1f, 0.66f, 0.22f), 0f, 0.3f, true, false),  // inner
        new Finish(new Color(1f, 1f, 1f), 0.05f, 0.35f, false, true),  // stripes
        new Finish(new Color(1f, 0.78f, 0.22f), 0.5f, 0.7f, false, false),  // badge
        new Finish(new Color(0.08f, 0.07f, 0.08f), 0.1f, 0.4f, false, false),  // badgeCore
        new Finish(new Color(1f, 0.62f, 0.12f), 0f, 0.3f, true, false),  // emblem
    };

    static readonly Finish[] VaultLook =
    {
        new Finish(new Color(0.11f, 0.13f, 0.25f), 0.2f, 0.5f, false, false),  // body
        new Finish(new Color(0.1f, 0.13f, 0.3f), 0.2f, 0.55f, false, false),  // bodyTop
        new Finish(new Color(0.78f, 0.82f, 0.92f), 0.5f, 0.75f, false, false),  // trim
        new Finish(new Color(0.04f, 0.05f, 0.1f), 0.1f, 0.35f, false, false),  // dark
        new Finish(new Color(0.2f, 0.85f, 1f), 0f, 0.3f, true, false),  // glow
        new Finish(new Color(0.4f, 0.78f, 1f), 0f, 0.3f, true, false),  // inner
        new Finish(new Color(0.2f, 0.85f, 1f), 0f, 0.3f, true, false),  // stripes
        new Finish(new Color(0.8f, 0.84f, 0.94f), 0.5f, 0.75f, false, false),  // badge
        new Finish(new Color(0.06f, 0.06f, 0.14f), 0.1f, 0.4f, false, false),  // badgeCore
        new Finish(new Color(0.74f, 0.42f, 1f), 0f, 0.3f, true, false),  // emblem
    };
}
