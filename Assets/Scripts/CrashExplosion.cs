using UnityEngine;

/// <summary>
/// The fireball when a run ends. Built from plain geometry rather than a
/// particle system on purpose: a ParticleSystem needs a material, and a
/// material found by shader name at runtime is the one thing that reliably
/// works in the editor and comes back null on the phone. Spheres and cubes
/// with the world's own lit shader cannot strip.
///
/// Self-destructs when it has burned out.
/// </summary>
public class CrashExplosion : MonoBehaviour
{
    const float Life = 1.6f;

    struct Piece
    {
        public Transform t;
        public Vector3 velocity;
        public Vector3 spin;
        public float startScale;
    }

    readonly System.Collections.Generic.List<Piece> pieces =
        new System.Collections.Generic.List<Piece>();

    Transform flash;
    float flashScale;
    float t;

    static Material fireMat, emberMat, smokeMat, debrisMat;

    public static void Play(Vector3 position, float size = 1f)
    {
        var go = new GameObject("CrashExplosion");
        go.transform.position = position;
        go.AddComponent<CrashExplosion>().Build(size);
    }

    /// <summary>
    /// The wreck left burning after the blast: flames licking over the body
    /// and smoke rolling up off it, until <see cref="ClearAll"/> (or a minute
    /// and a half, whichever comes first). <paramref name="rotation"/> is the
    /// car's, so the fire is spread along its length.
    /// </summary>
    public static void Burn(Vector3 position, Quaternion rotation, float size = 1f)
    {
        var go = new GameObject("WreckFire");
        go.transform.SetPositionAndRotation(position, rotation);
        go.AddComponent<CrashExplosion>().BuildFire(size);
    }

    /// <summary>Puts out every blast and fire - leaving the results, or a revive.</summary>
    public static void ClearAll()
    {
        foreach (var blast in FindObjectsByType<CrashExplosion>())
        {
            Destroy(blast.gameObject);
        }
    }

    static void EnsureMaterials()
    {
        if (fireMat != null) return;
        fireMat = Lit(new Color(1f, 0.55f, 0.12f), 3.2f);
        emberMat = Lit(new Color(1f, 0.82f, 0.25f), 4f);
        smokeMat = Lit(new Color(0.16f, 0.15f, 0.16f), 0f);
        debrisMat = Lit(new Color(0.09f, 0.09f, 0.11f), 0f);
    }

