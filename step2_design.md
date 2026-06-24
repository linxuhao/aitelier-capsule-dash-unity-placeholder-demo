# Technical Architecture Design — Capsule Dash: Player-Stationary Refactor & Bug Fixes

## Overview

This design refactors the Capsule Dash 3D endless runner to fix three bugs:
1. **Distance display frozen in bake mode** — root cause: UIManager event-subscription ordering edge case (GameManager.Instance null during Start() in baked scenes).
2. **No obstacle spawning in bake mode** — root cause: with stationary player, `_player.position.z` never changes, so the distance-based spawn trigger never fires after the initial threshold.
3. **Player falls off the runway** — root cause: player auto-runs forward on a finite ground plane; after ~13 seconds they hit the end.

The refactor makes the player **stationary on Z** (obstacles scroll toward them), adds a **code-driven wind/particle effect** to visually convey forward motion, and **hardens UIManager** for baked-scene initialization ordering. The goal is identical gameplay in both `press Play` (runtime bootstrap) and `Tools > Bake Scene to Hierarchy + Play` (baked scene) modes.

All changes integrate into the existing `SceneBootstrapper.BuildScene()` single-source-of-truth, the self-supplying `[SerializeField]` pattern, and the `Placeholders` runtime-asset utility.

---

## Architecture Diagram (Text)

```
┌──────────────────────────────────────────────────────────────────┐
│                      SceneBootstrapper                           │
│  Awake() → if not built → BuildScene()                          │
│  BuildScene(): single source of truth — creates & wires ALL     │
│  GameObjects: Player, Camera, UI, GameManager, ObstacleSpawner,  │
│  WindEffect, Ground, EventSystem, LaneMarkers                    │
└───────┬──────────────────────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────────────────────────────┐
│  ┌──────────────────┐   ┌──────────────────┐                     │
│  │     Player       │   │     Camera       │                     │
│  │   (Capsule)      │   │  (Follow, fixed  │                     │
│  │   Z=0 stationary │   │   Z offset)      │                     │
│  │  ┌─────────────┐ │   └────────┬─────────┘                     │
│  │  │ WindEffect   │ │            │                              │
│  │  │ (child GO,   │ │            │                              │
│  │  │ ParticleSys) │ │            │                              │
│  │  └─────────────┘ │            │                              │
│  └────────┬─────────┘            │                              │
│           │                      │                              │
│  ┌────────┴──────────────────────┴────────────────────────┐     │
│  │                    GameManager                          │     │
│  │  [DefaultExecutionOrder(-100)] singleton                │     │
│  │  State: Distance += ForwardSpeed * Time.deltaTime       │     │
│  │  Events: OnGameOver, OnRestart                          │     │
│  └──────────────────────┬─────────────────────────────────┘     │
│                         │                                        │
│  ┌──────────────────────┴─────────────────────────────────┐     │
│  │                 ObstacleSpawner                         │     │
│  │  ObjectPool<GameObject> (cube obstacles)                │     │
│  │  Virtual _scrollDistance counter (NOT player.position.z)│     │
│  │  Spawns at player.Z + _spawnDistance                    │     │
│  └──────────────────────┬─────────────────────────────────┘     │
│                         │                                        │
│  ┌──────────────────────┴─────────────────────────────────┐     │
│  │              Obstacle (per-instance)                    │     │
│  │  Scrolls toward player: Vector3.back * speed * dt       │     │
│  │  Self-returns to pool when Z < player.Z - 10f           │     │
│  └────────────────────────────────────────────────────────┘     │
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐     │
│  │                  UI Canvas                              │     │
│  │  UIManager: ScoreText (HUD) + GameOverPanel             │     │
│  │  Lazy re-subscription if GameManager null at Start()    │     │
│  └────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────┘
```

### Data Flow (Updated)

