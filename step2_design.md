# Technical Architecture Design — 3D Endless Runner (Flappy in 3D)

## Overview

A Unity 6 (6000.0+) pure-C# 3D endless runner. A capsule character auto-runs forward along the Z axis in a three-lane corridor. The player dodges left/right between lanes and jumps over oncoming cube obstacles. Collision ends the run; distance score tracks survival. All visuals are runtime-generated primitives; no binary assets are delivered.

**Core architecture**: A `SceneBootstrapper.BuildScene()` method is the single source of truth for constructing the playable scene — called at runtime in `Awake()` for "press Play and play", and called from an Editor menu item (`Tools > Bake Scene to Hierarchy`) in Edit mode to persist the generated GameObjects into the scene for manual asset replacement. All components are wired in `BuildScene()`; no component relies on Awake ordering.

---

## Architecture Diagram (Text)

```
┌─────────────────────────────────────────────────────────────┐
│                     SceneBootstrapper                       │
│  Awake() → if not built → BuildScene()                     │
│  BuildScene(): single source of truth for wiring           │
└──────────┬──────────────────────────────────────────────────┘
           │ creates & wires all GameObjects
           ▼
┌──────────────────────────────────────────────────────────────┐
│  ┌──────────┐  ┌───────────┐  ┌────────────┐  ┌──────────┐ │
│  │  Player  │  │  Camera   │  │  UI Canvas  │  │  Ground  │ │
│  │ (Capsule)│  │ (Follow)  │  │ (TMP Score) │  │ (Plane)  │ │
│  └────┬─────┘  └─────┬─────┘  └──────┬─────┘  └──────────┘ │
│       │              │               │                      │
│  ┌────┴──────────────┴───────────────┴───────────────────┐  │
│  │                   GameManager                         │  │
│  │  [DefaultExecutionOrder(-100)] singleton              │  │
│  │  State: Playing / GameOver                            │  │
│  │  Distance score tracking                              │  │
│  │  GameOver() / Restart()                               │  │
│  └──────────────────────┬───────────────────────────────┘  │
│                         │                                   │
│  ┌──────────────────────┴───────────────────────────────┐  │
│  │              ObstacleSpawner                          │  │
│  │  Owns ObjectPool<GameObject> (cube prefabs)           │  │
│  │  Spawns ahead of player in random lanes               │  │
│  └──────────────────────┬───────────────────────────────┘  │
│                         │                                   │
│  ┌──────────────────────┴───────────────────────────────┐  │
│  │              Obstacle (per-instance)                  │  │
│  │  Scrolls toward player each frame                     │  │
│  │  Self-returns to pool when past player                │  │
│  └──────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### Data Flow

```
UPDATE LOOP:
  InputHandler reads Keyboard.current → exposes LeftPressed/RightPressed/JumpPressed/RestartPressed
  PlayerController reads InputHandler → lane switch (X lerp) / jump (Rigidbody impulse)
  PlayerController auto-runs: Rigidbody.velocity.z = forwardSpeed
  ObstacleSpawner spawns cubes at spawnDistance ahead of player, random lane
  Each Obstacle moves: transform.position.z -= scrollSpeed * Time.deltaTime
  Each Obstacle checks: if Z < player.z - despawnDistance → return to pool
  GameManager.Update(): Distance += forwardSpeed * Time.deltaTime
  UIManager reads GameManager.Distance → updates TMP text

LATE UPDATE:
  CameraFollow: transform.position = player.position + offset (smooth Lerp)
  CameraFollow: transform.LookAt(player)

COLLISION (Player.OnCollisionEnter vs obstacle):
  PlayerController detects collision with tag "Obstacle"
  → calls GameManager.Instance.GameOver()
  → GameManager sets state = GameOver, disables PlayerController
  → UIManager shows GameOver panel + restart prompt
  → Player presses Restart → GameManager.Restart() → SceneManager.LoadScene(0)
```

---

## File Structure

```
Assets/
  Scripts/
    Placeholders.cs          — Static utility: CreatePrimitive(), CreateMaterial()
    SceneBootstrapper.cs     — MonoBehaviour: BuildScene() wiring method
    GameManager.cs           — Singleton: game state, distance score
    PlayerController.cs      — Auto-run, lane switch, jump, collision
    CameraFollow.cs          — Smooth 3D follow camera
    ObstacleSpawner.cs       — ObjectPool owner, spawns obstacles
    Obstacle.cs              — Per-obstacle scroll & pool return
    UIManager.cs             — Score display + Game Over panel
    InputHandler.cs          — New Input System wrapper
  Editor/
    SceneBaker.cs            — [MenuItem] "Tools/Bake Scene to Hierarchy"
