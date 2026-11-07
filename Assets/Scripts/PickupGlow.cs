using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a pickup on the road easier to spot, more so the rarer it is.
/// Coins get nothing. Power-ups get a soft halo in their own colour behind
/// them. Tyres get a bigger halo and a column of light rising from the road.
/// Mystery boxes get the lot, bigger and brighter: halo, a tall gold column,
/// rings rippling out across the road and sparkles circling them.
///
/// The light is additive (Resources/Shaders/Glow) and ignores fog, so it
/// carries a long way down the road, by day and by night. Everything hangs
/// off the pickup and goes when it is collected or left behind.
/// </summary>
public class PickupGlow : MonoBehaviour
{
    public enum Tier { Item, Tire, Box }

    struct Look
    {
        public float halo, haloAlpha, beamWidth, beamHeight, beamAlpha, bob;
        public bool rings, sparkles;
        public Color colour;
    }

    static Look LookFor(Tier tier, Color colour)
    {
        switch (tier)
        {
            case Tier.Item:
                return new Look { halo = 2.5f, haloAlpha = 0.8f, colour = colour, bob = 0.10f };
            case Tier.Tire:
                return new Look { halo = 3.0f, haloAlpha = 0.9f, beamWidth = 1.0f, beamHeight = 7f,
                                  beamAlpha = 0.55f, colour = colour, bob = 0.14f };
            default:
                return new Look { halo = 4.0f, haloAlpha = 1f, beamWidth = 1.5f, beamHeight = 11f,
                                  beamAlpha = 0.7f, colour = colour, bob = 0.16f,
                                  rings = true, sparkles = true };
        }
    }

    /// <summary>How far above the road the pickups float.</summary>
    const float Height = 1.2f;

    Look look;
    Transform halo, beam;
    Renderer haloR, beamR;
    readonly List<Transform> rings = new List<Transform>();
    readonly List<Renderer> ringR = new List<Renderer>();
    readonly List<Transform> sparks = new List<Transform>();
    readonly List<Renderer> sparkR = new List<Renderer>();
    readonly List<Material> owned = new List<Material>();
    MaterialPropertyBlock block;
    Vector3 home;
    float phase;
    static Camera cam;

    /// <summary>Puts the glow on a pickup that floats <see cref="Height"/> above the road.</summary>
    public static PickupGlow Add(Transform pickup, Tier tier, Color? colour = null)
    {
        Color c = colour ?? (tier == Tier.Box ? new Color(1f, 0.78f, 0.25f)
                           : tier == Tier.Tire ? new Color(0.55f, 0.85f, 1f) : Color.white);
        var glow = pickup.gameObject.AddComponent<PickupGlow>();
        glow.Build(LookFor(tier, c));
        return glow;
    }

