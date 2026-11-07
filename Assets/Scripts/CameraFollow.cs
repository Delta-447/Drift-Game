using UnityEngine;

/// <summary>
/// Chase camera: sits behind the car along the track direction, smooths its
/// motion, widens FOV with speed and shakes on crash.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;

    [Header("Position")]
    public float distanceBehind = 7.5f;
    public float height = 3.6f;
    public float positionSmoothing = 6f;

    [Header("Look")]
    public float lookHeight = 1.2f;
    public float lookAhead = 5f;

    [Header("Corner anticipation")]
    [Tooltip("How far along the track the camera peeks (meters).")]
    public float cornerLookAhead = 26f;
    [Tooltip("0 = look straight ahead, 1 = look fully at the track ahead.")]
    [Range(0f, 1f)] public float lookAnticipation = 0.6f;
    [Tooltip("How much the camera swings to see through upcoming corners.")]
    [Range(0f, 1f)] public float positionAnticipation = 0.4f;

    [Header("FOV")]
    public float baseFov = 58f;
    public float maxFovBoost = 16f;

    [Header("Drift feel")]
    [Tooltip("Camera rolls with hard slides - makes drifting feel physical.")]
    public float driftRoll = 4.5f;

    [Header("Start showcase")]
    [Tooltip("Seconds the opening camera move takes.")]
    public float introDuration = 3.6f;

    [Header("Lobby showcase")]
    [Tooltip("Where the camera sits behind the menu: the front three-quarter " +
             "view the opening move starts from.")]
    public float showcaseAngle = 165f;
    public float showcaseRadius = 7.5f;
    public float showcaseHeight = 1.5f;

    /// <summary>
    /// How far below the car the lobby camera aims, in metres - which lifts
    /// the car up the screen. The menu sets it, so the car sits between its
    /// title and its buttons; anything else leaves it at 0. The camera eases
    /// to a new value rather than jumping.
    /// </summary>
    [System.NonSerialized] public float lobbyLift;
    float liftNow, introLift;

    Camera cam;
    CarController car;
    float shake;
    float introT = -1f;
    bool introSnap, introSnapRot;
    float introRoll, lastIntroAngle;
    bool showcase;

    /// <summary>
    /// Holds the camera at the opening move's starting pose. The menu uses it
    /// so the car is already framed and rolling, and pressing play carries on
    /// from exactly where the lobby left off.
    /// </summary>
    public void SetShowcase(bool on)
    {
        showcase = on;
        wreck = false;
        if (on) introT = -1f;
    }

    [Header("Wreck shot")]
    [Tooltip("Degrees a second the camera circles the wreck after a crash.")]
    public float wreckOrbitSpeed = 7f;
    [Tooltip("How far from the wreck the camera settles, and how high above it.")]
    public float wreckRadius = 11f;
    public float wreckHeight = 3.6f;

    bool wreck;
    float wreckT, wreckAngle, wreckRadius0, wreckHeight0;
    Vector3 wreckFocus;

    /// <summary>
    /// The shot after a crash. From wherever the chase camera was, it is
    /// thrown up and out by the blast so the whole fireball is in view, then
    /// circles slowly round the burnt-out car - which it keeps a little above
    /// the middle of the screen, between the headline and the results.
    /// Anything that moves the camera on (the menu, a new run) turns it off.
    /// </summary>
    public void SetWreckShot(bool on)
    {
        wreck = on && target != null;
        if (!wreck) return;
        introT = -1f;
        showcase = false;
        wreckT = 0f;
        // the car has stopped dead, so the point it was at is all that matters
        wreckFocus = car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f;
        Vector3 off = transform.position - wreckFocus;
        wreckHeight0 = off.y;
        off.y = 0f;
        if (off.sqrMagnitude < 0.01f) off = -target.forward * distanceBehind;
        wreckRadius0 = off.magnitude;
        wreckAngle = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
    }

    void ApplyWreckShot(float dt)
    {
        wreckT += dt;

        // Thrown up and out to a set distance by the blast, easing off
        // quickly. Set, not "further back": at speed the chase camera trails
        // well behind the car, and from there the wreck would be tiny.
        float back = 1f - Mathf.Exp(-2.4f * wreckT);
        // then the slow circle, gathering speed gently
        float orbit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wreckT - 0.5f) / 1.5f));
        wreckAngle += wreckOrbitSpeed * orbit * dt;

        float radius = Mathf.Lerp(wreckRadius0, wreckRadius, back);
        float rise = Mathf.Lerp(wreckHeight0, wreckHeight, back);
        Vector3 pos = wreckFocus + Quaternion.Euler(0f, wreckAngle, 0f) * Vector3.forward * radius
                      + Vector3.up * rise;
        transform.position = pos;

        // aimed a little below the car, so the car sits above the middle of
        // the screen, with a slight lean to match the headline
        Vector3 aimAt = wreckFocus + Vector3.down * (1.3f * back);
        Quaternion look = Quaternion.LookRotation(aimAt - pos, Vector3.up)
                          * Quaternion.Euler(0f, 0f, -3f * back);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-5f * dt));

        if (shake > 0.001f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, 1.4f * dt);
        }

        if (cam != null)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 52f, 1f - Mathf.Exp(-3f * dt));
        }
    }

    /// <summary>
    /// Shifts what the showcase camera is framing, in world space. The garage
    /// uses it to slide from the car being replaced onto the one overtaking it.
    /// </summary>
    public Vector3 aimShift;

    void ApplyShowcase(float dt)
    {
        Vector3 fwd = target.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 offset = Quaternion.AngleAxis(showcaseAngle, Vector3.up)
                         * (-fwd) * showcaseRadius;
        // a slow drift up and down keeps it alive while the menu sits there
        float bob = Mathf.Sin(Time.unscaledTime * 0.6f) * 0.18f;
        Vector3 pos = target.position + aimShift + offset
                    + Vector3.up * (showcaseHeight + bob);

        // rigidly locked to the car: any lag here shows up as the car sliding
        // around the frame, and any jitter shows up as camera shake
        transform.position = pos;

        // Aim at the middle of the bodywork, exactly - smoothing the rotation
        // here would leave the car trailing off-centre, because it is moving
        // the whole time.
        liftNow = Mathf.Lerp(liftNow, lobbyLift, 1f - Mathf.Exp(-3f * dt));
        Vector3 aimAt = (car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f)
                        + aimShift - Vector3.up * liftNow;
        transform.rotation = Quaternion.LookRotation(aimAt - transform.position, Vector3.up);

        if (cam != null)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 40f, 1f - Mathf.Exp(-4f * dt));
        }
    }

    // ------------------------------------------------------ the opening title

    float titleShotT = -1f;
    int titleShotFrame;

    /// <summary>
    /// Drives the camera for the opening title (TitleIntro), which passes its
    /// own clock in every frame, so a skip moves the camera along with it.
    /// It only takes over while the lobby showcase is on, and it ends on the
    /// showcase pose exactly, so when the intro stops calling the lobby simply
    /// carries on from there.
    /// </summary>
    public void TitleShot(float t)
    {
        titleShotT = t;
        titleShotFrame = Time.frameCount;
    }

    public void EndTitleShot()
    {
        titleShotT = -1f;
    }

    // still being driven - if the intro vanishes without saying, it lets go
    bool TitleShotLive
    {
        get { return titleShotT >= 0f && Time.frameCount - titleShotFrame <= 2; }
    }

    /// <summary>
    /// One framing of the car on the showcase orbit: angle from directly
    /// behind it (positive round its left side, and not wrapped at 180, so a
    /// move always goes the way it is written), distance, height, lens, and
    /// where the camera aims - above the middle of the car and ahead of it.
    /// </summary>
    struct ShotPose
    {
        public float angle, radius, height, fov, aimUp, aimAhead, roll;

        public ShotPose(float angle, float radius, float height, float fov,
                        float aimUp, float aimAhead, float roll)
        {
            this.angle = angle;
            this.radius = radius;
            this.height = height;
            this.fov = fov;
            this.aimUp = aimUp;
            this.aimAhead = aimAhead;
            this.roll = roll;
        }

        public static ShotPose Lerp(ShotPose a, ShotPose b, float k)
        {
            return new ShotPose(
                Mathf.Lerp(a.angle, b.angle, k), Mathf.Lerp(a.radius, b.radius, k),
                Mathf.Lerp(a.height, b.height, k), Mathf.Lerp(a.fov, b.fov, k),
                Mathf.Lerp(a.aimUp, b.aimUp, k), Mathf.Lerp(a.aimAhead, b.aimAhead, k),
                Mathf.Lerp(a.roll, b.roll, k));
        }
    }

    // The intro's shots. The phone is held upright, so the car is framed down
    // its length - from behind, then from the nose - where it fills the
    // screen; side on it would not fit. Low, but never under 0.8 m: the road
    // climbs and dips (up to about 5%), and the camera must stay above it.
    // Behind the car, low, looking past it down the road:
    static readonly ShotPose TailWide = new ShotPose(8f, 5.4f, 0.85f, 54f, 0.35f, 3f, -4f);
    // creeping in on the back wheels, the horizon tipping the other way:
    static readonly ShotPose TailClose = new ShotPose(-6f, 4.7f, 0.80f, 50f, 0.30f, 2.5f, 3f);
    // the whip round the right-hand side lands on the nose:
    static readonly ShotPose NoseLow = new ShotPose(-168f, 6.0f, 0.85f, 46f, 0.45f, 0f, 0f);
    // and eases back while the name is up, car low in the frame under it:
    static readonly ShotPose NoseHero = new ShotPose(-178f, 7.0f, 1.00f, 44f, 0.50f, 0f, 0f);

    void ApplyTitleShot(float dt)
    {
        float t = titleShotT;

        // the lobby's own framing, which the whole move ends on
        float bob = Mathf.Sin(Time.unscaledTime * 0.6f) * 0.18f;
        // (aimed below the car by the lobby's lift, so the hand-over is exact)
        var lobby = new ShotPose(showcaseAngle - 360f, showcaseRadius, showcaseHeight + bob,
                                 40f, -lobbyLift, 0f, 0f);
        liftNow = lobbyLift;

        ShotPose pose;
        if (t < TitleIntro.WhipStart)
        {
            pose = ShotPose.Lerp(TailWide, TailClose, SmoothStep01(t / TitleIntro.WhipStart));
        }
        else if (t < TitleIntro.WhipEnd)
        {
            // fast through the middle; it swings in close past the flank and
            // banks into the turn, which is what sells the whip
            float k = (t - TitleIntro.WhipStart) / (TitleIntro.WhipEnd - TitleIntro.WhipStart);
            pose = ShotPose.Lerp(TailClose, NoseLow, EaseInOutCubic(k));
            pose.radius -= Mathf.Sin(k * Mathf.PI) * 0.6f;
            pose.roll -= Mathf.Sin(k * Mathf.PI) * 6f;
        }
        else if (t < TitleIntro.SettleStart)
        {
            float k = (t - TitleIntro.WhipEnd) / (TitleIntro.SettleStart - TitleIntro.WhipEnd);
            pose = ShotPose.Lerp(NoseLow, NoseHero, 1f - Mathf.Pow(1f - Mathf.Clamp01(k), 2f));
        }
        else
        {
            float k = (t - TitleIntro.SettleStart) / (TitleIntro.MoveEnd - TitleIntro.SettleStart);
            pose = ShotPose.Lerp(NoseHero, lobby, EaseInOutCubic(k));
        }
        PlaceOnOrbit(pose, dt);
    }

    void PlaceOnOrbit(ShotPose pose, float dt)
    {
        Vector3 fwd = target.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        // rigidly locked to the car, like the showcase, so it never lags
        Vector3 offset = Quaternion.AngleAxis(pose.angle, Vector3.up) * (-fwd) * pose.radius;
        Vector3 pos = target.position + aimShift + offset + Vector3.up * pose.height;
        Vector3 centre = car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f;
        Vector3 aimAt = centre + aimShift + Vector3.up * pose.aimUp + fwd * pose.aimAhead;

        if (shake > 0.001f)
        {
            pos += Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, 1.4f * dt);
        }
        transform.position = pos;
        transform.rotation = Quaternion.LookRotation(aimAt - pos, Vector3.up)
                             * Quaternion.Euler(0f, 0f, pose.roll);
        if (cam != null) cam.fieldOfView = pose.fov;
    }

    static float SmoothStep01(float p)
    {
        p = Mathf.Clamp01(p);
        return p * p * (3f - 2f * p);
    }

    static float EaseInOutCubic(float p)
    {
        p = Mathf.Clamp01(p);
        return p < 0.5f ? 4f * p * p * p : 1f - Mathf.Pow(-2f * p + 2f, 3f) / 2f;
    }

    /// <summary>
    /// Runs the opening sweep: starts low and ahead of the car looking back at
    /// it, orbits around to the outside, then settles into the chase position
    /// exactly as the countdown ends.
    /// </summary>
    public void PlayIntro(float duration)
    {
        introDuration = Mathf.Max(0.5f, duration);
        introT = 0f;
        // coming from the lobby the camera is already in place, so carry on
        // from there instead of snapping
        introSnap = !showcase;
        introSnapRot = !showcase;
        // the lobby's lift is let go of gradually through the move
        introLift = showcase ? liftNow : 0f;
        liftNow = 0f;
        showcase = false;
        wreck = false;
        introRoll = 0f;
        lastIntroAngle = showcaseAngle;
    }

    public void CancelIntro() { introT = -1f; }

    public bool IntroPlaying { get { return introT >= 0f; } }

    /// <summary>
    /// The showcase move. The camera is placed on an orbit around the car
    /// measured from directly behind it: 165 degrees is the front three-quarter
    /// view, 0 is the chase position. It trucks around the side while the
    /// framing eases from "on the car" to "down the road".
    /// </summary>
    void ApplyIntro(float dt)
    {
        introT += dt;
        float p = Mathf.Clamp01(introT / introDuration);

        // Drone timing: barely creeps off the mark, then accelerates hard
        // through the truck and decelerates into the final framing.
        float raw = Mathf.Clamp01((p - 0.14f) / 0.86f);
        float travel = raw < 0.5f
            ? 4f * raw * raw * raw                        // slow build
            : 1f - Mathf.Pow(-2f * raw + 2f, 3f) / 2f;    // hard stop
        travel = Mathf.Clamp01(travel);

        float angle = Mathf.Lerp(showcaseAngle, 0f, travel);
        // swings IN close as it passes the car's flank, then pulls back out
        // to the chase distance - the pass is what sells the flyby
        float radius = Mathf.Lerp(showcaseRadius, distanceBehind, travel)
                     - Mathf.Sin(travel * Mathf.PI) * 1.8f;
        // rises as it swings, with a slow float on top so it never feels rigid
        float camHeight = Mathf.Lerp(showcaseHeight, height, travel)
                        + Mathf.Sin(p * Mathf.PI * 1.6f) * 0.4f;

        Vector3 fwd = target.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        // rotate the "behind the car" vector around to wherever we are now
        Vector3 offset = Quaternion.AngleAxis(angle, Vector3.up) * (-fwd) * radius;
        Vector3 pos = target.position + offset + Vector3.up * camHeight;

        // snap on the first frame, then follow the path tightly - the car is
        // moving, so a soft chase would never catch up to the front of it
        if (introSnap)
        {
            introSnap = false;
            transform.position = pos;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, pos,
                1f - Mathf.Exp(-22f * dt));
        }

        // keep the car framed until the last stretch, then look up the road
        Vector3 atCar = (car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f)
                        - Vector3.up * introLift * (1f - travel);
        float aim = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((p - 0.62f) / 0.38f));
        Vector3 lookTarget = Vector3.Lerp(atCar, LookPoint(), aim);

        // Bank into the move like a drone leaning through a turn: the roll
        // comes from how fast the camera is swinging around the car.
        float swing = dt > 0f ? Mathf.DeltaAngle(lastIntroAngle, angle) / dt : 0f;
        lastIntroAngle = angle;
        float wantRoll = Mathf.Clamp(swing * 0.055f, -14f, 14f);
        introRoll = Mathf.Lerp(introRoll, wantRoll, 1f - Mathf.Exp(-6f * dt));

        Quaternion look = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up)
                          * Quaternion.Euler(0f, 0f, introRoll);
        transform.rotation = introSnapRot
            ? look
            : Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-20f * dt));
        introSnapRot = false;

        if (cam != null)
        {
            // tighter lens on the car, opening back up as it pulls away
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView,
                Mathf.Lerp(40f, baseFov, travel), 1f - Mathf.Exp(-7f * dt));
        }

        if (p >= 1f) introT = -1f;
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    public void SetTarget(Transform t)
    {
        target = t;
        car = t != null ? t.GetComponent<CarController>() : null;
        SnapToTarget();
    }

    public void Shake(float amount)
    {
        shake = amount;
    }

    public void SnapToTarget()
    {
        if (target == null) return;
        transform.position = DesiredPosition();
        transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position, Vector3.up);
    }

    Vector3 AheadDirection()
    {
        // blend the car's forward with the direction toward the track further
        // ahead - on corners this swings the camera so you can see through them
        if (car == null) return target.forward;
        Vector3 toAhead = car.GetTrackPointAhead(cornerLookAhead) - target.position;
        toAhead.y = 0f;
        if (toAhead.sqrMagnitude < 0.01f) return target.forward;
        Vector3 fwd = target.forward;
        fwd.y = 0f;
        // Anticipation is the camera swinging toward the corner BEFORE the car
        // gets there. That belongs to a car that is going round the corner -
        // with nobody steering it is not, so the camera just sits behind it.
        return Vector3.Slerp(fwd.normalized, toAhead.normalized,
                             positionAnticipation * Assist);
    }

    // The car's assist flips over half a second; the camera takes its own,
    // slower reading of it. Anticipation changes where the car sits in frame,
    // and a framing change that arrives quickly reads as the camera moving of
    // its own accord every time a thumb lands or lifts.
    float smoothedAssist = 1f;

    /// <summary>Stepped once a frame - it is read by both the position and the aim.</summary>
    void TickAssist(float dt)
    {
        float raw = car != null ? car.SteerAssist : 1f;
        smoothedAssist = Mathf.Lerp(smoothedAssist, raw, 1f - Mathf.Exp(-1.6f * dt));
    }

    float Assist { get { return smoothedAssist; } }

    Vector3 DesiredPosition()
    {
        return target.position - AheadDirection() * distanceBehind + Vector3.up * height;
    }

    Vector3 LookPoint()
    {
        // flattened: a rig pitched by a slope should not throw the aim point
        // up or down either
        Vector3 aimFwd = target.forward;
        aimFwd.y = 0f;
        if (aimFwd.sqrMagnitude < 0.001f) aimFwd = target.forward;

        Vector3 straight = target.position + Vector3.up * lookHeight
                           + aimFwd.normalized * lookAhead;
        if (car == null) return straight;
        Vector3 trackAhead = car.GetTrackPointAhead(cornerLookAhead) + Vector3.up * lookHeight;

        // Anticipation swings the aim sideways to see through a corner. It
        // must not move it vertically: the road ahead sits at a different
        // height on any crest or dip, so blending toward it tilted the camera
        // up and down - which is what made it look like the camera rose and
        // fell depending on whether the screen was being touched.
        trackAhead.y = straight.y;

        // and the aim stays on the car rather than reaching up the road
        return Vector3.Lerp(straight, trackAhead, lookAnticipation * Assist);
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (car == null && target == null)
            {
                CarController found = FindAnyObjectByType<CarController>();
                if (found != null) SetTarget(found.transform);
            }
            return;
        }

        float dt = Time.deltaTime;

        if (wreck)
        {
            ApplyWreckShot(dt);
            return;
        }

        if (introT >= 0f)
        {
            ApplyIntro(dt);
            return;
        }

        if (showcase)
        {
            if (TitleShotLive) ApplyTitleShot(dt);
            else ApplyShowcase(dt);
            return;
        }

        TickAssist(dt);

        Vector3 desired = DesiredPosition();
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmoothing * dt));

        float roll = car != null
            ? Mathf.Clamp(car.SlideVelocity / car.maxLateralSpeed, -1f, 1f) * -driftRoll
            : 0f;
        Quaternion look = Quaternion.LookRotation(LookPoint() - transform.position, Vector3.up)
                          * Quaternion.Euler(0f, 0f, roll);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-positionSmoothing * dt));

        if (shake > 0.001f)
        {
            transform.position += Random.insideUnitSphere * shake;
            shake = Mathf.MoveTowards(shake, 0f, 1.4f * dt);
        }

        if (cam != null && car != null)
        {
            float speedT = Mathf.InverseLerp(car.baseSpeed, car.maxSpeed, car.CurrentSpeed);
            float targetFov = baseFov + speedT * maxFovBoost;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-3f * dt));
        }
    }
}
