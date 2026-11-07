using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The tyre, built in code to match the tire icon: a chunky black tyre with
/// square tread blocks round it, a red stripe round each sidewall, and a
/// chrome rim with five twin spokes and a red centre cap.
///
/// It is one mesh, made once and shared by every tyre in the game - the
/// rare pickup on the road and the stacks on the shop's tire packs. It is
/// 1 unit across, its axle runs along local X, and it is centred on its
/// origin, so it spins true about any axis through the middle.
/// </summary>
public static class TireModel
{
    /// <summary>How wide the tread is, for a tyre 1 unit across.</summary>
    public const float Width = 0.42f;

    const float Outer = 0.5f;          // radius to the tread
    const float Bead = 0.33f;          // radius where the rubber meets the rim
    const float Half = Width * 0.5f;
    const int Around = 56;             // segments round the tyre

    // the parts, each drawn with its own material
    const int Rubber = 0, Stripe = 1, Chrome = 2, Dark = 3, Cap = 4, PartCount = 5;

#if !TIRE_CHECK
    static Mesh mesh;
    static Material[] materials;
#endif

#if !TIRE_CHECK
    /// <summary>
    /// A tyre <paramref name="size"/> across, centred on
    /// <paramref name="worldPos"/>, its axle along the parent's X.
    /// </summary>
    public static GameObject Create(Transform parent, Vector3 worldPos, float size)
    {
        var go = new GameObject("Tyre");
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = GetMesh();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = GetMaterials();
        return go;
    }

    // ------------------------------------------------------------ materials

    static Material[] GetMaterials()
    {
        if (materials != null && materials[0] != null) return materials;
        materials = new Material[PartCount];
        materials[Rubber] = Lit(new Color(0.11f, 0.11f, 0.13f), 0f, 0.32f);
        materials[Stripe] = Lit(new Color(1f, 0.16f, 0.20f), 0f, 0.55f);
        materials[Chrome] = Lit(new Color(0.90f, 0.92f, 0.97f), 0.4f, 0.75f);
        materials[Dark] = Lit(new Color(0.26f, 0.26f, 0.32f), 0.3f, 0.5f);
        materials[Cap] = Lit(new Color(1f, 0.16f, 0.20f), 0.2f, 0.75f);
        return materials;
    }

    static Material Lit(Color c, float metal, float smooth)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        return m;
    }

    // ------------------------------------------------------------ the mesh

    static Mesh GetMesh()
    {
        if (mesh != null) return mesh;
        var b = new Builder();
        Build(b);
        mesh = new Mesh { name = "Tyre" };
        mesh.SetVertices(b.verts);
        mesh.SetNormals(b.normals);
        mesh.subMeshCount = PartCount;
        for (int i = 0; i < PartCount; i++) mesh.SetTriangles(b.tris[i], i);
        mesh.RecalculateBounds();
        return mesh;
    }