    static Material Lit(Color colour, float emission)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh) { color = colour };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        if (emission > 0f && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", colour * emission);
        }
        return m;
    }

    void Build(float size)
    {
        EnsureMaterials();

        // the flash: one bright ball that swells and is gone
        flash = MakePart(PrimitiveType.Sphere, emberMat,
                         Vector3.zero, 0.9f * size).transform;
        flashScale = 0.9f * size;

        // fire, thrown mostly upward and outward
        for (int i = 0; i < 7; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), Random.Range(0.6f, 1.5f), Mathf.Sin(a));
            GameObject g = MakePart(PrimitiveType.Sphere, fireMat,
                Vector3.up * 0.3f + dir * Random.Range(0.1f, 0.5f) * size,
                Random.Range(0.5f, 1.1f) * size);
            pieces.Add(new Piece
            {
                t = g.transform,
                velocity = dir * Random.Range(3.5f, 7f) * size,
                spin = Random.insideUnitSphere * 90f,
                startScale = g.transform.localScale.x,
            });
        }

        // smoke, slower and longer lived
        for (int i = 0; i < 5; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a) * 0.6f, Random.Range(0.8f, 1.6f),
                                      Mathf.Sin(a) * 0.6f);
            GameObject g = MakePart(PrimitiveType.Sphere, smokeMat,
                Vector3.up * 0.5f, Random.Range(0.8f, 1.6f) * size);
            pieces.Add(new Piece
            {
                t = g.transform,
                velocity = dir * Random.Range(1.5f, 3f) * size,
                spin = Random.insideUnitSphere * 40f,
                startScale = g.transform.localScale.x,
            });
        }

        // and a few bits of the car
        for (int i = 0; i < 9; i++)
        {
            Vector3 dir = Random.insideUnitSphere.normalized;
            dir.y = Mathf.Abs(dir.y) + 0.4f;
            GameObject g = MakePart(PrimitiveType.Cube, debrisMat,
                Vector3.up * 0.4f, Random.Range(0.12f, 0.3f) * size);
            pieces.Add(new Piece
            {
                t = g.transform,
                velocity = dir * Random.Range(5f, 11f) * size,
                spin = Random.insideUnitSphere * 720f,
                startScale = g.transform.localScale.x,
            });
        }
    }

    GameObject MakePart(PrimitiveType prim, Material mat, Vector3 localPos, float scale)
    {
        GameObject g = GameObject.CreatePrimitive(prim);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Random.rotation;
        g.transform.localScale = Vector3.one * scale;
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // ------------------------------------------------------- the wreck fire

    const float BurnLife = 90f;
    const float PuffLife = 2.2f;
    const float PuffEvery = 0.2f;

    bool burning;
    float fireSize, nextPuff;
    Transform[] flames;
    Vector3[] flameHome;
    float[] flameScale;
    Transform[] puffs;
    float[] puffAge, puffSize;
    Vector3[] puffDrift;

    void BuildFire(float size)
    {
        EnsureMaterials();
        burning = true;
        fireSize = size;

        // a cluster of flames over the length of the body, the hottest in
        // the middle
        const int FlameCount = 6;
        flames = new Transform[FlameCount];
        flameHome = new Vector3[FlameCount];
        flameScale = new float[FlameCount];
        for (int i = 0; i < FlameCount; i++)
        {
            var home = new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(0.05f, 0.4f),
                                   Mathf.Lerp(-0.9f, 0.9f, i / (float)(FlameCount - 1))) * size;
            float scale = Random.Range(0.4f, 0.65f) * size;
            flames[i] = MakePart(PrimitiveType.Sphere, i % 3 == 1 ? emberMat : fireMat,
                                 home, scale).transform;
            flames[i].localRotation = Quaternion.identity;   // stretched upward, not any old way
            flames[i].localScale = Vector3.zero;     // they catch, rather than appear
            flameHome[i] = home;
            flameScale[i] = scale;
        }

        // the smoke is a small pool of puffs, used over and over
        const int PuffCount = 12;
        puffs = new Transform[PuffCount];
        puffAge = new float[PuffCount];
        puffSize = new float[PuffCount];
        puffDrift = new Vector3[PuffCount];
        for (int i = 0; i < PuffCount; i++)
        {
            puffs[i] = MakePart(PrimitiveType.Sphere, smokeMat, Vector3.zero, 1f).transform;
            puffs[i].gameObject.SetActive(false);
            puffAge[i] = -1f;
        }
    }

    void TickFire(float dt)
    {
        t += dt;
        float caught = Mathf.Clamp01(t / 0.6f);
        float dying = Mathf.Clamp01((t - (BurnLife - 3f)) / 3f);
        float strength = caught * (1f - dying);

        // flames flicker in size and lick upward, each to its own rhythm
        for (int i = 0; i < flames.Length; i++)
        {
            float n = Mathf.PerlinNoise(t * 3.1f + i * 7.3f, i * 1.7f);
            float s = flameScale[i] * strength * (0.6f + 0.8f * n);
            flames[i].localScale = new Vector3(s, s * (1.15f + 0.6f * n), s);
            flames[i].localPosition = flameHome[i] + Vector3.up * (0.15f * n * fireSize);
        }

        // smoke rolls up off it, growing as it rises and then thinning away
        Vector3 up = transform.InverseTransformDirection(Vector3.up);
        nextPuff -= dt;
        if (nextPuff <= 0f && strength > 0.2f)
        {
            nextPuff = PuffEvery;
            for (int i = 0; i < puffs.Length; i++)
            {
                if (puffAge[i] >= 0f) continue;
                puffAge[i] = 0f;
                puffSize[i] = Random.Range(0.45f, 0.75f) * fireSize;
                puffDrift[i] = new Vector3(Random.Range(-0.35f, 0.35f), 0f,
                                           Random.Range(-0.35f, 0.35f));
                puffs[i].localPosition = flameHome[Random.Range(0, flameHome.Length)]
                                         + up * (0.4f * fireSize);
                puffs[i].localRotation = Random.rotation;
                puffs[i].gameObject.SetActive(true);
                break;
            }
        }
        for (int i = 0; i < puffs.Length; i++)
        {
            if (puffAge[i] < 0f) continue;
            puffAge[i] += dt;
            float a = puffAge[i] / PuffLife;
            if (a >= 1f)
            {
                puffAge[i] = -1f;
                puffs[i].gameObject.SetActive(false);
                continue;
            }
            puffs[i].localPosition += (up * 1.5f + puffDrift[i]) * fireSize * dt;
            float s = puffSize[i] * (0.6f + 1.6f * a) * (1f - a * a);
            puffs[i].localScale = Vector3.one * Mathf.Max(0.001f, s);
        }

        if (t >= BurnLife) Destroy(gameObject);
    }

    void Update()
    {
        // unscaled: the crash pauses the game underneath it
        float dt = Time.unscaledDeltaTime;
        if (burning)
        {
            TickFire(dt);
            return;
        }
        t += dt;
        float p = Mathf.Clamp01(t / Life);

        if (flash != null)
        {
            // swells fast, then collapses to nothing - no transparency needed
            float f = Mathf.Clamp01(t / 0.12f);
            float shrink = Mathf.Clamp01((t - 0.12f) / 0.22f);
            flash.localScale = Vector3.one * flashScale * (0.4f + 2.6f * f) * (1f - shrink);
        }

        for (int i = 0; i < pieces.Count; i++)
        {
            Piece pc = pieces[i];
            if (pc.t == null) continue;

            pc.velocity += Vector3.down * 9.8f * dt;
            pc.velocity *= 1f - Mathf.Clamp01(1.6f * dt);     // air drag
            pc.t.localPosition += pc.velocity * dt;
            pc.t.Rotate(pc.spin * dt, Space.Self);

            // everything shrinks away rather than fading, so no transparent
            // material is needed and nothing can pop when it is destroyed
            float scale = pc.startScale * (1f - p * p);
            pc.t.localScale = Vector3.one * Mathf.Max(0.001f, scale);

            pieces[i] = pc;
        }

        if (t >= Life) Destroy(gameObject);
    }
}
