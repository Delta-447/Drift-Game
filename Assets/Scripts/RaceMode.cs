using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Race mode data and AI opponents. 4 biomes x 5 levels; beat the field to
/// unlock the next. GameManager drives this - it owns no UI itself.
/// </summary>
public class RaceMode : MonoBehaviour
{
    public const int BiomeCount = 4;
    public const int LevelsPerBiome = 5;
    public const int TotalLevels = BiomeCount * LevelsPerBiome;
    public const int Opponents = 5;

    public static readonly string[] BiomeNames =
        { "COUNTRYSIDE", "SUNSET", "CITY", "SNOW PEAKS" };

    /// <summary>Biome blend values (day/night, snow) for each race biome.</summary>
    public static void BlendsForBiome(int biomeIndex, out float dayNight, out float snow)
    {
        switch (biomeIndex)
        {
            case 0: dayNight = 0f; snow = 0f; break;      // bright countryside
            case 1: dayNight = 0.45f; snow = 0f; break;   // golden sunset
            case 2: dayNight = 1f; snow = 0f; break;      // night city
            default: dayNight = 1f; snow = 1f; break;     // snowy mountains
        }
    }


    public static int BiomeOf(int level) { return level / LevelsPerBiome; }
    public static int StageOf(int level) { return level % LevelsPerBiome; }

    /// <summary>Race length grows through each biome.</summary>
    public static float RaceDistance(int level)
    {
        return 900f + StageOf(level) * 260f + BiomeOf(level) * 350f;
    }

    /// <summary>Coin reward for finishing first.</summary>
    public static int Reward(int level)
    {
        return 400 + level * 220;
    }

    public static bool IsUnlocked(int level)
    {
        return level == 0 || PlayerPrefs.GetInt("RaceDone" + (level - 1), 0) == 1;
    }

    public static bool IsCompleted(int level)
    {
        return PlayerPrefs.GetInt("RaceDone" + level, 0) == 1;
    }

    public static int BestPlace(int level)
    {
        return PlayerPrefs.GetInt("RacePlace" + level, 0); // 0 = unraced
    }

    public static void RecordResult(int level, int place)
    {
        if (place == 1) PlayerPrefs.SetInt("RaceDone" + level, 1);
        int prev = BestPlace(level);
        if (prev == 0 || place < prev) PlayerPrefs.SetInt("RacePlace" + level, place);
        PlayerPrefs.Save();
    }

    // ------------------------------------------------------------ upgrades

    public enum Upgrade { Speed = 0, Strength = 1, CoinBonus = 2 }
    public static readonly string[] UpgradeNames = { "ENGINE", "CHASSIS", "COIN BONUS" };
    static readonly int[] UpgradeMaxLevel = { 20, 10, 90 };
    static readonly int[] UpgradeBaseCost = { 900, 1400, 500 };

    public static int Level(Upgrade u) { return PlayerPrefs.GetInt("Upg" + (int)u, 0); }
    public static int MaxLevel(Upgrade u) { return UpgradeMaxLevel[(int)u]; }
    public static bool IsMaxed(Upgrade u) { return Level(u) >= MaxLevel(u); }

    /// <summary>Cost climbs steeply so late levels are a real investment.</summary>
    public static int CostOf(Upgrade u)
    {
        int lv = Level(u);
        return Mathf.RoundToInt(UpgradeBaseCost[(int)u] * Mathf.Pow(1.18f, lv));
    }

    public static void Buy(Upgrade u)
    {
        PlayerPrefs.SetInt("Upg" + (int)u, Level(u) + 1);
        PlayerPrefs.Save();
    }

    /// <summary>Extra top speed in m/s from the engine upgrade.</summary>
    public static float SpeedBonus() { return Level(Upgrade.Speed) * 0.55f; }

    /// <summary>0 = stock chassis, 1 = fully reinforced.</summary>
    public static float Strength() { return Level(Upgrade.Strength) / (float)MaxLevel(Upgrade.Strength); }

    /// <summary>1.0 up to 10.0, in tenths.</summary>
    public static float CoinMultiplier() { return 1f + Level(Upgrade.CoinBonus) * 0.1f; }

