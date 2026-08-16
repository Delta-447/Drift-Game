using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds an endless drift track at runtime: straights and corners, road mesh,
/// edge posts, trees, and varied obstacles (fallen logs, construction sites,
/// oil spills). Generates ahead of the car and cleans up behind it.
/// </summary>
public class TrackGenerator : MonoBehaviour
{
    public enum ObstacleHit { None, Solid, OilSpill }
    public enum Biome { Forest, NightCity, SnowMountains }

    [Header("Biome")]
    public Biome biome = Biome.Forest;

    [Header("Road")]
    public float roadWidth = 11f;
    public float aheadDistance = 320f;
    public float behindDistance = 45f;
    [Tooltip("How much road exists behind the car at the start line.")]
    public float roadBehindStart = 40f;

    [Header("Difficulty (0 -> 1 over this many meters)")]
    public float difficultyRampMeters = 2600f;

    [Header("Obstacles")]
    public bool spawnObstacles = true;
    public float firstObstacleAt = 130f;
    public float obstacleSpacingStart = 60f;
    public float obstacleSpacingMin = 24f;
    public float nearMissRange = 2.6f;

    [Header("Trees")]
    public bool spawnTrees = true;
    public float treeSpacing = 3.5f;
    public float treeMinFromRoad = 2.5f;
    public float treeMaxFromRoad = 32f;
    [Range(0f, 1f)] public float treeDensity = 0.95f;
    public float treeHeightMin = 5.5f;
    public float treeHeightMax = 10.5f;

    static readonly string[] TreeModelNames =
    {
        "Nature/CommonTree_1", "Nature/CommonTree_2", "Nature/CommonTree_3",
        "Nature/CommonTree_4", "Nature/CommonTree_5",
        "Nature/PineTree_1", "Nature/PineTree_2", "Nature/PineTree_3",
        "Nature/PineTree_4", "Nature/PineTree_5",
        "Nature/BirchTree_1", "Nature/BirchTree_2", "Nature/BirchTree_3",
        "Nature/BirchTree_4", "Nature/BirchTree_5",
    };
    static readonly string[] BushModelNames =
    {
        "Nature/Bush_1", "Nature/Bush_2", "Nature/BushBerries_1", "Nature/BushBerries_2",
    };
    static readonly string[] SnowModelNames =
    {
        "Nature/CommonTree_Snow_1", "Nature/CommonTree_Snow_2", "Nature/CommonTree_Snow_3",
        "Nature/CommonTree_Snow_4", "Nature/CommonTree_Snow_5",
        "Nature/PineTree_Snow_1", "Nature/PineTree_Snow_2", "Nature/PineTree_Snow_3",
        "Nature/PineTree_Snow_4", "Nature/PineTree_Snow_5",
        "Nature/BirchTree_Snow_1", "Nature/BirchTree_Snow_2", "Nature/BirchTree_Snow_3",
    };
    static readonly string[] SnowRockNames =
    {
        "Nature/Rock_Snow_1", "Nature/Rock_Snow_2", "Nature/Rock_Snow_3",
        "Nature/Bush_Snow_1", "Nature/Bush_Snow_2",
    };
    List<GameObject> treePrefabs, bushPrefabs, snowPrefabs, snowRockPrefabs;

    [Tooltip("Menu backdrop mode: the road stays dead straight.")]
    public bool forceStraight;
    float straightUntil = -1f;

    /// <summary>
    /// Keeps the next stretch of new road straight. Used when a run begins so
    /// the player is not dropped straight into a corner.
    /// </summary>
    public void RequestStraightFor(float meters)
    {
        straightUntil = frontDistance + meters;
    }

    /// <summary>
    /// Pushes every spawner's next slot out to <paramref name="dist"/>, so
    /// turning them back on does not immediately dump a backlog of items onto
    /// the road right in front of the car.
    /// </summary>
    public void ResumeSpawnsAt(float dist)
    {
        nextObstacleDistance = Mathf.Max(nextObstacleDistance, dist);
        nextCoinRunDistance = Mathf.Max(nextCoinRunDistance, dist);
        nextPowerUpDistance = Mathf.Max(nextPowerUpDistance, dist + 60f);
        nextTrafficDistance = Mathf.Max(nextTrafficDistance, dist + 40f);
        nextTireDistance = Mathf.Max(nextTireDistance, dist + 200f);
    }

    [Header("Coins")]
    public bool spawnCoins = true;
    public float coinRunSpacingMin = 40f;
    public float coinRunSpacingMax = 85f;

    [Header("Traffic")]
    public bool spawnTraffic = true;
    [Tooltip("No traffic before this track distance.")]
    public float trafficStartAt = 220f;
    public float trafficSpacingStart = 140f;
    public float trafficSpacingMin = 70f;
    [Tooltip("Same-direction traffic speed range (m/s), right lane.")]
    public Vector2 sameDirSpeed = new Vector2(8f, 13f);
    [Tooltip("Oncoming traffic speed range (m/s), left lane.")]
    public Vector2 oncomingSpeed = new Vector2(14f, 20f);
    [Tooltip("Rotate the traffic car models if they face the wrong way.")]
    public float trafficCarYaw = 0f;

    class TrafficCar
    {
        public float distance;
        public float lateral;
        public float speed; // along track direction; negative = oncoming
        public bool nearMissGiven;
        public GameObject go;
    }

    readonly List<TrafficCar> traffic = new List<TrafficCar>();
    float nextTrafficDistance;
    GameObject trafficPrefab;
    readonly List<GameObject> trafficModels = new List<GameObject>();
    GameObject coinPrefab;
    bool coinPrefabSearched;

    [Header("Power-ups")]
    public bool spawnPowerUps = true;
    public float powerUpStartAt = 260f;
    public float powerUpSpacingMin = 140f;
    public float powerUpSpacingMax = 240f;

    public enum PowerUpType { Invincible = 0, DoubleCoins = 1, Magnet = 2, DoubleScore = 3, Springs = 4 }

    class PowerUpData
    {
        public float distance;
        public float lateral;
        public PowerUpType type;
        public GameObject go;
    }

    readonly List<PowerUpData> powerUps = new List<PowerUpData>();
    float nextPowerUpDistance;
    Material[] powerUpMats;

    [Header("Boost pads")]
    public bool spawnBoostPads;
    public float boostPadSpacingMin = 120f;
    public float boostPadSpacingMax = 220f;

    class BoostPad
    {
        public float distance;
        public float lateral;
        public float cooldown;
        public GameObject go;
    }
    readonly List<BoostPad> boostPads = new List<BoostPad>();
    float nextBoostDistance;
    Material boostMat;

    [Header("Tire pickups (very rare)")]
    public bool spawnTirePickups = true;
    public float tireSpacingMin = 800f;
    public float tireSpacingMax = 1500f;

    class TirePickupData
    {
        public float distance;
        public float lateral;
        public GameObject go;
    }

    readonly List<TirePickupData> tirePickups = new List<TirePickupData>();
    float nextTireDistance;
    GameObject tirePrefab;
    bool tirePrefabSearched;

    [Header("Clouds")]
    public bool spawnClouds = true;
    public float cloudSpacing = 20f;
    public float cloudMinHeight = 30f;
    public float cloudMaxHeight = 60f;

    [Header("Look")]
    public Color roadColor = new Color(0.16f, 0.16f, 0.19f);
    public Color postColorA = Color.white;
    public Color postColorB = new Color(0.9f, 0.25f, 0.2f);
    public Color groundColor = new Color(0.16f, 0.30f, 0.17f);
    public Color barrierColor = new Color(1f, 0.55f, 0.1f);
    public Color logColor = new Color(0.42f, 0.27f, 0.14f);
    public Color oilColor = new Color(0.05f, 0.05f, 0.07f);
    public Color trunkColor = new Color(0.36f, 0.24f, 0.13f);
    public Color leafColorA = new Color(0.16f, 0.40f, 0.15f);
    public Color leafColorB = new Color(0.28f, 0.52f, 0.18f);
    public Color leafColorC = new Color(0.45f, 0.55f, 0.20f);
    public Color autumnColor = new Color(0.78f, 0.45f, 0.12f);
    public bool createGround = true;

    const float SampleSpacing = 1f;
    const float ChunkLength = 40f;
    const float TreeLagBehindFront = 60f; // spawn trees only where the road is final

    struct Sample
    {
        public Vector3 pos;
        public float headingDeg;
        public float curvature; // signed, degrees per meter (positive = right turn)
    }

    enum ObstacleType { FallenLog, Construction, OilSpill, Cones, Boulder }

    class ObstacleData
    {
        public ObstacleType type;
        public float distance;
        public float lateral;
        public float halfWidth;   // across the road
        public float halfDepth;   // along the road
        public bool nearMissGiven;
        public bool consumed;     // oil spills only trigger once
        public GameObject go;
    }

    class Chunk
    {
        public float endDistance;
        public GameObject go;
    }

    class CoinData
    {
        public float distance;
        public float lateral;
        public bool taken;
        public bool pulledHome;   // magnet dragged it into the car
        public GameObject go;
    }

    readonly List<Sample> samples = new List<Sample>();
    readonly List<ObstacleData> obstacles = new List<ObstacleData>();
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly List<Chunk> decorations = new List<Chunk>(); // trees, pruned by distance
    readonly List<CoinData> coins = new List<CoinData>();

    float baseDistance;
    float frontDistance;
    float headingDeg;
    Vector3 frontPos;

    float segmentRemaining;
    float segmentCurvature;
    bool lastWasCorner;
    int lastCornerDir;

    float nextObstacleDistance;
    float nextPostDistance;
    float nextTreeDistance;
    float nextCloudDistance;
    float nextCoinRunDistance;
    int postFlip;

    GameObject currentChunk;
    float currentChunkStart;
    int currentChunkFirstSampleIndexOffset;

    [Header("Snow mountains")]
    public Color snowRoadColor = new Color(0.32f, 0.34f, 0.40f);
    public Color snowGroundColor = new Color(0.88f, 0.91f, 0.96f);
    public Color rockColor = new Color(0.33f, 0.32f, 0.36f);
    [Tooltip("Steepest climb/descent, in metres per metre.")]
    public float maxSlope = 0.055f;
    [Tooltip("How far the road runs before the gradient changes.")]
    public Vector2 slopeSegmentLength = new Vector2(120f, 240f);

    [Range(0f, 1f)] public float snowBlend;
    float currentSlope, targetSlope, slopeRemaining, elevation;

    [Header("Night city look")]
    public Color cityRoadColor = new Color(0.10f, 0.10f, 0.13f);
    public Color cityGroundColor = new Color(0.07f, 0.07f, 0.09f);
    public Color buildingColorA = new Color(0.13f, 0.13f, 0.18f);
    public Color buildingColorB = new Color(0.17f, 0.15f, 0.22f);

    Material roadMat, postMatA, postMatB, groundMat, cloudMat, coinMat;
    Material buildingMatA, buildingMatB, windowMat, neonPink, neonCyan, neonAmber, poleMat;
    Material magnetRedMat, magnetBlueMat, snowMat, rockMat, iceMat;
    Material barrierMat, stripeMat, logMat, oilMat, trunkMat;
    Material leafMatA, leafMatB, leafMatC, autumnMat;
    GameObject ground;

    // ------------------------------------------------------------------ setup