```
UPDATE LOOP:
  InputHandler reads Keyboard.current → LeftPressed/RightPressed/JumpPressed/RestartPressed
  PlayerController reads InputHandler → lane switch (X lerp) / jump (Impulse)
  PlayerController: Z velocity = 0 (STATIONARY — was ForwardSpeed)
  GameManager.Update(): Distance += ForwardSpeed * Time.deltaTime
  ObstacleSpawner: _scrollDistance += ForwardSpeed * Time.deltaTime (VIRTUAL)
    if _scrollDistance >= _nextSpawnZ → spawn obstacle at player.Z + _spawnDistance
  Each Obstacle: transform.position += Vector3.back * scrollSpeed * dt
  Each Obstacle: if Z < player.Z - 10f → return to pool
  UIManager reads GameManager.Distance → updates TMP text
  WindEffect: ParticleSystem auto-emits speed lines in World space

LATE UPDATE:
  CameraFollow: transform.position = player.position + offset (smooth Lerp, Z never changes)

COLLISION (Player.OnCollisionEnter vs Obstacle tag):
  → GameManager.GameOver() → UI shows panel → R restarts scene
```

### Key Architectural Change: Virtual Scroll Distance

The old design coupled obstacle spawning to `_player.position.z`:
```
if (_player.position.z >= _nextSpawnZ) → spawn
```

With a stationary player at Z=0, this trigger fires only once (Z=0 < _nextSpawnZ=20f → never spawns). The fix introduces a **virtual scroll distance** counter that conceptually represents "how far the world has scrolled":

```
_scrollDistance += ForwardSpeed * Time.deltaTime;  // virtual world scroll
if (_scrollDistance >= _nextSpawnZ) → spawn
```

`_scrollDistance` increments identically to `GameManager.Distance` — the two remain in sync, but `_scrollDistance` is local to `ObstacleSpawner` (no cross-component dependency).

---

## File Structure

```
Assets/
  Scripts/
    Placeholders.cs           — (NO CHANGE) Static: CreatePrimitive(), CreateMaterial()
    SceneBootstrapper.cs      — (MODIFY) BuildScene(): larger ground, attach WindEffect to player
    GameManager.cs            — (NO CHANGE) Distance scoring already survival-time-based
    PlayerController.cs       — (MODIFY) FixedUpdate: set Z velocity to 0 instead of ForwardSpeed
    CameraFollow.cs           — (NO CHANGE) Works unchanged with player at fixed Z
    ObstacleSpawner.cs        — (MODIFY) Virtual _scrollDistance replaces _player.position.z
    Obstacle.cs               — (MINOR) Despawn threshold adjusts for stationary player
    UIManager.cs              — (MODIFY) Lazy event re-subscription in Update()
    InputHandler.cs           — (NO CHANGE) Lane switch & jump input unchanged
    WindEffect.cs             — (NEW) ParticleSystem speed-lines effect attached to player
  Editor/
    SceneBaker.cs             — (NO CHANGE) Calls BuildScene(); no structural changes needed
RESOURCES.md                  — (MODIFY) Add WindEffect material replacement instructions
```

---

## Component Specifications

### 1. PlayerController (MODIFY)

**File**: `Assets/Scripts/PlayerController.cs`

**Changes**: 

**FixedUpdate()** — Remove the forward auto-run velocity on Z. The player stays at Z=0:

```csharp
// OLD:
_rb.velocity = new Vector3(0f, _rb.velocity.y, forwardSpeed);

// NEW:
_rb.velocity = new Vector3(0f, _rb.velocity.y, 0f);
```

This is a one-line change. Everything else — lane switching (X lerp via Transform), jumping (Y impulse), ground check (raycast down), collision death, death visual (material color → red) — remains unchanged.

**Rationale**: The SOTA report confirms this is the standard endless-runner pattern. Player never moves forward, so bug #3 (falling off) is eliminated — the player stays at Z=0 forever.

**No new serialized fields needed.**

---

### 2. ObstacleSpawner (MODIFY)

**File**: `Assets/Scripts/ObstacleSpawner.cs`

