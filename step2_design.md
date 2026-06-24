# Technical Architecture Design — Capsule Dash: Obstacle Death Fix & Wind Particle Enhancement

## Overview

This design addresses two issues in the Capsule Dash 3D endless runner:

1. **Obstacle Collision Death Fix**: The `OnCollisionEnter` code in `PlayerController` already exists and is logically correct, but collision detection may fail at runtime because (a) the player's Rigidbody uses discrete collision detection, allowing fast-moving obstacles to tunnel through the player (bullet-through-paper), and (b) the player's Rigidbody is configured with non-kinematic constraints that could interfere. The fix is minimal: enable **Continuous Dynamic collision detection** on the player Rigidbody and add a defensive debug-log to aid verification.

2. **Wind/Scroll Particle Enhancement**: The current `WindEffect` uses a basic `Stretch`-billboard `ParticleSystem` with white semi-transparent particles emitting from a box volume. This produces thin white dots that lack visual depth. The enhancement adds three additional ParticleSystem modules — **Trails** (PerParticle mode), **ColorOverLifetime**, and **SizeOverLifetime** — all purely code-configurable, requiring no new assets or Placeholders modifications. The result is realistic fading speed-line streaks with natural size variation and alpha fade-in/fade-out.

Both changes integrate into the existing code without architectural refactoring. No new files are needed — only modifications to two existing scripts: `PlayerController.cs` and `WindEffect.cs`. All other components (`SceneBootstrapper`, `GameManager`, `UIManager`, `ObstacleSpawner`, `Obstacle`, `InputHandler`, `CameraFollow`, `Placeholders`) remain unchanged.

---

## Architecture Diagram (Text)

```
┌──────────────────────────────────────────────────────────────────────┐
│                       SceneBootstrapper                              │
│  BuildScene(): wires Player + WindEffect + GameManager + Spawner     │
│  (NO CHANGE — already correctly attaches WindEffect to Player)       │
└───────┬──────────────────────────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────────────────────────────────┐
│  ┌────────────────────┐    ┌────────────────────┐                    │
│  │     Player         │    │     Camera         │                    │
│  │   (Capsule)        │    │   (Follow)          │                    │
│  │   Z=0 stationary   │    │   Z offset behind   │                    │
│  │  ┌───────────────┐ │    └────────────────────┘                    │
│  │  │  WindEffect    │ │                                              │
│  │  │  (child GO)    │ │                                              │
│  │  │  ┌───────────┐ │ │                                              │
│  │  │  │ Particle  │ │ │  ← Trails module (NEW)                       │
│  │  │  │ System    │ │ │  ← ColorOverLifetime (NEW)                   │
│  │  │  │ ───────── │ │ │  ← SizeOverLifetime (NEW)                    │
│  │  │  │ Stretch   │ │ │  ← existing: Shape, Emission, Velocity       │
│  │  │  └───────────┘ │ │                                              │
│  │  └───────────────┘ │                                              │
│  │  ┌───────────────┐ │                                              │
│  │  │ PlayerControl │ │  ← CCD added on Rigidbody (NEW)              │
│  │  │ OnCollision   │ │  ← _isDead guard → red material → GameOver() │
│  │  └───────────────┘ │                                              │
│  └────────┬───────────┘                                              │
│           │                                                           │
│  ┌────────┴──────────────────────────────────────────────────┐       │
│  │                    GameManager                             │       │
│  │  [DefaultExecutionOrder(-100)] singleton                   │       │
│  │  Distance += ForwardSpeed * dt                             │       │
│  │  GameOver() → OnGameOver event → UIManager.ShowGameOver()  │       │
│  └────────┬──────────────────────────────────────────────────┘       │
│           │                                                           │
│  ┌────────┴──────────────────────────────────────────────────┐       │
│  │              ObstacleSpawner                               │       │
│  │  ObjectPool<GameObject> (cube obstacles)                   │       │
│  │  CreateObstacle() → Placeholders.CreatePrimitive(Cube)     │       │
│  │  → tag = "Obstacle"  →  AddComponent<Obstacle>()           │       │
│  └────────┬──────────────────────────────────────────────────┘       │
│           │                                                           │
│  ┌────────┴──────────────────────────────────────────────────┐       │
│  │              Obstacle (per-instance)                       │       │
│  │  Scrolls toward player: Vector3.back * speed * dt          │       │
│  │  Self-returns to pool when Z < player.Z - 10f              │       │
│  │  BoxCollider (from CreatePrimitive) — NOT trigger          │       │
│  └───────────────────────────────────────────────────────────┘       │
│                                                                       │
│  ┌───────────────────────────────────────────────────────────┐       │
│  │                  UI Canvas                                 │       │
│  │  UIManager: ScoreText (HUD) + GameOverPanel                │       │
│  │  Subscribes to OnGameOver → shows panel on death           │       │
│  └───────────────────────────────────────────────────────────┘       │
└──────────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
UPDATE LOOP:
  InputHandler → LeftPressed/RightPressed/JumpPressed
  PlayerController:
    ┌─ Reads InputHandler → lane switch (X lerp) / jump (Impulse)
    ├─ FixedUpdate: Z velocity = 0 (stationary player)
    ├─ Ground check: raycast down
    └─ OnCollisionEnter(collision):
        if collision.CompareTag("Obstacle") ∧ !_isDead:
          _isDead = true
          renderer.material.color = Color.red
          GameManager.Instance.GameOver()
  
  ObstacleSpawner:
    └─ _scrollDistance += ForwardSpeed * dt
       if _scrollDistance >= _nextSpawnZ → spawn via pool.Get()
  
  Each Obstacle:
    └─ transform.position += Vector3.back * _scrollSpeed * dt
       if Z < player.Z - 10f → pool.Release()
  
  WindEffect:
    └─ ParticleSystem auto-emits (playOnAwake + loop)
       Trails module draws fading streaks behind each particle
       ColorOverLifetime fades alpha from 0→0.5→0
       SizeOverLifetime scales from 0.02→0.08→0.02

GAME OVER:
  PlayerController.OnCollisionEnter → GameManager.GameOver()
    → IsGameOver = true
    → OnGameOver event fires
      → UIManager.ShowGameOver() → activates GameOverPanel
      → Distance stops accumulating (GameManager.Update guard)
      → ObstacleSpawner stops spawning (IsGameOver guard)
```

