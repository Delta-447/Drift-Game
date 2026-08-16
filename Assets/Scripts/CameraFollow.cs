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
        if (on) introT = -1f;
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
        Vector3 aimAt = (car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f)
                        + aimShift;
        transform.rotation = Quaternion.LookRotation(aimAt - transform.position, Vector3.up);

        if (cam != null)
        {
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 40f, 1f - Mathf.Exp(-4f * dt));
        }
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
        showcase = false;
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
        Vector3 atCar = car != null ? car.BodyCentre : target.position + Vector3.up * 0.65f;
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
        return Vector3.Slerp(fwd.normalized, toAhead.normalized, positionAnticipation);
    }

    Vector3 DesiredPosition()
    {
        return target.position - AheadDirection() * distanceBehind + Vector3.up * height;
    }

    Vector3 LookPoint()
    {
        Vector3 straight = target.position + Vector3.up * lookHeight + target.forward * lookAhead;
        if (car == null) return straight;
        Vector3 trackAhead = car.GetTrackPointAhead(cornerLookAhead) + Vector3.up * lookHeight;
        return Vector3.Lerp(straight, trackAhead, lookAnticipation);
    }

    void LateUpdate()
    {
        if (target == null)
        {
            if (car == null && target == null)
            {
                CarController found = FindFirstObjectByType<CarController>();
                if (found != null) SetTarget(found.transform);
            }
            return;
        }

        float dt = Time.deltaTime;

        if (introT >= 0f)
        {
            ApplyIntro(dt);
            return;
        }

        if (showcase)
        {
            ApplyShowcase(dt);
            return;
        }

        Vector3 desired = DesiredPosition();
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionSmoothing * dt));

        float roll = car != null
            ? Mathf.Clamp(car.LateralVelocity / car.maxLateralSpeed, -1f, 1f) * -driftRoll
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