**Changes**: Replace `_player.position.z` with a virtual `_scrollDistance` counter for spawn triggering.

**New runtime state**:
```csharp
/// <summary>
/// Virtual scroll distance counter. Increments each frame by ForwardSpeed * dt,
/// conceptually representing "how far the world has scrolled." Replaces
/// _player.position.z as the spawn trigger — decouples spawning from actual
/// player position so obstacles spawn correctly when player is stationary at Z=0.
/// </summary>
private float _scrollDistance;
```

**Modified Update() logic**:
```csharp
private void Update()
{
    // Guard: GameManager must exist and game must be playing
    if (GameManager.Instance == null || GameManager.Instance.IsGameOver)
        return;
    if (_player == null)
        return;

    // Advance virtual scroll distance (replaces _player.position.z)
    _scrollDistance += GameManager.Instance.ForwardSpeed * Time.deltaTime;

    // Spawn check against virtual distance
    if (_scrollDistance >= _nextSpawnZ)
    {
        // ... spawn logic unchanged (lane selection, pool.Get(), positioning, Configure) ...

        // Advance spawn threshold from current virtual distance
        _nextSpawnZ = _scrollDistance + Random.Range(_minSpawnGap, _maxSpawnGap);
    }
}
```

**Spawn position formula** stays the same:
```csharp
obstacle.transform.position = new Vector3(xPos, 0.5f, _player.position.z + _spawnDistance);
```
With the player at Z=0, this becomes `Z = _spawnDistance` (e.g., 35f). Obstacles appear at a fixed world Z ahead of the player and scroll backward toward them.

**Initial `_nextSpawnZ`**: Changed from `20f` to `0f` so the first obstacle spawns immediately (the virtual distance starts at 0 and increments, crossing `_nextSpawnZ` on the very first frame). Alternatively, keep at `20f` for a short grace period before the first spawn.

**No new serialized fields needed.**

---

### 3. Obstacle (MINOR)

**File**: `Assets/Scripts/Obstacle.cs`

**Changes**: The despawn check already works correctly with a stationary player, but the threshold can be tightened for efficiency:

```csharp
// OLD: if (transform.position.z < _player.position.z - 10f)
// With player at Z=0, this is: z < -10f. Still correct, but obstacles scroll
// unnecessarily far behind the player before recycling.

// NEW: unchanged logic, but consider reducing the threshold to 5f-8f
// if performance tuning is desired. No functional bug here — left as-is.
```

**No required changes to Obstacle.cs.** The existing despawn logic (`z < player.z - 10f`) works identically with a stationary player. The only difference is that `player.z` is now always 0 instead of increasing. Obstacles scroll from e.g. Z=35 down past Z=-10 and recycle — same behavior.

---

### 4. GameManager (NO CHANGE)

**File**: `Assets/Scripts/GameManager.cs`

**No changes needed.** The distance scoring formula `Distance += ForwardSpeed * Time.deltaTime` is already survival-time-based. It accumulates correctly in both Play mode and baked scenes because `Update()` ticks in both cases when Play is pressed. The `[DefaultExecutionOrder(-100)]` attribute already ensures correct Awake ordering.

---

### 5. UIManager (MODIFY)

**File**: `Assets/Scripts/UIManager.cs`

**Changes**: Add lazy event re-subscription in `Update()` to handle the baked-scene edge case where `GameManager.Instance` is unexpectedly null during `Start()`.

**In Start()**, the existing null guard logs a warning if GameManager.Instance is null. The fix adds a recovery path:

