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
    /// Lowest point of the wheels in world space - where the car actually
    /// touches the road, which is far more reliable than the lowest point of
    /// the whole model.
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
    /// A wheel's real extent. It has to look at CHILDREN, not just the object
    /// itself: a wheel built from several pieces is held on a pivot with no
    /// mesh of its own, and asking that pivot for a Renderer returns nothing.
    /// That is what left cars hanging in the air - the seating code asked
    /// where the wheels were, got no answer at all, and gave up.
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
    /// the axes early is what made wheels spin sideways. It resolves itself on
    /// the first frame, once everything has settled.
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

        Collect(transform, reff);

        // Fewer than four means the naming only covered some of them - which is
        // worse than none, because the car ends up with two wheels turning and
        // two welded solid. Start over by position in that case.
        if (wheels.Count < 4)
        {
            wheels.Clear();
            CollectByShape(transform, reff);
        }

        // and if THAT found nothing usable, fall back to whatever the names
        // gave us rather than leaving the car with no wheels at all
        if (wheels.Count < 2)
        {
            wheels.Clear();
            Collect(transform, reff);
        }

        MatchRadii();
    }

    // ------------------------------------------------------------ finding them

    void Collect(Transform root, Transform reference)
    {
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

            AddWheel(t, rend, reference, body);
        }
    }

    /// <summary>
    /// Fallback for models whose parts have meaningless names - and most of
    /// them do: the packs are full of wheels called Circle.014 and Torus.003.
    ///
    /// The old version tested each mesh for being disc-shaped, which failed
    /// wherever a wheel is built from several pieces. A rim on its own is a
    /// thin plate, a brake disc is thinner still, a tyre may be a torus in
    /// three parts - each piece fails a "looks like a wheel" test even though
    /// together they obviously are one.
    ///
    /// So this works by POSITION instead. Anything low down, out to one side
    /// and toward an end of the car is a candidate; candidates sitting on top
    /// of each other are one wheel; each group gets a pivot at its centre and
    /// turns as a unit. That fixes rims that spun separately from their tyres
    /// at the same time, because now they cannot.
    /// </summary>
    void CollectByShape(Transform root, Transform reference)
    {
        Bounds body = WorldBounds(root);
        if (body.size == Vector3.zero) return;

        float bodyLength = Mathf.Max(body.size.x, body.size.z);
        if (bodyLength <= 0.01f) return;
        Vector3 bodyLocal = reference.InverseTransformPoint(body.center);

        var rends = root.GetComponentsInChildren<Renderer>(true);
        var parts = new List<Renderer>();
        var centres = new List<Vector3>();

        for (int i = 0; i < rends.Length; i++)
        {
            Bounds b = rends[i].bounds;
            float big = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (big <= 0.0001f) continue;

            Vector3 local = reference.InverseTransformPoint(b.center) - bodyLocal;

            // Where a wheel is, rather than what it looks like.
            if (local.y > 0f) continue;                              // upper body
            if (Mathf.Abs(local.x) < bodyLength * 0.10f) continue;   // centreline
            if (Mathf.Abs(local.z) < bodyLength * 0.10f) continue;   // mid-flank
            if (big > bodyLength * 0.34f) continue;                  // a whole panel
            if (big < bodyLength * 0.05f) continue;                  // a bolt

            parts.Add(rends[i]);
            centres.Add(b.center);
        }

        // group the pieces that sit on top of one another
        float joinDistance = bodyLength * 0.16f;
        var taken = new bool[parts.Count];

        for (int i = 0; i < parts.Count; i++)
        {
            if (taken[i]) continue;

            var group = new List<Renderer> { parts[i] };
            taken[i] = true;
            Bounds cluster = parts[i].bounds;

            // keep absorbing anything close to what the group covers so far
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (taken[j]) continue;
                    if (Vector3.Distance(centres[j], cluster.center) > joinDistance) continue;
                    taken[j] = true;
                    group.Add(parts[j]);
                    cluster.Encapsulate(parts[j].bounds);
                    grew = true;
                }
            }

            AddWheelGroup(group, cluster, reference, body);
        }
    }

    /// <summary>
    /// Turns a clump of meshes into one wheel by hanging them off a pivot at
    /// the middle of the clump. Spinning the pivot turns tyre, rim and disc
    /// together, about the true centre, whatever the individual pieces'
    /// own origins happen to be.
    /// </summary>
    void AddWheelGroup(List<Renderer> group, Bounds cluster, Transform reference, Bounds body)
    {
        if (group.Count == 0) return;

        Transform parent = group[0].transform.parent;
        if (parent == null) parent = transform;

        var pivotGo = new GameObject("WheelPivot");
        pivotGo.transform.SetParent(parent, false);
        pivotGo.transform.position = cluster.center;
        pivotGo.transform.rotation = parent.rotation;

        for (int i = 0; i < group.Count; i++)
        {
            group[i].transform.SetParent(pivotGo.transform, true);
        }

        Transform t = pivotGo.transform;
        float across = Mathf.Max(cluster.size.x, Mathf.Max(cluster.size.y, cluster.size.z));
        float zLocal = reference.InverseTransformPoint(cluster.center).z
                     - reference.InverseTransformPoint(body.center).z;

        wheels.Add(new Wheel
        {
            t = t,
            baseRot = t.localRotation,
            basePos = t.localPosition,
            // the pivot is already AT the centre, so nothing to correct for
            centreOffset = Vector3.zero,
            axle = LocalAxis(t, reference.right),
            steerAxis = LocalAxis(t, reference.up),
            radius = Mathf.Clamp(across * 0.5f, 0.15f, 0.65f),
            front = zLocal > 0f,
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

    void AddWheel(Transform t, Renderer rend, Transform reference, Bounds body)
    {
        Bounds b = rend.bounds;

        // The axle is the car's own sideways direction, expressed in the
        // wheel's local space. Snapping to the nearest local axis is what made
        // wheels wobble: models whose wheel objects are a few degrees off
        // ended up spinning about a tilted axis.
        Vector3 axle = LocalAxis(t, reference.right);
        Vector3 steerAxis = LocalAxis(t, reference.up);

        // roughly the wheel's diameter: the widest it measures across
        float across = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

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
            radius = Mathf.Clamp(across * 0.5f, 0.15f, 0.65f),
            front = zLocal > 0f
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