    /// <summary>Shown without decimals: "+40%" rather than "1.4x".</summary>
    public static string CoinBonusLabel() { return "+" + (Level(Upgrade.CoinBonus) * 10) + "%"; }

    // ------------------------------------------------------------ opponents

    public const int Lanes = 3;

    public class Racer
    {
        public GameObject go;
        public Transform visual;   // child that gets the drift angle
        public TrailRenderer skidL, skidR;
        public WheelSpinner wheels;
        public float distance;
        public float lateral;
        public float lateralVel;
        public float smoothLatVel;
        public float driftYaw;
        public int lane;
        public float laneCooldown;
        public float boostTimer;
        public float boostCooldown;
        public float speed;
        public float baseSpeed;
        public float paceFactor;
        public float weavePhase;
        public float weaveRate;
        public string name;
        public bool finished;
        public int place;
    }

    int finishedCount;
    readonly List<Racer> racers = new List<Racer>();
    public List<Racer> Racers { get { return racers; } }

    TrackGenerator track;

    public void Setup(TrackGenerator trackGen)
    {
        track = trackGen;
    }

    public void ClearRacers()
    {
        foreach (var r in racers)
        {
            if (r.go != null) Destroy(r.go);
        }
        racers.Clear();
    }

    /// <summary>
    /// Builds the AI field. Their pace is tuned around the player's own speed
    /// curve so early levels are winnable and later ones demand clean drifting.
    /// </summary>
    /// <param name="makeVisual">
    /// Supplied by GameManager: builds a correctly scaled, oriented car model
    /// (same routine the garage uses) for the given opponent index.
    /// </param>
    public void BuildField(int level, float startDistance, float stockMaxSpeed,
        System.Func<int, GameObject> makeVisual)
    {
        ClearRacers();
        finishedCount = 0;

        // level 1 is comfortably beatable in a stock car; by the last levels
        // the field is far quicker than a stock car and only a well-upgraded
        // engine keeps up. Pace is a fraction of the stock race speed.
        // Each level is worth roughly a level and a half of engine upgrade, so
        // the field pulls away from a car you have stopped improving. Level 1
        // is a comfortable win in a stock car; level 20 needs the engine maxed
        // and a clean run through the boost pads.
        float levelPace = 0.86f + level * 0.036f;   // 0.86 -> 1.54

        for (int i = 0; i < Opponents; i++)
        {
            // Grid forms up in front of the player but still short of the
            // start line, so every race begins from the back of the pack.
            int lane = i % Lanes;
            float gridDistance = startDistance + 7f + (i / Lanes) * 7f;

            var r = new Racer
            {
                distance = gridDistance,
                lane = lane,
                lateral = LaneCentre(lane),
                laneCooldown = Random.Range(2f, 6f),
                weavePhase = Random.Range(0f, Mathf.PI * 2f),
                weaveRate = Random.Range(0.30f, 0.55f),
                name = "RIVAL " + (i + 1),
            };
            // each rival is a small percentage off the player's pace; the
            // actual speed is matched to the player every frame in Tick()
            // a tight spread, so the field is a wall to get through rather
            // than one quick car and four stragglers
            r.paceFactor = levelPace * Random.Range(0.985f, 1.035f);
            r.baseSpeed = stockMaxSpeed * r.paceFactor;
            // everyone leaves the line at race pace - the player does too, so
            // nobody appears to rocket away from the field
            r.speed = r.baseSpeed;

            var root = new GameObject("Racer" + i);
            root.transform.SetParent(transform, false);

            GameObject visual = makeVisual != null ? makeVisual(i) : null;
            if (visual != null)
            {
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
            }
            r.go = root;
            r.visual = visual != null ? visual.transform : null;
            if (r.visual != null)
            {
                r.skidL = MakeSkid(r.visual, new Vector3(-0.62f, 0.06f, -1.2f));
                r.skidR = MakeSkid(r.visual, new Vector3(0.62f, 0.06f, -1.2f));
                r.wheels = WheelSpinner.Attach(visual, root.transform);
            }
            racers.Add(r);
        }
        PlaceAll();   // visible on the grid during the countdown
    }