```csharp
// New field:
private bool _eventsSubscribed;

private void Start()
{
    // ... existing self-supply discovery (Find ScoreText, GameOverPanel, etc.) ...

    _inputHandler = FindObjectOfType<InputHandler>();

    // Attempt subscription (may fail if GameManager not yet ready)
    TrySubscribeEvents();

    if (_gameOverPanel != null)
        _gameOverPanel.SetActive(false);
}

private void TrySubscribeEvents()
{
    if (_eventsSubscribed) return;

    if (GameManager.Instance != null)
    {
        GameManager.Instance.OnGameOver += ShowGameOver;
        GameManager.Instance.OnRestart += HideGameOver;
        _eventsSubscribed = true;
    }
}

private void Update()
{
    // Lazy re-subscription: if events weren't subscribed in Start(),
    // try again each frame until successful.
    if (!_eventsSubscribed)
    {
        TrySubscribeEvents();
    }

    if (GameManager.Instance == null)
        return;

    if (!GameManager.Instance.IsGameOver)
        UpdateScoreText();
    else
        CheckRestartInput();
}
```

**Rationale**: In baked scenes, the execution order of `Start()` across components is undefined. UIManager.Start() may run before GameManager's singleton assignment completes (even with `[DefaultExecutionOrder(-100)]`, which only affects Awake, not Start). The lazy retry pattern ensures that if the initial subscription fails, it will succeed on a subsequent frame once GameManager.Instance is populated.

---

### 6. WindEffect (NEW)

**File**: `Assets/Scripts/WindEffect.cs`

**Type**: MonoBehaviour. Attached to a child GameObject of the Player by `SceneBootstrapper.BuildScene()`.

**Responsibility**: Creates and configures a fully code-driven `ParticleSystem` that emits speed lines / dust particles rushing past the player from front to back, visually conveying forward motion even though the player is stationary.

**Serialized Fields** (self-supplying):
```csharp
/// <summary>
/// Material applied to wind particles. If null at Awake, a white semi-transparent
/// placeholder material is created via Placeholders.CreateMaterial().
/// </summary>
[SerializeField] private Material _particleMaterial;

/// <summary>
/// Speed at which particles move backward (world units/sec). Defaults to
/// GameManager.Instance.ForwardSpeed if available, otherwise 8f.
/// </summary>
[SerializeField] private float _particleSpeed = 8f;

/// <summary>
/// Number of particles emitted per second. Higher = denser speed lines.
/// </summary>
[SerializeField] private float _emissionRate = 50f;

/// <summary>
/// Lifetime of each particle in seconds.
/// </summary>
[SerializeField] private float _particleLifetime = 2f;

/// <summary>
/// Width of the emission box (X axis, across lanes).
/// </summary>
[SerializeField] private float _emissionWidth = 8f;

/// <summary>
/// Height of the emission box (Y axis).
/// </summary>
[SerializeField] private float _emissionHeight = 1f;

/// <summary>
/// Depth of the emission box (Z axis, along the run direction).
/// </summary>
[SerializeField] private float _emissionDepth = 3f;

/// <summary>
/// Z offset of the emission box center from the player. Positive = ahead of player.
/// </summary>
[SerializeField] private float _emissionZOffset = 5f;
```