#endif

    /// <summary>Lays the whole tyre out. Public so it can be checked outside the game.</summary>
    public static void Build(Builder b)
    {
        // --- the rubber: a fat ring with rounded shoulders, swept round the axle
        var profile = new List<Vector2>();   // (along the axle, radius)
        const float Shoulder = 0.085f;
        // up one sidewall from the bead, over the tread, and down the other
        profile.Add(new Vector2(-Half, Bead));
        profile.Add(new Vector2(-Half, (Bead + Outer - Shoulder) * 0.5f));
        Arc(profile, new Vector2(-Half + Shoulder, Outer - Shoulder), Shoulder, 180f, 90f, 8);
        Arc(profile, new Vector2(Half - Shoulder, Outer - Shoulder), Shoulder, 90f, 0f, 8);
        profile.Add(new Vector2(Half, (Bead + Outer - Shoulder) * 0.5f));
        profile.Add(new Vector2(Half, Bead));
        b.Lathe(profile, Around, Rubber);

        // --- chunky tread blocks in two staggered rows, like the icon's teeth
        const int Blocks = 26;
        for (int row = -1; row <= 1; row += 2)
        {
            for (int i = 0; i < Blocks; i++)
            {
                float a = (i + (row > 0 ? 0.5f : 0f)) * 360f / Blocks;
                Quaternion q = Quaternion.AngleAxis(a, Vector3.right);
                Vector3 centre = q * new Vector3(row * 0.068f, Outer + 0.01f, 0f);
                b.Box(centre, q, new Vector3(0.118f, 0.036f, 0.07f), Rubber);
            }
        }

        // --- a red stripe round each sidewall
        for (int side = -1; side <= 1; side += 2)
        {
            b.Annulus(side * (Half + 0.002f), 0.376f, 0.41f, side, Stripe);
        }

        // --- the rim, the same on both faces
        const float RimLip = 0.045f;
        float face = Half - 0.045f;               // the rim sits a little inside the rubber
        b.Tube(-face, face, Bead, Dark, true);    // the barrel, seen between the spokes
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * face;
            b.Tube(x, side * Half, Bead, Rubber, true);                          // the bead, down to the rim
            b.Annulus(x, Bead - RimLip, Bead + 0.004f, side, Chrome);           // the lip
            b.Annulus(side * 0.05f, 0f, Bead, side, Dark);                       // the dish behind the spokes
            // five twin spokes from the hub to the lip
            for (int k = 0; k < 5; k++)
            {
                for (int twin = -1; twin <= 1; twin += 2)
                {
                    float a = 90f + k * 72f + twin * 9f;
                    Quaternion q = Quaternion.AngleAxis(a, Vector3.right);
                    float r0 = 0.085f, r1 = Bead - RimLip * 0.5f;
                    float depth = (face - 0.05f) * 0.5f;
                    Vector3 c = q * new Vector3(side * (0.05f + depth), (r0 + r1) * 0.5f, 0f);
                    b.Box(c, q, new Vector3(depth * 2f, r1 - r0, 0.05f), Chrome);
                }
            }
            b.Disc(side * (face + 0.004f), 0.1f, side, Chrome);                  // the hub
            b.Tube(side * 0.05f, side * (face + 0.004f), 0.1f, Chrome, false);
            b.Tube(side * (face + 0.004f), side * (face + 0.03f), 0.055f, Cap, false); // the cap
            b.Disc(side * (face + 0.03f), 0.055f, side, Cap);
        }
    }

    /// <summary>Points round an arc, in degrees (0 = +axle, 90 = outwards).</summary>
    static void Arc(List<Vector2> pts, Vector2 centre, float r, float from, float to, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
            pts.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
    }

    // ------------------------------------------------------------ building blocks

    public class Builder
    {
        public readonly List<Vector3> verts = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<int>[] tris;

        public Builder()
        {
            tris = new List<int>[PartCount];
            for (int i = 0; i < PartCount; i++) tris[i] = new List<int>();
        }

        int Add(Vector3 p, Vector3 n)
        {
            verts.Add(p);
            normals.Add(n);
            return verts.Count - 1;
        }

        /// <summary>A triangle, turned to face along <paramref name="outward"/>.</summary>
        void Tri(int part, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            // Unity's front faces wind clockwise as seen from the front
            if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
            tris[part].Add(a);
            tris[part].Add(b);
            tris[part].Add(c);
        }

        /// <summary>
        /// A section swept round the X axis. Points are (along the axle,
        /// radius) and run so the outside of the section is on their left.
        /// </summary>
        public void Lathe(List<Vector2> section, int around, int part)
        {
            int n = section.Count;
            // smooth normals along the section, from its neighbours
            var sectionNormal = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = section[Mathf.Max(0, i - 1)], next = section[Mathf.Min(n - 1, i + 1)];
                Vector2 t = (next - prev).normalized;
                Vector2 nn = new Vector2(t.y, -t.x);     // to the right of travel
                // outwards is away from the middle of the section
                Vector2 mid = new Vector2(0f, (Outer + Bead) * 0.5f);
                if (Vector2.Dot(nn, section[i] - mid) < 0f) nn = -nn;
                sectionNormal[i] = nn;
            }
            int start = verts.Count;
            for (int j = 0; j <= around; j++)
            {
                Quaternion q = Quaternion.AngleAxis(j * 360f / around, Vector3.right);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = q * new Vector3(section[i].x, section[i].y, 0f);
                    Vector3 nm = q * new Vector3(sectionNormal[i].x, sectionNormal[i].y, 0f);
                    Add(p, nm.normalized);
                }
            }
            for (int j = 0; j < around; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = start + j * n + i, b2 = a + 1, c = a + n, d = c + 1;
                    Vector3 outward = (normals[a] + normals[d]).normalized;
                    Tri(part, a, b2, d, outward);
                    Tri(part, a, d, c, outward);
                }
            }
        }

        /// <summary>A flat ring across the axle at <paramref name="x"/>, facing +X or -X.</summary>
        public void Annulus(float x, float r0, float r1, int facing, int part)
        {
            Vector3 n = new Vector3(facing, 0f, 0f);
            int start = verts.Count;
            for (int j = 0; j <= Around; j++)
            {
                Quaternion q = Quaternion.AngleAxis(j * 360f / Around, Vector3.right);
                Add(q * new Vector3(x, r0, 0f), n);
                Add(q * new Vector3(x, r1, 0f), n);
            }
            for (int j = 0; j < Around; j++)
            {
                int a = start + j * 2, b2 = a + 1, c = a + 2, d = a + 3;
                Tri(part, a, b2, d, n);
                Tri(part, a, d, c, n);
            }
        }

        /// <summary>A flat disc across the axle at <paramref name="x"/>.</summary>
        public void Disc(float x, float r, int facing, int part)
        {
            Annulus(x, 0f, r, facing, part);
        }

        /// <summary>A plain cylinder wall round the axle, facing out (or in).</summary>
        public void Tube(float x0, float x1, float r, int part, bool inward)
        {
            int start = verts.Count;
            for (int j = 0; j <= Around; j++)
            {
                Quaternion q = Quaternion.AngleAxis(j * 360f / Around, Vector3.right);
                Vector3 n = q * new Vector3(0f, inward ? -1f : 1f, 0f);
                Add(q * new Vector3(x0, r, 0f), n);
                Add(q * new Vector3(x1, r, 0f), n);
            }
            for (int j = 0; j < Around; j++)
            {
                int a = start + j * 2, b2 = a + 1, c = a + 2, d = a + 3;
                Vector3 n = normals[a];
                Tri(part, a, b2, d, n);
                Tri(part, a, d, c, n);
            }
        }

        /// <summary>A box: centre, turn, and full size along its own axes.</summary>
        public void Box(Vector3 centre, Quaternion q, Vector3 size, int part)
        {
            Vector3 h = size * 0.5f;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 n = Vector3.zero;
                    n[axis] = s;
                    Vector3 u = Vector3.zero, v = Vector3.zero;
                    u[(axis + 1) % 3] = 1f;
                    v[(axis + 2) % 3] = 1f;
                    Vector3 c = Vector3.Scale(n, h);
                    Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                    Vector3 wn = q * n;
                    int a = Add(centre + q * (c - du - dv), wn);
                    int b2 = Add(centre + q * (c + du - dv), wn);
                    int c2 = Add(centre + q * (c + du + dv), wn);
                    int d = Add(centre + q * (c - du + dv), wn);
                    Tri(part, a, b2, c2, wn);
                    Tri(part, a, c2, d, wn);
                }
            }
        }
    }
}