    /// <summary>Snaps every rival onto the track without advancing them.</summary>
    public void PlaceAll()
    {
        foreach (var r in racers)
        {
            if (r.go == null) continue;
            track.SamplePose(r.distance, out Vector3 pos, out Vector3 fwd, out _);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            r.go.transform.position = pos + right * r.lateral;
            r.go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }
    }

    public float LaneCentre(int lane)
    {
        float laneWidth = track.roadWidth / Lanes;
        return (lane - (Lanes - 1) * 0.5f) * laneWidth;
    }

    static TrailRenderer MakeSkid(Transform parent, Vector3 localPos)
    {
        var go = new GameObject("RivalSkid");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 2.2f;
        tr.startWidth = 0.24f;
        tr.endWidth = 0.1f;
        tr.minVertexDistance = 0.2f;
        tr.numCapVertices = 2;
        tr.alignment = LineAlignment.TransformZ;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        tr.material = new Material(sh) { color = new Color(0.07f, 0.07f, 0.09f, 0.75f) };
        tr.emitting = false;
        return tr;
    }

    /// <summary>True when no rival occupies this lane near that distance.</summary>
    bool LaneFree(Racer self, int lane, float dist)
    {
        foreach (var o in racers)
        {
            if (o == self || o.finished) continue;
            if (o.lane != lane) continue;
            if (Mathf.Abs(o.distance - dist) < 14f) return false;
        }
        return true;
    }

    /// <summary>Advances every AI car. Returns how many have crossed the line.</summary>
    public float stockSpeedReference = 30f;

    public void Tick(float dt, float finishDistance, float playerDistance, float playerSpeed)
    {
        float maxLat = track.roadWidth * 0.5f - 1.8f;

        for (int i = 0; i < racers.Count; i++)
        {
            Racer r = racers[i];

            // rivals accelerate alongside the player: their speed tracks the
            // player's current speed, so the whole field ramps up together
            float lead = r.distance - playerDistance;
            // only a whisker of rubber-banding: enough to keep the field
            // together, not enough to hand the race back to a slow car
            float rubber = Mathf.Clamp(1f - lead / 320f, 0.985f, 1.02f);
            // fixed race pace - nobody accelerates as the race goes on
            float target = stockSpeedReference * r.paceFactor * rubber;
            // never drive into the back of a rival in the same lane
            foreach (var o in racers)
            {
                if (o == r || o.finished) continue;
                float ahead = o.distance - r.distance;
                if (ahead > 0f && ahead < 7f && Mathf.Abs(o.lateral - r.lateral) < 2.2f)
                {
                    target = Mathf.Min(target, o.speed * 0.94f);
                }
            }
            // rivals use the boost pads as well
            r.boostCooldown -= dt;
            r.boostTimer -= dt;
            if (r.boostCooldown <= 0f && track.IsOnBoostPad(r.distance, r.lateral, 1.1f))
            {
                r.boostTimer = 1.1f;
                r.boostCooldown = 2.5f;
            }
            if (r.boostTimer > 0f) target += 13f;

            r.speed = Mathf.MoveTowards(r.speed, target, (r.boostTimer > 0f ? 45f : 8f) * dt);
            r.distance += r.speed * dt;

            // --- lane discipline: hold a lane, change only when one is clear
            r.laneCooldown -= dt;
            if (r.laneCooldown <= 0f)
            {
                r.laneCooldown = Random.Range(2.5f, 6f);
                int dir = Random.value < 0.5f ? -1 : 1;
                int want = Mathf.Clamp(r.lane + dir, 0, Lanes - 1);
                if (want != r.lane && LaneFree(r, want, r.distance)) r.lane = want;
            }

            // ease onto the lane centre; no shoving, no fighting
            float targetLat = LaneCentre(r.lane);
            float prevLat = r.lateral;
            r.lateral = Mathf.MoveTowards(r.lateral, targetLat, 4.5f * dt);
            r.lateral = Mathf.Clamp(r.lateral, -maxLat, maxLat);
            r.lateralVel = dt > 0f ? (r.lateral - prevLat) / dt : 0f;
            // smoothed so the car body never twitches frame to frame
            r.smoothLatVel = Mathf.Lerp(r.smoothLatVel, r.lateralVel, 6f * dt);

            if (r.go != null)
            {
                track.SamplePose(r.distance, out Vector3 pos, out Vector3 fwd, out float curv);
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                r.go.transform.position = pos + right * r.lateral;
                r.go.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

                // drift pose: mostly driven by the corner, so the whole field
                // leans through bends together and looks deliberate
                if (r.wheels != null)
                {
                    r.wheels.speed = r.speed;
                    // rivals point their wheels into whatever they are doing
                    // same counter-steer as the player: cancel the body's
                    // drift so the wheels aim down the road
                    r.wheels.steerAngle = Mathf.Clamp(-r.driftYaw, -45f, 45f);
                }
                if (r.visual != null)
                {
                    float corner = Mathf.Clamp(curv * r.speed * 0.055f, -1f, 1f);
                    float slide = Mathf.Clamp(r.smoothLatVel / 6f, -1f, 1f);
                    float wantYaw = Mathf.Clamp((corner * 0.85f + slide * 0.3f) * 30f, -30f, 30f);
                    r.driftYaw = Mathf.Lerp(r.driftYaw, wantYaw, 5f * dt);

                    r.visual.localRotation = Quaternion.Euler(
                        0f, r.driftYaw, -r.driftYaw * 0.18f);

                    bool drifting = Mathf.Abs(r.driftYaw) > 9f;
                    if (r.skidL != null) r.skidL.emitting = drifting;
                    if (r.skidR != null) r.skidR.emitting = drifting;
                }
            }

            // they keep driving off down the road after the line rather than
            // parking on it, but their finishing order is locked in here
            if (!r.finished && r.distance >= finishDistance)
            {
                r.finished = true;
                r.place = ++finishedCount;
            }
        }
    }