**Awake logic**:
```csharp
private void Awake()
{
    // Self-supply material
    if (_particleMaterial == null)
    {
        // White, semi-transparent — particles look like dust/speed lines
        _particleMaterial = Placeholders.CreateMaterial(new Color(1f, 1f, 1f, 0.4f));
    }

    // Resolve speed from GameManager if not explicitly set
    if (_particleSpeed <= 0f && GameManager.Instance != null)
    {
        _particleSpeed = GameManager.Instance.ForwardSpeed;
    }

    // Create and configure the ParticleSystem
    ParticleSystem ps = gameObject.AddComponent<ParticleSystem>();
    ConfigureParticleSystem(ps);
}

private void ConfigureParticleSystem(ParticleSystem ps)
{
    // --- Main Module ---
    var main = ps.main;
    main.startSpeed = _particleSpeed;           // particles move backward (Vector3.back is emission direction)
    main.startLifetime = _particleLifetime;
    main.startSize = 0.05f;                     // thin lines
    main.startColor = new Color(1f, 1f, 1f, 0.5f);
    main.simulationSpace = ParticleSystemSimulationSpace.World;  // particles flow past player, don't follow
    main.playOnAwake = true;
    main.loop = true;

    // --- Emission Module ---
    var emission = ps.emission;
    emission.rateOverTime = _emissionRate;

    // --- Shape Module ---
    var shape = ps.shape;
    shape.shapeType = ParticleSystemShapeType.Box;
    shape.scale = new Vector3(_emissionWidth, _emissionHeight, _emissionDepth);
    shape.position = new Vector3(0f, 0f, _emissionZOffset);  // emit ahead of player

    // Set emission direction: particles spawn and move along -Z (toward player / past player)
    // startSpeed above is positive magnitude; the shape's rotation or velocity over lifetime
    // determines direction. We use a negative Z velocity.
    var velocityOverLifetime = ps.velocityOverLifetime;
    velocityOverLifetime.enabled = true;
    velocityOverLifetime.z = -_particleSpeed;       // backward

    // Disable start speed (we control velocity via the module)
    main.startSpeed = 0f;

    // --- Renderer Module ---
    var renderer = ps.GetComponent<ParticleSystemRenderer>();
    renderer.renderMode = ParticleSystemRenderMode.Stretch;  // speed-line stretch effect
    renderer.lengthScale = 2f;                                // how much to stretch
    renderer.velocityScale = 0.15f;                           // stretch proportional to velocity
    renderer.material = _particleMaterial;
    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    renderer.receiveShadows = false;
}
```

**Note on "Stretch" render mode**: `ParticleSystemRenderMode.Stretch` stretches each particle billboard in the direction of its velocity. This produces the classic "speed line" look — thin streaking lines flowing backward past the player. Combine with a white semi-transparent unlit material for the dust/wind effect.

**Alternative simpler approach** (if Stretch isn't available or doesn't look right in the Unity version):
- Use `ParticleSystemRenderMode.Billboard`
- Increase `startSize` to 0.1–0.2f
- The sheer number of particles creates the motion illusion

**Integration in SceneBootstrapper.BuildScene()**:
```csharp
// After creating the Player GameObject (step 3 in BuildScene):
GameObject windGo = new GameObject("WindEffect");
windGo.transform.SetParent(player.transform, false);
windGo.transform.localPosition = Vector3.zero;
windGo.AddComponent<WindEffect>();
```

---

### 7. SceneBootstrapper (MODIFY)

**File**: `Assets/Scripts/SceneBootstrapper.cs`

**Changes**:
1. **Ground scale**: Change Z from `20f` to `200f` (or `100f`) — with a stationary player, this is effectively infinite.
2. **WindEffect**: Create child GameObject on Player with `WindEffect` component.
3. **Player starting Z**: Explicitly set to `0f` (currently `new Vector3(0f, 1f, 0f)` — already correct).

**Modified BuildScene() excerpts**:

```csharp
// Step 2: Ground Plane — extend Z scale
GameObject ground = Placeholders.CreatePrimitive(
    PrimitiveType.Plane,
    new Color(0.3f, 0.3f, 0.3f),
    "Ground"
);
ground.transform.position = new Vector3(0f, 0f, 10f);
ground.transform.localScale = new Vector3(3f, 1f, 200f);  // was 20f → now 200f
ground.tag = "Ground";

// Step 3: Player Capsule — attach WindEffect
GameObject player = Placeholders.CreatePrimitive(
    PrimitiveType.Capsule,
    Color.blue,
    "Player"
);
player.transform.position = new Vector3(0f, 1f, 0f);  // Z=0, stationary
player.tag = "Player";

// Rigidbody, InputHandler, PlayerController (unchanged)
Rigidbody rb = player.AddComponent<Rigidbody>();
rb.mass = 1f;
rb.drag = 0f;
rb.constraints = RigidbodyConstraints.FreezeRotation;
player.AddComponent<InputHandler>();
player.AddComponent<PlayerController>();

// WindEffect — NEW: attach as child of player
GameObject windGo = new GameObject("WindEffect");
windGo.transform.SetParent(player.transform, false);
windGo.transform.localPosition = Vector3.zero;
windGo.AddComponent<WindEffect>();
```

