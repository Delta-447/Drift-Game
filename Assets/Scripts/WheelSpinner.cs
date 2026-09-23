using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rolls a car model's wheels at road speed and steers the front pair. Works
/// on any model with no per-car setup: wheels are found by name where the
/// artist named them, and by shape and position where they did not. Each
/// wheel's axle and radius are measured from its mesh.
/// </summary>
public class WheelSpinner : MonoBehaviour
{
    class Wheel
    {
        public Transform t;
        public Quaternion baseRot;
        public Vector3 basePos;
        /// <summary>
        /// Pivot to the wheel's true centre, before rotation. Many models put
        /// the pivot at the origin rather than the hub, and spinning about the
        /// wrong point is what makes a wheel look buckled.
        /// </summary>
        public Vector3 centreOffset;
        public Vector3 axle;       // spin axis, in the wheel's local space
        public Vector3 steerAxis;  // vertical axis, in the wheel's local space
        public float radius;
        public float angle;
        public bool front;
        /// <summary>Every mesh that turns with this wheel - used to find where it touches the road.</summary>
        public List<Renderer> parts;
    }

    /// <summary>
    /// A mesh measured as a cylinder: where its middle is, how big it is and
    /// which way its axle points. Taken from the mesh's OWN box, not the
    /// world-space box Unity reports - that one is the mesh's box turned and
    /// re-boxed, so a tyre whose mesh sits even slightly rotated about its
    /// axle reports a bottom well below the real one, and a car seated on it
    /// hangs in the air.
    /// </summary>
    struct Disc
    {
        public Vector3 centre;     // world
        public float radius;       // world metres
        public float halfWidth;    // world metres
        public Vector3 axle;       // world direction
        public bool round;         // two equal sides and a thinner third
    }

    /// <summary>A wheel as found, before anything is moved.</summary>
    class Found
    {
        public Transform spin;              // the named object that turns, or null for a group
        public List<Renderer> parts = new List<Renderer>();
        public Disc disc;                   // the biggest round piece: the tyre
    }

    static readonly string[] WheelWords = { "wheel", "tire", "tyre", "rim" };

    readonly List<Wheel> wheels = new List<Wheel>();

    /// <summary>Metres per second the car is travelling. Set every frame.</summary>
    public float speed;

    /// <summary>Degrees the front wheels are turned. Set every frame.</summary>
    public float steerAngle;

    public float steerSmoothing = 12f;
    float shownSteer;