---

## File Structure

```
Assets/
  Scripts/
    Placeholders.cs           — (NO CHANGE) 
    SceneBootstrapper.cs      — (NO CHANGE) Already wires WindEffect to player
    GameManager.cs            — (NO CHANGE) 
    PlayerController.cs       — (MODIFY) Add CCD on Rigidbody in Awake()
    CameraFollow.cs           — (NO CHANGE) 
    ObstacleSpawner.cs        — (NO CHANGE) Already tags obstacles correctly
    Obstacle.cs               — (NO CHANGE) Pool-safe, no Rigidbody needed
    UIManager.cs              — (NO CHANGE) Already subscribes to OnGameOver
    InputHandler.cs           — (NO CHANGE) 
    WindEffect.cs             — (MODIFY) Add Trails, ColorOverLifetime, SizeOverLifetime
  Editor/
    SceneBaker.cs             — (NO CHANGE) 
```

---

## Component Specifications

### 1. PlayerController (MODIFY — Collision Death Fix)

**File**: `Assets/Scripts/PlayerController.cs`

**Changes**: Two additions in `Awake()`:

1. **Enable Continuous Dynamic Collision Detection** on the player's Rigidbody. This prevents the bullet-through-paper problem where a fast-moving static collider (obstacle) tunnels through the player between physics frames.

2. **Add a one-time debug log** inside `OnCollisionEnter` to confirm collisions are detected. This is a development aid — remove or comment out after verification.

**Modified Awake() — add after existing Rigidbody configuration**:

