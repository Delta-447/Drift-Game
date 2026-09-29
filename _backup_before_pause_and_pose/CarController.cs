using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drift car that follows the procedural track. The car auto-accelerates;
/// the player slides a thumb left/right to control lateral drift.
/// Corners push the car outward (centrifugal force), so holding a clean
/// line through a corner at speed IS the drift. GameManager drives Tick().
/// Lift the thumb and nobody is driving: the car runs dead straight on the
/// heading it had, so letting go in a corner puts it off the road.
/// </summary>
public class CarController : MonoBehaviour
{
    public enum TickResult { Ok, CrashedObstacle, CrashedOffRoad, HitOil }

    [Header("Visual")]
    public Transform carVisual;
    [Tooltip("Toggle this if the car model drives backwards.")]
    public bool flipVisual180 = true;
    [Tooltip("Snaps the car model onto the rig so it sits centred on the road.")]
    public bool centerVisual = true;
    [Tooltip("Raise/lower the car model. Negative = lower toward the road.")]
    public float visualHeightOffset = -0.2f;

    [Header("Speed")]
    public float baseSpeed = 13f;
    public float maxSpeed = 34f;
    [Tooltip("m/s gained per second of survival.")]
    public float speedGainPerSecond = 0.35f;

    [Header("Steering / drift feel")]
    [Tooltip("Fraction of screen width the thumb slides for full steering lock.")]
    public float steerZoneFraction = 0.18f;
    [Tooltip("Thumb drives the REAR of the car: slide right = tail kicks right = car goes left.")]
    public bool thumbControlsRear = true;
    [Tooltip("How gentle the steering is around the middle of the thumb's " +
             "travel. 0 = straight line (every bit of thumb is the same " +
             "amount of slide). Higher = small and medium moves slide less, " +
             "so holding a drift and making corrections is calmer, while a " +
             "full slide of the thumb still gives the full drift.")]
    // (GameManager sets this at startup - change it there.)
    [Range(0f, 0.9f)] public float steerCenterSoftness = 0.6f;
    [Tooltip("Flips all steering input (settings menu option).")]
    public bool invertSteering = false;
    [Tooltip("Ignores the thumb entirely - used while a countdown is running.")]
    public bool steeringLocked = false;

    [Header("Hover car")]
    public bool hoverMode = false;
    public float hoverHeight = 0.5f;
    [Tooltip("How hard the hover car banks into drifts.")]
    public float hoverBankAngle = 22f;
    [Tooltip("How much of its height the hover car gives up in a full drift. " +
             "0 = stays at full height, 1 = sinks as low as its lean allows.")]
    [Range(0f, 1f)] public float hoverDriftDrop = 0.4f;
    [Tooltip("Smallest gap kept under the hover car's lowest edge, whatever " +
             "it is doing, in metres.")]
    public float hoverMinGap = 0.18f;
    [Tooltip("Drift intensity above which sparks crackle under the hover car.")]
    public float sparkThreshold = 0.35f;
    float appliedVisualY;
    float currentDriftPercent;
    float smoothedDriftYaw;
    float driftYawRate;         // how fast the body is swinging, deg/s
    float hoverLift;            // how high the hover car is floating right now
    float smoothedBank;
    float smoothedJumpPitch;
    ParticleSystem sparkL, sparkR;
    float invulnTimer;

    [Header("Items (driven by GameManager)")]
    public bool itemInvincible;   // survive obstacle hits while on the road
    public bool springsActive;    // tap to jump
    public float jumpDuration = 0.75f;
    public float jumpHeight = 2.3f;
    [Tooltip("How far the nose lifts and drops through a spring jump.")]
    public float jumpPitchAngle = 11f;
    [Tooltip("Fraction of the screen height a finger must travel upward to jump.")]
    public float jumpSwipeFraction = 0.06f;
    float jumpTimer;
    bool jumpStarted;
    public bool IsJumping { get { return jumpTimer > 0f; } }

    /// <summary>True once per jump, so the caller can play the spring sound.</summary>
    public bool ConsumeJumpStarted()
    {
        if (!jumpStarted) return false;
        jumpStarted = false;
        return true;
    }
    float pendingBoost;
    float boostBonus;
    [Tooltip("How quickly a boost's extra top speed bleeds away (m/s per second).")]
    public float boostFadeRate = 11f;
    [Tooltip("How fast queued boosts feed into speed (m/s per second).")]
    public float boostRampRate = 45f;
    [Header("Handling")]
    [Tooltip("Fastest the sideways speed can change, in m/s per second. Big " +
             "thumb swings build over a beat instead of snapping. The " +
             "sensitivity setting scales this.")]
    public float steerAcceleration = 42f;
    [Tooltip("Sideways speed at full steering lock.")]
    public float maxLateralSpeed = 17f;
    [Tooltip("How quickly steering input is eased in. Lower = smoother, laggier.")]
    public float steerSmoothing = 15f;
    [Tooltip("How quickly the slide settles to where the thumb is. Low is " +
             "loose and gradual, high is direct and twitchy.")]
    public float angleSeek = 10f;
    float smoothedSteer;
    float slideTarget;          // where the slide is heading, eased toward the thumb
    float slideRate;            // how fast the slide is changing, m/s per second
    // Big swings build at this multiple of steerAcceleration. Above 1 so a
    // full left-to-right flick still lands in well under a second.
    const float SwingRateScale = 2.0f;
    [Tooltip("Strength of outward push in corners. The core difficulty knob.")]
    public float centrifugalFactor = 0.85f;
    [Tooltip("How well the tyres hold against that push while you are driving. " +
             "Lower = corners carry the car wider and need more counter-steer.")]
    public float cornerGrip = 10f;
    [Tooltip("After the screen is let go: how quickly any drift straightens " +
             "out. From then on the car runs dead straight wherever it is " +
             "pointing - it never follows the road on its own.")]
    public float releaseRegrip = 8f;
    [Tooltip("Sideways speed needed before it counts as a drift.")]
    public float driftThreshold = 6.5f;
    [Tooltip("Fraction of that threshold where smoke, skid marks and the " +
             "drift sound start. Very low means they show up the instant the " +
             "car steps out at all.")]
    [Range(0.02f, 1f)] public float effectsEarlyFactor = 0.05f;

    [Header("Drift visual")]
    [Tooltip("How far ahead of centre the car pivots when drifting. " +
             "Higher = the rear swings out more.")]
    public float driftPivotForward = 0.85f;
    public float driftAngle = 42f;
    public float driftRotationSpeed = 9f;
    public float driftBankAngle = 7f;
    [Tooltip("How much the nose points into corners (sells the drift).")]
    public float cornerDriftVisual = 0.45f;
    [Tooltip("Body angle for a SMALL slide, as a share of the full-size " +
             "angle. Lower = small corrections barely turn the body, so it " +
             "only really swings out once you commit to a drift.")]
    [Range(0.05f, 1f)] public float smallDriftAngleScale = 0.3f;
    [Tooltip("How much the body turns with your THUMB rather than waiting for " +
             "the slide to build. 0 = the body only follows the slide (it " +
             "trails your thumb by nearly half a second); higher = the nose " +
             "starts turning as you steer and the slide follows, like a real " +
             "car.")]
    [Range(0f, 1f)] public float bodyLead = 0.5f;
    [Tooltip("Size of slide (share of full lock) from which the body angle " +
             "is at full strength. Below it the angle is eased down.")]
    [Range(0.05f, 1f)] public float fullDriftAngleFrom = 0.4f;

    [Header("Crash")]
    public float carRadius = 1.1f;
    [Tooltip("Size the hitbox from the fitted car model instead of the value above.")]
    public bool autoFitHitbox = true;
    [Tooltip("Fraction of the model's half-width used as the hitbox radius.")]
    [Range(0.5f, 1.2f)] public float hitboxTightness = 0.85f;
    public float offRoadMargin = 0.6f;

    [Header("Oil spill spin-out")]
    public float spinDuration = 0.95f;
    [Tooltip("Speed multiplier applied when you hit oil.")]
    public float spinSpeedPenalty = 0.85f;

    // --- runtime state -----------------------------------------------------
    public float DistanceTraveled { get; private set; }
    public float CurrentSpeed { get; private set; }
    public float LateralOffset { get; private set; }
    public float LateralVelocity { get; private set; }
    public bool IsDrifting { get; private set; }
    /// <summary>True as soon as the car steps out - before it scores as a drift.</summary>
    public bool IsSliding { get; private set; }
    /// <summary>Eased thumb input, -1 to 1. Read by the tutorial coaching.</summary>
    public float SteerInput { get { return smoothedSteer; } }

    /// <summary>
    /// Whether the tyres should be smoking, marking and squealing. A car
    /// running straight off the road with nobody steering it is not drifting.
    /// An oil spin is the exception - that is worth seeing however it happened.
    /// </summary>
    public bool EffectsActive
    {
        get { return IsSpinning || IsSliding || IsDrifting; }
    }

    // --- hands on / hands off
    // A finger on the screen is the player driving, and the car goes where the
    // thumb puts it. Lift the finger and nobody is driving: the car keeps the
    // heading it had and runs dead straight, so a corner it was in takes it
    // off the road. There is no grace period and no fade - the handling lets
    // go the same frame the finger does.
    bool driving = true;
    bool coasting;              // nobody driving - running straight
    float coastWorldYaw;        // the world heading held while coasting
    float coastSlide;           // leftover drift bleeding away after release
    float coastTime;            // seconds since the finger lifted
    float headingOffset;        // rig yaw off the road's heading, degrees
    float handsOn = 1f;         // eased driving flag - camera and effects only
    float poseFade = 1f;        // 1 = posed by the drift, 0 = squared up

    // How fast the nose swings back into line with the road once the player
    // takes the car back. Presentation only: the slide is what moves the car.
    const float RegainTurnRate = 7f;
    // How far ahead and behind the road's grade is read - about where the
    // axles are, so the tyres sit on the slope rather than the car's middle.
    const float GradeReach = 1.3f;

