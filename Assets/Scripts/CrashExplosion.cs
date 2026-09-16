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
        if (fireMat == null)
        {
            fireMat = Lit(new Color(1f, 0.55f, 0.12f), 3.2f);
            emberMat = Lit(new Color(1f, 0.82f, 0.25f), 4f);
            smokeMat = Lit(new Color(0.16f, 0.15f, 0.16f), 0f);
            debrisMat = Lit(new Color(0.09f, 0.09f, 0.11f), 0f);
        }

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

    void Update()
    {
        // unscaled: the crash pauses the game underneath it
        float dt = Time.unscaledDeltaTime;
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