    /// <summary>Straightens the wheels and stops them, e.g. back in the menu.</summary>
    public void ResetWheels()
    {
        speed = 0f;
        steerAngle = 0f;
        shownSteer = 0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            Wheel w = wheels[i];
            if (w.t == null) continue;
            w.angle = 0f;
            w.t.localRotation = w.baseRot;
            w.t.localPosition = w.basePos;
        }
    }

    public int WheelCount { get { return wheels.Count; } }

    /// <summary>
    /// Finds and measures the wheels now rather than on the first frame. Call
    /// it while the car is square to the road - the axles are read from the
    /// car's orientation at that moment.
    /// </summary>
    public void Prepare()
    {
        if (!setupDone) Setup();
    }

    /// <summary>
    /// Where the REAR wheels actually sit, relative to the car's rig: how far
    /// out to the side, and how far back. Skid marks are drawn from these, so
    /// they line up with the tyres on every model instead of being guessed
    /// from the bodywork's proportions.
    /// </summary>
    public bool TryGetRearWheelOffsets(Transform reference, out float halfWidth, out float back)
    {
        halfWidth = 0f;
        back = 0f;
        if (!setupDone) Setup();
        if (wheels.Count == 0 || reference == null) return false;

        // collect every wheel's position in the car's own space
        var local = new List<Vector3>(wheels.Count);
        for (int i = 0; i < wheels.Count; i++)
        {
            Bounds wb;
            if (!WheelBounds(wheels[i].t, out wb)) continue;
            local.Add(reference.InverseTransformPoint(wb.center));
        }
        if (local.Count == 0) return false;

        // the rear pair is everything behind the middle of the wheelbase
        float midZ = 0f;
        for (int i = 0; i < local.Count; i++) midZ += local[i].z;
        midZ /= local.Count;

        int n = 0;
        float sumX = 0f, sumZ = 0f;
        for (int i = 0; i < local.Count; i++)
        {
            if (local[i].z > midZ) continue;
            sumX += Mathf.Abs(local[i].x);
            sumZ += local[i].z;
            n++;
        }
        if (n == 0) return false;

        halfWidth = sumX / n;
        back = -(sumZ / n);          // positive = behind the car's centre
        return true;
    }

    /// <summary>
    /// Lowest point of the wheels' world-space boxes. Only the hover car still
    /// seats itself on this; everything else uses TryGetContactHeight, which
    /// is exact.
    /// </summary>
    public bool TryGetWheelBottom(out float y)
    {
        y = 0f;
        if (!setupDone) Setup();
        if (wheels.Count == 0) return false;

        bool any = false;
        float lowest = float.MaxValue;
        for (int i = 0; i < wheels.Count; i++)
        {
            Bounds wb;
            if (!WheelBounds(wheels[i].t, out wb)) continue;
            lowest = Mathf.Min(lowest, wb.min.y);
            any = true;
        }
        if (!any) return false;
        y = lowest;
        return true;
    }

    /// <summary>
    /// Where the tyres touch the ground, as a height in <paramref name="frame"/>'s
    /// own space. Each tyre is measured as the cylinder it is, so the answer
    /// is the same however far the wheels have rolled or the frame is tilted
    /// - and it is exact, where the world-space boxes can be tens of
    /// centimetres out.
    /// </summary>
    public bool TryGetContactHeight(Transform frame, out float y)
    {
        y = 0f;
        if (frame == null) return false;
        if (!setupDone) Setup();
        if (wheels.Count == 0) return false;

        bool any = false;
        float lowest = float.MaxValue;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].parts == null) continue;
            if (LowestContact(wheels[i].parts, frame, out float c))
            {
                lowest = Mathf.Min(lowest, c);
                any = true;
            }
        }
        if (!any) return false;
        y = lowest;
        return true;
    }

    /// <summary>
    /// The same measurement for a model that has no spinner yet (rivals and
    /// the garage cars are seated before they get one). Nothing is moved.
    /// </summary>
    public static bool MeasureContactHeight(GameObject model, Transform reference, Transform frame, out float y)
    {
        y = 0f;
        if (model == null || frame == null) return false;
        if (reference == null) reference = model.transform;

        List<Found> found = Find(model.transform, reference);
        if (found.Count == 0) return false;

        bool any = false;
        float lowest = float.MaxValue;
        for (int i = 0; i < found.Count; i++)
        {
            if (LowestContact(found[i].parts, frame, out float c))
            {
                lowest = Mathf.Min(lowest, c);
                any = true;
            }
        }
        if (!any) return false;
        y = lowest;
        return true;
    }

    /// <summary>Lowest point of a wheel's round pieces (all of them, if none is round).</summary>
    static bool LowestContact(List<Renderer> parts, Transform frame, out float y)
    {
        y = float.MaxValue;
        bool anyRound = false;
        for (int pass = 0; pass < 2 && !anyRound; pass++)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (!MeasureDisc(parts[i], out Disc d)) continue;
                if (pass == 0 && !d.round) continue;
                y = Mathf.Min(y, ContactIn(d, frame));
                anyRound = true;
            }
        }
        return anyRound;
    }

    /// <summary>
    /// The lowest point of a cylinder, measured along the frame's down: the
    /// full radius when the axle is level, the half-width when it stands up.
    /// </summary>
    static float ContactIn(Disc d, Transform frame)
    {
        Vector3 c = frame.InverseTransformPoint(d.centre);
        Vector3 a = frame.InverseTransformDirection(d.axle);
        a = a.sqrMagnitude > 1e-8f ? a.normalized : Vector3.right;
        float s = Mathf.Abs(frame.lossyScale.y);
        if (s < 1e-6f) s = 1f;
        float ay = Mathf.Abs(a.y);
        return c.y - (d.radius * Mathf.Sqrt(Mathf.Max(0f, 1f - ay * ay)) + d.halfWidth * ay) / s;
    }

    static bool MeasureDisc(Renderer r, out Disc d)
    {
        d = new Disc();
        if (r == null) return false;

        Bounds lb = r.localBounds;
        Transform t = r.transform;
        float lx = t.TransformVector(new Vector3(lb.extents.x, 0f, 0f)).magnitude;
        float ly = t.TransformVector(new Vector3(0f, lb.extents.y, 0f)).magnitude;
        float lz = t.TransformVector(new Vector3(0f, 0f, lb.extents.z)).magnitude;

        float thin, mid, big;
        Vector3 thinDir;
        if (lx <= ly && lx <= lz)      { thin = lx; thinDir = t.right;   mid = Mathf.Min(ly, lz); big = Mathf.Max(ly, lz); }
        else if (ly <= lx && ly <= lz) { thin = ly; thinDir = t.up;      mid = Mathf.Min(lx, lz); big = Mathf.Max(lx, lz); }
        else                           { thin = lz; thinDir = t.forward; mid = Mathf.Min(lx, ly); big = Mathf.Max(lx, ly); }
        if (big <= 1e-5f) return false;

        d.centre = t.TransformPoint(lb.center);
        d.radius = big;
        d.halfWidth = thin;
        d.axle = thinDir;
        // a tyre, rim or brake disc: round, and clearly narrower than it is tall
        d.round = mid >= big * 0.85f && thin <= big * 0.75f;
        return true;
    }

    /// <summary>
    /// A wheel's real extent. It has to look at CHILDREN, not just the object
    /// itself: a wheel built from several pieces is held on a pivot with no
    /// mesh of its own, and asking that pivot for a Renderer returns nothing.
    /// </summary>
    static bool WheelBounds(Transform t, out Bounds b)
    {
        b = new Bounds();
        if (t == null) return false;

        var rends = t.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return false;

        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return true;
    }

    Transform setupReference;
    bool setupDone;

    /// <summary>
    /// Attaches a spinner. The wheels are not measured yet: a car model is
    /// still being rotated and seated by its owner at this point, and reading
    /// the axes early is what made wheels spin sideways. The owner calls
    /// Prepare once the car is square, or it happens on the first frame.
    /// </summary>
    public static WheelSpinner Attach(GameObject model, Transform reference)
    {
        if (model == null) return null;

        // The same GameObject can be handed to Attach more than once: swapped
        // models are fresh instances, but the starter car is the one object in
        // the scene and it gets re-attached every time it is re-equipped.
        // A leftover spinner keeps writing the wheel rotations every frame
        // with its own stale steering angle, and whichever of the two runs
        // last that frame wins - which looks exactly like the steering has
        // jammed. Clear out any previous ones first.
        var stale = model.GetComponents<WheelSpinner>();
        for (int i = 0; i < stale.Length; i++)
        {
            if (stale[i] == null) continue;
            stale[i].ResetWheels();     // put the wheels back before letting go
            stale[i].enabled = false;   // Destroy is deferred; stop it now
            Destroy(stale[i]);
        }

        var spinner = model.AddComponent<WheelSpinner>();
        spinner.setupReference = reference != null ? reference : model.transform;
        return spinner;
    }

    void Setup()
    {
        setupDone = true;
        Transform reff = setupReference != null ? setupReference : transform;
        Bounds body = WorldBounds(transform);

        List<Found> found = Find(transform, reff);
        for (int i = 0; i < found.Count; i++)
        {
            if (found[i].spin != null) AddWheel(found[i], reff, body);
            else AddWheelGroup(found[i], reff, body);
        }

        MatchRadii();
    }

    // ------------------------------------------------------------ finding them

    /// <summary>
    /// Named wheels first. Fewer than four means the naming only covered some
    /// of them - which is worse than none, because the car ends up with two
    /// wheels turning and two welded solid - so it starts over by shape. And
    /// if THAT finds nothing usable, whatever the names gave is better than no
    /// wheels at all.
    /// </summary>
    static List<Found> Find(Transform root, Transform reference)
    {
        List<Found> named = FindByName(root, reference);
        if (named.Count >= 4) return named;

        List<Found> shaped = FindByShape(root, reference);
        if (shaped.Count >= 2) return shaped;

        return named;
    }

    static List<Found> FindByName(Transform root, Transform reference)
    {
        var result = new List<Found>();
        Bounds body = WorldBounds(root);
        var all = root.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == root || !IsWheelName(t.name)) continue;

            var rend = t.GetComponent<Renderer>();
            if (rend == null) continue;              // a bare group node
            if (HasWheelAncestor(t, root)) continue; // a rim inside a wheel
            // a correctly named part can still be all four wheels merged into
            // one mesh - spinning that swings them around the car's middle
            if (!IsSingleWheel(rend.bounds, body, reference)) continue;

            var f = new Found { spin = t };
            f.parts.AddRange(t.GetComponentsInChildren<Renderer>(true));
            MeasureDisc(rend, out f.disc);
            result.Add(f);
        }
        return result;
    }

    /// <summary>
    /// For models whose parts have meaningless names - and most of them do:
    /// the packs are full of wheels called Circle.014 and Cylinder.003.
    ///
    /// The previous version grouped anything low, off to the side and near an
    /// end of the car. That also caught wheel-arch panels, exhausts and bits
    /// of sill, which then span round with the wheels.
    ///
    /// This one starts from TYRES: a mesh that is round, narrower than it is
    /// tall, with its axle across the car, sitting low near a corner. Each
    /// tyre then collects only the pieces that sit on its own axle, inside its
    /// own circle - the rim, the brake disc, the hubcap. A fender is bigger
    /// than the tyre and off its centre; an exhaust is not round and not on
    /// an axle; so neither can join.
    /// </summary>
    static List<Found> FindByShape(Transform root, Transform reference)
    {
        var result = new List<Found>();
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return result;

        // the car's box in the reference frame, built from every part's own
        // box so it does not depend on which way the car is pointing
        Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 mx = -mn;
        for (int i = 0; i < rends.Length; i++)
        {
            Bounds lb = rends[i].localBounds;
            Transform t = rends[i].transform;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? lb.min.x : lb.max.x,
                    (c & 2) == 0 ? lb.min.y : lb.max.y,
                    (c & 4) == 0 ? lb.min.z : lb.max.z);
                Vector3 p = reference.InverseTransformPoint(t.TransformPoint(corner));
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }
        }
        Vector3 size = mx - mn;
        Vector3 centre = (mn + mx) * 0.5f;

        // across the car is its narrower horizontal side
        bool acrossIsX = size.x <= size.z;
        float length = acrossIsX ? size.z : size.x;
        if (length <= 0.01f) return result;
        Vector3 acrossWorld = acrossIsX ? reference.right : reference.forward;
        float refScale = Mathf.Abs(reference.lossyScale.x);
        if (refScale < 1e-6f) refScale = 1f;

        var discs = new Disc[rends.Length];
        var measured = new bool[rends.Length];
        var seeds = new List<int>();
        for (int i = 0; i < rends.Length; i++)
        {
            measured[i] = MeasureDisc(rends[i], out discs[i]);
            if (!measured[i]) continue;
            Disc d = discs[i];
            if (!d.round) continue;
            if (Mathf.Abs(Vector3.Dot(d.axle, acrossWorld)) < 0.8f) continue;  // axle across the car

            Vector3 local = reference.InverseTransformPoint(d.centre) - centre;
            float across = acrossIsX ? local.x : local.z;
            float along = acrossIsX ? local.z : local.x;
            float diameter = 2f * d.radius / refScale;

            if (local.y > 0f) continue;                              // upper body
            if (Mathf.Abs(across) < length * 0.10f) continue;        // centreline
            if (Mathf.Abs(along) < length * 0.10f) continue;         // mid-flank
            if (diameter < length * 0.07f) continue;                 // a bolt
            if (diameter > length * 0.40f) continue;                 // a whole panel
            seeds.Add(i);
        }

        // biggest first, so a tyre claims its own rim rather than the reverse
        seeds.Sort((a, b) => discs[b].radius.CompareTo(discs[a].radius));

        var used = new bool[rends.Length];
        for (int s = 0; s < seeds.Count; s++)
        {
            int si = seeds[s];
            if (used[si]) continue;
            Disc tyre = discs[si];

            var f = new Found { disc = tyre };
            f.parts.Add(rends[si]);
            used[si] = true;

            for (int j = 0; j < rends.Length; j++)
            {
                if (used[j] || !measured[j]) continue;
                if (discs[j].radius > tyre.radius * 1.05f) continue;    // bigger than the tyre

                Vector3 dv = discs[j].centre - tyre.centre;
                float onAxle = Vector3.Dot(dv, tyre.axle);
                float offAxle = (dv - tyre.axle * onAxle).magnitude;
                if (offAxle > tyre.radius * 0.3f) continue;             // not on this axle
                if (Mathf.Abs(onAxle) > tyre.radius * 0.8f) continue;   // further along the axle than a wheel is wide

                f.parts.Add(rends[j]);
                used[j] = true;
            }
            result.Add(f);
        }

        // Only wheels that reach the ground. A spare on the tailgate is round
        // and has its axle across the car too.
        if (result.Count > 0)
        {
            float lowest = float.MaxValue, biggest = 0f;
            var contact = new float[result.Count];
            for (int i = 0; i < result.Count; i++)
            {
                contact[i] = ContactIn(result[i].disc, reference);
                lowest = Mathf.Min(lowest, contact[i]);
                biggest = Mathf.Max(biggest, result[i].disc.radius / refScale);
            }
            for (int i = result.Count - 1; i >= 0; i--)
            {
                if (contact[i] > lowest + biggest * 0.35f) result.RemoveAt(i);
            }
        }
        return result;
    }

    /// <summary>
    /// Turns a tyre and the pieces on its axle into one wheel by hanging them
    /// off a pivot at the hub. Spinning the pivot turns tyre, rim and disc
    /// together about the true centre, whatever the pieces' own origins are.
    /// </summary>
    void AddWheelGroup(Found f, Transform reference, Bounds body)
    {
        if (f.parts.Count == 0) return;

        // Never drag bodywork along: a piece whose children include meshes
        // that are not part of this wheel stays where it is.
        var mine = new HashSet<Renderer>(f.parts);
        var moving = new List<Renderer>();
        for (int i = 0; i < f.parts.Count; i++)
        {
            bool clean = true;
            var kids = f.parts[i].GetComponentsInChildren<Renderer>(true);
            for (int k = 0; k < kids.Length; k++)
            {
                if (!mine.Contains(kids[k])) { clean = false; break; }
            }
            if (clean) moving.Add(f.parts[i]);
        }
        if (moving.Count == 0) return;

        Transform parent = moving[0].transform.parent;
        if (parent == null) parent = transform;

        var pivotGo = new GameObject("WheelPivot");
        pivotGo.transform.SetParent(parent, false);
        pivotGo.transform.position = f.disc.centre;
        pivotGo.transform.rotation = parent.rotation;

        for (int i = 0; i < moving.Count; i++)
        {
            moving[i].transform.SetParent(pivotGo.transform, true);
        }

        Transform t = pivotGo.transform;
        // the tyre's own axle, pointing the same way as the car's right so
        // every wheel rolls in the same direction
        Vector3 axleWorld = f.disc.axle;
        if (Vector3.Dot(axleWorld, reference.right) < 0f) axleWorld = -axleWorld;
        float zLocal = reference.InverseTransformPoint(f.disc.centre).z
                     - reference.InverseTransformPoint(body.center).z;

        wheels.Add(new Wheel
        {
            t = t,
            baseRot = t.localRotation,
            basePos = t.localPosition,
            // the pivot is already AT the hub, so nothing to correct for
            centreOffset = Vector3.zero,
            axle = LocalAxis(t, axleWorld),
            steerAxis = LocalAxis(t, reference.up),
            radius = Mathf.Clamp(f.disc.radius, 0.15f, 0.65f),
            front = zLocal > 0f,
            parts = moving,
        });
    }

    /// <summary>
    /// True if these bounds could be one wheel: small next to the car, sitting
    /// low, clearly to one side of the centreline, and not spanning both sides.
    /// </summary>
    static bool IsSingleWheel(Bounds b, Bounds body, Transform reference)
    {
        float bodyLength = Mathf.Max(body.size.x, body.size.z);
        if (bodyLength <= 0.01f) return true;

        Vector3 local = reference.InverseTransformPoint(b.center)
                      - reference.InverseTransformPoint(body.center);

        float big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (big > bodyLength * 0.34f) return false;      // too big to be a wheel
        if (big < bodyLength * 0.07f) return false;      // a bolt or a badge

        // a merged set of wheels sits on the centreline; a real one is off to
        // one side. Measured against length, since the world box's axes follow
        // whichever way the car happens to be pointing.
        if (Mathf.Abs(local.x) < bodyLength * 0.09f) return false;
        if (local.y > 0f) return false;                  // wheels live down low
        return true;
    }

    void AddWheel(Found f, Transform reference, Bounds body)
    {
        Transform t = f.spin;
        Renderer rend = t.GetComponent<Renderer>();
        Bounds b = rend.bounds;

        // The axle is the car's own sideways direction, expressed in the
        // wheel's local space. Snapping to the nearest local axis is what made
        // wheels wobble: models whose wheel objects are a few degrees off
        // ended up spinning about a tilted axis.
        Vector3 axle = LocalAxis(t, reference.right);
        Vector3 steerAxis = LocalAxis(t, reference.up);

        // The wheel's radius, from the mesh's own box. The world box is
        // inflated whenever the mesh is turned about its axle, which made
        // those wheels roll too slowly for the road going past.
        float radius = f.disc.radius > 0f
            ? f.disc.radius
            : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) * 0.5f;

        // front wheels are the ones ahead of the middle of the car
        float zLocal = reference.InverseTransformPoint(b.center).z
                     - reference.InverseTransformPoint(body.center).z;

        // offset from the pivot to the middle of the wheel, expressed before
        // any rotation is applied, so the spin can be made to happen about the
        // hub instead of wherever the pivot happens to sit
        Vector3 offset = Vector3.zero;
        if (t.parent != null)
        {
            Vector3 centreInParent = t.parent.InverseTransformPoint(b.center);
            offset = Quaternion.Inverse(t.localRotation) * (centreInParent - t.localPosition);
        }

        wheels.Add(new Wheel
        {
            t = t,
            baseRot = t.localRotation,
            basePos = t.localPosition,
            centreOffset = offset,
            axle = axle,
            steerAxis = steerAxis,
            radius = Mathf.Clamp(radius, 0.15f, 0.65f),
            front = zLocal > 0f,
            parts = f.parts,
        });
    }

    /// <summary>
    /// A world direction written in the wheel's own local space, so rotating
    /// about it turns the wheel about that true world axis.
    /// </summary>
    static Vector3 LocalAxis(Transform t, Vector3 worldDir)
    {
        Vector3 local = t.InverseTransformDirection(worldDir);
        return local.sqrMagnitude < 0.0001f ? Vector3.right : local.normalized;
    }

    static Bounds WorldBounds(Transform root)
    {
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    /// <summary>
    /// Models often split a wheel into a tyre and a rim. They are concentric,
    /// so they must turn at one rate - the smaller rim would otherwise spin
    /// faster than the tyre around it.
    /// </summary>
    void MatchRadii()
    {
        float biggest = 0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i].radius > biggest) biggest = wheels[i].radius;
        }
        if (biggest <= 0f) return;
        for (int i = 0; i < wheels.Count; i++) wheels[i].radius = biggest;
    }

    static bool IsWheelName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("steering")) return false;   // not the one in the cabin
        for (int i = 0; i < WheelWords.Length; i++)
        {
            if (n.Contains(WheelWords[i])) return true;
        }
        return false;
    }

    static bool HasWheelAncestor(Transform t, Transform root)
    {
        for (Transform p = t.parent; p != null && p != root; p = p.parent)
        {
            if (IsWheelName(p.name) && p.GetComponent<Renderer>() != null) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ motion

    void LateUpdate()
    {
        if (!setupDone) Setup();
        if (wheels.Count == 0) return;

        float dt = Time.deltaTime;
        // the front wheels ease into the turn rather than snapping to it
        shownSteer = dt > 0f
            ? Mathf.Lerp(shownSteer, steerAngle, Mathf.Clamp01(steerSmoothing * dt))
            : steerAngle;

        for (int i = 0; i < wheels.Count; i++)
        {
            Wheel w = wheels[i];
            if (w.t == null) continue;

            // rolling without slipping: one turn per circumference travelled
            w.angle += speed / w.radius * Mathf.Rad2Deg * dt;
            if (w.angle > 360f) w.angle -= 360f;
            else if (w.angle < -360f) w.angle += 360f;

            // steer first, then roll about the now-turned axle
            Quaternion spin = Quaternion.AngleAxis(w.angle, w.axle);
            if (w.front)
            {
                spin = Quaternion.AngleAxis(shownSteer, w.steerAxis) * spin;
            }

            w.t.localRotation = w.baseRot * spin;
            // keep the hub still: undo however far the rotation moved it
            w.t.localPosition = w.basePos
                + w.baseRot * (w.centreOffset - spin * w.centreOffset);
        }
    }
}