```csharp
// After the existing Rigidbody config (_rb.constraints = FreezeRotation;):
// Enable Continuous Dynamic CCD to prevent obstacles from tunneling through
// the player at high relative speeds (bullet-through-paper effect).
// Obstacles move via Transform (static colliders), so CCD on the player's
// Rigidbody ensures the sweep test catches them every frame.
_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
```

**Rationale**: The SOTA research identified discrete collision detection as the most likely root cause of missed collisions. Obstacles move at 8 units/s via `transform.position` (making them "static colliders" from Unity's perspective). At this speed, an obstacle could traverse the player's capsule radius between physics steps when using discrete detection. `ContinuousDynamic` adds a sweep test that guarantees the collision is detected regardless of relative speed.

**Cost**: Negligible — `ContinuousDynamic` on a single Rigidbody has no measurable performance impact.

**OnCollisionEnter** — add a debug log (temporary, for verification):

```csharp
private void OnCollisionEnter(Collision collision)
{
    // Debug log to verify collision detection is firing
    // Remove after confirming death works correctly
    Debug.Log($"Player.OnCollisionEnter: collided with '{collision.gameObject.name}' (tag={collision.gameObject.tag})");

    if (_isDead)
        return;

    if (collision.gameObject.CompareTag("Obstacle"))
    {
        _isDead = true;

        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.color = Color.red;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.GameOver();
        }
    }
}
```

**No other changes to PlayerController.** Lane switching, jumping, ground check, death visual, and GameOver() call remain unchanged.

---

### 2. WindEffect (MODIFY — Particle Enhancement)

**File**: `Assets/Scripts/WindEffect.cs`

**Changes**: Add three new modules to `ConfigureParticleSystem()`:
- **Trails module** (`PerParticle` mode) — creates fading streaks behind each particle for realistic speed-line appearance
- **ColorOverLifetime module** — fades particle alpha from 0→0.5→0 for smooth birth/death
- **SizeOverLifetime module** — scales particles from 0.02→0.08→0.02 for natural size variation

All three are purely code-configurable via the Unity ParticleSystem API. No new fields, no Placeholders changes, no assets.

**Modified `ConfigureParticleSystem()` method** — additions at the end, after the existing Renderer configuration:

```csharp
private void ConfigureParticleSystem(ParticleSystem ps)
{
    // ---- Main Module (existing — slightly adjusted) ----
    var main = ps.main;
    main.startLifetime = _particleLifetime;
    main.startSize = 0.05f;
    main.startColor = new Color(1f, 1f, 1f, 0.5f);
    main.simulationSpace = ParticleSystemSimulationSpace.World;
    main.playOnAwake = true;
    main.loop = true;
    main.startSpeed = 0f;

    // ---- Emission Module (existing) ----
    var emission = ps.emission;
    emission.rateOverTime = _emissionRate;

    // ---- Shape Module (existing) ----
    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Box;
    shape.scale = new Vector3(_emissionWidth, _emissionHeight, _emissionDepth);
    shape.position = new Vector3(0f, 0f, _emissionZOffset);

    // ---- Velocity Over Lifetime Module (existing) ----
    var velocityOverLifetime = ps.velocityOverLifetime;
    velocityOverLifetime.enabled = true;
    velocityOverLifetime.z = -_particleSpeed;

    // ---- Color Over Lifetime Module (NEW) ----
    // Fade particles in at birth and out near death for smooth alpha transitions.
    // Gradient: alpha 0 at 0% → 0.5 at 40% → 0.5 at 60% → 0 at 100%
    var colorOverLifetime = ps.colorOverLifetime;
    colorOverLifetime.enabled = true;
    Gradient colorGradient = new Gradient();
    colorGradient.SetKeys(
        new GradientColorKey[]
        {
            new GradientColorKey(Color.white, 0f),
            new GradientColorKey(Color.white, 1f)
        },
        new GradientAlphaKey[]
        {
            new GradientAlphaKey(0f,   0.0f),  // transparent at birth
            new GradientAlphaKey(0.5f, 0.4f),  // fade in to half-opaque
            new GradientAlphaKey(0.5f, 0.6f),  // hold at half-opaque
            new GradientAlphaKey(0f,   1.0f)   // fade out before death
        }
    );
    colorOverLifetime.color = new ParticleSystem.MinMaxGradient(colorGradient);

    // ---- Size Over Lifetime Module (NEW) ----
    // Grow particles after birth, then shrink before death.
    // Creates a natural "streak" appearance: particles swell as they pass by.
    var sizeOverLifetime = ps.sizeOverLifetime;
    sizeOverLifetime.enabled = true;
    AnimationCurve sizeCurve = new AnimationCurve(
        new Keyframe(0f,   0.02f),   // small at birth
        new Keyframe(0.3f, 0.08f),   // grow to full size
        new Keyframe(0.7f, 0.08f),   // hold at full size
        new Keyframe(1f,   0.02f)    // shrink before death
    );
    sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

    // ---- Trails Module (NEW) — PRIMARY ENHANCEMENT ----
    // PerParticle trails produce fading streaks behind each particle,
    // creating realistic speed-line visuals that convincingly convey
    // forward motion past a stationary player.
    var trails = ps.trails;
    trails.enabled = true;
    trails.mode = ParticleSystemTrailMode.PerParticle;
    trails.lifetime = 0.3f;  // trail persists for 0.3s behind particle

    // Trail width curve: thinner at birth, thicker as particle ages
    AnimationCurve trailWidthCurve = new AnimationCurve(
        new Keyframe(0f, 0.02f),
        new Keyframe(0.5f, 0.08f),
        new Keyframe(1f, 0.01f)
    );
    trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, trailWidthCurve);

    // Trail color: match particle color with its own alpha fade
    Gradient trailColorGradient = new Gradient();
    trailColorGradient.SetKeys(
        new GradientColorKey[]
        {
            new GradientColorKey(Color.white, 0f),
            new GradientColorKey(Color.white, 1f)
        },
        new GradientAlphaKey[]
        {
            new GradientAlphaKey(0.3f, 0f),
            new GradientAlphaKey(0f,   1f)
        }
    );
    trails.colorOverTrail = new ParticleSystem.MinMaxGradient(trailColorGradient);

    // Each particle gets its own trail (PerParticle mode)
    trails.ratio = 1.0f;

    // ---- Renderer Module (existing — unchanged) ----
    var renderer = ps.GetComponent<ParticleSystemRenderer>();
    renderer.renderMode = ParticleSystemRenderMode.Stretch;
    renderer.lengthScale = 2f;
    renderer.velocityScale = 0.15f;
    renderer.material = _particleMaterial;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    renderer.receiveShadows = false;
}
```

**Serialized Fields**: No new fields needed. The existing fields (`_particleMaterial`, `_particleSpeed`, `_emissionRate`, `_particleLifetime`, `_emissionWidth`, `_emissionHeight`, `_emissionDepth`, `_emissionZOffset`) are sufficient — the new modules use hardcoded curves that produce good visual results and can be tweaked by developers directly in the code.

**Rationale for each module**:

| Module | Purpose | Visual Impact |
|--------|---------|---------------|
| **Trails (PerParticle)** | Each particle leaves a fading streak behind it | Produces the classic "speed line" look — the single most impactful enhancement. Trails fade naturally over 0.3s and thin at the tip. |
| **ColorOverLifetime** | Alpha ramp: 0→0.5→0 over particle lifetime | Eliminates the "pop in / pop out" artifact of the current system. Particles fade in softly at birth and out near death. |
| **SizeOverLifetime** | Scale ramp: 0.02→0.08→0.02 | Particles swell as they pass the player and shrink as they recede, adding depth and organic feel. Combined with trails, this creates a convincing speed-streak effect. |

**Performance note**: The Trails module increases draw calls per particle, but the `_emissionRate` of 50 and lifetime of 2s means at most ~100 active particles at any time. PerParticle trails add one trail quad per particle — well within budget for any modern platform. If performance tuning is needed, reduce `_emissionRate` via the existing Inspector field.

**Ribbon mode avoided**: The SOTA research flagged a Unity bug where `ParticleSystemTrailMode.Ribbon` produces incorrect indexing above 31 particles. PerParticle mode is safe at any particle count and produces visually comparable results.

---

## Component Interaction Matrix (Unchanged)

| From → To | Game Manager | Player Controller | Obstacle Spawner | Obstacle | UI Manager | Wind Effect | Camera Follow | Input Handler |
|------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **GameManager** | — | — | — | — | events | — | — | — |
| **PlayerController** | `.IsGameOver` `.GameOver()` | — | — | — | — | — | — | reads `.LeftPressed` etc. |
| **ObstacleSpawner** | `.IsGameOver` `.ForwardSpeed` | `.position.z` | — | `.Configure()` | — | — | — | — |
| **Obstacle** | `.ForwardSpeed` (cached) | `.position.z` | — | — | — | — | — | — |
| **UIManager** | `.Distance` `.OnGameOver` `.OnRestart` | — | — | — | — | — | — | `.RestartPressed` |
| **WindEffect** | `.ForwardSpeed` (fallback) | — | — | — | — | — | — | — |
| **CameraFollow** | — | `.position` | — | — | — | — | — | — |

No interaction changes. The CCD addition in `PlayerController` is internal (Rigidbody property). The particle module additions in `WindEffect` are internal (ParticleSystem configuration).

---

## Integration in SceneBootstrapper.BuildScene()

**No changes needed.** The `SceneBootstrapper.BuildScene()` method already:

1. Creates the Player capsule with Rigidbody, InputHandler, and PlayerController (step 3, lines 77–94)
2. Creates WindEffect as a child GameObject of Player and attaches the `WindEffect` component (step 3, lines 98–101)
3. Creates the ObstacleSpawner (step 8, line 241–242) — which creates obstacles tagged `"Obstacle"` with BoxColliders

Both the collision death fix and the wind particle enhancement are internal to their respective components (`PlayerController.Awake()` and `WindEffect.Awake()`/`ConfigureParticleSystem()`). The bootstrapper automatically picks up the changes because it instantiates the same components.

---

## Technical Stack

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Engine | Unity 6 (6000.0+) | Required by project brief |
| Language | C# (pure scripts) | Project constraint |
| Collision detection | `Rigidbody.collisionDetectionMode = ContinuousDynamic` | Prevents bullet-through-paper for fast-moving static colliders |
| Death response | `OnCollisionEnter` + tag check + `GameManager.GameOver()` | Already implemented; CCD makes it reliable |
| Particle trails | `ParticleSystemTrailMode.PerParticle` | Fading speed-line streaks behind particles |
| Particle alpha fade | `ParticleSystem.ColorOverLifetimeModule` + `Gradient` with alpha keys | Smooth fade-in/fade-out for particles |
| Particle size variation | `ParticleSystem.SizeOverLifetimeModule` + `AnimationCurve` | Natural swell-and-shrink for particles |
| Particle rendering | `ParticleSystemRenderMode.Stretch` | Existing stretch billboard for speed lines |
| Scene build | `SceneBootstrapper.BuildScene()` | No changes needed |
| Placeholders | `Placeholders.CreateMaterial()` | No changes needed |

---

## Bug Fix Traceability

| Issue | Root Cause | Fix | File(s) Changed |
|-------|-----------|-----|-----------------|
| **Player doesn't die on obstacle collision** | Player's Rigidbody uses discrete collision detection → fast-moving obstacles (static colliders at 8 units/s) can tunnel through the player capsule between physics frames. The `OnCollisionEnter` code itself is correct. | Add `_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic` in `PlayerController.Awake()`. Add debug log for verification. | `PlayerController.cs` |
| **Wind/scroll particles look basic** | Current system uses only Stretch billboard + VelocityOverLifetime. Particles are flat white dots that pop in/out — no trails, no fade, no size variation. | Add Trails (PerParticle mode), ColorOverLifetime (alpha gradient), and SizeOverLifetime (size curve) modules to `WindEffect.ConfigureParticleSystem()`. | `WindEffect.cs` |

---

## Edge Cases Addressed

### Collision Death

| Edge Case | Mitigation | Where |
|-----------|-----------|-------|
| Bullet-through-paper (obstacle tunnels through player) | `ContinuousDynamic` CCD sweep-tests the player's collider each physics step | `PlayerController.Awake()` |
| Double-death trigger | `_isDead` guard in `OnCollisionEnter` (existing) | `PlayerController.cs` |
| Pooled obstacle collider state corruption | `Obstacle.OnDisable()` safety net returns to pool; `actionOnGet` calls `SetActive(true)` which re-enables collider | `Obstacle.cs`, `ObstacleSpawner.Awake()` |
| Missing "Obstacle" tag | `CreateObstacle()` explicitly sets `cube.tag = "Obstacle"` (existing) | `ObstacleSpawner.CreateObstacle()` |
| Player Rigidbody becomes kinematic | `[RequireComponent(typeof(Rigidbody))]` ensures Rigidbody exists; `Awake()` configures it as non-kinematic (existing, mass/constraints are set) | `PlayerController.cs` |
| Collider accidentally set as trigger | `GameObject.CreatePrimitive()` creates colliders with `isTrigger = false` by default; no code sets it to true | N/A |
| GameOver after death | `GameManager.GameOver()` is idempotent (`IsGameOver` guard) | `GameManager.cs` |

### Wind Particle

| Edge Case | Mitigation | Where |
|-----------|-----------|-------|
| Trail module Ribbon mode indexing bug | Use `PerParticle` mode, not `Ribbon` — works correctly at any particle count | `WindEffect.ConfigureParticleSystem()` |
| Particles/shadows rendering over UI | `ScreenSpaceOverlay` Canvas always renders on top of world-space particles (existing) | `SceneBootstrapper.BuildScene()` |
| World-space particles during lane switch | `simulationSpace = World` — particles stay in world space, player moves through them (existing) | `WindEffect.ConfigureParticleSystem()` |
| Lifetime/speed mismatch | Existing `_particleLifetime` (2s) × `_particleSpeed` (8 units/s) = 16 units travel distance, well within camera far plane (100 units) | `WindEffect.cs` fields |
| Performance at high particle counts | `_emissionRate = 50` × 2s lifetime = ~100 active particles; PerParticle trails add ~100 trail quads — well within budget | `WindEffect.cs` |
| Material transparency across pipelines | `Placeholders.CreateMaterial()` uses shader fallback chain (URP Lit → Standard → Unlit/Color) — `Unlit/Color` supports alpha blending for particles | `Placeholders.cs` |

---

## Extensibility Points (Future, Not Implemented Now)

- **WindEffect stops on GameOver**: Subscribe to `GameManager.OnGameOver` and call `ps.Stop()` / `ps.Clear()` to freeze particles when the run ends.
- **Scroll speed increase over time**: Modify `GameManager.ForwardSpeed`; `WindEffect._particleSpeed` can re-read it each frame for auto-acceleration of particles.
- **Additional wind layers**: Instantiate a second `WindEffect` child with different emission parameters for parallax depth.
- **Obstacle death audio/vfx**: The `OnCollisionEnter` callback can trigger a one-shot particle burst or screen shake (out of scope for this fix).
- **Tune trail/color/size curves via Inspector**: Convert hardcoded curves to `[SerializeField] AnimationCurve` / `[SerializeField] Gradient` fields for designer tweaking.

---

## RESOURCES.md Update Notes

The existing `RESOURCES.md` already documents:
- The `WindEffect` component and its `_particleMaterial` field for material replacement
- The player-stationary architecture

No structural changes to `RESOURCES.md` are needed. The added particle modules (Trails, ColorOverLifetime, SizeOverLifetime) are code-configured and produce their visuals at runtime — users replacing the `_particleMaterial` with a custom material will see the effect with the new trails/fade/size behavior automatically.

---

## Linter Manifest

Only `.cs` files in this project. C# compilation is handled automatically by the Unity build system. The `linter_manifest.json` covers non-C# files only.