    /// <summary>
    /// 1 while the player is steering, 0 once they have let go. Eased, and
    /// only for presentation (camera framing) - the handling itself switches
    /// the instant the finger lifts.
    /// </summary>
    public float SteerAssist { get { return Mathf.SmoothStep(0f, 1f, handsOn); } }

    /// <summary>True while a finger (or a steering key) is down.</summary>
    public bool IsDriving { get { return driving; } }

    /// <summary>
    /// The part of the sideways motion that is the tyres actually sliding.
    /// While driving that is all of it. With nobody driving, most sideways
    /// motion is just the car going straight as the road bends away, and only
    /// the leftover drift counts.
    /// </summary>
    public float SlideVelocity { get { return coasting ? coastSlide : LateralVelocity; } }

    /// <summary>Back to driving along the road, facing down it.</summary>
    void ClearCoast()
    {
        coasting = false;
        coastSlide = 0f;
        coastTime = 0f;
        headingOffset = 0f;
        slideTarget = LateralVelocity;
        slideRate = 0f;
    }
    public bool IsSpinning { get { return spinTimer > 0f; } }
    public float DriftTime { get; private set; }

    TrackGenerator track;
    Quaternion startingVisualRotation;
    bool visualRotationCaptured;
    float currentCurvDegPerM;
    float baseVisualY;
    float spinTimer;
    float spinDir;
    float driftGrace;
    TrailRenderer trailL, trailR;
    Transform originalVisual;
    GameObject swappedModel;
    bool modelIsSwapped;
    Quaternion originalCapturedRotation;
    float originalCapturedY;

    [Header("Skid marks")]
    public float rearWheelHalfWidth = 0.55f;
    public float rearWheelBack = 1.15f;
    public float skidHeight = 0.07f;

    [Header("Drift smoke")]
    public float smokeRate = 42f;
    ParticleSystem smokeL, smokeR;
    static Material smokeMat;

    float touchAnchorX;
    bool anchorActive;
    int steerFingerId = -1;     // the finger doing the steering
    bool inputHeld;             // a finger or steering key is down this frame
    float jumpSwipeBaseY = -1f;

    /// <summary>
    /// True on a fresh upward flick of the finger that is already down. The
    /// thumb never has to be lifted - which matters, because lifting it also
    /// releases the steering - and letting go can no longer jump by accident.
    /// </summary>
    bool SwipedUp()
    {
        float y;
        bool held;

        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            y = t.position.y;
            held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
        }
        else
        {
            y = Input.mousePosition.y;
            held = Input.GetMouseButton(0);
        }

        if (!held) { jumpSwipeBaseY = -1f; return false; }
        if (jumpSwipeBaseY < 0f) { jumpSwipeBaseY = y; return false; }

        // the reference point trails the finger downward, so a swipe is always
        // measured from the lowest point it has reached
        if (y < jumpSwipeBaseY) jumpSwipeBaseY = y;

        if (y - jumpSwipeBaseY < Screen.height * jumpSwipeFraction) return false;

