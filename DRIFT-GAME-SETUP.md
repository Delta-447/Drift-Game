# Drift Game — Setup & Tuning

## What changed

Your project now has a complete playable loop: endless procedural track with corners, thumb-slide drift steering, obstacles, speed that ramps up over time, scoring (distance + drift bonus + near-miss bonus), high score saved on device, crash + instant tap-to-retry, and a runtime-built UI.

New/rewritten scripts in `Assets/Scripts`:

| Script | Role |
|---|---|
| `GameManager.cs` (new) | Game states, scoring, high score, builds all UI at runtime |
| `TrackGenerator.cs` (new) | Endless road: corners, road mesh, edge posts, obstacles, cleanup |
| `CarController.cs` (rewritten) | Track-following drift physics + thumb-slide input |
| `CameraFollow.cs` (rewritten) | Chase cam with smoothing, speed-based FOV, crash shake |

`RoadChunk.cs` and the `Straight Road` prefab are no longer used (safe to delete later).

## Scene setup (2 minutes)

1. Open `SampleScene`.
2. Create an empty GameObject, name it **Game**, add the **GameManager** component. That's the only required step — it finds your PlayerCar, sets up the camera, track, fog, and UI automatically.
3. Your **PlayerCar** already has the CarController component (same script file). It auto-uses its first child as the car model. **If the car drives backwards visually, tick `Flip Visual 180` on CarController.**
4. Disable or delete the old **Road Network** / **Default Road 001** (EasyRoads) and the **Terrain** — the game spawns its own road and ground. If you keep the terrain, make sure it's flat where the car starts.
5. The old `MainMenuPanel` is hidden automatically at runtime; you can delete it and the StartButton whenever.
6. Press **Play**. Click to start, drag the mouse left/right to steer (that's the thumb-slide on a phone). Arrow keys / A-D also work in the editor.

## How it plays

- The car accelerates forever (`baseSpeed` → `maxSpeed`).
- Corners throw the car outward (centrifugal force). Holding the line through a corner at speed **is** the drift — the faster you go, the harder you fight the slide.
- Crash by leaving the road or hitting an obstacle. Tap → instantly back in.
- Score = meters traveled + drift time bonus + 150 per near-miss.

## Tuning knobs (the ones that matter)

| Where | Field | Effect |
|---|---|---|
| CarController | `centrifugalFactor` | The core difficulty knob. Higher = corners fight you harder |
| CarController | `speedGainPerSecond`, `maxSpeed` | How fast runs escalate |
| CarController | `steerZoneFraction` | Thumb sensitivity (lower = twitchier) |
| CarController | `grip` | How fast slides settle when you release |
| TrackGenerator | `difficultyRampMeters` | Distance until corners/obstacles reach max difficulty |
| TrackGenerator | `obstacleSpacingStart/Min` | Obstacle density |
| TrackGenerator | `roadWidth` | Forgiveness |
| GameManager | `driftPointsPerSecond`, `nearMissBonus` | Score feel |

Tune in Play mode, note the values, apply after.

## Mobile build

1. File → Build Profiles → Android (or iOS) → Switch Platform.
2. Player Settings: **Portrait** orientation recommended (UI is designed 1080×1920, one-thumb play), IL2CPP + ARM64 (required for stores), Vulkan+GLES3 for Android.
3. The game already caps at 60 fps and everything is flat-shaded primitives — it will run on anything.

## Good next steps (roughly in order of impact)

1. **Audio** — looping engine pitch tied to `CurrentSpeed`, a skid loop while `IsDrifting`, crash thump. Biggest missing piece of juice.
2. **Skid marks + smoke** — two `TrailRenderer`s at the rear wheels + a `ParticleSystem`, emit while `IsDrifting`.
3. **Use your car pack** — swap the placeholder visual for the Designersoup models; unlockable cars are a classic retention hook.
4. **Obstacle variety** — swap the cubes in `TrackGenerator.SpawnObstacle` for prefabs (cones, barriers, parked cars).
5. **Leaderboards** — Google Play Games / Game Center once the loop feels right.