**No other changes to BuildScene()**. Camera, UI, EventSystem, GameManager, ObstacleSpawner, LaneMarkers all remain unchanged.

---

### 8. CameraFollow (NO CHANGE)

**File**: `Assets/Scripts/CameraFollow.cs`

**No changes needed.** With the player at fixed Z=0, the camera's target position `_player.position + _offset` (where `_offset.z = -8f`) stays at Z=-8. The `LookAt` with `_lookAheadZ` still looks slightly ahead of the player along +Z. No jitter — the Z component is constant.

---

### 9. InputHandler (NO CHANGE)

**File**: `Assets/Scripts/InputHandler.cs`

**No changes needed.** Lane switching, jumping, and restart input are unaffected by the stationary-player refactor.

---

### 10. Placeholders (NO CHANGE)

**File**: `Assets/Scripts/Placeholders.cs`

**No changes needed.** `CreateMaterial()` already handles the white semi-transparent material for `WindEffect`. The shader fallback chain (URP Lit → Standard → Unlit/Color) works for particles.

---

### 11. SceneBaker (NO CHANGE)

**File**: `Assets/Editor/SceneBaker.cs`

**No changes needed.** Calls `BuildScene()` which now creates the WindEffect and larger ground automatically.

---

## Component Interaction Matrix

| From → To | Game Manager | Player Controller | Obstacle Spawner | Obstacle | UI Manager | Wind Effect | Camera Follow | Input Handler |
|------------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **GameManager** | — | — | — | — | events | — | — | — |
| **PlayerController** | `.Instance.IsGameOver` `.GameOver()` | — | — | — | — | — | — | reads `.LeftPressed` etc. |
| **ObstacleSpawner** | `.Instance.IsGameOver` `.Instance.ForwardSpeed` | `.position.z` (for spawn pos) | — | `.Configure()` | — | — | — | — |
| **Obstacle** | `.Instance.ForwardSpeed` (cached) | `.position.z` (despawn check) | — | — | — | — | — | — |
| **UIManager** | `.Instance.Distance` `.OnGameOver` `.OnRestart` | — | — | — | — | — | — | `.RestartPressed` |
| **WindEffect** | `.Instance.ForwardSpeed` (fallback) | — | — | — | — | — | — | — |
| **CameraFollow** | — | `.position` (follow target) | — | — | — | — | — | — |

---