    /// <summary>
    /// Works out how far left and right the player may move before running
    /// into a rival. Cars block each other solidly - nobody gets shoved.
    /// </summary>
    public bool GetPlayerLateralLimits(float playerDist, float playerLat, float carRadius,
        float strength, float dt, out float minLat, out float maxLat)
    {
        minLat = float.NegativeInfinity;
        maxLat = float.PositiveInfinity;
        bool touching = false;
        float minGap = 0.92f + carRadius;

        foreach (var r in racers)
        {
            if (r.finished || r.go == null) continue;
            // must really be side by side - not just near in the queue
            if (Mathf.Abs(r.distance - playerDist) > 1.9f + carRadius) continue;

            float gap = playerLat - r.lateral;
            if (Mathf.Abs(gap) < minGap + 0.05f)
            {
                touching = true;
                // a reinforced chassis shoves rivals aside instead of being
                // stopped dead by them
                if (strength > 0.01f)
                {
                    float shove = (1.5f + strength * 7f) * dt;
                    r.lateral -= Mathf.Sign(gap == 0f ? 1f : gap) * shove;
                    float lim = track.roadWidth * 0.5f - 1.6f;
                    r.lateral = Mathf.Clamp(r.lateral, -lim, lim);
                    r.speed *= 1f - 0.25f * strength * dt;
                }
            }

            // the stronger the car, the less contact holds it up
            float give = minGap * (1f - 0.75f * strength);
            if (r.lateral > playerLat) maxLat = Mathf.Min(maxLat, r.lateral - give);
            else minLat = Mathf.Max(minLat, r.lateral + give);
        }
        return touching;
    }

    /// <summary>The player's current position in the field (1 = leading).</summary>
    public int PlayerPlace(float playerDistance)
    {
        int ahead = 0;
        foreach (var r in racers)
        {
            // A rival that has already crossed the line is ahead of the player
            // no matter where it is now - without this the player was counted
            // first the moment they caught the finishers' final positions.
            if (r.finished || r.distance > playerDistance) ahead++;
        }
        return ahead + 1;
    }
}