        jumpSwipeBaseY = y;      // one flick, one jump
        return true;
    }

    bool tickedThisFrame;

    /// <summary>
    /// Whenever nothing is driving the car - paused, crashed, rewinding, sat
    /// in a menu - the front wheels ease back to straight. Without this they
    /// freeze at whatever angle they were on when the last Tick ran and stay
    /// there, which looks like the steering has jammed.
    /// </summary>
    void Update()
    {
        if (tickedThisFrame) { tickedThisFrame = false; return; }
        if (wheelSpin == null) return;
        wheelSpin.steerAngle = Mathf.MoveTowards(wheelSpin.steerAngle, 0f,
                                                 120f * Time.deltaTime);
    }

    void Awake()
    {
        if (carVisual == null && transform.childCount > 0)
        {
            carVisual = transform.GetChild(0);
        }
        CaptureVisualRotation();
        trailL = MakeSkidTrail();
        trailR = MakeSkidTrail();
        smokeL = MakeSmoke();
        smokeR = MakeSmoke();
        sparkL = MakeSparks();
        sparkR = MakeSparks();
    }

    ParticleSystem MakeSparks()
    {
        var go = new GameObject("RimSparks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        main.startColor = new Color(1f, 0.85f, 0.35f, 1f);
        main.gravityModifier = 1.3f;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.06f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0f),
                    new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = GetSmokeMaterial();
        rend.renderMode = ParticleSystemRenderMode.Stretch; // streaky sparks
        rend.velocityScale = 0.05f;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        ps.Play();
        return ps;
    }

    static Material GetSmokeMaterial()
    {
        if (smokeMat != null) return smokeMat;

        // soft radial puff drawn in code
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x - S * 0.5f) / (S * 0.5f);
                float dy = (y - S * 0.5f) / (S * 0.5f);
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        tex.Apply();

        Shader sh = Shader.Find("Sprites/Default");
        smokeMat = new Material(sh) { mainTexture = tex };
        return smokeMat;
    }

    ParticleSystem MakeSmoke()
    {
        var go = new GameObject("DriftSmoke");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.95f, 0.95f, 0.95f, 0.5f);
        main.gravityModifier = -0.07f; // smoke drifts up
        main.maxParticles = 600;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        rend.material = GetSmokeMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        ps.Play();
        return ps;
    }

    TrailRenderer MakeSkidTrail()
    {
        var go = new GameObject("Skid");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 2.4f;
        tr.startWidth = 0.26f;
        tr.endWidth = 0.12f;
        tr.minVertexDistance = 0.2f;
        tr.numCapVertices = 2;
        tr.numCornerVertices = 2;
        tr.alignment = LineAlignment.TransformZ;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(sh);
        mat.color = new Color(0.07f, 0.07f, 0.09f, 0.8f);
        tr.material = mat;

        tr.emitting = false;
        return tr;
    }

    void CaptureVisualRotation()
    {
        if (carVisual != null && !visualRotationCaptured)
        {
            startingVisualRotation = carVisual.localRotation;
            baseVisualY = carVisual.localPosition.y;
            // remember the untouched scene values - restoring the original
            // car must NEVER re-read them after drift rotation / height
            // offsets have been applied, or they get baked in twice
            originalCapturedRotation = startingVisualRotation;
            originalCapturedY = baseVisualY;
            visualRotationCaptured = true;
        }
    }

    /// <summary>Reset to the start of a fresh run on the given track.</summary>
    public void ResetRun(TrackGenerator trackGenerator)
    {
        track = trackGenerator;
        DistanceTraveled = track.roadBehindStart; // start with road behind us
        CurrentSpeed = baseSpeed;
        LateralOffset = 0f;
        LateralVelocity = 0f;
        smoothedSteer = 0f;
        handsOn = 1f;
        poseFade = 1f;
        driving = true;
        inputHeld = false;
        steerFingerId = -1;
        ClearCoast();
        IsDrifting = false;
        IsSliding = false;
        anchorActive = false;
        currentCurvDegPerM = 0f;
        idleMode = false;
        idleYawPercent = 0f;
        idleYawBase = 0f;
        CaptureVisualRotation();
        spinTimer = 0f;
        smoothedDriftYaw = 0f;
        driftYawRate = 0f;
        driftSize = 0f;
        smoothedBank = 0f;
        DriftTime = 0f;
        driftGrace = 0f;
        invulnTimer = 0f;
        jumpTimer = 0f;
        pendingBoost = 0f;
        boostBonus = 0f;
        itemInvincible = false;
        springsActive = false;
        jumpStarted = false;
        if (shieldGo != null) shieldGo.SetActive(false);
        if (wheelSpin != null) wheelSpin.ResetWheels();
        if (trailL != null) { trailL.Clear(); trailL.emitting = false; }
        if (trailR != null) { trailR.Clear(); trailR.emitting = false; }
        if (smokeL != null) smokeL.Clear();
        if (smokeR != null) smokeR.Clear();
        if (sparkL != null) sparkL.Clear();
        if (sparkR != null) sparkR.Clear();
        if (carVisual != null && centerVisual)
        {
            // kill any sideways/forward offset the model had in the old scene,
            // adjust its height with visualHeightOffset (swapped shop models
            // are already normalized, so the manual offset only applies to
            // the original scene car)
            float y = baseVisualY + (modelIsSwapped ? 0f : visualHeightOffset);
            carVisual.localPosition = new Vector3(0f, y, 0f);
            appliedVisualY = y;
        }
        ApplyPose(0f);
        if (carVisual != null)
        {
            carVisual.localRotation = startingVisualRotation * BaseVisualRotation();
        }

        // Seat a newly fitted car on its tyres NOW, while the body is square
        // to the road and the wheels have not turned. Measured later, mid-drift,
        // the body's lean and the rolled wheels both fed into the answer and
        // the car could end up sitting in the air. The hover car is seated the
        // same way - on the ground - and floats up from there in ApplyPose.
        if (!wheelSeatDone) SeatOnWheels();
    }

    Quaternion BaseVisualRotation()
    {
        return flipVisual180 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
    }

    /// <summary>Advance the simulation one frame. Called by GameManager while playing.</summary>
    public TickResult Tick(float dt)
    {
        tickedThisFrame = true;
        LastTickFrame = Time.frameCount;
        if (track == null) return TickResult.Ok;

        // The menu's showcase drive sets its own body angle. Driving for real
        // hands that back to the physics, or the car would slide sideways
        // without ever rotating into the drift.
        idleMode = false;

        // --- speed ramp (steady gain plus any queued boost, eased in)
        boostBonus = Mathf.MoveTowards(boostBonus, 0f, boostFadeRate * dt);
        float speedCeiling = maxSpeed + boostBonus;
        float gain = speedGainPerSecond * dt;
        if (pendingBoost > 0f)
        {
            float step = Mathf.Min(pendingBoost, boostRampRate * dt);
            pendingBoost -= step;
            gain += step;
        }
        CurrentSpeed = Mathf.Min(speedCeiling, CurrentSpeed + gain);
        if (CurrentSpeed > speedCeiling) CurrentSpeed = speedCeiling;

        // Races run at a fixed pace - no ramp - so nothing was ever pulling
        // the speed back down after a boost pad. The ceiling dropped away but
        // the car stayed pinned to whatever top speed its upgrades allowed,
        // for the rest of the race. Bots never showed it because they run
        // their own pacing.
        if (speedGainPerSecond <= 0f && pendingBoost <= 0f && boostBonus <= 0f
            && CurrentSpeed > baseSpeed)
        {
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, baseSpeed, boostFadeRate * dt);
        }

        // --- input (no control while spinning out, or before the lights go)
        float steer = 0f;
        if (steeringLocked)
        {
            // On rails through a countdown. Forget the thumb, so steering
            // picks up from wherever it is when the lights go rather than
            // jumping to wherever it wandered in the meantime.
            anchorActive = false;
            steerFingerId = -1;
            inputHeld = false;
        }
        else
        {
            steer = ReadSteerInput();
            lastRawSteer = steer;
        }
        if (invertSteering) steer = -steer;

        // the thumb is eased in, so the car leans into a slide instead of
        // stepping out the instant the finger moves
        smoothedSteer = dt > 0f
            ? Mathf.Lerp(smoothedSteer, steer, 1f - Mathf.Exp(-steerSmoothing * dt))
            : steer;
        steer = smoothedSteer;

        // springs item: flick the thumb UP to jump, without lifting it
        if (springsActive && jumpTimer <= 0f && SwipedUp())
        {
            jumpTimer = jumpDuration;
            jumpStarted = true;       // GameManager plays the boing
        }
        if (jumpTimer > 0f) jumpTimer -= dt;
        UpdateShield(dt);

        // wheels roll at road speed; hover cars have none touching the road
        if (wheelSpin != null) wheelSpin.speed = hoverMode ? 0f : CurrentSpeed;
        if (spinTimer > 0f)
        {
            spinTimer -= dt;
            steer = 0f;
        }

        // --- lateral physics
        track.SamplePose(DistanceTraveled, out _, out Vector3 roadFwd, out float curvDegPerM);
        float curvRadPerM = curvDegPerM * Mathf.Deg2Rad;
        // What the car FEELS of the bend - the push and the nose-in pose -
        // is the road's curvature averaged over a short stretch either side.
        // The track switches from one bend straight into the next in a single
        // metre, so read raw, every S-bend kicked the body round by itself
        // and yanked the slide the other way in the same instant. (The exact
        // curvature is still used where geometry needs it, below.)
        currentCurvDegPerM = FeltCurvature(DistanceTraveled);
        float roadYaw = Mathf.Atan2(roadFwd.x, roadFwd.z) * Mathf.Rad2Deg;

        // Somebody is driving while a finger is down. The countdown and an oil
        // spin count as well - the player has no say in either, so the car is
        // not left to run off on its own through them.
        driving = steeringLocked || spinTimer > 0f || inputHeld;

        // Camera framing and tyre smoke follow along, eased so a quick regrip
        // does not flicker them. The handling does not wait for this.
        handsOn = Mathf.MoveTowards(handsOn, driving ? 1f : 0f,
                                    dt / (driving ? 0.25f : 0.15f));
        // one clean, quick straighten of the body when the player lets go
        poseFade = Mathf.MoveTowards(poseFade, driving ? 1f : 0f, dt / 0.3f);

        // positive curvature = right turn = outward push to the LEFT (negative lateral)
        float centrifugal = -currentCurvDegPerM * Mathf.Deg2Rad
                            * CurrentSpeed * CurrentSpeed * centrifugalFactor;
        float alongRoad = CurrentSpeed;

        if (driving)
        {
            // Taking the car back after letting go: the sideways speed it built
            // up running wide carries straight over, and the steering below
            // catches it. The nose swings back into line in ApplyPose.
            if (coasting)
            {
                coasting = false;
                coastSlide = 0f;
                // pick the slide up exactly where it is - no jump - and let the
                // tyres bite straight away rather than carrying on drifting out
                slideTarget = LateralVelocity;
                slideRate = 0f;
            }

            if (spinTimer > 0f)
            {
                // Oil: the tyres have nothing. The corner throws the car and
                // the slide only bleeds away slowly.
                float before = LateralVelocity;
                LateralVelocity += centrifugal * dt;
                LateralVelocity *= Mathf.Exp(-0.6f * dt);
                slideTarget = LateralVelocity;
                slideRate = dt > 0f ? (LateralVelocity - before) / dt : 0f;
            }
            else
            {
                // THE STEERING. Where the thumb sits is the slide you get, and
                // the car always arrives exactly there, so you are in control:
                // no grip that suddenly breaks away, nothing parking the car at
                // an angle you did not choose.
                //
                // Corners shift that target outward, harder the faster you go,
                // and holding the line means steering against it. On rails
                // through a countdown there is no push, so the car sits on its
                // line until the lights go.
                float cornerPush = steeringLocked ? 0f
                    : centrifugal / Mathf.Max(0.1f, cornerGrip);
                // Softer in the middle of the thumb's travel, full at the ends.
                // Recorded driving showed medium moves throwing the car 10-15
                // m/s sideways - across most of the road - so every
                // correction overshot and needed correcting back, and the car
                // weaved. Now a medium move is a medium slide, and the big
                // drifts are still there at the ends of the travel.
                float shaped = steer * ((1f - steerCenterSoftness)
                                        + steerCenterSoftness * steer * steer);
                float target = Mathf.Clamp(shaped * maxLateralSpeed + cornerPush,
                                           -maxLateralSpeed, maxLateralSpeed);

                // How it gets there is what makes it smooth. The old response
                // started moving at full rate the instant the thumb moved and
                // stopped just as abruptly, and a left-to-right flick also got
                // a burst of extra bite halfway through - three hard edges in
                // every swing, which is the jolt you felt.
                //
                // Now a big swing builds at a steady pace...
                slideTarget = Mathf.MoveTowards(slideTarget, target,
                                                steerAcceleration * SwingRateScale * dt);

                // ...and the car follows it like a well-damped spring: it eases
                // off the old slide, carries through the middle and settles on
                // the new one without overshooting. Solved exactly over each
                // frame, so it feels the same at any frame rate.
                float w = Mathf.Max(0.5f, angleSeek);
                float e = LateralVelocity - slideTarget;
                float decay = Mathf.Exp(-w * dt);
                float eNext = (e + (slideRate + w * e) * dt) * decay;
                slideRate = (slideRate - w * (slideRate + w * e) * dt) * decay;
                LateralVelocity = slideTarget + eNext;
            }

            LateralVelocity = Mathf.Clamp(LateralVelocity, -maxLateralSpeed, maxLateralSpeed);
        }
        else
        {
            // NOBODY DRIVING: the car goes dead straight. It keeps the world
            // heading it had when the finger lifted and the road bends away
            // underneath it - on a straight it carries on down the road, and
            // in a corner it runs off the outside and crashes.
            if (!coasting)
            {
                coasting = true;
                coastTime = 0f;
                coastWorldYaw = roadYaw + headingOffset;
                // Whatever drift was on when the finger lifted is the tyres
                // sliding, not where the car is pointed. It bleeds away below
                // as they grip, leaving the car travelling where it faces.
                coastSlide = LateralVelocity
                             - CurrentSpeed * Mathf.Sin(headingOffset * Mathf.Deg2Rad);
            }
            coastTime += dt;

            float theta = Mathf.Clamp(Mathf.DeltaAngle(roadYaw, coastWorldYaw), -85f, 85f)
                          * Mathf.Deg2Rad;
            coastSlide *= Mathf.Exp(-releaseRegrip * dt);
            float wasLateral = LateralVelocity;
            LateralVelocity = CurrentSpeed * Mathf.Sin(theta) + coastSlide;
            slideRate = dt > 0f ? (LateralVelocity - wasLateral) / dt : 0f;

            // Off the middle of a bend the road's edge is longer or shorter
            // than its centre line. Ignoring that lets the car curve a few
            // degrees round with the road - a little self-steering - so it is
            // accounted for, blended in over a moment so the pace never steps.
            float bendScale = Mathf.Clamp(1f - curvRadPerM * LateralOffset, 0.4f, 2.5f);
            float blend = Mathf.Clamp01(coastTime / 0.25f);
            alongRoad = CurrentSpeed * Mathf.Cos(theta) * Mathf.Lerp(1f, 1f / bendScale, blend);
        }

        LateralOffset += LateralVelocity * dt;
        DistanceTraveled += alongRoad * dt;

        track.EnsureGenerated(DistanceTraveled);

        // Drifting (and scoring) is the player throwing the car sideways. A
        // car nobody is steering, running off the outside of a bend, is not
        // drifting - it is crashing. No combo for it.
        IsDrifting = driving && Mathf.Abs(LateralVelocity) > driftThreshold;
        // Smoke, skid marks and the drift sound come on with the INPUT, not
        // with the slide - touch the screen and the car is already laying
        // rubber, long before it has stepped out far enough to score.
        IsSliding = handsOn > 0.35f &&
                    (Mathf.Abs(smoothedSteer) > 0.015f ||
                     Mathf.Abs(SlideVelocity) > driftThreshold * effectsEarlyFactor);

        // sustained drift timer, with a short grace window so a quick
        // straighten-up between corners doesn't reset the multiplier
        if (IsDrifting)
        {
            DriftTime += dt;
            driftGrace = 0.5f;
        }
        else
        {
            driftGrace -= dt;
            if (driftGrace <= 0f) DriftTime = 0f;
        }

        ApplyPose(dt);
        SeatOnWheels();

        // Front wheels point where the car is actually going. The body is
        // yawed by the drift, and the wheels live inside it, so cancelling
        // that yaw leaves them aimed down the road - which is exactly the
        // counter-steer a real drifter holds. A little of the thumb input is
        // added on top so they visibly answer the steering.
        if (wheelSpin != null && !hoverMode)
        {
            // THE STUCK WHEEL. An oil spin sweeps the body angle through a
            // full 360, and Mathf.LerpAngle leaves the running value
            // unwrapped - so it can settle at 350 rather than -10. The car
            // looks straight, because a rotation does not care, but this line
            // clamps to 45 degrees and -350 clamps to full lock. The wheels
            // then sat at maximum steering for the rest of the run.
            // DeltaAngle folds it back into -180..180 before the clamp.
            float bodyYaw = Mathf.DeltaAngle(0f, smoothedDriftYaw);
            float target = spinTimer > 0f
                ? 0f
                : Mathf.Clamp(-bodyYaw + steer * frontWheelSteerAngle * 0.35f,
                              -45f, 45f);
            wheelSpin.steerAngle = spinTimer > 0f
                ? Mathf.MoveTowards(wheelSpin.steerAngle, target, 180f * dt)
                : target;
        }

        // --- crashes
        if (Mathf.Abs(LateralOffset) > track.roadWidth * 0.5f + offRoadMargin)
        {
            return TickResult.CrashedOffRoad;
        }

        if (invulnTimer > 0f) invulnTimer -= dt;

        // protected: rewind mercy window, invincibility item, or mid-jump
        bool protectedNow = invulnTimer > 0f || itemInvincible || jumpTimer > 0f;
        if (!protectedNow)
        {
            TrackGenerator.ObstacleHit hit = track.CheckObstacleHit(DistanceTraveled, LateralOffset, carRadius);
            if (hit == TrackGenerator.ObstacleHit.Solid)
            {
                return TickResult.CrashedObstacle;
            }
            if (hit == TrackGenerator.ObstacleHit.OilSpill)
            {
                TriggerSpinOut();
                return TickResult.HitOil;
            }
        }
        return TickResult.Ok;
    }

    void ApplyPose(float dt)
    {
        track.SamplePose(DistanceTraveled, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        transform.position = pos + right * LateralOffset;

        // The rig points down the road while somebody is driving - the slide
        // shows in the body's pose, below. With nobody driving it holds the
        // world heading it had when the finger lifted, so the car, and the
        // camera behind it, run dead straight while the road turns away.
        Quaternion roadRot = Quaternion.LookRotation(fwd, Vector3.up);
        float roadYaw = roadRot.eulerAngles.y;

        if (coasting && !idleMode)
        {
            headingOffset = Mathf.Clamp(Mathf.DeltaAngle(roadYaw, coastWorldYaw), -85f, 85f);
        }
        else if (dt > 0f)
        {
            // taken back: the nose swings smoothly back into line with the road
            headingOffset = Mathf.Lerp(headingOffset, 0f, 1f - Mathf.Exp(-RegainTurnRate * dt));
            if (Mathf.Abs(headingOffset) < 0.01f) headingOffset = 0f;
        }

        // Follow the road's grade too. The rig used to stay dead level
        // whatever the road did, so on a climb the front tyres sank into the
        // tarmac while the rear ones hung in the air, and the other way round
        // going down - a car that looked like it was floating on every hill.
        float gradePitch = 0f;
        track.SamplePose(DistanceTraveled + GradeReach, out Vector3 gradeAhead, out _, out _);
        track.SamplePose(DistanceTraveled - GradeReach, out Vector3 gradeBehind, out _, out _);
        Vector3 rise = gradeAhead - gradeBehind;
        float run = new Vector2(rise.x, rise.z).magnitude;
        if (run > 0.01f) gradePitch = -Mathf.Atan2(rise.y, run) * Mathf.Rad2Deg;

        transform.rotation = roadRot * Quaternion.Euler(0f, headingOffset, 0f)
                             * Quaternion.Euler(gradePitch, 0f, 0f);

        if (carVisual == null) return;

        // sideways slide + nose-into-corner pose while cornering at speed
        // The body leads: part of its angle comes from the slide the thumb is
        // asking for, not only the slide that has built up so far. Recorded
        // driving had the body trailing the thumb by 430 ms, which is the
        // delay you felt; leading halves it without touching the handling.
        float poseSlide = coasting ? coastSlide
            : Mathf.Lerp(LateralVelocity, slideTarget, bodyLead);
        float slideTerm = poseSlide / maxLateralSpeed;
        float cornerTerm = currentCurvDegPerM * CurrentSpeed * cornerDriftVisual / driftAngle;
        float driftPercent = idleMode
            ? Mathf.Clamp(idleYawPercent, -1f, 1f)
            : Mathf.Clamp(slideTerm + cornerTerm, -1f, 1f);

        // THE BODY ANGLE. It used to be a straight multiple of the slide, and
        // with a big full-lock angle that meant the smallest nudge of the
        // thumb swung the body round by 10-20 degrees - every little
        // correction looked like a drift the game was doing for you. Small
        // slides now get a small angle, and the angle only builds to full
        // strength as the slide grows into a real drift. Full drifts are
        // exactly as big as before.
        if (!idleMode) driftPercent = EaseSmallDrifts(driftPercent, dt);

        // Nobody driving: the body squares back up rather than hanging at a
        // drift angle with no one holding it there. The car keeps running wide
        // - only the pose changes - so you watch its back as it drives off the
        // outside of the bend.
        // one clean, quick straighten when the player lets go - then nothing
        if (spinTimer <= 0f && !idleMode) driftPercent *= poseFade;

        float visualYaw = driftPercent * driftAngle;

        // full 360 while sliding on oil
        if (spinTimer > 0f)
        {
            float spinProgress = 1f - Mathf.Clamp01(spinTimer / spinDuration);
            visualYaw += spinDir * 360f * spinProgress;
        }

        currentDriftPercent = driftPercent;
        UpdateSkidTrails(visualYaw);

        // Springs: a proper arc. It leaves the ground quickly, floats through
        // the top and settles back down, with the nose lifting on the way up
        // and dropping on the way down, so the car looks like it is being
        // thrown rather than sliding up an invisible ramp.
        float jumpY = 0f;
        float jumpPitch = 0f;
        if (jumpTimer > 0f)
        {
            float p = 1f - jumpTimer / jumpDuration;          // 0 -> 1
            float arc = 4f * p * (1f - p);                    // parabola
            arc = Mathf.Pow(arc, 0.72f);                      // fuller at the top
            jumpY = arc * jumpHeight;

            // vertical speed drives the pitch: nose up rising, down falling
            jumpPitch = -(1f - 2f * p) * jumpPitchAngle;

            // and a last-moment squash as it lands
            if (p > 0.88f) jumpY -= (p - 0.88f) / 0.12f * 0.06f;
        }

        // Hover car: floats, and banks hard into drifts, settling a little
        // lower as it leans. It used to dip until the rim touched the road -
        // and banked over, the low side of the body swung down by half its
        // width times the lean, so it went through. However far it leans or
        // pitches, it now always keeps its lowest edge clear of the road.
        // (Handling is untouched - this is pure animation.)
        float hoverPitch = 0f;
        float bank = -driftPercent * driftBankAngle;
        if (hoverMode && carVisual != null)
        {
            float dip = Mathf.Abs(driftPercent);
            float bob = Mathf.Sin(Time.time * 2.4f) * 0.12f * (1f - dip);
            hoverPitch = Mathf.Sin(Time.time * 1.7f) * 2.2f * (1f - dip);
            bank = -driftPercent * hoverBankAngle;

            // how far the lowest corner of the body drops for the lean and
            // pitch it has now or is heading for
            float lean = Mathf.Max(Mathf.Abs(bank), Mathf.Abs(smoothedBank)) * Mathf.Deg2Rad;
            float tilt = (Mathf.Abs(hoverPitch) + Mathf.Abs(smoothedJumpPitch)) * Mathf.Deg2Rad;
            float needed = bodyWidth * 0.5f * Mathf.Sin(lean)
                         + bodyLength * 0.5f * Mathf.Sin(tilt)
                         + hoverMinGap;

            float wanted = hoverHeight * (1f - hoverDriftDrop * dip) + bob;
            float target = Mathf.Max(wanted, needed);
            // eased, so settling into a drift is a glide, not a drop
            hoverLift = dt > 0f
                ? Mathf.Lerp(hoverLift, target, 1f - Mathf.Exp(-6f * dt))
                : target;
            // but never late getting clear of the road
            hoverLift = Mathf.Max(hoverLift, needed);

            carVisual.localPosition = new Vector3(0f, appliedVisualY + hoverLift + jumpY, 0f);
        }
        else if (carVisual != null && centerVisual)
        {
            carVisual.localPosition = new Vector3(0f, appliedVisualY + jumpY, 0f);
        }

        // Ease the drift angle itself, so the body and its pivot stay in sync.
        // A well-damped spring rather than a plain lag: the plain lag turned
        // the body at its fastest the very instant the slide changed, which
        // is the snap. The spring picks the angle up gently, swings through
        // and settles without overshooting.
        if (dt > 0f)
        {
            float w = Mathf.Max(0.5f, driftRotationSpeed * 1.7f);
            float e = Mathf.DeltaAngle(visualYaw, smoothedDriftYaw);
            float decay = Mathf.Exp(-w * dt);
            float eNext = (e + (driftYawRate + w * e) * dt) * decay;
            driftYawRate = (driftYawRate - w * (driftYawRate + w * e) * dt) * decay;
            smoothedDriftYaw = visualYaw + eNext;
        }
        else
        {
            smoothedDriftYaw = visualYaw;
            driftYawRate = 0f;
        }
        // Folded back into -180..180 once the spin is over. LerpAngle takes
        // the short way round but does not wrap its result, so without this
        // the value wanders further from zero with every spin and anything
        // reading it as a steering angle goes with it.
        if (spinTimer <= 0f) smoothedDriftYaw = Mathf.DeltaAngle(0f, smoothedDriftYaw);
        smoothedBank = dt > 0f
            ? Mathf.Lerp(smoothedBank, bank, Mathf.Clamp01(driftRotationSpeed * dt))
            : bank;

        // the jump's pitch is eased in so the nose lifts smoothly rather than
        // snapping to an angle the moment the car leaves the ground
        smoothedJumpPitch = dt > 0f
            ? Mathf.Lerp(smoothedJumpPitch, jumpPitch, 1f - Mathf.Exp(-9f * dt))
            : jumpPitch;

        Quaternion driftRot = Quaternion.Euler(hoverPitch + smoothedJumpPitch,
                                               smoothedDriftYaw, smoothedBank);
        carVisual.localRotation = startingVisualRotation * BaseVisualRotation() * driftRot;

        // The car pivots about a point ahead of its centre, so the tail swings
        // wide the way a real drift does instead of spinning on the spot. An
        // oil spin is different: that one turns about the middle of the car,
        // or the whole body swings around the nose like a compass needle.
        float pivotFwd = spinTimer > 0f ? 0f : driftPivotForward;
        Vector3 pivot = new Vector3(0f, 0f, pivotFwd);
        Vector3 pivotShift = pivot - driftRot * pivot;
        carVisual.localPosition += new Vector3(pivotShift.x, 0f, pivotShift.z);
    }

    /// <summary>
    /// Scales a drift pose so small ones are gentle and big ones are untouched:
    /// the gain rises smoothly from smallDriftAngleScale to 1 as the drift
    /// grows to fullDriftAngleFrom.
    ///
    /// The size it goes by is REMEMBERED - it rises straight away but lets go
    /// slowly. Read fresh every frame, a big left-to-right flick passed
    /// through a small pose on its way across and the body eased off in the
    /// middle of the swing, then snatched again on the far side. With the
    /// memory, a committed flick swings through in one even motion, and only
    /// genuinely small driving stays gentle.
    /// </summary>
    float EaseSmallDrifts(float pose, float dt)
    {
        float size = Mathf.Abs(pose);
        driftSize = dt > 0f && size < driftSize
            ? Mathf.Lerp(driftSize, size, 1f - Mathf.Exp(-DriftMemoryFade * dt))
            : size;
        float grow = Mathf.SmoothStep(0f, 1f, driftSize / Mathf.Max(0.01f, fullDriftAngleFrom));
        return pose * Mathf.Lerp(smallDriftAngleScale, 1f, grow);
    }

    float driftSize;                        // remembered drift size, 0-1
    const float DriftMemoryFade = 1.5f;     // per second
    // How much road either side of the car the felt curvature averages over,
    // in seconds of travel at the current speed.
    const float CurveBlendSeconds = 0.4f;

    /// <summary>
    /// The road's curvature averaged over a short stretch centred on
    /// <paramref name="dist"/>, weighted toward the middle. Centred, so a
    /// bend is felt a moment before it arrives and let go of a moment after
    /// it ends, with no lag.
    /// </summary>
    float FeltCurvature(float dist)
    {
        float reach = Mathf.Max(3f, CurrentSpeed * CurveBlendSeconds);
        float sum = 0f, weights = 0f;
        for (int i = -2; i <= 2; i++)
        {
            float w = 3f - Mathf.Abs(i);          // 1 2 3 2 1
            track.SamplePose(dist + reach * i * 0.5f, out _, out _, out float c);
            sum += w * c;
            weights += w;
        }
        return sum / weights;
    }

    void UpdateSkidTrails(float visualYaw)
    {
        if (trailL == null) return;

        // Trails sit under the visible body (which is offset by the drift
        // pivot) and always on the road surface, not at the model's height.
        Quaternion wheelRot = transform.rotation * Quaternion.Euler(0f, smoothedDriftYaw, 0f);
        Vector3 bodyPos = carVisual != null ? carVisual.position : transform.position;
        Vector3 ground = new Vector3(bodyPos.x, transform.position.y + skidHeight, bodyPos.z);

        trailL.transform.position = ground + wheelRot * new Vector3(-rearWheelHalfWidth, 0f, -rearWheelBack);
        trailR.transform.position = ground + wheelRot * new Vector3(rearWheelHalfWidth, 0f, -rearWheelBack);

        // effects kick in at a lower slide than the scoring drift does
        bool emit = EffectsActive;
        // hover cars leave no tire marks (the smoke stays - reads as hover dust)
        trailL.emitting = emit && !hoverMode;
        trailR.emitting = emit && !hoverMode;

        if (smokeL != null)
        {
            smokeL.transform.position = trailL.transform.position + Vector3.up * 0.12f;
            smokeR.transform.position = trailR.transform.position + Vector3.up * 0.12f;
            // harder slides = thicker smoke; hover cars make none
            float intensity = Mathf.Clamp01(Mathf.Abs(SlideVelocity) / maxLateralSpeed + (IsSpinning ? 0.6f : 0f));
            // a decent puff right away, building to a cloud in a full slide
            float rate = emit && !hoverMode ? smokeRate * (0.85f + 1.5f * intensity) : 0f;
            var eL = smokeL.emission; eL.rateOverTime = rate;
            var eR = smokeR.emission; eR.rateOverTime = rate;
        }

        // hover car: sparks fly from the rim on the side it's banking toward
        if (sparkL != null)
        {
            // under the low edge of the body - the hover car no longer touches
            // the road, so the sparks crackle off its underside instead
            float sparkY = transform.position.y + 0.07f;
            if (hoverMode)
            {
                sparkY += Mathf.Max(0f, hoverLift
                    - bodyWidth * 0.5f * Mathf.Abs(Mathf.Sin(smoothedBank * Mathf.Deg2Rad)));
            }
            Vector3 sparkBase = new Vector3(bodyPos.x, sparkY, bodyPos.z);
            sparkL.transform.position = sparkBase
                + wheelRot * new Vector3(-rearWheelHalfWidth * 1.1f, 0f, -rearWheelBack * 0.5f);
            sparkR.transform.position = sparkBase
                + wheelRot * new Vector3(rearWheelHalfWidth * 1.1f, 0f, -rearWheelBack * 0.5f);

            bool scraping = hoverMode && emit && Mathf.Abs(currentDriftPercent) > sparkThreshold;
            float sparkRate = 110f * Mathf.Clamp01(Mathf.Abs(currentDriftPercent));
            var sL = sparkL.emission;
            var sR = sparkR.emission;
            sR.rateOverTime = scraping && currentDriftPercent > 0f ? sparkRate : 0f;
            sL.rateOverTime = scraping && currentDriftPercent < 0f ? sparkRate : 0f;
        }
    }

    /// <summary>The fitted car model, for things like repainting it.</summary>
    public Transform CarModelRoot { get { return carVisual; } }

    public Transform GetOriginalVisual()
    {
        return originalVisual != null ? originalVisual : carVisual;
    }

    /// <summary>Swap the car model. Pass null to restore the original scene car.</summary>
    /// <param name="extraYaw">Per-model facing correction in degrees.</param>
    public void SetCarModel(GameObject prefab, float extraYaw = 0f)
    {
        if (originalVisual == null) originalVisual = carVisual;
        if (swappedModel != null) Destroy(swappedModel);

        if (prefab == null)
        {
            if (originalVisual != null)
            {
                originalVisual.gameObject.SetActive(true);
                FitWheelPointsTo(originalVisual.gameObject);
                wheelSpin = WheelSpinner.Attach(originalVisual.gameObject, transform);
            }
            carVisual = originalVisual;
            modelIsSwapped = false;
            // restore the values captured before any runtime modification
            startingVisualRotation = originalCapturedRotation;
            baseVisualY = originalCapturedY;
        }
        else
        {
            if (originalVisual != null) originalVisual.gameObject.SetActive(false);

            // container keeps normalization offsets separate from drift motion
            swappedModel = new GameObject("CarModel");
            swappedModel.transform.SetParent(transform, false);
            GameObject model = Instantiate(prefab, swappedModel.transform);
            BlackenWindows(model);
            NormalizeModel(swappedModel.transform, model);
            FitWheelPointsTo(model, swappedModel.transform);
            wheelSpin = WheelSpinner.Attach(model, transform);
            carVisual = swappedModel.transform;
            modelIsSwapped = true;
            // match the scene car's FACING (yaw) only. Inheriting its full
            // rotation would duplicate the tilt the FBX already carries
            // internally and shove the model into the ground.
            Quaternion sceneNet = originalCapturedRotation * BaseVisualRotation();
            float sceneYaw = sceneNet.eulerAngles.y;
            startingVisualRotation = Quaternion.Euler(0f, sceneYaw + extraYaw, 0f)
                                     * Quaternion.Inverse(BaseVisualRotation());
            baseVisualY = 0f;
        }
        visualRotationCaptured = true;
        bodyCentreMeasured = false;
        wheelSeatDone = false;
    }

    /// <summary>
    /// Lines the skid marks up with whichever car is fitted, using the
    /// model's own width and length rather than fixed numbers.
    /// </summary>
    void FitWheelPointsTo(GameObject model, Transform host = null)
    {
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        if (host == null) host = model.transform;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        float length = Mathf.Max(b.size.x, b.size.z);
        float width = Mathf.Min(b.size.x, b.size.z);

        rearWheelHalfWidth = width * 0.40f;   // just inside the bodywork
        rearWheelBack = length * 0.31f;       // roughly the rear axle

        // remembered so the shield bubble can be sized to the actual bodywork
        bodyLength = length;
        bodyWidth = width;
        bodyHeight = Mathf.Max(b.size.y, 0.5f);
        // where the bodywork actually sits relative to whatever the shield
        // will hang off - models differ in whether their origin is the wheels
        // or the middle of the car, so this is measured, not assumed
        bodyCenterLocal = host.InverseTransformPoint(b.center);
        if (shieldGo != null) { Destroy(shieldGo); shieldGo = null; }

        // collision size follows the actual bodywork, so a wide car really is
        // harder to thread through a gap than a narrow one
        if (autoFitHitbox)
        {
            carRadius = Mathf.Clamp(width * 0.5f * hitboxTightness, 0.6f, 1.6f);
        }
    }

    // ---------------------------------------------------------- shield bubble
    GameObject shieldGo;
    Material shieldMat;
    float shieldPulse;
    [Tooltip("How far the front wheels turn at full steering lock.")]
    public float frontWheelSteerAngle = 38f;
    WheelSpinner wheelSpin;
    bool wheelSeatDone;

    /// <summary>
    /// Drops the fitted model so its WHEELS rest on the road. Model bounds are
    /// unreliable - spoilers, mirrors and stray parts all move the lowest
    /// point - but the wheels are by definition where the car meets the
    /// ground, so once they have been identified they are the truth.
    /// </summary>
    void SeatOnWheels()
    {
        if (wheelSeatDone || carVisual == null) return;

        // The hover car used to be seated with its float already applied, so
        // the seating took the float straight back out: it sat on the road,
        // and every time it dipped into a drift it sank through it. Now it is
        // seated on the ground like everything else and floats up from there.
        wheelSeatDone = true;

        // Where the tyres touch the ground, in the model holder's own space.
        // That space turns and leans with the body's drift pose, so nothing
        // the car is doing at this moment can creep into the answer, and each
        // tyre is measured as the cylinder it is rather than from Unity's
        // world box - which on some models reaches well below the real tyre
        // and was lifting those cars clear of the road.
        float bottom;
        if (wheelSpin == null || !wheelSpin.TryGetContactHeight(carVisual, out bottom))
        {
            // No wheels found on this model: rest it on its bodywork instead,
            // ignoring the odd part that hangs below everything else.
            bottom = RestingHeightIn(carVisual);
        }

        // The holder sits square under the rig, so the tyres touch the road
        // when the holder is lowered by exactly that much.
        float delta = -bottom - appliedVisualY;
        if (Mathf.Abs(delta) > 0.0005f)
        {
            baseVisualY += delta;
            appliedVisualY += delta;
            carVisual.localPosition += new Vector3(0f, delta, 0f);
        }

        // and put the skid marks under the real rear tyres rather than at a
        // guessed fraction of the bodywork
        if (wheelSpin != null && wheelSpin.TryGetRearWheelOffsets(transform, out float hw, out float back))
        {
            rearWheelHalfWidth = Mathf.Clamp(hw, 0.3f, 1.4f);
            rearWheelBack = Mathf.Clamp(back, 0.4f, 2.2f);
        }
    }

    /// <summary>
    /// The lowest point of the bodywork in <paramref name="frame"/>'s space,
    /// ignoring a few parts that hang below everything else.
    /// </summary>
    static float RestingHeightIn(Transform frame)
    {
        var rends = frame.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return 0f;
        var lows = new List<float>(rends.Length);
        for (int i = 0; i < rends.Length; i++)
        {
            Bounds lb = rends[i].localBounds;
            Transform t = rends[i].transform;
            float low = float.MaxValue;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = new Vector3(
                    (c & 1) == 0 ? lb.min.x : lb.max.x,
                    (c & 2) == 0 ? lb.min.y : lb.max.y,
                    (c & 4) == 0 ? lb.min.z : lb.max.z);
                low = Mathf.Min(low, frame.InverseTransformPoint(t.TransformPoint(corner)).y);
            }
            lows.Add(low);
        }
        lows.Sort();
        return lows[Mathf.Min(lows.Count - 1, Mathf.Max(0, lows.Count / 8))];
    }


    float bodyLength = 4.2f, bodyWidth = 1.9f, bodyHeight = 1.3f;
    Vector3 bodyCenterLocal = new Vector3(0f, 0.65f, 0f);

    /// <summary>
    /// Blue energy bubble shown while the invincibility item is active. Built
    /// once, then just switched on and off.
    /// </summary>
    void UpdateShield(float dt)
    {
        if (itemInvincible && shieldGo == null) BuildShield();
        if (shieldGo == null) return;

        if (!itemInvincible)
        {
            if (shieldGo.activeSelf) shieldGo.SetActive(false);
            return;
        }

        if (!shieldGo.activeSelf) shieldGo.SetActive(true);
        shieldPulse += dt;

        // a subtle breath - the bubble should feel solid, not wobbly
        float breathe = 1f + Mathf.Sin(shieldPulse * 2.2f) * 0.025f;
        shieldGo.transform.localScale = shieldBaseScale * breathe;

        if (shieldMat != null && shieldMat.HasProperty("_Intensity"))
        {
            shieldMat.SetFloat("_Intensity", 0.9f + Mathf.Sin(shieldPulse * 4f) * 0.12f);
        }
    }

    Vector3 shieldBaseScale = Vector3.one;

    void BuildShield()
    {
        shieldGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shieldGo.name = "ShieldBubble";
        var col = shieldGo.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Parented to the car's visual, not the rig root: the body is offset
        // by the drift pivot, so anything on the root drifts away from the car
        // it is meant to be wrapping.
        Transform host = carVisual != null ? carVisual : transform;
        shieldGo.transform.SetParent(host, false);

        // enclose the whole car - the bubble radius comes from the model's own
        // length, which is what was making it sit half in front before
        float radius = Mathf.Max(bodyLength, bodyWidth) * 0.5f + 0.25f;
        Vector3 ls = host.lossyScale;
        // a true sphere, centred on the car - so the lower part of it sinks
        // into the road rather than the car sitting on the bubble's floor
        shieldBaseScale = new Vector3(
            radius * 2f / Mathf.Max(0.0001f, ls.x),
            radius * 2f / Mathf.Max(0.0001f, ls.y),
            radius * 2f / Mathf.Max(0.0001f, ls.z));
        shieldGo.transform.localScale = shieldBaseScale;
        shieldGo.transform.localRotation = Quaternion.identity;
        // sit on the measured centre of the bodywork, so the car is exactly
        // in the middle of the sphere however the model's pivot is set up
        shieldGo.transform.localPosition = bodyCenterLocal;

        // custom fresnel shell - see Resources/Shaders/ShieldBubble.shader
        Shader sh = Resources.Load<Shader>("Shaders/ShieldBubble");
        if (sh == null) sh = Shader.Find("Driftline/ShieldBubble");
        bool custom = sh != null;
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");

        shieldMat = new Material(sh);
        if (custom)
        {
            shieldMat.SetColor("_Color", new Color(0.35f, 0.75f, 1f, 0.85f));
            shieldMat.SetFloat("_RimPower", 2.2f);
            shieldMat.SetFloat("_Intensity", 1f);
        }
        else
        {
            MakeTransparent(shieldMat);
            shieldMat.color = new Color(0.35f, 0.72f, 1f, 0.2f);
            if (shieldMat.HasProperty("_BaseColor"))
            {
                shieldMat.SetColor("_BaseColor", new Color(0.35f, 0.72f, 1f, 0.2f));
            }
        }

        var rend = shieldGo.GetComponent<Renderer>();
        rend.sharedMaterial = shieldMat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        shieldGo.SetActive(false);
    }

    /// <summary>Switches a URP material over to alpha blending.</summary>
    static void MakeTransparent(Material m)
    {
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // 1 = transparent
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // 0 = alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.DisableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static Material tintedGlassMat;

    /// <summary>Darkens any window/glass materials on a car model.</summary>
    public static void BlackenWindows(GameObject model)
    {
        if (model == null) return;
        if (tintedGlassMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            tintedGlassMat = new Material(sh) { color = new Color(0.03f, 0.03f, 0.04f) };
            if (tintedGlassMat.HasProperty("_Smoothness")) tintedGlassMat.SetFloat("_Smoothness", 0.85f);
            if (tintedGlassMat.HasProperty("_Metallic")) tintedGlassMat.SetFloat("_Metallic", 0.2f);
        }

        // where the middle of the car is, so lights (low, at the ends) can be
        // told apart from windows (high, in the middle)
        var all = model.GetComponentsInChildren<Renderer>(true);
        if (all.Length == 0) return;
        Bounds body = all[0].bounds;
        for (int i = 1; i < all.Length; i++) body.Encapsulate(all[i].bounds);
        float midHeight = body.center.y;

        foreach (var r in all)
        {
            // decals: badges, model names, lettering on light bars
            // Anything that carries branding is hidden outright: badges,
            // model lettering, maker emblems, sponsor stickers.
            string objName = r.gameObject.name.ToLowerInvariant();

            // "decal" is unambiguous - a part called that IS the lettering,
            // however big it is. Some models run stripes and text the length
            // of the whole body as a single mesh.
            bool isDecalPart = objName.Contains("decal") || objName.Contains("sticker")
                            || objName.Contains("numberplate")
                            || objName.Contains("licence") || objName.Contains("license");

            // The vaguer words get a size test. Models name whole panels -
            // and whole windows - things like "text" or "brand", and hiding
            // those took the glass out of the car. A badge is always small.
            bool smallEnough = r.bounds.size.magnitude < body.size.magnitude * 0.2f;
            bool isBrandingPart = smallEnough &&
                (objName.Contains("logo") || objName.Contains("badge")
                || objName.Contains("text") || objName.Contains("emblem")
                || objName.Contains("brand") || objName.Contains("marque")
                || objName.Contains("letter"));

            if (isDecalPart || isBrandingPart)
            {
                r.enabled = false;
                continue;
            }

            Material[] mats = r.sharedMaterials;
            bool hasGlass = false;
            for (int g = 0; g < mats.Length; g++)
            {
                if (mats[g] == null) continue;
                string mn = mats[g].name.ToLowerInvariant();
                if (mn.Contains("window") || mn.Contains("glass")
                    || mn.Contains("windshield") || mn.Contains("windscreen"))
                {
                    hasGlass = true;
                    break;
                }
            }

            // Glass is glass wherever it sits. The height test exists to stop
            // low bodywork being taken for a window, but a rear screen on a
            // low car sits under the midline and was skipped entirely - left
            // with whatever the import gave it, which on some models is
            // nothing you can see.
            if (hasGlass) r.enabled = true;
            else if (r.bounds.center.y < midHeight) continue;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name.ToLowerInvariant();
                // headlight and indicator lenses are often called "glass" too -
                // blacking those out puts a dead eye on the front of the car
                bool isLight = n.Contains("light") || n.Contains("lamp")
                            || n.Contains("head") || n.Contains("tail")
                            || n.Contains("indicator") || n.Contains("signal")
                            || objName.Contains("light") || objName.Contains("lamp");
                if (!isLight &&
                    (n.Contains("window") || n.Contains("glass") || n.Contains("windshield")
                     || n.Contains("windscreen")))
                {
                    mats[i] = tintedGlassMat;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    [Tooltip("Per-model height correction applied when the car is fitted.")]
    public float modelHeightFix;

    /// <summary>
    /// Drops the model so its wheels touch the road. The lowest point of the
    /// whole model is not reliable - some models carry a stray part hanging
    /// below the car, which would hold the whole thing up in the air. So the
    /// floor is taken from where the bulk of the low geometry sits, ignoring a
    /// few outliers underneath it.
    /// </summary>
    void SeatOnGround(Transform container, GameObject model)
    {
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length < 4) return;

        var lows = new System.Collections.Generic.List<float>(rends.Length);
        for (int i = 0; i < rends.Length; i++) lows.Add(rends[i].bounds.min.y);
        lows.Sort();

        float lowest = lows[0];
        float span = 0f;
        for (int i = 0; i < rends.Length; i++)
        {
            span = Mathf.Max(span, rends[i].bounds.size.y);
        }

        // the height the bottom tenth of the parts reach
        int idx = Mathf.Clamp(Mathf.RoundToInt(lows.Count * 0.12f), 1, lows.Count - 1);
        float bulk = lows[idx];

        // only treat the lowest parts as outliers if they hang clearly below
        float floorY = (bulk - lowest) > span * 0.12f ? bulk : lowest;

        float lift = container.position.y - floorY;
        if (Mathf.Abs(lift) > 0.001f)
        {
            model.transform.position += new Vector3(0f, lift, 0f);
        }
    }

    // scale to a consistent car length, centre the pivot, rest wheels on the road
    void NormalizeModel(Transform container, GameObject model)
    {
        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        float length = Mathf.Max(b.size.x, b.size.z, 0.01f);
        float k = 4.2f / length;

        // measure BEFORE scaling, in container-local space
        Vector3 p0 = model.transform.localPosition;               // pivot position
        Vector3 centerLocal = container.InverseTransformPoint(b.center);

        model.transform.localScale = model.transform.localScale * k;

        // scaling happens around the model's own pivot, so the mesh centre
        // moves to p0 + k * (centre - p0), NOT centre * k
        Vector3 scaledCenter = p0 + k * (centerLocal - p0);
        float halfHeight = b.extents.y * k;

        model.transform.localPosition = new Vector3(
            p0.x - scaledCenter.x,
            // bottom rests on the road, plus any per-model correction for
            // models whose bounds reach below the wheels
            p0.y - scaledCenter.y + halfHeight + 0.02f + modelHeightFix,
            p0.z - scaledCenter.z);

        SeatOnGround(container, model);
    }

    /// <summary>World position of the track centreline N meters ahead of the car.</summary>
    public Vector3 GetTrackPointAhead(float meters)
    {
        if (track == null) return transform.position + transform.forward * meters;
        track.SamplePose(DistanceTraveled + meters, out Vector3 pos, out _, out _);
        return pos;
    }

    /// <summary>Places the car at an earlier track position (rewind animation frames).</summary>
    public void SetRewound(float dist, float lateral)
    {
        DistanceTraveled = dist;
        LateralOffset = lateral;
        LateralVelocity = 0f;
        spinTimer = 0f;
        IsDrifting = false;
        DriftTime = 0f;
        ClearCoast();
        if (trailL != null) { trailL.Clear(); trailR.Clear(); }
        ApplyPose(0f);
    }

    /// <summary>Called when the rewind completes: mercy window + gentler speed.</summary>
    public void FinishRewind(float invulnSeconds)
    {
        invulnTimer = invulnSeconds;
        CurrentSpeed = Mathf.Max(baseSpeed, CurrentSpeed * 0.8f);
    }

    /// <summary>
    /// Mercy window without touching speed - a revive drops you back in at the
    /// pace you were already carrying.
    /// </summary>
    public void GrantMercy(float invulnSeconds)
    {
        invulnTimer = invulnSeconds;
    }

    /// <summary>
    /// Locks the car to one speed for a race - no ramp, no top-speed climb.
    /// Boost pads still work, since they lift the ceiling temporarily.
    /// </summary>
    public void SetConstantSpeed(float speed)
    {
        baseSpeed = speed;
        maxSpeed = speed;
        speedGainPerSecond = 0f;
        CurrentSpeed = speed;
    }

    /// <summary>
    /// Holds the car to a speed it could actually have reached by now. Used
    /// on a revive, so coming back can never hand you a top-speed car.
    /// </summary>
    public void CapSpeed(float limit)
    {
        CurrentSpeed = Mathf.Clamp(CurrentSpeed, baseSpeed, Mathf.Max(baseSpeed, limit));
    }

    static Material scorchedMat;
    bool scorched;

    /// <summary>True once the car has been burnt and needs rebuilding.</summary>
    public bool IsScorched { get { return scorched; } }

    /// <summary>
    /// Chars the car after the fireball. Every material slot on every renderer,
    /// because a car is a multi-material mesh and replacing only the first
    /// leaves the glass and lights looking showroom-fresh on a wreck.
    /// </summary>
    public void Scorch()
    {
        if (carVisual == null) return;
        scorched = true;

        if (scorchedMat == null)
        {
            Shader sh = Resources.Load<Shader>("Shaders/FlatBlack");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            scorchedMat = new Material(sh);
            var soot = new Color(0.055f, 0.05f, 0.05f, 1f);
            scorchedMat.color = soot;
            if (scorchedMat.HasProperty("_BaseColor")) scorchedMat.SetColor("_BaseColor", soot);
            if (scorchedMat.HasProperty("_Metallic")) scorchedMat.SetFloat("_Metallic", 0f);
            if (scorchedMat.HasProperty("_Smoothness")) scorchedMat.SetFloat("_Smoothness", 0.05f);
        }

        foreach (var r in carVisual.GetComponentsInChildren<Renderer>(true))
        {
            var slots = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < slots.Length; i++) slots[i] = scorchedMat;
            r.sharedMaterials = slots;
        }
    }

    /// <summary>Called once the car has been rebuilt clean.</summary>
    public void ClearScorch() { scorched = false; }

    /// <summary>
    /// Sets the speed a run actually begins at. Without this the car simply
    /// carries whatever speed it happened to have: the lobby's slow cruise
    /// into a fresh run, or the speed you died at into a retry.
    /// </summary>
    public void LaunchAt(float speed)
    {
        CurrentSpeed = speed;
        pendingBoost = 0f;
        boostBonus = 0f;
    }

    /// <summary>Cancels any queued or active boost.</summary>
    public void ClearBoost()
    {
        pendingBoost = 0f;
        boostBonus = 0f;
    }

    /// <summary>Small speed reward (e.g. for banking a drift combo).</summary>
    public void Boost(float amount)
    {
        // queued rather than applied instantly, so the surge (and the engine
        // note that follows it) ramps in smoothly. The bonus lifts the speed
        // ceiling too, otherwise a boost at top speed does nothing at all.
        pendingBoost += amount;
        boostBonus = Mathf.Max(boostBonus, amount);
    }

    /// <summary>
    /// Hard limit on sideways position, used when another car is alongside.
    /// The car simply cannot travel further in that direction - no shoving.
    /// </summary>
    public void BlockLateral(float minLat, float maxLat, float dt)
    {
        // resolve the overlap gradually and only cancel the part of the
        // heading pushing into the other car, so steering stays free
        const float Softness = 9f;   // metres per second of separation

        if (LateralOffset > maxLat)
        {
            LateralOffset = Mathf.MoveTowards(LateralOffset, maxLat, Softness * dt);
            if (LateralVelocity > 0f) LateralVelocity = Mathf.MoveTowards(LateralVelocity, 0f, 40f * dt);
        }
        else if (LateralOffset < minLat)
        {
            LateralOffset = Mathf.MoveTowards(LateralOffset, minLat, Softness * dt);
            if (LateralVelocity < 0f) LateralVelocity = Mathf.MoveTowards(LateralVelocity, 0f, 40f * dt);
        }
    }

    /// <summary>
    /// Drives the car forward on the track with no input and no scoring, for
    /// the menu backdrop. Steering, drifting and crashes are all skipped.
    /// </summary>
    public void TickIdle(float dt)
    {
        tickedThisFrame = true;
        if (track == null) return;
        idleMode = true;
        // the showcase drive is on rails - nothing is coasting
        coasting = false;
        driving = true;

        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, baseSpeed, 8f * dt);
        DistanceTraveled += CurrentSpeed * dt;
        track.EnsureGenerated(DistanceTraveled);

        // Look a little up the road and throw the car sideways through the
        // bend, the way a driver showing off would - the tail steps out, the
        // tyres light up, and it gathers itself back on the straights.
        track.SamplePose(DistanceTraveled + showcaseLookAhead,
            out _, out _, out float curveAhead);

        // the pose code needs the curvature under the car as well: it is what
        // points the nose INTO the corner. Without it the body yaws purely on
        // the slide and ends up facing the wrong way through a bend.
        currentCurvDegPerM = FeltCurvature(DistanceTraveled);

        // nose into the bend: positive curvature is a right-hand turn, and a
        // positive yaw points the car right, so the sign carries straight over
        float wantYaw = Mathf.Clamp(curveAhead * CurrentSpeed * showcaseYawGain,
            -showcaseYawMax, showcaseYawMax);

        // A perfectly held angle looks robotic. Two slow noise waves stand in
        // for a driver's corrections, and they grow with the angle so the
        // straights stay clean and the corners get the character.
        float slow = Mathf.PerlinNoise(Time.time * 0.75f, 4.2f) - 0.5f;
        float quick = Mathf.PerlinNoise(Time.time * 2.4f, 9.1f) - 0.5f;

        // The angle itself eases in and out slowly - a drift is entered and
        // gathered up over a beat, not snapped into. The wobble rides on top
        // at its own pace so the corrections stay lively.
        idleYawBase = Mathf.MoveTowards(idleYawBase, wantYaw, showcaseYawRate * dt);
        float wobble = (slow * 0.30f + quick * 0.12f)
                     * (0.25f + Mathf.Abs(idleYawBase) * 2.2f);
        idleYawPercent = idleYawBase + wobble;

        // the slide is only there to feed smoke and skid marks now
        float want = -curveAhead * CurrentSpeed * showcaseDriftGain;
        want = Mathf.Clamp(want, -maxLateralSpeed * 0.5f, maxLateralSpeed * 0.5f);
        LateralVelocity = Mathf.MoveTowards(LateralVelocity, want, 14f * dt);
        slideTarget = LateralVelocity;
        slideRate = 0f;

        // the showcase car holds the middle of the road - the slide is pure
        // presentation, so it must not push the car off the centre line
        LateralOffset = Mathf.MoveTowards(LateralOffset, 0f, 6f * dt);

        IsDrifting = Mathf.Abs(LateralVelocity) > driftThreshold;
        IsSliding = Mathf.Abs(LateralVelocity) > driftThreshold * effectsEarlyFactor;
        ApplyPose(dt);
        SeatOnWheels();

        if (wheelSpin != null)
        {
            wheelSpin.speed = hoverMode ? 0f : CurrentSpeed;
            // counter-steer, same as when the player is driving
            wheelSpin.steerAngle = Mathf.Clamp(-smoothedDriftYaw, -45f, 45f);
        }
    }

    [Header("Menu showcase drive")]
    [Tooltip("How far up the road the showcase car reads the next corner.")]
    public float showcaseLookAhead = 22f;
    [Tooltip("How hard it throws the car sideways through bends.")]
    public float showcaseDriftGain = 0.32f;
    [Tooltip("How far the nose swings into a corner during the showcase.")]
    public float showcaseYawGain = 0.0035f;
    [Tooltip("Largest showcase drift angle, as a fraction of full drift lock.")]
    [Range(0.1f, 1f)] public float showcaseYawMax = 0.4f;
    [Tooltip("How quickly the showcase drift builds and releases. Lower = smoother.")]
    public float showcaseYawRate = 0.55f;
    bool idleMode;
    float idleYawPercent, idleYawBase;

    /// <summary>
    /// Middle of the bodywork in world space - what a camera should aim at to
    /// frame the car, rather than the rig's origin down at road level.
    /// </summary>
    public Vector3 BodyCentre
    {
        get
        {
            if (!bodyCentreMeasured) MeasureBodyCentre();
            // Deliberately hung off the rig, not the visual: the body wags
            // about while drifting, and following that wag would shake the
            // camera. The rig glides smoothly down the road.
            return transform.TransformPoint(new Vector3(0f, bodyCentreHeight, 0f));
        }
    }

    bool bodyCentreMeasured;
    float bodyCentreHeight = 0.65f;

    /// <summary>
    /// Height of the middle of the bodywork above the road, measured once from
    /// the mesh. Trails and particle systems are ignored - their bounds swing
    /// around far beyond the car.
    /// </summary>
    void MeasureBodyCentre()
    {
        Transform host = carVisual != null ? carVisual : transform;
        var rends = host.GetComponentsInChildren<MeshRenderer>(false);
        if (rends.Length == 0) return;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        bodyCentreHeight = Mathf.Clamp(b.center.y - transform.position.y, 0.2f, 2.5f);
        bodyCentreMeasured = true;
    }

    /// <summary>
    /// Drops the car back in at a distance it was already at - used when the
    /// model is swapped mid-shot so it does not jump back to the start.
    /// </summary>
    public void ResumeFrom(float distance)
    {
        DistanceTraveled = distance;
        if (track != null) track.EnsureGenerated(DistanceTraveled);
        ApplyPose(0f);
    }

    /// <summary>Places the car across the road without touching its speed.</summary>
    public void SetLateral(float lateral)
    {
        LateralOffset = lateral;
        LateralVelocity = 0f;
        ClearCoast();
        ApplyPose(0f);
    }

    /// <summary>Snap to the centre of the road (used on revive).</summary>
    public void CenterLane()
    {
        LateralOffset = 0f;
        LateralVelocity = 0f;
        spinTimer = 0f;
        ClearCoast();
        ApplyPose(0f);
    }

    public void TriggerSpinOut()
    {
        if (spinTimer > 0f) return;
        spinTimer = spinDuration;
        spinDir = Random.value < 0.5f ? -1f : 1f;
        LateralVelocity = Mathf.Clamp(LateralVelocity + spinDir * 6f, -maxLateralSpeed, maxLateralSpeed);
        // Floored below the starting speed, not at it. Flooring at baseSpeed
        // meant oil cost nothing early in a run - and could even hand speed
        // back - because the car has barely climbed above it by then.
        CurrentSpeed = Mathf.Max(baseSpeed * 0.8f, CurrentSpeed * spinSpeedPenalty);
    }

    // ------------------------------------------------------------------ input

    float ReadSteerInput()
    {
        // touch or mouse: thumb-slide relative to where the finger went down
        if (TryGetSteerPointer(out float x, out bool fresh))
        {
            inputHeld = true;
            if (fresh || !anchorActive)
            {
                touchAnchorX = x;
                anchorActive = true;
            }
            return SteerFromAnchor(x) * (thumbControlsRear ? -1f : 1f);
        }

        anchorActive = false;

        // keyboard fallback for the editor: holding a steering key - or
        // up / W to hold it straight - counts as a hand on the wheel
        float key = Input.GetAxisRaw("Horizontal");
        inputHeld = key != 0f || Input.GetAxisRaw("Vertical") > 0f;
        return key;
    }

    /// <summary>
    /// Finds the finger doing the steering. It sticks with the same finger for
    /// as long as it is down; if that one lifts while another is still on the
    /// screen, steering carries on with the other, re-anchored where it is so
    /// nothing jumps. Only when no finger is down has the player let go.
    /// </summary>
    bool TryGetSteerPointer(out float x, out bool fresh)
    {
        bool found = FindSteerPointer(out x, out fresh);
        lastPointerX = found ? x : -1f;
        return found;
    }

    bool FindSteerPointer(out float x, out bool fresh)
    {
        x = 0f;
        fresh = false;

        int count = Input.touchCount;
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                Touch t = Input.GetTouch(i);
                if (t.fingerId == steerFingerId && TouchActive(t))
                {
                    x = t.position.x;
                    return true;
                }
            }
            for (int i = 0; i < count; i++)
            {
                Touch t = Input.GetTouch(i);
                if (!TouchActive(t)) continue;
                steerFingerId = t.fingerId;
                x = t.position.x;
                fresh = true;
                return true;
            }
            steerFingerId = -1;
            return false;
        }
        steerFingerId = -1;

        // mouse drag: editor / desktop testing
        if (Input.GetMouseButton(0))
        {
            x = Input.mousePosition.x;
            fresh = Input.GetMouseButtonDown(0);
            return true;
        }
        return false;
    }

    static bool TouchActive(Touch t)
    {
        return t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
    }

    float SteerFromAnchor(float x)
    {
        float range = Screen.width * steerZoneFraction;
        float steer = (x - touchAnchorX) / range;

        // if the thumb slides past full lock, drag the anchor along so
        // reversing direction responds instantly
        if (steer > 1f) { touchAnchorX = x - range; steer = 1f; }
        else if (steer < -1f) { touchAnchorX = x + range; steer = -1f; }

        return steer;
    }

    // ------------------------------------------------------------ recording

    /// <summary>Frame number of the last Tick - the recorder logs only frames the car was driven.</summary>
    public int LastTickFrame { get; private set; }
    float lastRawSteer;
    float lastPointerX = -1f;

    public const string DebugHeader =
        "t,dt,pointerX,rawSteer,steer,driving,speed,dist,latOffset,latVel,slideVel," +
        "slideTarget,slideRate,curv,driftPct,bodyYaw,bodyYawRate,bank,rigYaw,bodyWorldYaw," +
        "headingOffset,visualY,drifting";

    /// <summary>One line of numbers for DriveRecorder. Read-only - changes nothing.</summary>
    public string DebugRow()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        float bodyWorldYaw = carVisual != null ? carVisual.eulerAngles.y : 0f;
        return string.Format(ci,
            "{0:0.0000},{1:0.0000},{2:0.0},{3:0.0000},{4:0.0000},{5},{6:0.000},{7:0.00},{8:0.0000},{9:0.0000},{10:0.0000}," +
            "{11:0.0000},{12:0.000},{13:0.0000},{14:0.0000},{15:0.000},{16:0.00},{17:0.000},{18:0.000},{19:0.000}," +
            "{20:0.000},{21:0.0000},{22}",
            Time.time, Time.deltaTime, lastPointerX, lastRawSteer, smoothedSteer, driving ? 1 : 0,
            CurrentSpeed, DistanceTraveled, LateralOffset, LateralVelocity, SlideVelocity,
            slideTarget, slideRate, currentCurvDegPerM, currentDriftPercent, smoothedDriftYaw,
            driftYawRate, smoothedBank, transform.eulerAngles.y, bodyWorldYaw,
            headingOffset, appliedVisualY, IsDrifting ? 1 : 0);
    }
}