    /// <summary>The pickup made its own materials (a mystery box does): they go when it does.</summary>
    public void OwnMaterials(GameObject model)
    {
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var m in r.sharedMaterials)
                if (m != null && !owned.Contains(m)) owned.Add(m);
        }
    }

    void Build(Look l)
    {
        look = l;
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;
        home = transform.localPosition;

        halo = MakeQuad("Halo", GlowTexture(), out haloR);
        if (l.beamWidth > 0f) beam = MakeQuad("Beam", BeamTexture(), out beamR);
        if (l.rings)
        {
            for (int i = 0; i < 2; i++)
            {
                Renderer r;
                rings.Add(MakeQuad("Ring", RingTexture(), out r));
                ringR.Add(r);
            }
        }
        if (l.sparkles)
        {
            for (int i = 0; i < 5; i++)
            {
                Renderer r;
                sparks.Add(MakeQuad("Sparkle", SparkTexture(), out r));
                sparkR.Add(r);
            }
        }
        LateUpdate();
    }

    Transform MakeQuad(string name, Texture2D tex, out Renderer r)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = MaterialFor(tex);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        r = mr;
        return go.transform;
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        float t = Time.time + phase;

        // a gentle float up and down
        transform.localPosition = home + Vector3.up * (Mathf.Sin(t * 2.4f) * look.bob);
        Vector3 centre = transform.position;
        Vector3 road = centre + Vector3.down * Height;

        Vector3 view = cam != null ? cam.transform.forward : Vector3.forward;
        Vector3 flat = new Vector3(view.x, 0f, view.z);
        if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;
        Quaternion facing = Quaternion.LookRotation(view);
        Quaternion upright = Quaternion.LookRotation(flat.normalized);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 3.2f);

        // the halo, just behind the pickup so the pickup sits in front of it
        halo.SetPositionAndRotation(centre + view * 0.7f, facing);
        halo.localScale = Vector3.one * (look.halo * (0.92f + 0.12f * pulse));
        Tint(haloR, look.colour, look.haloAlpha * (0.75f + 0.25f * pulse));

        // the column of light, standing on the road and turning to face you
        if (beam != null)
        {
            beam.SetPositionAndRotation(road + Vector3.up * (look.beamHeight * 0.5f) + view * 0.4f, upright);
            beam.localScale = new Vector3(look.beamWidth * (0.9f + 0.15f * pulse), look.beamHeight, 1f);
            Tint(beamR, look.colour, look.beamAlpha * (0.8f + 0.2f * pulse));
        }

        // rings rippling out across the road, one after the other
        for (int i = 0; i < rings.Count; i++)
        {
            float k = Mathf.Repeat(t * 0.8f + i * 0.5f, 1f);
            rings[i].SetPositionAndRotation(road + Vector3.up * 0.06f, Quaternion.Euler(90f, 0f, 0f));
            rings[i].localScale = Vector3.one * Mathf.Lerp(1.2f, 4.2f, k);
            Tint(ringR[i], look.colour, 0.85f * (1f - k) * Mathf.Clamp01(k * 6f));
        }

        // sparkles circling, each winking in turn
        for (int i = 0; i < sparks.Count; i++)
        {
            float a = t * 1.6f + i * (Mathf.PI * 2f / sparks.Count);
            Vector3 around = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.25f
                           + Vector3.up * (Mathf.Sin(t * 2.1f + i * 1.9f) * 0.55f + 0.1f);
            sparks[i].SetPositionAndRotation(centre + around, facing);
            float wink = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * 5f + i * 2.3f), 3f);
            sparks[i].localScale = Vector3.one * (0.25f + 0.35f * wink);
            Tint(sparkR[i], Color.Lerp(look.colour, Color.white, 0.6f), 0.4f + 0.6f * wink);
        }
    }

    void Tint(Renderer r, Color c, float alpha)
    {
        c.a = alpha;
        block.SetColor(TintId, c);
        r.SetPropertyBlock(block);
    }

    void OnDestroy()
    {
        foreach (var m in owned) if (m != null) Destroy(m);
        owned.Clear();
    }

    // --------------------------------------------------------- shared assets

    static readonly int TintId = Shader.PropertyToID("_Color");
    static Mesh quad;
    static readonly Dictionary<Texture2D, Material> materials = new Dictionary<Texture2D, Material>();
    static Texture2D glowTex, beamTex, ringTex, sparkTex;

    static Material MaterialFor(Texture2D tex)
    {
        Material m;
        if (materials.TryGetValue(tex, out m) && m != null) return m;
        Shader sh = Resources.Load<Shader>("Shaders/Glow");
        if (sh == null) sh = Resources.Load<Shader>("Shaders/FlatIcon");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        m = new Material(sh) { mainTexture = tex };
        m.renderQueue = 3100;
        materials[tex] = m;
        return m;
    }

    /// <summary>A 1 x 1 square on its own XY plane, centred.</summary>
    static Mesh QuadMesh()
    {
        if (quad != null) return quad;
        quad = new Mesh { name = "GlowQuad" };
        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
        };
        quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quad.RecalculateBounds();
        // drawn whatever the angle, so never culled for sitting at the edge
        quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        return quad;
    }

    delegate float Shape(float u, float v);

    static Texture2D Paint(int w, int h, Shape shape)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float a = Mathf.Clamp01(shape((x + 0.5f) / w, (y + 0.5f) / h));
                px[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        return tex;
    }

    /// <summary>A soft round glow with a brighter heart.</summary>
    static Texture2D GlowTexture()
    {
        if (glowTex != null) return glowTex;
        glowTex = Paint(128, 128, (u, v) =>
        {
            float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float edge = Mathf.Clamp01(1f - r);
            return edge * edge * 0.75f + Mathf.Pow(edge, 6f) * 0.5f;
        });
        return glowTex;
    }

    /// <summary>A column of light: bright at its foot, fading up into nothing, soft at the sides.</summary>
    static Texture2D BeamTexture()
    {
        if (beamTex != null) return beamTex;
        beamTex = Paint(64, 256, (u, v) =>
        {
            float across = (u - 0.5f) * 2f;
            float side = Mathf.Exp(-across * across * 4.5f);
            float up = Mathf.Pow(1f - v, 1.6f) * Mathf.Clamp01(v * 30f);
            return side * up;
        });
        return beamTex;
    }

    /// <summary>A thin bright ring with a faint fill inside.</summary>
    static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        ringTex = Paint(128, 128, (u, v) =>
        {
            float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
            float d = (r - 0.86f) / 0.07f;
            return Mathf.Exp(-d * d) + (r < 0.86f ? 0.12f * r : 0f);
        });
        return ringTex;
    }

    /// <summary>A four-pointed glint.</summary>
    static Texture2D SparkTexture()
    {
        if (sparkTex != null) return sparkTex;
        sparkTex = Paint(64, 64, (u, v) =>
        {
            float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
            float arms = Mathf.Max(Mathf.Clamp01(1f - x * 7f - y), Mathf.Clamp01(1f - y * 7f - x));
            float core = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) * 2.2f);
            return Mathf.Max(arms, core);
        });
        return sparkTex;
    }
}
