using UnityEngine;

/// <summary>
/// Drift car that follows the procedural track. The car auto-accelerates;
/// the player slides a thumb left/right to control lateral drift.
/// Corners push the car outward (centrifugal force), so holding a clean
/// line through a corner at speed IS the drift. GameManager drives Tick().
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
    [Tooltip("Flips all steering input (settings menu option).")]
    public bool invertSteering = false;

    [Header("Hover car")]
    public bool hoverMode = false;
    public float hoverHeight = 0.5f;
    [Tooltip("How hard the hover car banks into drifts.")]
    public float hoverBankAngle = 22f;
    [Tooltip("Drift intensity above which the rim scrapes and sparks fly.")]
    public float sparkThreshold = 0.35f;
    float appliedVisualY;
    float currentDriftPercent;
    float smoothedDriftYaw;
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
    public float steerAcceleration = 42f;
    public float maxLateralSpeed = 17f;
    [Tooltip("How fast sideways speed decays when not steering.")]
    public float grip = 26f;
    [Tooltip("How quickly steering input is eased in. Lower = smoother, laggier.")]
    public float steerSmoothing = 15f;
    [Tooltip("How fast a slide bleeds off with no steering input.")]
    public float gripDecayFree = 6.5f;
    [Tooltip("How fast a slide bleeds off while a steer is held. Low = long drifts.")]
    public float gripDecaySteering = 1.6f;
    [Tooltip("Extra steering authority when catching a slide.")]
    public float counterSteerBoost = 1.6f;
    [Tooltip("Angle a held counter-steer settles at, as a share of maximum.")]
    [Range(0.3f, 1f)] public float driftHoldFraction = 0.66f;
    [Tooltip("How firmly the held angle is locked in.")]
    public float driftHoldGrab = 3.2f;
    float smoothedSteer;
    [Tooltip("Strength of outward push in corners. The core difficulty knob.")]
    public float centrifugalFactor = 0.85f;
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
    }

    Quaternion BaseVisualRotation()
    {
        return flipVisual180 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
    }

    /// <summary>Advance the simulation one frame. Called by GameManager while playing.</summary>
    public TickResult Tick(float dt)
    {
        tickedThisFrame = true;
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

        // --- input (no control while spinning out)
        float steer = ReadSteerInput();
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
        track.SamplePose(DistanceTraveled, out _, out _, out float curvDegPerM);
        currentCurvDegPerM = curvDegPerM;
        float curvRadPerM = curvDegPerM * Mathf.Deg2Rad;

        // positive curvature = right turn = outward push to the LEFT (negative lateral)
        float centrifugal = -curvRadPerM * CurrentSpeed * CurrentSpeed * centrifugalFactor;

        // Steering into a slide that is already going the other way is a
        // counter-steer: give it extra bite, because catching and holding the
        // angle is the skill the whole game is built on.
        bool countering = steer * LateralVelocity < 0f;
        float authority = steerAcceleration * (countering ? counterSteerBoost : 1f);
        LateralVelocity += (steer * authority + centrifugal) * dt;

        // Grip bleeds sideways speed away proportionally, so a slide tails off
        // instead of stopping dead. Hands off, it settles quickly; while you
        // hold a steer it barely bleeds at all, so the drift keeps going.
        float decay = spinTimer > 0f ? 0.6f
                    : Mathf.Abs(steer) > 0.05f ? gripDecaySteering
                    : gripDecayFree;
        LateralVelocity *= Mathf.Exp(-decay * dt);

        // The sweet spot: counter-steering out of a decent slide parks the car
        // at a held angle rather than letting it wash out or spin further.
        float slideFrac = Mathf.Abs(LateralVelocity) / maxLateralSpeed;
        if (countering && slideFrac > 0.40f && spinTimer <= 0f)
        {
            float hold = Mathf.Sign(LateralVelocity) * maxLateralSpeed * driftHoldFraction;
            LateralVelocity = Mathf.Lerp(LateralVelocity, hold,
                1f - Mathf.Exp(-driftHoldGrab * dt));
        }
        LateralVelocity = Mathf.Clamp(LateralVelocity, -maxLateralSpeed, maxLateralSpeed);

        LateralOffset += LateralVelocity * dt;
        DistanceTraveled += CurrentSpeed * dt;

        track.EnsureGenerated(DistanceTraveled);

        // Drifting means the player is actively throwing the car sideways -
        // simply being pushed through a corner does not count, otherwise the
        // car "drifts" every bend without any input.
        // Purely a question of how far the car has stepped out - being carried
        // wide through a corner counts as a drift too, which is what makes the
        // combo build naturally on a fast line.
        IsDrifting = Mathf.Abs(LateralVelocity) > driftThreshold;
        // Smoke, skid marks and the drift sound come on with the INPUT, not
        // with the slide - touch the screen and the car is already laying
        // rubber, long before it has stepped out far enough to score.
        IsSliding = Mathf.Abs(smoothedSteer) > 0.015f ||
                    Mathf.Abs(LateralVelocity) > driftThreshold * effectsEarlyFactor;

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
            wheelSpin.steerAngle = Mathf.Clamp(
                -smoothedDriftYaw + steer * frontWheelSteerAngle * 0.35f, -45f, 45f);
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
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        if (carVisual == null) return;

        // sideways slide + nose-into-corner pose while cornering at speed
        float slideTerm = LateralVelocity / maxLateralSpeed;
        float cornerTerm = currentCurvDegPerM * CurrentSpeed * cornerDriftVisual / driftAngle;
        float driftPercent = idleMode
            ? Mathf.Clamp(idleYawPercent, -1f, 1f)
            : Mathf.Clamp(slideTerm + cornerTerm, -1f, 1f);
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

        // hover car: banks hard into drifts, dipping until the rim kisses
        // the road (handling is untouched - this is pure animation)
        float hoverPitch = 0f;
        float bank = -driftPercent * driftBankAngle;
        if (hoverMode && carVisual != null)
        {
            float dip = Mathf.Abs(driftPercent);
            float bob = Mathf.Sin(Time.time * 2.4f) * 0.12f * (1f - dip);
            float height = hoverHeight * (1f - 0.7f * dip);
            carVisual.localPosition = new Vector3(0f, appliedVisualY + height + bob + jumpY, 0f);
            hoverPitch = Mathf.Sin(Time.time * 1.7f) * 2.2f * (1f - dip);
            bank = -driftPercent * hoverBankAngle;
        }
        else if (carVisual != null && centerVisual)
        {
            carVisual.localPosition = new Vector3(0f, appliedVisualY + jumpY, 0f);
        }

        // ease the drift angle itself, so the body and its pivot stay in sync
        smoothedDriftYaw = dt > 0f
            ? Mathf.LerpAngle(smoothedDriftYaw, visualYaw, Mathf.Clamp01(driftRotationSpeed * dt))
            : visualYaw;
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
        bool emit = IsSliding || IsDrifting || IsSpinning;
        // hover cars leave no tire marks (the smoke stays - reads as hover dust)
        trailL.emitting = emit && !hoverMode;
        trailR.emitting = emit && !hoverMode;

        if (smokeL != null)
        {
            smokeL.transform.position = trailL.transform.position + Vector3.up * 0.12f;
            smokeR.transform.position = trailR.transform.position + Vector3.up * 0.12f;
            // harder slides = thicker smoke; hover cars make none
            float intensity = Mathf.Clamp01(Mathf.Abs(LateralVelocity) / maxLateralSpeed + (IsSpinning ? 0.6f : 0f));
            // a decent puff right away, building to a cloud in a full slide
            float rate = emit && !hoverMode ? smokeRate * (0.85f + 1.5f * intensity) : 0f;
            var eL = smokeL.emission; eL.rateOverTime = rate;
            var eR = smokeR.emission; eR.rateOverTime = rate;
        }

        // hover car: sparks fly from the rim on the side it's banking toward
        if (sparkL != null)
        {
            Vector3 sparkBase = new Vector3(bodyPos.x, transform.position.y + 0.07f, bodyPos.z);
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
        wheelSeatTries = 0;   // re-measured for the new model
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
    int wheelSeatTries;

    /// <summary>
    /// Drops the fitted model so its WHEELS rest on the road. Model bounds are
    /// unreliable - spoilers, mirrors and stray parts all move the lowest
    /// point - but the wheels are by definition where the car meets the
    /// ground, so once they have been identified they are the truth.
    /// </summary>
    void SeatOnWheels()
    {
        if (wheelSeatDone || carVisual == null) return;
        if (wheelSpin == null || wheelSeatTries > 90) { wheelSeatDone = true; return; }

        wheelSeatTries++;
        if (!wheelSpin.TryGetWheelBottom(out float bottom)) return;

        float delta = transform.position.y - bottom;
        if (Mathf.Abs(delta) > 0.004f)
        {
            baseVisualY += delta;
            appliedVisualY += delta;
            carVisual.localPosition += new Vector3(0f, delta, 0f);
        }

        // and put the skid marks under the real rear tyres rather than at a
        // guessed fraction of the bodywork
        if (wheelSpin.TryGetRearWheelOffsets(transform, out float hw, out float back))
        {
            rearWheelHalfWidth = Mathf.Clamp(hw, 0.3f, 1.4f);
            rearWheelBack = Mathf.Clamp(back, 0.4f, 2.2f);
        }

        wheelSeatDone = true;
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
            if (objName.Contains("decal") || objName.Contains("logo")
                || objName.Contains("badge") || objName.Contains("text")
                || objName.Contains("emblem") || objName.Contains("sticker")
                || objName.Contains("brand") || objName.Contains("marque")
                || objName.Contains("letter") || objName.Contains("numberplate")
                || objName.Contains("licence") || objName.Contains("license"))
            {
                r.enabled = false;
                continue;
            }

            // anything sitting below the car's midline is not a window
            if (r.bounds.center.y < midHeight) continue;

            Material[] mats = r.sharedMaterials;
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
        track.SamplePose(DistanceTraveled, out _, out _, out float curveHere);
        currentCurvDegPerM = curveHere;

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
        ApplyPose(0f);
    }

    /// <summary>Snap to the centre of the road (used on revive).</summary>
    public void CenterLane()
    {
        LateralOffset = 0f;
        LateralVelocity = 0f;
        spinTimer = 0f;
        ApplyPose(0f);
    }

    public void TriggerSpinOut()
    {
        if (spinTimer > 0f) return;
        spinTimer = spinDuration;
        spinDir = Random.value < 0.5f ? -1f : 1f;
        LateralVelocity = Mathf.Clamp(LateralVelocity + spinDir * 6f, -maxLateralSpeed, maxLateralSpeed);
        CurrentSpeed = Mathf.Max(baseSpeed, CurrentSpeed * spinSpeedPenalty);
    }

    // ------------------------------------------------------------------ input

    float ReadSteerInput()
    {
        // touch: thumb-slide relative to where the finger went down
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began || !anchorActive)
            {
                touchAnchorX = t.position.x;
                anchorActive = true;
            }
            return SteerFromAnchor(t.position.x) * (thumbControlsRear ? -1f : 1f);
        }

        // mouse drag: editor / desktop testing
        if (Input.GetMouseButton(0))
        {
            if (Input.GetMouseButtonDown(0) || !anchorActive)
            {
                touchAnchorX = Input.mousePosition.x;
                anchorActive = true;
            }
            return SteerFromAnchor(Input.mousePosition.x) * (thumbControlsRear ? -1f : 1f);
        }

        anchorActive = false;

        // keyboard fallback
        return Input.GetAxisRaw("Horizontal");
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
}