    public void Init(Vector3 origin, float startHeadingDeg)
    {
        Clear();
        biome = Biome.NightCity; // force the reset below to actually apply
        CreateMaterials();
        SetBiome(Biome.Forest);  // every run starts in daylight
        LoadNaturePrefabs();

        baseDistance = 0f;
        frontDistance = 0f;
        headingDeg = startHeadingDeg;
        // the track begins behind the car so the start line isn't the road's edge
        Vector3 dir = Quaternion.Euler(0f, startHeadingDeg, 0f) * Vector3.forward;
        frontPos = origin + Vector3.up * 0.05f - dir * roadBehindStart;

        currentSlope = targetSlope = 0f;
        slopeRemaining = 200f;
        elevation = 0f;
        snowBlend = 0f;
        segmentRemaining = roadBehindStart + firstObstacleAt * 0.8f;
        segmentCurvature = 0f;
        lastWasCorner = false;
        lastCornerDir = Random.value < 0.5f ? -1 : 1;

        nextObstacleDistance = roadBehindStart + firstObstacleAt;
        nextPostDistance = 0f;
        nextTreeDistance = 10f;
        nextCloudDistance = 0f;
        nextCoinRunDistance = roadBehindStart + 60f;
        nextTrafficDistance = roadBehindStart + trafficStartAt;
        nextPowerUpDistance = roadBehindStart + powerUpStartAt;
        nextTireDistance = roadBehindStart + Random.Range(tireSpacingMin, tireSpacingMax) * 0.5f;
        nextBoostDistance = roadBehindStart + 120f;
        boostPads.Clear();
        postFlip = 0;

        samples.Add(new Sample { pos = frontPos, headingDeg = headingDeg, curvature = 0f });
        StartNewChunk(0f);

        if (createGround)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "Ground";
            Object.Destroy(ground.GetComponent<Collider>());
            ground.transform.SetParent(transform, false);
            ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(1200f, 1200f, 1f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.transform.position = new Vector3(origin.x, origin.y - 0.05f, origin.z);
        }

        EnsureGenerated(0f);
    }

    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Object.Destroy(transform.GetChild(i).gameObject);
        }
        samples.Clear();
        obstacles.Clear();
        chunks.Clear();
        decorations.Clear();
        coins.Clear();
        traffic.Clear();
        powerUps.Clear();
        tirePickups.Clear();
        currentChunk = null;
        ground = null;
    }

    void CreateMaterials()
    {
        if (roadMat != null) return;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        roadMat = new Material(shader) { color = roadColor };
        postMatA = new Material(shader) { color = postColorA };
        postMatB = new Material(shader) { color = postColorB };
        groundMat = new Material(shader) { color = groundColor };
        barrierMat = new Material(shader) { color = barrierColor };
        stripeMat = new Material(shader) { color = Color.white };
        logMat = new Material(shader) { color = logColor };
        oilMat = new Material(shader) { color = oilColor };
        trunkMat = new Material(shader) { color = trunkColor };
        cloudMat = new Material(shader) { color = new Color(0.98f, 0.98f, 1f) };
        coinMat = new Material(shader) { color = new Color(1f, 0.82f, 0.1f) };
        powerUpMats = new[]
        {
            new Material(shader) { color = new Color(1f, 0.95f, 0.55f) },  // invincible - white gold
            new Material(shader) { color = new Color(1f, 0.8f, 0.15f) },   // double coins - deep gold
            new Material(shader) { color = new Color(0.25f, 0.55f, 1f) },  // magnet - blue
            new Material(shader) { color = new Color(0.75f, 0.3f, 1f) },   // double score - purple
            new Material(shader) { color = new Color(0.3f, 1f, 0.4f) },    // springs - green
        };
        if (coinMat.HasProperty("_Metallic")) coinMat.SetFloat("_Metallic", 0.8f);
        if (coinMat.HasProperty("_Smoothness")) coinMat.SetFloat("_Smoothness", 0.65f);

        snowMat = new Material(shader) { color = new Color(0.96f, 0.97f, 1f) };
        boostMat = MakeGlowMat(shader, new Color(0.2f, 1f, 0.35f), 2.8f);
        iceMat = new Material(shader) { color = new Color(0.72f, 0.88f, 0.96f) };
        if (iceMat.HasProperty("_Smoothness")) iceMat.SetFloat("_Smoothness", 0.9f);
        rockMat = new Material(shader) { color = rockColor };
        magnetRedMat = new Material(shader) { color = new Color(0.85f, 0.13f, 0.13f) };
        magnetBlueMat = new Material(shader) { color = new Color(0.15f, 0.32f, 0.9f) };

        // --- night city materials
        buildingMatA = new Material(shader) { color = buildingColorA };
        buildingMatB = new Material(shader) { color = buildingColorB };
        poleMat = new Material(shader) { color = new Color(0.12f, 0.12f, 0.14f) };
        windowMat = MakeGlowMat(shader, new Color(1f, 0.88f, 0.55f), 2.2f);
        neonPink = MakeGlowMat(shader, new Color(1f, 0.2f, 0.65f), 3.5f);
        neonCyan = MakeGlowMat(shader, new Color(0.25f, 0.95f, 1f), 3.5f);
        neonAmber = MakeGlowMat(shader, new Color(1f, 0.65f, 0.15f), 3.2f);

        leafMatA = new Material(shader) { color = leafColorA };
        leafMatB = new Material(shader) { color = leafColorB };
        leafMatC = new Material(shader) { color = leafColorC };
        autumnMat = new Material(shader) { color = autumnColor };
    }

    // ------------------------------------------------------------ generation

    public void EnsureGenerated(float carDistance)
    {
        while (frontDistance < carDistance + aheadDistance)
        {
            GenerateOneSample();
        }
        if (pendingFinishDistance > 0f && frontDistance > pendingFinishDistance + 12f)
        {
            BuildFinishLine(pendingFinishDistance);
            pendingFinishDistance = -1f;
        }

        Prune(carDistance);

        if (ground != null)
        {
            // the flat world plane sits well below the road so it can never
            // slice through climbs; the verge strips cover the near ground
            Vector3 p = SamplePosition(carDistance);
            // the road climbs and dives in the mountains, so the flat world
            // plane has to sit well below the lowest point it can reach
            float drop = Mathf.Lerp(0.06f, 120f, snowBlend);
            ground.transform.position = new Vector3(p.x, p.y - drop, p.z);
        }
    }

    void GenerateOneSample()
    {
        if (segmentRemaining <= 0f) StartNewSegment();

        // vertical profile: only the mountains have real gradients, and the
        // slope eases toward its target so crests and dips stay driveable
        slopeRemaining -= SampleSpacing;
        if (slopeRemaining <= 0f)
        {
            slopeRemaining = Random.Range(slopeSegmentLength.x, slopeSegmentLength.y);
            targetSlope = snowBlend > 0.05f
                ? Random.Range(-maxSlope, maxSlope) * snowBlend
                : 0f;
        }
        currentSlope = Mathf.MoveTowards(currentSlope, targetSlope, 0.0022f * SampleSpacing);
        elevation += currentSlope * SampleSpacing;

        headingDeg += segmentCurvature * SampleSpacing;
        Vector3 step = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward * SampleSpacing;
        step.y = currentSlope * SampleSpacing;
        frontPos += step;
        frontDistance += SampleSpacing;
        segmentRemaining -= SampleSpacing;

        samples.Add(new Sample { pos = frontPos, headingDeg = headingDeg, curvature = segmentCurvature });

        SpawnPropsUpTo(frontDistance);

        if (frontDistance - currentChunkStart >= ChunkLength)
        {
            BakeCurrentChunk();
            StartNewChunk(frontDistance);
        }
    }

    void StartNewSegment()
    {
        float t = Mathf.Clamp01(frontDistance / difficultyRampMeters);

        // straight either because the backdrop asked for it, or because a run
        // just started and the opening stretch should be clean
        if (forceStraight || frontDistance < straightUntil)
        {
            segmentCurvature = 0f;
            segmentRemaining = 60f;
            lastWasCorner = false;
            return;
        }

        if (lastWasCorner)
        {
            segmentCurvature = 0f;
            segmentRemaining = Mathf.Lerp(45f, 16f, t) * Random.Range(0.6f, 1.4f);
            lastWasCorner = false;
        }
        else
        {
            int dir = Random.value < 0.7f ? -lastCornerDir : lastCornerDir;
            float radius = Mathf.Lerp(58f, 26f, t) * Random.Range(0.8f, 1.25f);
            float angle = Random.Range(Mathf.Lerp(30f, 45f, t), Mathf.Lerp(60f, 110f, t));

            segmentCurvature = dir * Mathf.Rad2Deg / radius;
            segmentRemaining = angle / Mathf.Abs(segmentCurvature);
            lastWasCorner = true;
            lastCornerDir = dir;
        }
    }

    void SpawnPropsUpTo(float dist)
    {
        while (nextPostDistance <= dist)
        {
            SpawnPostPair(nextPostDistance);
            nextPostDistance += 7f;
        }

        while (nextObstacleDistance <= dist)
        {
            if (spawnObstacles && nextObstacleDistance >= firstObstacleAt) SpawnObstacle(nextObstacleDistance);
            float t = Mathf.Clamp01(frontDistance / difficultyRampMeters);
            nextObstacleDistance += Mathf.Lerp(obstacleSpacingStart, obstacleSpacingMin, t)
                                    * Random.Range(0.75f, 1.4f);
        }

        // trees spawn a little behind the generation front so the road shape
        // around them is final and they never end up ON a later corner
        if (spawnTrees)
        {
            while (nextTreeDistance <= dist - TreeLagBehindFront)
            {
                // during a blend, each slot rolls for the new biome, so the
                // change reads as scenery mixing rather than switching
                if (Random.value < snowBlend) SpawnSnowSceneryAt(nextTreeDistance);
                else if (Random.value < biomeBlend) SpawnCityAt(nextTreeDistance);
                else SpawnTreesAt(nextTreeDistance);
                float spacing = Mathf.Lerp(treeSpacing, treeSpacing * 3.2f, biomeBlend);
                spacing = Mathf.Lerp(spacing, treeSpacing * 1.6f, snowBlend);
                nextTreeDistance += spacing * Random.Range(0.6f, 1.5f);
            }
        }

        // clouds thin out as dusk falls, gone by full night
        if (spawnClouds && biomeBlend < 0.75f)
        {
            while (nextCloudDistance <= dist)
            {
                if (Random.value > biomeBlend) SpawnCloud(nextCloudDistance);
                nextCloudDistance += cloudSpacing * Random.Range(0.6f, 1.6f);
            }
        }
        else
        {
            nextCloudDistance = dist + cloudSpacing;
        }

        if (spawnTraffic)
        {
            while (nextTrafficDistance <= dist)
            {
                SpawnTrafficCar(nextTrafficDistance);
                float t = Mathf.Clamp01(frontDistance / difficultyRampMeters);
                nextTrafficDistance += Mathf.Lerp(trafficSpacingStart, trafficSpacingMin, t)
                                       * Random.Range(0.7f, 1.4f);
            }
        }

        if (spawnPowerUps)
        {
            while (nextPowerUpDistance <= dist)
            {
                SpawnPowerUp(nextPowerUpDistance);
                nextPowerUpDistance += Random.Range(powerUpSpacingMin, powerUpSpacingMax);
            }
        }

        if (spawnBoostPads)
        {
            while (nextBoostDistance <= dist)
            {
                SpawnBoostPad(nextBoostDistance);
                nextBoostDistance += Random.Range(boostPadSpacingMin, boostPadSpacingMax);
            }
        }

        if (spawnTirePickups)
        {
            while (nextTireDistance <= dist)
            {
                SpawnTirePickup(nextTireDistance);
                nextTireDistance += Random.Range(tireSpacingMin, tireSpacingMax);
            }
        }

        // coin runs spawn once their whole length has road under it
        if (spawnCoins)
        {
            while (nextCoinRunDistance + 26f <= dist)
            {
                SpawnCoinRun(nextCoinRunDistance);
                nextCoinRunDistance += Random.Range(coinRunSpacingMin, coinRunSpacingMax);
            }
        }
    }

    void SpawnCoinRun(float startDist)
    {
        int count = Random.Range(6, 11);
        float maxLat = roadWidth * 0.5f - 2f;
        float latFrom = Random.Range(-maxLat, maxLat);
        // half the runs sweep across the road like a Subway Surfers arc
        float latTo = Random.value < 0.5f ? latFrom : Random.Range(-maxLat, maxLat);

        for (int i = 0; i < count; i++)
        {
            float d = startDist + i * 2.4f;
            float lat = Mathf.Lerp(latFrom, latTo, count > 1 ? (float)i / (count - 1) : 0f);

            if (ObstacleNear(d, lat)) continue;

            SamplePose(d, out Vector3 pos, out Vector3 fwd, out _);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            GameObject go = BuildCoinVisual(pos + right * lat + Vector3.up * 1.1f);
            coins.Add(new CoinData { distance = d, lateral = lat, go = go });
        }
    }

    // -------------------------------------------------------------- traffic

    void SpawnTrafficCar(float dist)
    {
        bool oncoming = Random.value < 0.5f;
        // same direction drives the right lane, oncoming the left
        float lane = roadWidth * 0.25f;

        // skip this car if it would seal the road with a nearby obstacle
        float wantLat = oncoming ? -lane : lane;
        if (!LeavesGapForTraffic(dist, wantLat)) return;

        var tc = new TrafficCar
        {
            distance = dist,
            lateral = wantLat,
            speed = oncoming ? -Random.Range(oncomingSpeed.x, oncomingSpeed.y)
                             : Random.Range(sameDirSpeed.x, sameDirSpeed.y),
            go = BuildTrafficVisual()
        };
        traffic.Add(tc);
        PlaceTrafficCar(tc);
    }

    GameObject BuildTrafficVisual()
    {
        if (trafficPrefab == null && trafficModels.Count == 0)
        {
            trafficPrefab = Resources.Load<GameObject>("Traffic/cars");
            if (trafficPrefab != null)
            {
                foreach (Transform child in trafficPrefab.transform)
                {
                    if (child.GetComponentInChildren<MeshRenderer>() != null)
                    {
                        trafficModels.Add(child.gameObject);
                    }
                }
            }
        }

        var root = new GameObject("TrafficCar");
        root.transform.SetParent(transform, false);

        if (trafficModels.Count > 0)
        {
            GameObject pick = trafficModels[Random.Range(0, trafficModels.Count)];
            GameObject model = Instantiate(pick, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = pick.transform.localRotation * Quaternion.Euler(0f, trafficCarYaw, 0f);

            // normalize to a sensible car size and seat it on the road
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float length = Mathf.Max(b.size.x, b.size.z, 0.01f);
                float k = 4.1f / length;
                model.transform.localScale = model.transform.localScale * k;

                Vector3 p0 = model.transform.localPosition;
                Vector3 centerLocal = root.transform.InverseTransformPoint(b.center);
                Vector3 scaledCenter = p0 + k * (centerLocal - p0);
                float halfHeight = b.extents.y * k;
                model.transform.localPosition = new Vector3(
                    p0.x - scaledCenter.x,
                    p0.y - scaledCenter.y + halfHeight + 0.02f,
                    p0.z - scaledCenter.z);
            }
        }
        else
        {
            // fallback: simple two-box car
            Color bodyC = new Color(Random.value * 0.7f + 0.2f, Random.value * 0.6f + 0.2f, Random.value * 0.7f + 0.2f);
            var bodyMat = new Material(roadMat.shader) { color = bodyC };
            MakePart(PrimitiveType.Cube, root.transform, bodyMat,
                root.transform.position + Vector3.up * 0.5f, Quaternion.identity, new Vector3(1.8f, 0.8f, 4.0f));
            MakePart(PrimitiveType.Cube, root.transform, bodyMat,
                root.transform.position + Vector3.up * 1.15f - root.transform.forward * 0.3f,
                Quaternion.identity, new Vector3(1.6f, 0.6f, 2.0f));
        }
        return root;
    }

    void PlaceTrafficCar(TrafficCar tc)
    {
        if (tc.go == null) return;
        SamplePose(tc.distance, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        tc.go.transform.position = pos + right * tc.lateral;
        tc.go.transform.rotation = Quaternion.LookRotation(tc.speed >= 0f ? fwd : -fwd, Vector3.up);
    }

    /// <summary>Moves traffic. Call once per frame while playing.</summary>
    public void TickTraffic(float dt, float carDistance)
    {
        for (int i = traffic.Count - 1; i >= 0; i--)
        {
            TrafficCar tc = traffic[i];
            tc.distance += tc.speed * dt;

            if (tc.distance < carDistance - 40f || tc.distance > carDistance + aheadDistance + 40f
                || tc.distance < baseDistance + 2f)
            {
                if (tc.go != null) Object.Destroy(tc.go);
                traffic.RemoveAt(i);
                continue;
            }
            PlaceTrafficCar(tc);
        }
    }

    /// <summary>Builds a display coin for UI showcases (not collectible).</summary>
    public GameObject BuildCoinDisplay(Vector3 worldPos, Transform parent)
    {
        GameObject go = BuildCoinVisual(worldPos);
        go.transform.SetParent(parent, true);
        return go;
    }

    void SpawnPowerUp(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float maxLat = roadWidth * 0.5f - 2f;
        float lat = Random.Range(-maxLat, maxLat);
        if (ObstacleNear(dist, lat)) return;

        var type = (PowerUpType)Random.Range(0, 5);
        var root = new GameObject("PowerUp_" + type);
        root.transform.SetParent(transform, false);
        root.transform.position = pos + right * lat + Vector3.up * 1.2f;
        root.AddComponent<Coin>().spinSpeed = 140f;

        BuildPowerUpShape(root.transform, root.transform.position, type);

        powerUps.Add(new PowerUpData { distance = dist, lateral = lat, type = type, go = root });
    }

    // each item gets a distinct silhouette so players learn them at speed
    void BuildPowerUpShape(Transform parent, Vector3 c, PowerUpType type)
    {
        Material m = powerUpMats[(int)type];
        switch (type)
        {
            case PowerUpType.Invincible:
            {
                // shield: wide top tapering to a point, with a bright boss
                MakePart(PrimitiveType.Cube, parent, m,
                    c + new Vector3(0f, 0.34f, 0f), Quaternion.identity,
                    new Vector3(1.05f, 0.62f, 0.16f));
                MakePart(PrimitiveType.Cube, parent, m,
                    c + new Vector3(0f, -0.06f, 0f), Quaternion.identity,
                    new Vector3(0.82f, 0.42f, 0.16f));
                MakePart(PrimitiveType.Cube, parent, m,
                    c + new Vector3(0f, -0.38f, 0f), Quaternion.Euler(0f, 0f, 45f),
                    new Vector3(0.38f, 0.38f, 0.16f)); // pointed tip
                MakePart(PrimitiveType.Cube, parent, stripeMat,
                    c + new Vector3(0f, 0.2f, 0.1f), Quaternion.identity,
                    new Vector3(0.2f, 0.66f, 0.06f)); // cross detail
                MakePart(PrimitiveType.Cube, parent, stripeMat,
                    c + new Vector3(0f, 0.34f, 0.1f), Quaternion.identity,
                    new Vector3(0.66f, 0.2f, 0.06f));
                break;
            }

            case PowerUpType.DoubleCoins:
                // two offset gold coins
                MakePart(PrimitiveType.Cylinder, parent, coinMat,
                    c + new Vector3(-0.18f, 0f, 0.05f), Quaternion.Euler(90f, 0f, 0f),
                    new Vector3(0.7f, 0.06f, 0.7f));
                MakePart(PrimitiveType.Cylinder, parent, coinMat,
                    c + new Vector3(0.18f, 0.12f, -0.05f), Quaternion.Euler(90f, 0f, 12f),
                    new Vector3(0.7f, 0.06f, 0.7f));
                break;

            case PowerUpType.Magnet:
            {
                // classic comic horseshoe magnet: U shape - arch at the BOTTOM,
                // both legs rising, silver pole tips at the top
                MakePart(PrimitiveType.Cube, parent, magnetRedMat,
                    c + new Vector3(-0.3f, -0.02f, 0f), Quaternion.identity,
                    new Vector3(0.28f, 0.78f, 0.28f));
                MakePart(PrimitiveType.Cube, parent, magnetBlueMat,
                    c + new Vector3(0.3f, -0.02f, 0f), Quaternion.identity,
                    new Vector3(0.28f, 0.78f, 0.28f));
                // arch across the bottom, split down the middle in both colours
                MakePart(PrimitiveType.Cube, parent, magnetRedMat,
                    c + new Vector3(-0.16f, -0.53f, 0f), Quaternion.identity,
                    new Vector3(0.6f, 0.3f, 0.28f));
                MakePart(PrimitiveType.Cube, parent, magnetBlueMat,
                    c + new Vector3(0.16f, -0.53f, 0f), Quaternion.identity,
                    new Vector3(0.6f, 0.3f, 0.28f));
                // silver pole tips at the top
                MakePart(PrimitiveType.Cube, parent, stripeMat,
                    c + new Vector3(-0.3f, 0.45f, 0f), Quaternion.identity,
                    new Vector3(0.3f, 0.2f, 0.3f));
                MakePart(PrimitiveType.Cube, parent, stripeMat,
                    c + new Vector3(0.3f, 0.45f, 0f), Quaternion.identity,
                    new Vector3(0.3f, 0.2f, 0.3f));
                break;
            }

            case PowerUpType.DoubleScore:
                // purple 8-point burst: two offset tilted cubes
                MakePart(PrimitiveType.Cube, parent, m, c,
                    Quaternion.Euler(0f, 0f, 0f), new Vector3(0.7f, 0.7f, 0.7f));
                MakePart(PrimitiveType.Cube, parent, m, c,
                    Quaternion.Euler(0f, 0f, 45f), new Vector3(0.7f, 0.7f, 0.7f));
                break;

            case PowerUpType.Springs:
                // green coil: stacked squashed cylinders + base plate
                MakePart(PrimitiveType.Cube, parent, stripeMat,
                    c + new Vector3(0f, -0.5f, 0f), Quaternion.identity,
                    new Vector3(0.75f, 0.1f, 0.75f));
                for (int i = 0; i < 4; i++)
                {
                    MakePart(PrimitiveType.Cylinder, parent, m,
                        c + new Vector3(0f, -0.35f + i * 0.28f, 0f), Quaternion.identity,
                        new Vector3(0.62f - i * 0.05f, 0.05f, 0.62f - i * 0.05f));
                }
                break;
        }
    }

    GameObject finishLineGo;

    float pendingFinishDistance = -1f;

    /// <summary>
    /// Queues a finish line. It is built once the road has been generated
    /// that far - never force-generate ahead, that would prune the road the
    /// car is currently driving on.
    /// </summary>
    public void RequestFinishLine(float dist)
    {
        ClearFinishLine();
        pendingFinishDistance = dist;
    }

    /// <summary>Builds a checkered gate at a distance the road already covers.</summary>
    public void BuildFinishLine(float dist)
    {
        if (finishLineGo != null) Object.Destroy(finishLineGo);
        CreateMaterials();

        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Quaternion along = Quaternion.LookRotation(fwd);

        finishLineGo = BuildCheckerGate(dist, "FinishLine");
    }

    GameObject BuildCheckerGate(float dist, string name)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Quaternion along = Quaternion.LookRotation(fwd);

        var root = new GameObject(name);
        root.transform.SetParent(transform, false);

        // checkered strip across the road
        int cols = Mathf.Max(6, Mathf.RoundToInt(roadWidth / 0.9f));
        float cell = roadWidth / cols;
        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < 2; r++)
            {
                bool white = (c + r) % 2 == 0;
                Vector3 p = pos + right * (-roadWidth * 0.5f + (c + 0.5f) * cell)
                            + fwd * (r * cell - cell * 0.5f) + Vector3.up * 0.05f;
                MakePart(PrimitiveType.Cube, root.transform,
                    white ? stripeMat : oilMat, p, along, new Vector3(cell, 0.04f, cell));
            }
        }

        // gate posts and overhead beam
        float half = roadWidth * 0.5f + 1.2f;
        for (int side = -1; side <= 1; side += 2)
        {
            MakePart(PrimitiveType.Cube, root.transform, barrierMat,
                pos + right * (half * side) + Vector3.up * 3.2f, along,
                new Vector3(0.55f, 6.4f, 0.55f));
        }
        MakePart(PrimitiveType.Cube, root.transform, barrierMat,
            pos + Vector3.up * 6.9f, along, new Vector3(roadWidth + 3f, 1.1f, 0.5f));

        // checkered banner face
        int bcols = 14;
        float bw = (roadWidth + 2.4f) / bcols;
        for (int c = 0; c < bcols; c++)
        {
            for (int r = 0; r < 2; r++)
            {
                bool white = (c + r) % 2 == 0;
                MakePart(PrimitiveType.Cube, root.transform,
                    white ? stripeMat : oilMat,
                    pos + right * (-(roadWidth + 2.4f) * 0.5f + (c + 0.5f) * bw)
                        + Vector3.up * (6.5f + r * 0.62f) + fwd * -0.32f,
                    along, new Vector3(bw, 0.62f, 0.12f));
            }
        }
        return root;
    }

    public void ClearFinishLine()
    {
        if (finishLineGo != null) Object.Destroy(finishLineGo);
        finishLineGo = null;
        pendingFinishDistance = -1f;
        if (startLineGo != null) Object.Destroy(startLineGo);
        startLineGo = null;
    }

    GameObject startLineGo;

    /// <summary>Checkered start line with a gate, drawn at the grid.</summary>
    public void BuildStartLine(float dist)
    {
        if (startLineGo != null) Object.Destroy(startLineGo);
        CreateMaterials();
        startLineGo = BuildCheckerGate(dist, "StartLine");
        BuildStartLights(dist, startLineGo.transform);
    }

    // --- the light tree hanging under the start gantry
    readonly List<Renderer> startLights = new List<Renderer>();
    Material lightOffMat, lightRedMat, lightGreenMat;
    const int StartLightCount = 5;

    void BuildStartLights(float dist, Transform parent)
    {
        startLights.Clear();
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Quaternion along = Quaternion.LookRotation(fwd);

        if (lightOffMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            lightOffMat = new Material(sh) { color = new Color(0.10f, 0.09f, 0.11f) };
            lightRedMat = MakeGlowMaterial(sh, new Color(1f, 0.12f, 0.10f));
            lightGreenMat = MakeGlowMaterial(sh, new Color(0.25f, 1f, 0.35f));
        }

        // a dark backing board so the bulbs read against the sky
        Vector3 boardPos = pos + Vector3.up * 5.1f;
        MakePart(PrimitiveType.Cube, parent, lightOffMat, boardPos, along,
            new Vector3(StartLightCount * 1.25f + 0.5f, 1.5f, 0.25f));

        for (int i = 0; i < StartLightCount; i++)
        {
            float x = (i - (StartLightCount - 1) * 0.5f) * 1.25f;
            GameObject bulb = MakePart(PrimitiveType.Sphere, parent, lightOffMat,
                boardPos + right * x + fwd * -0.2f, along,
                new Vector3(0.95f, 0.95f, 0.55f));
            startLights.Add(bulb.GetComponent<Renderer>());
        }
    }

    static Material MakeGlowMaterial(Shader sh, Color c)
    {
        var m = new Material(sh) { color = c };
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2.2f);
        }
        return m;
    }

    /// <summary>
    /// Lights the tree: <paramref name="lit"/> bulbs on in red as the count
    /// runs down, or all of them green when the race is away.
    /// </summary>
    public void SetStartLights(int lit, bool green)
    {
        for (int i = 0; i < startLights.Count; i++)
        {
            if (startLights[i] == null) continue;
            Material m = green ? lightGreenMat
                       : i < lit ? lightRedMat
                       : lightOffMat;
            startLights[i].sharedMaterial = m;
        }
    }

    void SpawnBoostPad(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float lat = Random.Range(-(roadWidth * 0.5f - 2.2f), roadWidth * 0.5f - 2.2f);
        Quaternion along = Quaternion.LookRotation(fwd);

        var root = new GameObject("BoostPad");
        root.transform.SetParent(transform, false);
        Vector3 basePos = pos + right * lat + Vector3.up * 0.04f;

        // three wide green chevrons pointing down the road
        for (int i = 0; i < 3; i++)
        {
            Vector3 c = basePos + fwd * (-2.2f + i * 2.2f);
            // two arms meeting at a tip that points down the road: ^
            MakePart(PrimitiveType.Cube, root.transform, boostMat,
                c - right * 1.15f - fwd * 0.55f,
                along * Quaternion.Euler(0f, 38f, 0f), new Vector3(0.5f, 0.05f, 3.2f));
            MakePart(PrimitiveType.Cube, root.transform, boostMat,
                c + right * 1.15f - fwd * 0.55f,
                along * Quaternion.Euler(0f, -38f, 0f), new Vector3(0.5f, 0.05f, 3.2f));
        }

        boostPads.Add(new BoostPad { distance = dist, lateral = lat, go = root });
    }

    /// <summary>True if the car just drove over a boost pad.</summary>
    /// <summary>Non-consuming test used by the AI racers.</summary>
    public bool IsOnBoostPad(float dist, float lateral, float carRadius)
    {
        for (int i = 0; i < boostPads.Count; i++)
        {
            BoostPad b = boostPads[i];
            if (Mathf.Abs(b.distance - dist) < 3.5f && Mathf.Abs(b.lateral - lateral) < 2.2f + carRadius)
            {
                return true;
            }
        }
        return false;
    }

    public bool TryCollectBoost(float dist, float lateral, float carRadius, float dt)
    {
        bool hit = false;
        for (int i = 0; i < boostPads.Count; i++)
        {
            BoostPad b = boostPads[i];
            if (b.cooldown > 0f) { b.cooldown -= dt; continue; }

            // the pad stays on the road - it just cannot retrigger instantly
            if (Mathf.Abs(b.distance - dist) < 3.5f && Mathf.Abs(b.lateral - lateral) < 2.2f + carRadius)
            {
                b.cooldown = 1.5f;
                hit = true;
            }
        }
        return hit;
    }

    void SpawnTirePickup(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float lat = Random.Range(-(roadWidth * 0.5f - 2f), roadWidth * 0.5f - 2f);
        if (ObstacleNear(dist, lat)) return;

        if (!tirePrefabSearched)
        {
            tirePrefabSearched = true;
            tirePrefab = Resources.Load<GameObject>("Tire/tire");
        }

        var root = new GameObject("TirePickup");
        root.transform.SetParent(transform, false);
        root.transform.position = pos + right * lat + Vector3.up * 1.1f;
        root.AddComponent<Coin>().spinSpeed = 120f;

        if (tirePrefab != null)
        {
            GameObject model = Instantiate(tirePrefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.001f);
                float k = 1.1f / maxDim;
                model.transform.localScale = model.transform.localScale * k;
                Vector3 centerLocal = root.transform.InverseTransformPoint(b.center);
                model.transform.localPosition = -centerLocal * k;
                // stand upright: thinnest axis across the road
                if (b.size.y <= b.size.x && b.size.y <= b.size.z)
                    model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 0f, 90f);
                else if (b.size.z <= b.size.x && b.size.z <= b.size.y)
                    model.transform.localRotation = model.transform.localRotation * Quaternion.Euler(0f, 90f, 0f);
                foreach (var r in rends) r.sharedMaterial = oilMat; // near-black rubber
            }
        }
        else
        {
            // fallback: dark torus-ish ring from a squashed cylinder
            MakePart(PrimitiveType.Cylinder, root.transform, oilMat,
                root.transform.position, Quaternion.Euler(90f, 0f, 0f),
                new Vector3(1.0f, 0.18f, 1.0f));
        }

        tirePickups.Add(new TirePickupData { distance = dist, lateral = lat, go = root });
    }

    public bool TryCollectTire(float dist, float lateral, float carRadius)
    {
        float range = carRadius + 1.0f;
        for (int i = 0; i < tirePickups.Count; i++)
        {
            TirePickupData t = tirePickups[i];
            if (Mathf.Abs(t.distance - dist) < range && Mathf.Abs(t.lateral - lateral) < range)
            {
                if (t.go != null) Object.Destroy(t.go);
                tirePickups.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>Removes obstacles and traffic near/ahead of a revived player.</summary>
    public void ClearObstaclesAhead(float dist, float range)
    {
        for (int i = obstacles.Count - 1; i >= 0; i--)
        {
            if (obstacles[i].distance > dist - 6f && obstacles[i].distance < dist + range)
            {
                if (obstacles[i].go != null) Object.Destroy(obstacles[i].go);
                obstacles.RemoveAt(i);
            }
        }
        for (int i = traffic.Count - 1; i >= 0; i--)
        {
            if (traffic[i].distance > dist - 6f && traffic[i].distance < dist + range)
            {
                if (traffic[i].go != null) Object.Destroy(traffic[i].go);
                traffic.RemoveAt(i);
            }
        }
    }

    /// <summary>True if the car just drove through a power-up; outputs its type.</summary>
    public bool TryCollectPowerUp(float dist, float lateral, float carRadius, out PowerUpType type)
    {
        type = PowerUpType.Invincible;
        float range = carRadius + 1.0f;
        for (int i = 0; i < powerUps.Count; i++)
        {
            PowerUpData p = powerUps[i];
            if (Mathf.Abs(p.distance - dist) < range && Mathf.Abs(p.lateral - lateral) < range)
            {
                type = p.type;
                if (p.go != null) Object.Destroy(p.go);
                powerUps.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    GameObject BuildCoinVisual(Vector3 worldPos)
    {
        if (!coinPrefabSearched)
        {
            coinPrefabSearched = true;
            coinPrefab = Resources.Load<GameObject>("Coin/coin");
        }

        var root = new GameObject("Coin");
        root.transform.SetParent(transform, false);
        root.transform.position = worldPos;
        root.AddComponent<Coin>();

        if (coinPrefab == null)
        {
            // fallback: the old cylinder coin
            MakePart(PrimitiveType.Cylinder, root.transform, coinMat,
                worldPos, Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.95f, 0.07f, 0.95f));
            return root;
        }

        var tilt = new GameObject("Tilt");
        tilt.transform.SetParent(root.transform, false);

        GameObject model = Instantiate(coinPrefab, tilt.transform);
        model.transform.localPosition = Vector3.zero;

        // the FBX ships with a light and camera from the modelling scene
        foreach (var l in model.GetComponentsInChildren<Light>(true)) Object.Destroy(l.gameObject);
        foreach (var c in model.GetComponentsInChildren<Camera>(true)) Object.Destroy(c.gameObject);

        var rends = model.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            // scale so the coin is ~1m across, centred exactly on the root
            float maxDim = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.001f);
            float k = 1.0f / maxDim;
            model.transform.localScale = model.transform.localScale * k;
            Vector3 centerLocal = tilt.transform.InverseTransformPoint(b.center);
            model.transform.localPosition = -centerLocal * k;

            // stand the coin upright: rotate its thinnest axis to face forward
            if (b.size.y <= b.size.x && b.size.y <= b.size.z)
                tilt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            else if (b.size.x <= b.size.y && b.size.x <= b.size.z)
                tilt.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            // guarantee the gold look regardless of how the material imports
            foreach (var r in rends) r.sharedMaterial = coinMat;
        }
        return root;
    }

    bool ObstacleNear(float dist, float lateral)
    {
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleData o = obstacles[i];
            if (Mathf.Abs(o.distance - dist) < o.halfDepth + 1.6f &&
                Mathf.Abs(o.lateral - lateral) < o.halfWidth + 1.3f) return true;
        }
        return false;
    }

    /// <summary>
    /// Magnet effect: pulls nearby uncollected coins toward the car so the
    /// player can see them stream in.
    /// </summary>
    public void AttractCoins(float dist, float lateral, float range, float dt)
    {
        SamplePose(dist, out Vector3 carPos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        carPos += right * lateral + Vector3.up * 1.0f;

        for (int i = 0; i < coins.Count; i++)
        {
            CoinData c = coins[i];
            if (c.taken || c.go == null) continue;
            if (c.distance < dist - 6f || c.distance > dist + range) continue;

            float gap = Mathf.Abs(c.distance - dist);
            if (gap > range) continue;

            // closer coins accelerate harder, so they visibly zip in
            float pull = Mathf.Lerp(26f, 8f, gap / range);
            c.go.transform.position = Vector3.MoveTowards(
                c.go.transform.position, carPos, pull * dt);

            // once the model reaches the car, treat it as collected
            if ((c.go.transform.position - carPos).sqrMagnitude < 1.2f)
            {
                c.pulledHome = true;
            }
        }
    }

    /// <summary>
    /// Counts coins the car passed through between two points along the road.
    /// Sweeping the whole step matters at speed - a single point test can jump
    /// clean over a coin between frames.
    /// </summary>
    public int CollectCoins(float fromDist, float dist, float lateral, float carRadius)
    {
        int count = 0;
        float range = carRadius + 1.1f;
        float lo = Mathf.Min(fromDist, dist) - range;
        float hi = Mathf.Max(fromDist, dist) + range;

        for (int i = 0; i < coins.Count; i++)
        {
            CoinData c = coins[i];
            if (c.taken) continue;
            bool reached = c.pulledHome
                || (c.distance >= lo && c.distance <= hi && Mathf.Abs(c.lateral - lateral) < range);
            if (reached)
            {
                c.taken = true;
                if (c.go != null) Object.Destroy(c.go);
                count++;
            }
        }
        return count;
    }

    void SpawnCloud(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        Vector3 center = pos
            + right * Random.Range(-170f, 170f)
            + Vector3.up * Random.Range(cloudMinHeight, cloudMaxHeight);

        var cloud = new GameObject("Cloud");
        cloud.transform.SetParent(transform, false);
        cloud.transform.position = center;

        int puffs = Random.Range(3, 6);
        float baseScale = Random.Range(0.8f, 1.6f);
        for (int i = 0; i < puffs; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-7f, 7f), Random.Range(-1.2f, 1.2f), Random.Range(-4f, 4f));
            Vector3 scale = new Vector3(
                Random.Range(7f, 12f), Random.Range(2.2f, 3.6f), Random.Range(5f, 9f)) * baseScale;
            MakePart(PrimitiveType.Sphere, cloud.transform, cloudMat,
                center + offset * baseScale, Quaternion.identity, scale);
        }

        // linger a while behind the car - big shapes vanishing early is jarring
        decorations.Add(new Chunk { endDistance = dist + 70f, go = cloud });
    }

    void SpawnPostPair(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float half = roadWidth * 0.5f + 0.7f;
        Material m = (postFlip++ % 2 == 0) ? postMatA : postMatB;

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject post = MakePart(PrimitiveType.Cube, currentChunk.transform, m,
                pos + right * (half * side) + Vector3.up * 0.45f,
                Quaternion.LookRotation(fwd),
                new Vector3(0.35f, 0.9f, 0.35f));
            post.name = "Post";
        }
    }

    // ---------------------------------------------------------------- trees

    static Material MakeGlowMat(Shader shader, Color c, float intensity)
    {
        var m = new Material(shader) { color = c };
        m.EnableKeyword("_EMISSION");
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        return m;
    }

    [Tooltip("0 = day forest, 1 = night city. Driven by GameManager.")]
    [Range(0f, 1f)] public float biomeBlend;

    /// <summary>
    /// Continuous day-forest to night-city blend. Ground and road darken
    /// gradually and buildings replace trees a few at a time, so the
    /// changeover reads as driving from countryside into the outskirts.
    /// </summary>
    public void SetBiomeBlend(float t)
    {
        biomeBlend = Mathf.Clamp01(t);
        RefreshBiomeMaterials();
    }

    /// <summary>0 = city, 1 = full snowy mountains (applied after the city).</summary>
    public void SetSnowBlend(float t)
    {
        snowBlend = Mathf.Clamp01(t);
        RefreshBiomeMaterials();
    }

    void RefreshBiomeMaterials()
    {
        CreateMaterials();

        Color road = Color.Lerp(roadColor, cityRoadColor, biomeBlend);
        Color ground = Color.Lerp(groundColor, cityGroundColor, biomeBlend);
        roadMat.color = Color.Lerp(road, snowRoadColor, snowBlend);
        groundMat.color = Color.Lerp(ground, snowGroundColor, snowBlend);
        if (roadMat.HasProperty("_Smoothness"))
        {
            float s = Mathf.Lerp(0.2f, 0.6f, biomeBlend);
            roadMat.SetFloat("_Smoothness", Mathf.Lerp(s, 0.45f, snowBlend));
        }

        biome = snowBlend >= 0.5f ? Biome.SnowMountains
              : biomeBlend >= 0.5f ? Biome.NightCity : Biome.Forest;
    }

    /// <summary>Hard switch (kept for convenience/testing).</summary>
    public void SetBiome(Biome b)
    {
        SetBiomeBlend(b == Biome.NightCity ? 1f : 0f);
    }

    // ---------------------------------------------------------- night city

    void SpawnCityAt(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float half = roadWidth * 0.5f;

        for (int side = -1; side <= 1; side += 2)
        {
            // street lamp every other block
            if (Random.value < 0.5f) SpawnStreetLamp(dist, pos, right, side, half + 1.6f);

            if (Random.value > treeDensity) continue;

            float offset = half + Random.Range(6f, 26f);
            Vector3 spot = pos + right * (offset * side)
                           + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, 3f);
            if (!AreaClearOfRoad(spot, half + 4f)) continue;

            SpawnBuilding(dist, spot, fwd);
        }
    }

    void SpawnBuilding(float dist, Vector3 spot, Vector3 fwd)
    {
        var b = new GameObject("Building");
        b.transform.SetParent(transform, false);
        b.transform.position = spot;
        b.transform.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, Random.Range(-6f, 6f), 0f);

        float w = Random.Range(6f, 13f);
        float d = Random.Range(6f, 12f);
        float h = Random.Range(10f, 46f);
        Material body = Random.value < 0.5f ? buildingMatA : buildingMatB;

        MakePart(PrimitiveType.Cube, b.transform, body,
            spot + Vector3.up * (h * 0.5f), b.transform.rotation, new Vector3(w, h, d));

        // lit windows on all four faces, so every angle shows a living building
        int floors = Mathf.Clamp(Mathf.FloorToInt(h / 3.0f), 2, 16);
        for (int face = 0; face < 4; face++)
        {
            bool alongZ = face < 2;              // 0,1 = front/back, 2,3 = sides
            float sign = (face % 2 == 0) ? 1f : -1f;
            float faceW = alongZ ? w : d;
            int cols = Mathf.Max(2, Mathf.FloorToInt(faceW / 2.4f));

            for (int f = 0; f < floors; f++)
            {
                float y = 2.0f + f * 3.0f;
                if (y > h - 1.0f) break;
                for (int c = 0; c < cols; c++)
                {
                    if (Random.value < 0.32f) continue; // dark windows
                    float u = -faceW * 0.38f + c * (faceW * 0.76f / Mathf.Max(1, cols - 1));
                    Vector3 local = alongZ
                        ? new Vector3(u, y, sign * (d * 0.5f + 0.06f))
                        : new Vector3(sign * (w * 0.5f + 0.06f), y, u);
                    Vector3 scale = alongZ
                        ? new Vector3(1.1f, 1.45f, 0.12f)
                        : new Vector3(0.12f, 1.45f, 1.1f);
                    // a few windows glow cooler, like screens
                    Material wm = Random.value < 0.12f ? neonCyan : windowMat;
                    MakePart(PrimitiveType.Cube, b.transform, wm,
                        spot + b.transform.rotation * local, b.transform.rotation, scale);
                }
            }
        }

        // rooftop neon sign on the taller towers
        if (h > 24f && Random.value < 0.55f)
        {
            Material neon = Random.value < 0.34f ? neonPink
                          : Random.value < 0.5f ? neonCyan : neonAmber;
            MakePart(PrimitiveType.Cube, b.transform, neon,
                spot + Vector3.up * (h + 1.4f), b.transform.rotation,
                new Vector3(w * 0.75f, 2.2f, 0.25f));
        }

        decorations.Add(new Chunk { endDistance = dist, go = b });
    }

    void SpawnStreetLamp(float dist, Vector3 pos, Vector3 right, int side, float offset)
    {
        Vector3 basePos = pos + right * (offset * side);
        var lamp = new GameObject("StreetLamp");
        lamp.transform.SetParent(transform, false);
        lamp.transform.position = basePos;

        MakePart(PrimitiveType.Cylinder, lamp.transform, poleMat,
            basePos + Vector3.up * 3f, Quaternion.identity, new Vector3(0.16f, 3f, 0.16f));
        MakePart(PrimitiveType.Cube, lamp.transform, poleMat,
            basePos + Vector3.up * 5.9f - right * (side * 0.7f), Quaternion.identity,
            new Vector3(1.5f, 0.16f, 0.16f));
        MakePart(PrimitiveType.Cube, lamp.transform, neonAmber,
            basePos + Vector3.up * 5.7f - right * (side * 1.35f), Quaternion.identity,
            new Vector3(0.7f, 0.18f, 0.5f));

        decorations.Add(new Chunk { endDistance = dist, go = lamp });
    }

    // ------------------------------------------------------ snow mountains

    void SpawnSnowSceneryAt(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        float half = roadWidth * 0.5f;

        for (int side = -1; side <= 1; side += 2)
        {
            if (Random.value > treeDensity) continue;

            float offset = half + Random.Range(treeMinFromRoad, treeMaxFromRoad);
            Vector3 spot = pos + right * (offset * side)
                + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, 2.5f);
            if (!AreaClearOfRoad(spot, half + 2f)) continue;

            LoadNaturePrefabs();
            float roll = Random.value;

            // real snowy models from the nature pack when available
            if (roll < 0.62f && snowPrefabs != null && snowPrefabs.Count > 0)
            {
                GameObject p = snowPrefabs[Random.Range(0, snowPrefabs.Count)];
                GameObject go = PlaceNatureModel(p, spot, Random.Range(treeHeightMin, treeHeightMax));
                decorations.Add(new Chunk { endDistance = dist, go = go });
            }
            else if (roll < 0.85f && snowRockPrefabs != null && snowRockPrefabs.Count > 0)
            {
                GameObject p = snowRockPrefabs[Random.Range(0, snowRockPrefabs.Count)];
                GameObject go = PlaceNatureModel(p, spot, Random.Range(1.2f, 3.2f));
                decorations.Add(new Chunk { endDistance = dist, go = go });
            }
            else if (roll < 0.93f) SpawnSnowPine(dist, spot);   // procedural fallback
            else SpawnSnowDrift(dist, spot);
        }

    }

    void SpawnSnowPine(float dist, Vector3 spot)
    {
        var tree = new GameObject("SnowPine");
        tree.transform.SetParent(transform, false);
        tree.transform.position = spot;
        tree.transform.rotation = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(0f, 360f), Random.Range(-3f, 3f));

        float s = Random.Range(0.8f, 2.0f);
        float trunkH = 1.3f * s;
        MakePart(PrimitiveType.Cylinder, tree.transform, trunkMat,
            spot + Vector3.up * (trunkH * 0.5f), Quaternion.identity,
            new Vector3(0.26f * s, trunkH * 0.5f, 0.26f * s));

        for (int i = 0; i < 4; i++)
        {
            float w = 3.0f * (1f - i * 0.23f) * s;
            float y = trunkH + (0.2f + i * 0.85f) * s;
            // dark greengreen tier with a snow cap on top
            MakePart(PrimitiveType.Sphere, tree.transform, leafMatA,
                spot + Vector3.up * y, Quaternion.identity, new Vector3(w, 1.0f * s, w));
            MakePart(PrimitiveType.Sphere, tree.transform, snowMat,
                spot + Vector3.up * (y + 0.28f * s), Quaternion.identity,
                new Vector3(w * 0.86f, 0.45f * s, w * 0.86f));
        }
        decorations.Add(new Chunk { endDistance = dist, go = tree });
    }

    void SpawnRock(float dist, Vector3 spot, float size)
    {
        var rock = new GameObject("Rock");
        rock.transform.SetParent(transform, false);
        rock.transform.position = spot;

        int lumps = Random.Range(2, 4);
        for (int i = 0; i < lumps; i++)
        {
            Vector3 off = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(-0.2f, 0.2f), Random.Range(-0.4f, 0.4f)) * size;
            MakePart(PrimitiveType.Cube, rock.transform, rockMat,
                spot + off + Vector3.up * size * 0.35f,
                Quaternion.Euler(Random.Range(0f, 40f), Random.Range(0f, 360f), Random.Range(0f, 40f)),
                Vector3.one * size * Random.Range(0.6f, 1.1f));
        }
        // snow settled on top
        MakePart(PrimitiveType.Sphere, rock.transform, snowMat,
            spot + Vector3.up * size * 0.75f, Quaternion.identity,
            new Vector3(size * 0.95f, size * 0.35f, size * 0.95f));

        decorations.Add(new Chunk { endDistance = dist, go = rock });
    }

    void SpawnSnowDrift(float dist, Vector3 spot)
    {
        var drift = new GameObject("SnowDrift");
        drift.transform.SetParent(transform, false);
        drift.transform.position = spot;
        float w = Random.Range(3f, 7f);
        MakePart(PrimitiveType.Sphere, drift.transform, snowMat,
            spot + Vector3.up * 0.2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
            new Vector3(w, Random.Range(0.9f, 1.8f), w * 0.6f));
        decorations.Add(new Chunk { endDistance = dist, go = drift });
    }

    void SpawnTreesAt(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

        float half = roadWidth * 0.5f;
        float midBand = treeMinFromRoad + (treeMaxFromRoad - treeMinFromRoad) * 0.4f;

        for (int side = -1; side <= 1; side += 2)
        {
            if (Random.value > treeDensity) continue; // occasional gap

            // near row: right at the roadside, and often a second tree deeper in
            SpawnOneTree(dist, pos, right, side,
                half + Random.Range(treeMinFromRoad, midBand));

            if (Random.value < 0.85f)
            {
                SpawnOneTree(dist, pos, right, side,
                    half + Random.Range(midBand, treeMaxFromRoad));
            }
        }
    }

    void LoadNaturePrefabs()
    {
        if (treePrefabs != null) return;
        treePrefabs = new List<GameObject>();
        bushPrefabs = new List<GameObject>();
        foreach (string n in TreeModelNames)
        {
            GameObject p = Resources.Load<GameObject>(n);
            if (p != null) treePrefabs.Add(p);
        }
        foreach (string n in BushModelNames)
        {
            GameObject p = Resources.Load<GameObject>(n);
            if (p != null) bushPrefabs.Add(p);
        }
        snowPrefabs = new List<GameObject>();
        foreach (string n in SnowModelNames)
        {
            GameObject p = Resources.Load<GameObject>(n);
            if (p != null) snowPrefabs.Add(p);
        }
        snowRockPrefabs = new List<GameObject>();
        foreach (string n in SnowRockNames)
        {
            GameObject p = Resources.Load<GameObject>(n);
            if (p != null) snowRockPrefabs.Add(p);
        }
    }

    /// <summary>Places a nature prefab, scaled to a target height and seated on the ground.</summary>
    GameObject PlaceNatureModel(GameObject prefab, Vector3 spot, float targetHeight)
    {
        GameObject go = Instantiate(prefab, spot,
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * prefab.transform.rotation, transform);

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            float k = targetHeight / Mathf.Max(0.01f, b.size.y);
            go.transform.localScale = go.transform.localScale * k;
            float newBottom = go.transform.position.y + (b.min.y - go.transform.position.y) * k;
            go.transform.position += Vector3.up * (spot.y - newBottom);
        }
        return go;
    }

    void SpawnOneTree(float dist, Vector3 pos, Vector3 right, int side, float offset)
    {
        // jitter along the road direction so trees don't form neat rows
        Vector3 fwdJitter = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward
                            * Random.Range(0f, 2.5f);
        Vector3 spot = pos + right * (offset * side) + fwdJitter;

        if (!AreaClearOfRoad(spot, roadWidth * 0.5f + 2f)) return;

        bool bush = Random.value < 0.15f;
        List<GameObject> pool = bush ? bushPrefabs : treePrefabs;
        if (pool != null && pool.Count > 0)
        {
            GameObject prefab = pool[Random.Range(0, pool.Count)];
            // keep the model's own import rotation (axis correction) and
            // only ADD a random yaw - overriding it lays the tree flat
            GameObject go = Instantiate(prefab, spot,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * prefab.transform.rotation,
                transform);

            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

                float target = bush ? Random.Range(0.7f, 1.5f)
                                    : Random.Range(treeHeightMin, treeHeightMax);
                float k = target / Mathf.Max(0.01f, b.size.y);
                go.transform.localScale = go.transform.localScale * k;

                // seat the model's bottom on the ground (pivot isn't always at the base)
                float newBottom = go.transform.position.y
                                  + (b.min.y - go.transform.position.y) * k;
                go.transform.position += Vector3.up * (spot.y - newBottom);
            }

            decorations.Add(new Chunk { endDistance = dist, go = go });
            return;
        }

        SpawnProceduralTree(dist, spot);
    }

    // fallback if the nature models are missing
    void SpawnProceduralTree(float dist, Vector3 spot)
    {
        var tree = new GameObject("Tree");
        tree.transform.SetParent(transform, false);
        tree.transform.position = spot;

        float s = Random.Range(0.55f, 2.3f);

        // colour: mostly greens, occasional yellow-green, rare autumn tree
        float roll = Random.value;
        Material leaf = roll < 0.42f ? leafMatA
                      : roll < 0.77f ? leafMatB
                      : roll < 0.94f ? leafMatC : autumnMat;

        // small bushes fill the ground level between big trees
        if (Random.value < 0.16f)
        {
            MakePart(PrimitiveType.Sphere, tree.transform, leaf,
                spot + Vector3.up * (0.45f * s), Quaternion.identity,
                new Vector3(1.5f, 0.9f, 1.5f) * s * 0.8f);
            MakePart(PrimitiveType.Sphere, tree.transform, leaf,
                spot + new Vector3(0.6f, 0.3f, 0.3f) * s * 0.8f, Quaternion.identity,
                new Vector3(1.0f, 0.7f, 1.0f) * s * 0.8f);
            decorations.Add(new Chunk { endDistance = dist, go = tree });
            return;
        }

        if (Random.value < 0.5f)
        {
            // pine: four stacked tiers tapering to a point
            float trunkH = 1.2f * s;
            MakePart(PrimitiveType.Cylinder, tree.transform, trunkMat,
                spot + Vector3.up * (trunkH * 0.5f), Quaternion.identity,
                new Vector3(0.28f * s, trunkH * 0.5f, 0.28f * s));

            for (int i = 0; i < 4; i++)
            {
                float w = 3.1f * (1f - i * 0.22f) * s;
                float h = 1.05f * (1f - i * 0.08f) * s;
                float y = trunkH + (0.15f + i * 0.8f) * s;
                MakePart(PrimitiveType.Sphere, tree.transform, leaf,
                    spot + Vector3.up * y, Quaternion.identity,
                    new Vector3(w, h, w));
            }
        }
        else
        {
            // leafy tree: tall trunk, clustered canopy with colour depth
            float trunkH = 2.6f * s;
            MakePart(PrimitiveType.Cylinder, tree.transform, trunkMat,
                spot + Vector3.up * (trunkH * 0.5f), Quaternion.identity,
                new Vector3(0.38f * s, trunkH * 0.5f, 0.38f * s));

            Vector3 top = spot + Vector3.up * (trunkH + 0.8f * s);
            MakePart(PrimitiveType.Sphere, tree.transform, leaf,
                top, Quaternion.identity, new Vector3(2.9f, 2.5f, 2.9f) * s);

            // ring of side blobs, occasionally a different green for depth
            float ringStart = Random.Range(0f, 360f);
            for (int i = 0; i < 3; i++)
            {
                float ang = (ringStart + i * 120f) * Mathf.Deg2Rad;
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.15f * s
                              + Vector3.up * Random.Range(-0.5f, 0.1f) * s;
                Material blobMat = Random.value < 0.25f
                    ? (leaf == leafMatA ? leafMatB : leafMatA) : leaf;
                float bs = Random.Range(1.5f, 2.0f) * s;
                MakePart(PrimitiveType.Sphere, tree.transform, blobMat,
                    top + off, Quaternion.identity, new Vector3(bs, bs * 0.85f, bs));
            }

            // crown blob
            MakePart(PrimitiveType.Sphere, tree.transform, leaf,
                top + Vector3.up * 1.0f * s, Quaternion.identity,
                new Vector3(1.6f, 1.4f, 1.6f) * s);
        }

        // slight random lean so the forest doesn't look machine-planted
        tree.transform.rotation = Quaternion.Euler(
            Random.Range(-3.5f, 3.5f), Random.Range(0f, 360f), Random.Range(-3.5f, 3.5f));

        decorations.Add(new Chunk { endDistance = dist, go = tree });
    }

    bool AreaClearOfRoad(Vector3 spot, float clearance)
    {
        float sq = clearance * clearance;
        for (int i = 0; i < samples.Count; i += 4)
        {
            Vector3 d = samples[i].pos - spot;
            d.y = 0f;
            if (d.sqrMagnitude < sq) return false;
        }
        return true;
    }

    // ------------------------------------------------------------- obstacles

    [Tooltip("Clear width the car must always have to squeeze through.")]
    public float minPassableGap = 3.4f;

    /// <summary>
    /// Picks a lateral position that always leaves a drivable gap next to
    /// anything already blocking this stretch of road - two obstacles can
    /// never seal the lane at the same distance.
    /// </summary>
    bool FindOpenLane(float dist, float halfWidth, float lookRange, out float lateral)
    {
        float limit = roadWidth * 0.5f - halfWidth - 0.3f;
        lateral = 0f;
        if (limit <= 0f) return false;

        // collect what already blocks this stretch
        var blocks = new List<Vector2>(); // (min, max) lateral spans
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleData o = obstacles[i];
            if (Mathf.Abs(o.distance - dist) > lookRange) continue;
            blocks.Add(new Vector2(o.lateral - o.halfWidth, o.lateral + o.halfWidth));
        }
        for (int i = 0; i < traffic.Count; i++)
        {
            TrafficCar t = traffic[i];
            if (Mathf.Abs(t.distance - dist) > lookRange) continue;
            blocks.Add(new Vector2(t.lateral - 1.05f, t.lateral + 1.05f));
        }

        // try random spots, keeping a clear corridor somewhere on the road
        for (int attempt = 0; attempt < 14; attempt++)
        {
            float cand = Random.Range(-limit, limit);
            if (LeavesGap(cand, halfWidth, blocks)) { lateral = cand; return true; }
        }
        return false; // road too congested here - skip this spawn
    }

    bool LeavesGapForTraffic(float dist, float lateral)
    {
        var blocks = new List<Vector2>();
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleData o = obstacles[i];
            if (Mathf.Abs(o.distance - dist) > 12f) continue;
            blocks.Add(new Vector2(o.lateral - o.halfWidth, o.lateral + o.halfWidth));
        }
        for (int i = 0; i < traffic.Count; i++)
        {
            TrafficCar t = traffic[i];
            if (Mathf.Abs(t.distance - dist) > 12f) continue;
            blocks.Add(new Vector2(t.lateral - 1.05f, t.lateral + 1.05f));
        }
        return LeavesGap(lateral, 1.05f, blocks);
    }

    bool LeavesGap(float cand, float halfWidth, List<Vector2> blocks)
    {
        // walk the road left to right and check the widest free corridor
        var spans = new List<Vector2>(blocks) { new Vector2(cand - halfWidth, cand + halfWidth) };
        spans.Sort((a, b) => a.x.CompareTo(b.x));

        float edge = -roadWidth * 0.5f;
        float widest = 0f;
        foreach (var s in spans)
        {
            widest = Mathf.Max(widest, s.x - edge);
            edge = Mathf.Max(edge, s.y);
        }
        widest = Mathf.Max(widest, roadWidth * 0.5f - edge);
        return widest >= minPassableGap;
    }

    void SpawnObstacle(float dist)
    {
        SamplePose(dist, out Vector3 pos, out Vector3 fwd, out _);
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Quaternion along = Quaternion.LookRotation(fwd);

        // each biome gets obstacles that belong in it
        float roll = Random.value;
        ObstacleType blocker = biome == Biome.SnowMountains ? ObstacleType.Boulder
                             : biome == Biome.NightCity ? ObstacleType.Cones
                             : ObstacleType.FallenLog;
        ObstacleType type = roll < 0.36f ? blocker
                          : roll < 0.72f ? ObstacleType.Construction
                          : ObstacleType.OilSpill;

        var data = new ObstacleData { type = type, distance = dist };
        var rootGo = new GameObject("Obstacle_" + type);
        rootGo.transform.SetParent(transform, false);
        data.go = rootGo;

        switch (type)
        {
            case ObstacleType.FallenLog:
            {
                // a log lying across part of the road
                float halfLen = Random.Range(1.8f, 2.6f);
                // hitbox smaller than the visual so clipping a log's edge is forgiven
                data.halfWidth = halfLen * 0.60f;
                data.halfDepth = 0.32f;
                if (!FindOpenLane(dist, data.halfWidth, 9f, out data.lateral))
                { Object.Destroy(rootGo); return; }

                Vector3 basePos = pos + right * data.lateral + Vector3.up * 0.45f;
                Quaternion lie = along * Quaternion.Euler(0f, Random.Range(-15f, 15f), 90f);
                MakePart(PrimitiveType.Cylinder, rootGo.transform, logMat,
                    basePos, lie, new Vector3(0.85f, halfLen, 0.85f));
                // a couple of stubby branches
                MakePart(PrimitiveType.Cylinder, rootGo.transform, logMat,
                    basePos + Vector3.up * 0.35f + right * (halfLen * 0.4f),
                    lie * Quaternion.Euler(70f, 0f, 0f), new Vector3(0.25f, 0.6f, 0.25f));
                break;
            }

            case ObstacleType.Construction:
            {
                // striped barriers blocking part of a lane
                // (hitbox tighter than the visuals - grazing is forgiven)
                data.halfWidth = 1.05f;
                data.halfDepth = 0.7f;
                if (!FindOpenLane(dist, data.halfWidth, 9f, out data.lateral))
                { Object.Destroy(rootGo); return; }

                Vector3 basePos = pos + right * data.lateral;
                for (int i = -1; i <= 1; i += 2)
                {
                    Quaternion rot = along * Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f);
                    Vector3 p = basePos + fwd * (i * 0.75f) + right * Random.Range(-0.5f, 0.5f);
                    MakePart(PrimitiveType.Cube, rootGo.transform, barrierMat,
                        p + Vector3.up * 0.55f, rot, new Vector3(2.6f, 0.8f, 0.35f));
                    MakePart(PrimitiveType.Cube, rootGo.transform, stripeMat,
                        p + Vector3.up * 1.05f, rot, new Vector3(2.6f, 0.18f, 0.37f));
                }
                break;
            }

            case ObstacleType.Cones:
            {
                // a little row of traffic cones with a warning light
                data.halfWidth = 1.05f;
                data.halfDepth = 0.55f;
                if (!FindOpenLane(dist, data.halfWidth, 9f, out data.lateral))
                { Object.Destroy(rootGo); return; }
                Vector3 basePos = pos + right * data.lateral;

                int count = Random.Range(3, 5);
                for (int i = 0; i < count; i++)
                {
                    float u = -1.1f + i * (2.2f / Mathf.Max(1, count - 1));
                    Vector3 p = basePos + right * u + fwd * Random.Range(-0.35f, 0.35f);
                    // stacked cylinders fake a tapered cone
                    MakePart(PrimitiveType.Cube, rootGo.transform, stripeMat,
                        p + Vector3.up * 0.05f, along, new Vector3(0.62f, 0.1f, 0.62f));
                    MakePart(PrimitiveType.Cylinder, rootGo.transform, barrierMat,
                        p + Vector3.up * 0.3f, along, new Vector3(0.42f, 0.28f, 0.42f));
                    MakePart(PrimitiveType.Cylinder, rootGo.transform, stripeMat,
                        p + Vector3.up * 0.56f, along, new Vector3(0.3f, 0.06f, 0.3f));
                    MakePart(PrimitiveType.Cylinder, rootGo.transform, barrierMat,
                        p + Vector3.up * 0.74f, along, new Vector3(0.2f, 0.2f, 0.2f));
                }
                // blinking-style amber lamp on top of the middle cone
                MakePart(PrimitiveType.Sphere, rootGo.transform, neonAmber,
                    basePos + Vector3.up * 1.0f, Quaternion.identity,
                    new Vector3(0.28f, 0.28f, 0.28f));
                break;
            }

            case ObstacleType.Boulder:
            {
                // snow-capped boulder fallen onto the road
                data.halfWidth = 1.25f;
                data.halfDepth = 0.95f;
                if (!FindOpenLane(dist, data.halfWidth, 9f, out data.lateral))
                { Object.Destroy(rootGo); return; }

                Vector3 bp = pos + right * data.lateral;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 off = new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-0.4f, 0.4f));
                    MakePart(PrimitiveType.Cube, rootGo.transform, rockMat,
                        bp + off + Vector3.up * 0.75f,
                        Quaternion.Euler(Random.Range(0f, 35f), Random.Range(0f, 360f), Random.Range(0f, 35f)),
                        Vector3.one * Random.Range(1.1f, 1.8f));
                }
                MakePart(PrimitiveType.Sphere, rootGo.transform, snowMat,
                    bp + Vector3.up * 1.5f, Quaternion.identity, new Vector3(1.9f, 0.6f, 1.7f));
                break;
            }

            case ObstacleType.OilSpill:
            {
                // doesn't kill you - makes you spin out
                data.halfWidth = 1.9f;
                data.halfDepth = 1.9f;
                if (!FindOpenLane(dist, data.halfWidth, 9f, out data.lateral))
                { Object.Destroy(rootGo); return; }

                Vector3 basePos = pos + right * data.lateral + Vector3.up * 0.04f;
                // black oil in the forest and city, pale ice on the mountain
                Material slick = biome == Biome.SnowMountains ? iceMat : oilMat;
                MakePart(PrimitiveType.Cylinder, rootGo.transform, slick,
                    basePos, Quaternion.identity, new Vector3(3.8f, 0.02f, 3.8f));
                MakePart(PrimitiveType.Cylinder, rootGo.transform, slick,
                    basePos + fwd * 1.4f + right * 0.9f, Quaternion.identity,
                    new Vector3(1.6f, 0.02f, 1.6f));
                break;
            }
        }

        obstacles.Add(data);
    }

    GameObject MakePart(PrimitiveType prim, Transform parent, Material mat,
        Vector3 worldPos, Quaternion worldRot, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(prim);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        go.transform.rotation = worldRot;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // -------------------------------------------------------------- chunking

    void StartNewChunk(float startDist)
    {
        currentChunk = new GameObject("RoadChunk");
        currentChunk.transform.SetParent(transform, false);
        currentChunkStart = startDist;
        currentChunkFirstSampleIndexOffset = Mathf.Max(0, samples.Count - 1);
    }

    void BakeCurrentChunk()
    {
        int first = currentChunkFirstSampleIndexOffset;
        int last = samples.Count - 1;
        if (last <= first || currentChunk == null) return;

        int count = last - first + 1;
        var verts = new Vector3[count * 2];
        var uvs = new Vector2[count * 2];
        var tris = new int[(count - 1) * 6];

        float half = roadWidth * 0.5f;
        for (int i = 0; i < count; i++)
        {
            Sample s = samples[first + i];
            Vector3 fwd = Quaternion.Euler(0f, s.headingDeg, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            verts[i * 2] = s.pos - right * half;
            verts[i * 2 + 1] = s.pos + right * half;
            uvs[i * 2] = new Vector2(0f, i);
            uvs[i * 2 + 1] = new Vector2(1f, i);
        }

        for (int i = 0; i < count - 1; i++)
        {
            int v = i * 2;
            int tIdx = i * 6;
            tris[tIdx] = v;
            tris[tIdx + 1] = v + 2;
            tris[tIdx + 2] = v + 1;
            tris[tIdx + 3] = v + 1;
            tris[tIdx + 4] = v + 2;
            tris[tIdx + 5] = v + 3;
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mf = currentChunk.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = currentChunk.AddComponent<MeshRenderer>();
        mr.sharedMaterial = roadMat;

        BuildMarkings(first, last);
        BuildVerges(first, last);

        chunks.Add(new Chunk { endDistance = frontDistance, go = currentChunk });
        currentChunk = null;
    }

    /// <summary>
    /// Wide ground strips either side of the road that follow its elevation.
    /// Without these, a flat world plane cuts straight through sloped road
    /// and hides what is coming.
    /// </summary>
    // white edge lines + dashed centre line (two lanes)
    void BuildMarkings(int first, int last)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        float edgeOffset = roadWidth * 0.5f - 0.45f;
        const float LineHalf = 0.13f;
        const float DashPeriod = 6f; // 3m dash, 3m gap

        for (int i = first; i < last; i++)
        {
            Sample a = samples[i];
            Sample b = samples[i + 1];
            Vector3 ra = Vector3.Cross(Vector3.up, Quaternion.Euler(0f, a.headingDeg, 0f) * Vector3.forward).normalized;
            Vector3 rb = Vector3.Cross(Vector3.up, Quaternion.Euler(0f, b.headingDeg, 0f) * Vector3.forward).normalized;

            AddLineQuad(verts, tris, a.pos + ra * edgeOffset, b.pos + rb * edgeOffset, ra, rb, LineHalf);
            AddLineQuad(verts, tris, a.pos - ra * edgeOffset, b.pos - rb * edgeOffset, ra, rb, LineHalf);

            float d = baseDistance + i * SampleSpacing;
            if (d % DashPeriod < 3f)
            {
                AddLineQuad(verts, tris, a.pos, b.pos, ra, rb, LineHalf);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Markings");
        go.transform.SetParent(currentChunk.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = stripeMat;
    }

    void AddLineQuad(List<Vector3> verts, List<int> tris,
        Vector3 pa, Vector3 pb, Vector3 ra, Vector3 rb, float half)
    {
        Vector3 lift = Vector3.up * 0.03f;
        int v = verts.Count;
        verts.Add(pa - ra * half + lift);
        verts.Add(pa + ra * half + lift);
        verts.Add(pb - rb * half + lift);
        verts.Add(pb + rb * half + lift);
        tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
        tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
    }

    void BuildVerges(int first, int last)
    {
        // three rings out from the road, each a little lower, so the ground
        // reads as a shoulder falling away instead of a flat sheet that can
        // slice through a climbing road
        float[] widths = { 9f, 20f, 38f };
        float[] drops = { 0.06f, 0.5f, 2.2f };

        int count = last - first + 1;
        if (count < 2) return;

        int rings = widths.Length;
        int cols = (rings + 1) * 2;           // mirrored either side of the road
        var verts = new Vector3[count * cols];
        var tris = new List<int>((count - 1) * (cols - 1) * 6);
        float half = roadWidth * 0.5f;

        for (int i = 0; i < count; i++)
        {
            Sample sm = samples[first + i];
            Vector3 fwd = Quaternion.Euler(0f, sm.headingDeg, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // left side, far to near, then right side, near to far
            for (int rIdx = 0; rIdx < rings; rIdx++)
            {
                int band = rings - 1 - rIdx;
                verts[i * cols + rIdx] = sm.pos
                    - right * (half + widths[band]) - Vector3.up * drops[band];
            }
            verts[i * cols + rings] = sm.pos - right * half - Vector3.up * drops[0];
            verts[i * cols + rings + 1] = sm.pos + right * half - Vector3.up * drops[0];
            for (int rIdx = 0; rIdx < rings; rIdx++)
            {
                verts[i * cols + rings + 2 + rIdx] = sm.pos
                    + right * (half + widths[rIdx]) - Vector3.up * drops[rIdx];
            }
        }

        for (int i = 0; i < count - 1; i++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                // skip the strip directly under the road - the road covers it
                if (c == rings) continue;
                int v = i * cols + c;
                tris.Add(v); tris.Add(v + cols); tris.Add(v + 1);
                tris.Add(v + 1); tris.Add(v + cols); tris.Add(v + cols + 1);
            }
        }

        var mesh = new Mesh();
        mesh.vertices = verts;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Verge");
        go.transform.SetParent(currentChunk.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
    }

    void Prune(float carDistance)
    {
        float cutoff = carDistance - behindDistance;

        int removeCount = Mathf.FloorToInt((cutoff - baseDistance) / SampleSpacing) - 2;
        if (removeCount > 0 && removeCount < samples.Count - 4)
        {
            samples.RemoveRange(0, removeCount);
            baseDistance += removeCount * SampleSpacing;
            currentChunkFirstSampleIndexOffset = Mathf.Max(0, currentChunkFirstSampleIndexOffset - removeCount);
        }

        PruneList(chunks, cutoff);
        PruneList(decorations, cutoff);

        for (int i = obstacles.Count - 1; i >= 0; i--)
        {
            if (obstacles[i].distance < carDistance - 25f)
            {
                if (obstacles[i].go != null) Object.Destroy(obstacles[i].go);
                obstacles.RemoveAt(i);
            }
        }

        for (int i = coins.Count - 1; i >= 0; i--)
        {
            if (coins[i].taken || coins[i].distance < carDistance - 25f)
            {
                if (coins[i].go != null) Object.Destroy(coins[i].go);
                coins.RemoveAt(i);
            }
        }

        for (int i = powerUps.Count - 1; i >= 0; i--)
        {
            if (powerUps[i].distance < carDistance - 25f)
            {
                if (powerUps[i].go != null) Object.Destroy(powerUps[i].go);
                powerUps.RemoveAt(i);
            }
        }

        for (int i = boostPads.Count - 1; i >= 0; i--)
        {
            if (boostPads[i].distance < carDistance - 25f)
            {
                if (boostPads[i].go != null) Object.Destroy(boostPads[i].go);
                boostPads.RemoveAt(i);
            }
        }

        for (int i = tirePickups.Count - 1; i >= 0; i--)
        {
            if (tirePickups[i].distance < carDistance - 25f)
            {
                if (tirePickups[i].go != null) Object.Destroy(tirePickups[i].go);
                tirePickups.RemoveAt(i);
            }
        }
    }

    void PruneList(List<Chunk> list, float cutoff)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].endDistance < cutoff)
            {
                if (list[i].go != null) Object.Destroy(list[i].go);
                list.RemoveAt(i);
            }
        }
    }

    // ---------------------------------------------------------------- queries

    public void SamplePose(float dist, out Vector3 pos, out Vector3 forward, out float curvatureDegPerMeter)
    {
        if (samples.Count == 0)
        {
            pos = Vector3.zero;
            forward = Vector3.forward;
            curvatureDegPerMeter = 0f;
            return;
        }

        float f = (dist - baseDistance) / SampleSpacing;
        int i0 = Mathf.Clamp(Mathf.FloorToInt(f), 0, samples.Count - 1);
        int i1 = Mathf.Min(i0 + 1, samples.Count - 1);
        float t = Mathf.Clamp01(f - i0);

        Sample a = samples[i0];
        Sample b = samples[i1];

        pos = Vector3.Lerp(a.pos, b.pos, t);
        float heading = Mathf.LerpAngle(a.headingDeg, b.headingDeg, t);
        forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
        curvatureDegPerMeter = Mathf.Lerp(a.curvature, b.curvature, t);
    }

    public Vector3 SamplePosition(float dist)
    {
        SamplePose(dist, out Vector3 p, out _, out _);
        return p;
    }

    public ObstacleHit CheckObstacleHit(float dist, float lateral, float carRadius)
    {
        ObstacleHit result = ObstacleHit.None;
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleData o = obstacles[i];
            if (Mathf.Abs(o.distance - dist) > 6f) continue;

            bool overlapping =
                Mathf.Abs(o.distance - dist) < o.halfDepth + carRadius &&
                Mathf.Abs(o.lateral - lateral) < o.halfWidth + carRadius;
            if (!overlapping) continue;

            if (o.type == ObstacleType.OilSpill)
            {
                if (!o.consumed)
                {
                    o.consumed = true;
                    if (result == ObstacleHit.None) result = ObstacleHit.OilSpill;
                }
            }
            else
            {
                return ObstacleHit.Solid; // solid hits always win
            }
        }

        // traffic cars are solid
        for (int i = 0; i < traffic.Count; i++)
        {
            TrafficCar tc = traffic[i];
            if (Mathf.Abs(tc.distance - dist) < 1.8f + carRadius &&
                Mathf.Abs(tc.lateral - lateral) < 0.9f + carRadius)
            {
                return ObstacleHit.Solid;
            }
        }
        return result;
    }

    /// <summary>
    /// Shield hit: punts any solid obstacle or traffic car the player is
    /// overlapping clean off the road. Returns true if something was hit.
    /// Oil spills are left alone - there is nothing solid to knock away.
    /// </summary>
    public bool TryKnockAside(float dist, float lateral, float carRadius, float speed)
    {
        bool hitSomething = false;
        SamplePose(dist, out Vector3 pos, out Vector3 forward, out _);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        for (int i = obstacles.Count - 1; i >= 0; i--)
        {
            ObstacleData o = obstacles[i];
            if (o.type == ObstacleType.OilSpill) continue;
            if (Mathf.Abs(o.distance - dist) > o.halfDepth + carRadius) continue;
            if (Mathf.Abs(o.lateral - lateral) > o.halfWidth + carRadius) continue;

            // away from the car, so it never flies across the player's view
            float dir = o.lateral >= lateral ? 1f : -1f;
            KnockedProp.Launch(o.go, forward, right, dir, Mathf.Max(14f, speed * 0.75f));
            obstacles.RemoveAt(i);
            hitSomething = true;
        }

        for (int i = traffic.Count - 1; i >= 0; i--)
        {
            TrafficCar tc = traffic[i];
            if (Mathf.Abs(tc.distance - dist) > 1.8f + carRadius) continue;
            if (Mathf.Abs(tc.lateral - lateral) > 0.9f + carRadius) continue;

            float dir = tc.lateral >= lateral ? 1f : -1f;
            KnockedProp.Launch(tc.go, forward, right, dir, Mathf.Max(16f, speed * 0.85f));
            traffic.RemoveAt(i);
            hitSomething = true;
        }

        return hitSomething;
    }

    public int ConsumeNearMisses(float dist, float lateral, float carRadius)
    {
        int count = 0;
        for (int i = 0; i < obstacles.Count; i++)
        {
            ObstacleData o = obstacles[i];
            if (o.nearMissGiven || o.type == ObstacleType.OilSpill) continue;
            if (dist > o.distance + o.halfDepth && dist < o.distance + o.halfDepth + 6f)
            {
                o.nearMissGiven = true;
                float gap = Mathf.Abs(o.lateral - lateral) - o.halfWidth - carRadius;
                if (gap < nearMissRange) count++;
            }
        }

        // squeezing past traffic counts too
        for (int i = 0; i < traffic.Count; i++)
        {
            TrafficCar tc = traffic[i];
            if (tc.nearMissGiven) continue;
            if (dist > tc.distance + 2.1f && dist < tc.distance + 10f)
            {
                tc.nearMissGiven = true;
                float gap = Mathf.Abs(tc.lateral - lateral) - 1.05f - carRadius;
                if (gap < nearMissRange) count++;
            }
        }
        return count;
    }
}