## Technical Stack

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Engine | Unity 6 (6000.0+) | Required by project brief |
| Language | C# (pure scripts) | Project constraint |
| Rendering | URP (fallback to Built-in) | Placeholders shader chain handles both |
| Visuals | `GameObject.CreatePrimitive()` + runtime Materials | Zero assets; Placeholders utility |
| Wind effect | `ParticleSystem` (code-configured) | Built-in, no assets; `Stretch` render mode for speed lines |
| Physics | `Rigidbody` + `OnCollisionEnter` | Built-in gravity + collision detection |
| Input | `UnityEngine.InputSystem` (direct C# polling) | Cross-platform; no .inputactions assets |
| UI | Canvas + `TextMeshProUGUI` (TMP) | TMP included in Unity 6 by default |
| Pooling | `UnityEngine.Pool.ObjectPool<GameObject>` | Built-in, zero-GC after warm-up |
| Scene build | `SceneBootstrapper.BuildScene()` | Single source of truth for Play + Bake |
| Bake | `[MenuItem]` + `EditorSceneManager` | Standard Unity Editor scripting |

---

## Bug Fix Traceability

| Bug | Root Cause | Fix | File(s) Changed |
|-----|-----------|-----|-----------------|
| **#1: Distance frozen in bake mode** | UIManager.Start() may run before GameManager singleton is set in baked scenes → events not subscribed → score never updates in UI | Add lazy `TrySubscribeEvents()` called from `Update()` until subscription succeeds | `UIManager.cs` |
| **#2: No obstacle spawning in bake mode** | Stationary player at Z=0 → `_player.position.z >= _nextSpawnZ` never fires (since Z never increases past 20f threshold) | Replace `_player.position.z` with virtual `_scrollDistance` counter in spawn trigger | `ObstacleSpawner.cs` |
| **#3: Player falls off runway** | Player auto-runs at 8 units/sec on a ground plane scaled Z=20 (200 units of ground from Z=0 to Z=40) → falls off after ~13 seconds | Remove Z velocity from PlayerController (player stays at Z=0) + scale ground Z to 200 for visual safety margin | `PlayerController.cs`, `SceneBootstrapper.cs` |
| **Motion feel (UX)** | Stationary player feels static without visual cues | Add WindEffect child with code-driven ParticleSystem speed lines | `WindEffect.cs` (new), `SceneBootstrapper.cs` |

---

## Edge Cases Addressed

| Edge Case | Mitigation | File |
|-----------|-----------|------|
| UIManager.Start() races with GameManager singleton in baked scene | Lazy retry in `Update()` via `TrySubscribeEvents()` | `UIManager.cs` |
| Obstacle spawning never fires with stationary player | Virtual `_scrollDistance` decoupled from player position | `ObstacleSpawner.cs` |
| Player falls off finite ground | Player Z velocity = 0; ground Z scale = 200+ | `PlayerController.cs`, `SceneBootstrapper.cs` |
| Wind particles follow player during lane switch | `simulationSpace = World` so particles stay in world space | `WindEffect.cs` |
| WindEffect material is null | Self-supplying fallback to white semi-transparent Placeholders material | `WindEffect.cs` |
| ParticleSystem Stretch render mode not available on older Unity | Fallback: Billboard mode + increased particle count/size | `WindEffect.cs` |
| Ground texture UV stretching (future-proofing) | Not an issue with solid-color placeholder; documented in RESOURCES.md | `RESOURCES.md` |
| Obstacles spawn behind player on first spawn | `_nextSpawnZ` initial = 0f so first obstacle spawns immediately at player.z + _spawnDistance = 35f | `ObstacleSpawner.cs` |
| Restart with stationary player | `SceneManager.LoadScene` reloads scene; GameManager.Awake() resets Distance and IsGameOver | `GameManager.cs` |
| Double subscription if TrySubscribeEvents runs multiple times | Guard flag `_eventsSubscribed` prevents re-subscription | `UIManager.cs` |
| Wind particles continue emitting after GameOver | ParticleSystem auto-plays; acceptable — particles don't affect gameplay. Could stop on GameOver via event subscription (future enhancement) | `WindEffect.cs` |

---

## Extensibility Points (Future, Not Implemented Now)

- **WindEffect stops on death**: Subscribe `WindEffect` to `GameManager.OnGameOver` to stop/clear particles when the run ends.
- **Scroll speed increase over time**: Modify `GameManager.ForwardSpeed` to gradually increase; `ObstacleSpawner._scrollDistance` and `WindEffect._particleSpeed` read `ForwardSpeed` each frame so they'd automatically accelerate.
- **Multiple wind layers**: Add a second `WindEffect` child with different particle size/color for a parallax dust effect.
- **Obstacle variety**: `ObstacleSpawner` can pool multiple obstacle types via separate `ObjectPool` instances.

---

## Linter Manifest

Only `.cs` files in this project. C# compilation is handled automatically by the Unity build system. The `linter_manifest.json` covers markdown documentation only.

## RESOURCES.md Update Notes

The existing `RESOURCES.md` must be updated to:
1. Document the new `WindEffect` component's `_particleMaterial` field — users can replace the white semi-transparent placeholder with a custom particle material.
2. Note that player is now stationary (Z velocity = 0) and ground is larger.
3. All other sections remain valid.