RESOURCES.md                 — Optional asset-replacement guide
```

---

## Component Specifications

### 1. Placeholders (`Assets/Scripts/Placeholders.cs`)

**Type**: Static utility class (not a MonoBehaviour).

**Responsibility**: Create colored primitive GameObjects and materials at runtime with no imported assets.

**Public API**:
```csharp
public static class Placeholders
{
    /// <summary>Creates a primitive GameObject with a solid-color material.</summary>
    public static GameObject CreatePrimitive(PrimitiveType type, Color color, string name = null);

    /// <summary>Creates a simple unlit Material with the given color.</summary>
    public static Material CreateMaterial(Color color);
}
```

**Internal**: `CreateMaterial` uses `new Material(Shader.Find("Universal Render Pipeline/Lit"))` with a fallback chain (URP → Standard → Unlit/Color). Sets `_BaseColor` / `_Color` depending on the shader found. `CreatePrimitive` calls `GameObject.CreatePrimitive(type)`, replaces its default material with the result of `CreateMaterial(color)`.

**Edge cases**: If no compatible shader is found, falls back to `Shader.Find("Unlit/Color")` which is guaranteed in all Unity render pipelines. The material uses `HideFlags.HideAndDontSave` when created during Play mode only.

---

### 2. SceneBootstrapper (`Assets/Scripts/SceneBootstrapper.cs`)

**Type**: MonoBehaviour. The user adds this to an empty GameObject in an otherwise-empty scene.

**Responsibility**: Single source of truth for constructing the playable scene. Called at runtime (Awake) and at edit time (Bake menu).

**Fields**:
```csharp
[SerializeField] private bool _buildOnAwake = true;
private static bool _sceneBuilt = false;
```

**Public API**:
```csharp
public void BuildScene()
```

**BuildScene() sequence**:

1. **Guard**: If `FindObjectOfType<GameManager>() != null`, return (scene already built).
2. **Physics**: Set `Physics.gravity = new Vector3(0, -25f, 0)`.
3. **Ground**: Create Plane primitive (gray), scale (3, 1, 20), position (0, 0, 10). Tag "Ground".
4. **Player**: Create Capsule primitive (blue), position (0, 1, 0), tag "Player". Add `Rigidbody` (constraints: freeze rotation XYZ + freeze position X during running; mass=1, drag=0). Add `PlayerController`. Add `InputHandler`.
5. **Camera**: Find or create MainCamera. Set position behind/above player. Add `CameraFollow`. Clear flags = Skybox, background = dark gray.
6. **UI Canvas**: Create GameObject "UI", add `Canvas` (RenderMode=ScreenSpaceOverlay), `CanvasScaler` (ScaleWithScreenSize, ref=1920x1080), `GraphicRaycaster`. Create child "ScoreText" with `TextMeshProUGUI`. Create child "GameOverPanel" (initially inactive) with "GameOverTitle" and "RestartPrompt" TMP texts. Add `UIManager`.
7. **EventSystem**: Create if not present (needed for UI raycasting).
8. **GameManager**: Create GameObject "GameManager", add `GameManager` component (execution order -100).
9. **ObstacleSpawner**: Create GameObject "ObstacleSpawner", add `ObstacleSpawner` component. Its pool and spawn parameters are configured via its own Awake/Start.
10. **Lane Markers** (optional visual aid): Three thin Cylinder primitives (dark gray) at Z positions 5, 10, 15, 20, 25; X = -2, 0, +2.
11. **Mark built**: `_sceneBuilt = true`.

**Important**: In Play mode, after `BuildScene()` completes, the bootstrapper destroys its own GameObject (the bootstrapper is no longer needed). In Edit mode (bake), the bootstrapper is destroyed by the caller (SceneBaker).

---

### 3. GameManager (`Assets/Scripts/GameManager.cs`)

**Type**: MonoBehaviour singleton, `[DefaultExecutionOrder(-100)]`.

**Responsibility**: Central game state authority. Tracks distance score. Orchestrates Game Over and Restart.

**Public API**:
```csharp
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public float Distance { get; private set; }        // running distance score
    public bool IsGameOver { get; private set; }       // current state
    public float ForwardSpeed => 8f;                    // shared forward speed constant

    public event System.Action OnGameOver;
    public event System.Action OnRestart;

    public void GameOver();    // triggers death sequence
    public void Restart();     // reloads scene
}
```

**Awake**: Singleton enforcement (`if (Instance != null) Destroy(gameObject); else Instance = this;`).

**Update**: If `!IsGameOver`, `Distance += ForwardSpeed * Time.deltaTime`.

**GameOver()**: Sets `IsGameOver = true`, invokes `OnGameOver` event. UIManager and PlayerController subscribe to this.

**Restart()**: `SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex)`.

---

### 4. InputHandler (`Assets/Scripts/InputHandler.cs`)

**Type**: MonoBehaviour. Attached to the Player GameObject by the bootstrapper.

**Responsibility**: Single point of input querying. Wraps `UnityEngine.InputSystem` (Keyboard, Mouse, Touchscreen) for cross-platform support. No `.inputactions` assets — direct C# device queries with null guards.

**Public API**:
```csharp
public class InputHandler : MonoBehaviour
{
    public bool LeftPressed  { get; private set; }
    public bool RightPressed { get; private set; }
    public bool JumpPressed  { get; private set; }
    public bool RestartPressed { get; private set; }
}
```

**Update**: Each frame, checks:
- `LeftPressed`: `Keyboard.current?.aKey.wasPressedThisFrame` OR `Keyboard.current?.leftArrowKey.wasPressedThisFrame`
- `RightPressed`: `Keyboard.current?.dKey.wasPressedThisFrame` OR `Keyboard.current?.rightArrowKey.wasPressedThisFrame`
- `JumpPressed`: `Keyboard.current?.spaceKey.wasPressedThisFrame` OR `Keyboard.current?.wKey.wasPressedThisFrame` OR `Keyboard.current?.upArrowKey.wasPressedThisFrame`
- `RestartPressed`: `Keyboard.current?.rKey.wasPressedThisFrame`
- Touch: `Touchscreen.current` — swipe left/right (horizontal delta > threshold), swipe up (jump), tap (restart when game over). Each device is null-checked.

**Design rationale**: All input quirks are localized here. PlayerController never touches `Keyboard.current` directly — it only reads the boolean properties.

---

### 5. PlayerController (`Assets/Scripts/PlayerController.cs`)

**Type**: MonoBehaviour. `[RequireComponent(typeof(Rigidbody))]`. Attached to the Player Capsule.

**Responsibility**: Auto-run forward movement, lane switching (smooth interpolation), jump, and collision death detection.

**Serialized Fields** (self-supplying with Placeholders fallback):
```csharp
[SerializeField] private Material _playerMaterial;       // falls back to blue Placeholders material in Awake
[SerializeField] private float _laneDistance = 2.5f;      // X offset between lanes
[SerializeField] private float _laneSwitchSpeed = 12f;    // X interpolation speed
[SerializeField] private float _jumpForce = 10f;          // upward impulse
[SerializeField] private float _groundCheckDistance = 1.2f; // raycast length for ground detection
```

**Runtime State**:
```csharp
private InputHandler _input;
private Rigidbody _rb;
private GameManager _gm;
private int _currentLane = 1;     // 0=left, 1=center, 2=right
private float _targetX;
private bool _isGrounded;
private bool _isDead;
```

**Awake**: `_rb = GetComponent<Rigidbody>()`; `_input = GetComponent<InputHandler>()`. Freeze Rigidbody rotation on all axes. If `_playerMaterial` is null, create placeholder blue material and apply to `GetComponent<MeshRenderer>()`. Find `GameManager.Instance` lazily.

**Update** (input + lane switching):
- If `_isDead`: return.
- If `_input.LeftPressed && _currentLane > 0`: `_currentLane--`.
- If `_input.RightPressed && _currentLane < 2`: `_currentLane++`.
- `_targetX = (_currentLane - 1) * _laneDistance`.
- Smoothly move X: `Vector3 pos = transform.position; pos.x = Mathf.Lerp(pos.x, _targetX, _laneSwitchSpeed * Time.deltaTime); transform.position = pos;`.
- If `_input.JumpPressed && _isGrounded`: `_rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse)`.
- Ground check: `_isGrounded = Physics.Raycast(transform.position, Vector3.down, _groundCheckDistance)`.

**FixedUpdate** (physics movement):
- If `_isDead`: `_rb.velocity = Vector3.zero`; return.
- `_rb.velocity = new Vector3(0, _rb.velocity.y, _gm.ForwardSpeed)`.
  (Note: X is handled in Update via transform, not velocity, to avoid physics fighting the lane interpolation.)

**OnCollisionEnter**: If `collision.gameObject.CompareTag("Obstacle")`: `_isDead = true; _gm.GameOver();` Apply death visual: change material color to red.

---

### 6. CameraFollow (`Assets/Scripts/CameraFollow.cs`)

**Type**: MonoBehaviour. Attached to the Main Camera GameObject.

**Responsibility**: Smooth 3D perspective follow of the player capsule from behind and above.

**Serialized Fields**:
```csharp
[SerializeField] private Vector3 _offset = new Vector3(0, 5, -8);  // relative to player
[SerializeField] private float _smoothSpeed = 8f;
[SerializeField] private float _lookAheadZ = 3f;   // look slightly ahead of player
```

**Awake**: `_player = GameObject.FindGameObjectWithTag("Player")?.transform`. Camera field of view = 60, near clip = 0.3f, far clip = 100f.

**LateUpdate**: If player exists:
- Target position = player.position + offset (offset Z is relative so camera trails behind)
- `transform.position = Vector3.Lerp(transform.position, targetPosition, _smoothSpeed * Time.deltaTime)`
- `transform.LookAt(player.position + Vector3.forward * _lookAheadZ)`

---

### 7. ObstacleSpawner (`Assets/Scripts/ObstacleSpawner.cs`)

**Type**: MonoBehaviour. Attached to its own "ObstacleSpawner" GameObject.

**Responsibility**: Owns the `ObjectPool<GameObject>` for cube obstacles. Spawns obstacles ahead of the player at random intervals and in random lanes.

**Serialized Fields**:
```csharp
[SerializeField] private Material _obstacleMaterial;    // falls back to red placeholder
[SerializeField] private float _spawnDistance = 35f;     // how far ahead to spawn
[SerializeField] private float _despawnDistance = 10f;   // how far behind player before recycling
[SerializeField] private float _minSpawnInterval = 0.8f;
[SerializeField] private float _maxSpawnInterval = 2.0f;
[SerializeField] private int _poolSize = 25;
```

**Runtime State**:
```csharp
private ObjectPool<GameObject> _pool;
private Transform _player;
private float _nextSpawnZ;       // Z position where next obstacle spawns
private int _lastLane = -1;       // track last lane to avoid repeats (optional)
```

**Awake**: Find player via tag. Create the pool:
```csharp
_pool = new ObjectPool<GameObject>(
    createFunc: () => {
        var cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, Color.red, "Obstacle");
        cube.tag = "Obstacle";
        cube.AddComponent<Obstacle>();
        return cube;
    },
    actionOnGet: (go) => { go.SetActive(true); },
    actionOnRelease: (go) => { go.SetActive(false); },
    actionOnDestroy: (go) => Destroy(go),
    collectionCheck: false,
    defaultCapacity: 10,
    maxSize: _poolSize
);
```

**Update** (spawn logic):
- If `GameManager.Instance.IsGameOver`: return.
- Spawn obstacles at intervals based on distance: when player reaches `_nextSpawnZ`, spawn one.
- Randomly select lane (0, 1, 2). Optionally avoid same lane twice in a row.
- `GameObject obs = _pool.Get()`;
- Position: `new Vector3((lane - 1) * 2.5f, 0.5f, player.position.z + _spawnDistance)`
- Set `obs.GetComponent<Obstacle>().Configure(_pool, player)` (pass release callback + player reference)
- Set `_nextSpawnZ = player.position.z + Random.Range(_minSpawnInterval * _gm.ForwardSpeed, _maxSpawnInterval * _gm.ForwardSpeed)`

**Note on spawn strategy**: Instead of time-based intervals (which cause uneven spacing if the game starts slow), use distance-based spawns: each spawn sets `_nextSpawnZ` some distance ahead. This ensures consistent obstacle density regardless of speed.

---

### 8. Obstacle (`Assets/Scripts/Obstacle.cs`)

**Type**: MonoBehaviour. Attached to each pooled cube by the spawner's `createFunc`.

**Responsibility**: Scroll toward the player each frame. Detect when it has passed the player and return itself to the pool.

**Fields**:
```csharp
private float _scrollSpeed;        // matches GameManager.ForwardSpeed
private Transform _player;
private System.Action<GameObject> _releaseAction;
```

**Configure(ObjectPool<GameObject> pool, Transform player)**:
```csharp
public void Configure(UnityEngine.Pool.ObjectPool<GameObject> pool, Transform player)
{
    _player = player;
    _releaseAction = (go) => pool.Release(go);
    _scrollSpeed = GameManager.Instance.ForwardSpeed;
}
```

**Update**:
- `transform.position += Vector3.back * _scrollSpeed * Time.deltaTime;`
- If `transform.position.z < _player.position.z - 10f`: `_releaseAction?.Invoke(gameObject);`

---

### 9. UIManager (`Assets/Scripts/UIManager.cs`)

**Type**: MonoBehaviour. Attached to the "UI" Canvas GameObject.

**Responsibility**: Display running distance score, show/hide Game Over panel with restart prompt.

**Serialized Fields** (assigned by bootstrapper in BuildScene):
```csharp
[SerializeField] private TextMeshProUGUI _scoreText;
[SerializeField] private GameObject _gameOverPanel;
[SerializeField] private TextMeshProUGUI _gameOverScoreText;
```

**Start**: Subscribe to `GameManager.Instance.OnGameOver += ShowGameOver; GameManager.Instance.OnRestart += HideGameOver;`. Panel starts inactive.

**Update**: If game is playing, `_scoreText.text = $"Distance: {GameManager.Instance.Distance:F0}m"`.

**ShowGameOver()**: `_gameOverPanel.SetActive(true); _gameOverScoreText.text = $"Game Over!\nDistance: {GameManager.Instance.Distance:F0}m\nPress R to restart";`

**HideGameOver()**: `_gameOverPanel.SetActive(false);`

The `_scoreText` and `_gameOverPanel` fields use the self-supplying pattern: if the bootstrapper doesn't assign them (e.g., after bake and manual edits), the component logs a warning but doesn't crash.

---

### 10. SceneBaker (`Assets/Editor/SceneBaker.cs`)

**Type**: Editor-only script (`#if UNITY_EDITOR`). Static class with `[MenuItem]`.

**Responsibility**: Calls `SceneBootstrapper.BuildScene()` in Edit mode to persist GameObjects into the scene hierarchy for manual asset replacement and saving.

**Menu path**: `Tools > Bake Scene to Hierarchy`

**Flow**:
1. Validate `!Application.isPlaying` (show dialog if in Play mode).
2. Create a temporary GameObject named "___Baker".
3. Add `SceneBootstrapper` component to it. Set its `_buildOnAwake = false`.
4. Call `bootstrapper.BuildScene()`.
5. Destroy the temporary "___Baker" GameObject.
6. Mark scene dirty: `EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene())`.
7. Log success: "Scene baked. Save the scene (Ctrl+S) to persist."

**Important**: Because `BuildScene()` runs in Edit mode, `GameObject.CreatePrimitive()`, `new GameObject()`, and all component additions produce persistent objects. The user then saves the scene manually (or we prompt them to). The bootstrapper is destroyed so it doesn't re-build on Play.

---

## Technical Stack

| Concern | Technology | Rationale |
|---|---|---|
| Engine | Unity 6 (6000.0+) | Latest LTS, required by architect directive |
| Language | C# (pure scripts) | Project constraint |
| Rendering | URP (Universal Render Pipeline) | Default modern pipeline in Unity 6; fallback to Built-in |
| Visuals | `GameObject.CreatePrimitive()` + runtime Materials | Zero assets; Placeholders utility |
| Physics | `Rigidbody` + `OnCollisionEnter` | Simple, built-in gravity + collision |
| Input | `UnityEngine.InputSystem` (direct C# device queries) | New Input System, no .inputactions assets |
| UI | `UnityEngine.UI.Canvas` + `TextMeshProUGUI` (TMP) | TMP included in Unity 6 by default |
| Pooling | `UnityEngine.Pool.ObjectPool<GameObject>` | Built-in, zero allocation after warm-up |
| Scene composition | `SceneBootstrapper.BuildScene()` | Single source of truth; Play + Bake |
| Editor Bake | `[MenuItem]` + `EditorSceneManager` | Standard Unity Editor scripting |

---

## Key Design Decisions & Rationale

1. **New Input System without .inputactions**: The architect requires the new Input System (`UnityEngine.InputSystem`) for cross-platform compatibility. We use direct C# device queries (`Keyboard.current?.aKey.wasPressedThisFrame`) instead of `.inputactions` assets, keeping the project pure-C#. Null-conditional operators guard against missing devices.

2. **TMP over Legacy Text**: Unity 6 includes TextMeshPro as a built-in package. TMP provides superior text rendering and is the modern standard. The default LiberationSans SDF font asset is loaded via `Resources.Load<TMP_FontAsset>()` without any asset imports.

3. **Rigidbody for forward movement + Transform for lane switching**: `Rigidbody.velocity.z` handles auto-run (physics-correct), `Rigidbody.AddForce` handles jump (natural gravity arc), and `Transform.position.x` handles lane switching (smooth interpolation without physics fighting). This hybrid approach is pragmatic and common in Unity arcade games.

4. **Distance-based obstacle spawning**: Instead of time-based intervals, obstacles spawn when the player crosses a distance threshold (`_nextSpawnZ`). This ensures consistent obstacle density regardless of GameManager speed changes (future-proof).

5. **Singleton GameManager with explicit execution order**: `[DefaultExecutionOrder(-100)]` guarantees GameManager.Awake runs before all other components, eliminating Awake-order bugs in both Play mode and baked scenes.

6. **Self-supplying [SerializeField] pattern**: Every visual component exposes `[SerializeField]` fields for materials/fonts and falls back to `Placeholders` in `Awake` if null. This means the bootstrapper doesn't need to inject every reference — and after Bake, users can drag real assets into Inspector slots.

---

## Edge Cases Addressed

| Edge Case | Mitigation |
|---|---|
| SceneBootstrapper double-build | `_sceneBuilt` static flag + `FindObjectOfType<GameManager>() != null` guard |
| Bake during Play mode | `SceneBaker` checks `Application.isPlaying` and aborts with warning dialog |
| Object pool exhaustion | `maxSize = 25`, spawner throttles; `collectionCheck = false` for perf |
| Two obstacles in same lane consecutively | Spawner tracks `_lastLane` and avoids it (optional, configurable) |
| Collision with ground triggers death | Only `collision.gameObject.CompareTag("Obstacle")` triggers death |
| Canvas doesn't render | Bootstrapper creates `EventSystem` if missing; `CanvasScaler` set to `ScaleWithScreenSize` |
| Player slides on ground | Rigidbody constraints freeze X/Z (except during lane switch via transform) |
| Player tumbles after jump | `RigidbodyConstraints.FreezeRotation` on all axes |
| Camera aspect ratio | FOV = 60 works for 16:9; `CanvasScaler` handles UI |
| TMP default font missing | Graceful fallback: if `Resources.Load<TMP_FontAsset>` fails, use `TMP_Settings.defaultFontAsset` |
| Player falls through ground | Ground has `MeshCollider` (from `CreatePrimitive(Plane)`); player has `CapsuleCollider` |
| Restart: duplicate singletons | `SceneManager.LoadScene` destroys everything; `Awake` singleton guard handles edge case |

---

## Extensibility Points (future, not implemented now)

- **Difficulty scaling**: `GameManager.ForwardSpeed` can increase over time (currently constant 8f).
- **Obstacle variety**: `ObstacleSpawner` can pool multiple obstacle types (tall cubes, wide barriers spanning 2 lanes).
- **Collectibles**: Add a second `ObjectPool` for coin Spheres in `ObstacleSpawner`.
- **Visual themes**: Replace `Placeholders.CreateMaterial` calls with real materials via Inspector after Bake.
- **Mobile controls**: `InputHandler` already queries `Touchscreen.current` — just needs swipe detection thresholds.

---

## Linter Manifest

Only `.cs` files in this project. C# compilation is handled automatically by the Unity build system, so no additional linters are needed.

## RESOURCES.md Deliverable

`RESOURCES.md` will be written as part of implementation and must cover:
- Unity version requirement (6000.0+)
- Required UPM packages (Input System — included by default; TextMeshPro — included by default)
- How to swap placeholder materials for real art (Inspector fields on each component)
- How to use the Bake menu to persist the scene for editing
- How to create prefabs from baked GameObjects
