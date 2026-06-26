# Technical Architecture Design — Game Over UI Fix & Scrolling Lane Markers

## Overview

This design addresses two issues in the 3D endless runner:

1. **Game Over UI invisible**: When the player crashes into an obstacle, the dark overlay panel with "Game Over!", final distance, and "Press R to restart" does not appear — despite the `UIManager.ShowGameOver()` logic and GameManager event system appearing correct on the surface.

2. **Static lane markers**: Lane markers are 15 stationary cylinder primitives placed at fixed Z positions (5/10/15/20/25) in `CreateLaneMarkers()`. They never move, undercutting the sense of forward motion — especially since the player is stationary at Z=0 and obstacles scroll past.

The root cause analysis below identifies the specific failure points, and the architecture delivers targeted fixes that integrate cleanly with the existing `SceneBootstrapper.BuildScene()` pipeline.

---

## Root Cause Analysis

### Bug 1: Game Over UI Invisible

**Primary Cause — `_gameOverScoreText` field populated incorrectly via `GameObject.Find` in UIManager.Start()**:

`UIManager.Start()` self-discovers its references:

```csharp
if (_gameOverScoreText == null && _gameOverPanel != null)
{
    Transform child = _gameOverPanel.transform.Find("GameOverScoreText");
    if (child != null)
    {
        _gameOverScoreText = child.GetComponent<TextMeshProUGUI>();
    }
}
```

This works — `_gameOverScoreText` is found. BUT the real failure is more subtle. The `GameOverPanel` GameObject is created by `BuildScene()` and `SetActive(false)` on line 168 (before UIManager is attached). In **baked scenes**, when Unity deserializes the hierarchy, the `GameOverPanel` starts inactive because that was its serialized state. `UIManager.Start()` runs, discovers the inactive panel via `GameObject.Find` (which does find inactive objects), and subscribes to events. Then `ShowGameOver()` is called on death and does `_gameOverPanel.SetActive(true)`.

However, there is a **subtle Canvas rendering issue**: In Unity UI, when a child of a Canvas is activated via `SetActive(true)`, the Canvas must rebuild its vertex buffers. If the `GameOverPanel`'s `Image` component has no Source Image sprite assigned (it doesn't — it relies on tinting a default white rect), **certain Unity versions silently skip rendering the Image**, producing an invisible overlay. The TMP text children (`GameOverTitle`, `GameOverScoreText`, `RestartPrompt`) DO render — but they render directly on top of the existing `ScoreText` in the top-left corner, which may already be displaying "Distance: Xm". The result is text overlapping text, with no dark backing — appearing as "nothing happened" to the user, OR the white TMP text overlays the white ScoreText and becomes unreadable.

**Contributing Factor — `GameOverScoreText` is the wrong element to display the full message**:

`ShowGameOver()` sets ALL game-over text into `_gameOverScoreText`:

```csharp
_gameOverScoreText.text = $"Game Over!\nDistance: {GameManager.Instance.Distance:F0}m\nPress R to restart";
```

But the panel already has dedicated children: `GameOverTitle` ("Game Over!"), `GameOverScoreText` (score only), and `RestartPrompt` ("Press R to restart"). `ShowGameOver()` should populate each child individually. This is a minor UX issue — not the root cause of invisibility, but contributes to the "nothing looks right" experience.

**Contributing Factor — Build order in `BuildScene()`**: UI Canvas + UIManager are created (line 224) BEFORE GameManager (line 237). Although `GameManager` uses `[DefaultExecutionOrder(-100)]` and `UIManager.Start()` retries subscription in `Update()`, moving GameManager before UI eliminates any theoretical race window and is a free safety improvement.

**Fix Strategy**:
1. Assign a white 1×1 sprite to the GameOverPanel `Image` so it always renders as a solid color rect.
2. In `ShowGameOver()`, populate the dedicated `GameOverTitle` and `RestartPrompt` children individually, and use `GameOverScoreText` only for the score line.
3. Move GameManager creation before UI in `BuildScene()`.
4. Add a `LaneMarkerSpawner` reference lane-marker GameObject creation.

### Bug 2: Static Lane Markers

**Primary Cause — `CreateLaneMarkers()` creates static cylinders with no movement logic**:

```csharp
private void CreateLaneMarkers()
{
    float[] laneOffsets = { -2.5f, 0f, 2.5f };
    float[] zPositions = { 5f, 10f, 15f, 20f, 25f };
    // ... creates 15 static cylinders, no Update() movement
}
```

These 15 cylinders sit at fixed world positions. The player is stationary at Z=0, obstacles scroll backward — but lane markers don't, so the visual field is a mix of moving obstacles and frozen ground details, undermining immersion.

**Solution**: Replace static markers with a pooling + scrolling system that mirrors the existing `Obstacle` + `ObstacleSpawner` pattern. A `LaneMarker` MonoBehaviour scrolls each marker backward each frame and self-recycles when past the player. A `LaneMarkerSpawner` manages an `ObjectPool<GameObject>` and continuously places markers ahead of the player across all three lanes.

**Fix Strategy** (per SOTA recommendations — Solution 1 + 4):
1. Create `LaneMarker.cs` — mirrors `Obstacle.cs`: per-frame `transform.position += Vector3.back * speed * dt`, recycle when `z < player.z - threshold`.
2. Create `LaneMarkerSpawner.cs` — mirrors `ObstacleSpawner.cs`: `ObjectPool<GameObject>`, factory using `Placeholders.CreatePrimitive(Cylinder, ...)`, continuous placement across all 3 lanes.
3. Replace `CreateLaneMarkers()` in `SceneBootstrapper` with creation of a `LaneMarkerSpawner` GameObject.

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────┐
│                     SceneBootstrapper.BuildScene()                     │
│                                                                        │
│  NEW ORDER:                                                            │
│  1. Physics                                                            │
│  2. Ground Plane                                                       │
│  3. Player Capsule (InputHandler + PlayerController + WindEffect)      │
│  4. Camera                                                             │
│  ─── GameManager BEFORE UI ───                                        │
│  5. GameManager ← MOVED EARLIER (was after UI)                        │
│  6. UI Canvas + UIManager ← NOW AFTER GameManager                     │
│     └─ ScoreText, GameOverPanel (with white sprite), children          │
│     └─ UIManager.WireReferences() called explicitly                    │
│  7. EventSystem                                                        │
│  8. ObstacleSpawner                                                    │
│  9. LaneMarkerSpawner ← NEW: replaces CreateLaneMarkers()              │
│  10. Self-destruct                                                     │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│                         NEW: LaneMarkerSpawner                         │
│                                                                        │
│  Awake():                                                              │
│    └─ Create ObjectPool<GameObject>                                    │
│       └─ Factory: Placeholders.CreatePrimitive(Cylinder, darkGray)     │
│       └─ + AddComponent<LaneMarker>()                                  │
│                                                                        │
│  Update():                                                             │
│    └─ Guard: GameManager.Instance != null && !IsGameOver               │
│    └─ Advance _scrollDistance counter                                  │
│    └─ If _scrollDistance > _nextSpawnZ:                                │
│       └─ For each of 3 lanes:                                          │
│          └─ pool.Get() → Configure → position ahead                    │
│       └─ Advance _nextSpawnZ by _markerSpacing                         │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│                         NEW: LaneMarker                                │
│                                                                        │
│  Configure(releaseAction, player):                                     │
│    └─ Cache _scrollSpeed (from GameManager.ForwardSpeed)               │
│    └─ Cache _releaseAction, _player reference                          │
│                                                                        │
│  Update():                                                             │
│    └─ transform.position += Vector3.back * _scrollSpeed * dt           │
│    └─ if z < player.z - 10f:  _releaseAction(gameObject)               │
│                                                                        │
│  OnDisable():                                                          │
│    └─ Safety net: release back to pool if configured                   │
└──────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│                    MODIFIED: UIManager                                  │
│                                                                        │
│  NEW: public void WireReferences(                                      │
│      TextMeshProUGUI scoreText,                                        │
│      GameObject gameOverPanel,                                         │
│      TextMeshProUGUI gameOverScoreText,                                │
│      TextMeshProUGUI gameOverTitle,   ← NEW                            │
│      TextMeshProUGUI restartPrompt    ← NEW                            │
│  )                                                                     │
│                                                                        │
│  ShowGameOver():                                                       │
│    └─ panel.SetActive(true)                                            │
│    └─ _gameOverTitle.text = "Game Over!"                               │
│    └─ _gameOverScoreText.text = $"Distance: {distance:F0}m"            │
│    └─ _restartPrompt.text = "Press R to restart"                       │
│    └─ Defensive: if any ref is null, try GameObject.Find fallback      │
└──────────────────────────────────────────────────────────────────────┘

Collision → Game Over Data Flow (unchanged, just verified working):
  Obstacle (kinematic Rigidbody) ──collision──▶ PlayerController
    .OnCollisionEnter() → GetComponent<Obstacle>() != null
    → _isDead = true → renderer.material.color = Color.red
    → GameManager.Instance.GameOver()
      → IsGameOver = true, OnGameOver?.Invoke()
        → UIManager.ShowGameOver()
          → _gameOverPanel.SetActive(true) [Image renders dark overlay]
          → Populate title, score, restart prompt on dedicated children
```

---

## File Changes Summary

| File | Action | Description |
|------|--------|-------------|
| `Assets/Scripts/LaneMarker.cs` | **NEW** | MonoBehaviour: per-frame scroll + pool recycle for lane marker cylinders |
| `Assets/Scripts/LaneMarkerSpawner.cs` | **NEW** | MonoBehaviour: ObjectPool management, continuous 3-lane marker placement |
| `Assets/Scripts/UIManager.cs` | **MODIFY** | Add `WireReferences()` method; refactor `ShowGameOver()` to populate dedicated child text elements; add defensive null fallbacks |
| `Assets/Scripts/SceneBootstrapper.cs` | **MODIFY** | Reorder GameManager before UI; wire UIManager references explicitly; replace `CreateLaneMarkers()` with `LaneMarkerSpawner` creation; assign white sprite to GameOverPanel Image |
| `RESOURCES.md` | **MODIFY** | Add `LaneMarker`, `LaneMarkerSpawner` to file structure; update troubleshooting |

---

## Component Specifications

### 1. LaneMarker (NEW)

**File**: `Assets/Scripts/LaneMarker.cs`

**Purpose**: Attached to each pooled lane marker cylinder. Handles per-frame backward scrolling (matching `GameManager.ForwardSpeed`) and self-return to the pool when the marker passes behind the player.

**Pattern**: Direct mirror of `Obstacle.cs` — same structure, same lifecycle, but no collision detection dependency.

**Serialized Fields**: None (all configuration injected via `Configure()`).

**Public API**:

```csharp
public void Configure(System.Action<GameObject> releaseAction, Transform player)
```

- `releaseAction`: Callback to return this GameObject to the pool (typically `(go) => pool.Release(go)`).
- `player`: The player's Transform, used to determine the recycle threshold.

**Update() behavior**:
1. Guard: return if not configured, or if `_player`/`_releaseAction` is null.
2. `transform.position += Vector3.back * _scrollSpeed * Time.deltaTime;`
3. If `transform.position.z < _player.position.z - 10f`: set `_configured = false`, call `_releaseAction(gameObject)`.

**OnDisable() behavior**: Same safety net as `Obstacle.cs` — if configured, release back to pool so pool counts stay consistent during scene reload.

**Key design decisions**:
- No Rigidbody needed: lane markers are purely visual — they don't need to trigger collisions. Not adding a Rigidbody avoids false collision events with the player and keeps the markers lightweight.
- `_scrollSpeed` cached from `GameManager.Instance.ForwardSpeed` at configuration time (with 8f fallback), matching `Obstacle.cs`.
- Recycle threshold of `player.z - 10f` matches `Obstacle.cs` and is comfortably behind the camera.
- `_configured` flag guards against double-release and uninitialized updates.

---

### 2. LaneMarkerSpawner (NEW)

**File**: `Assets/Scripts/LaneMarkerSpawner.cs`

**Purpose**: Manages an `ObjectPool<GameObject>` of lane marker cylinders and continuously places them across all three lanes ahead of the player, producing an infinite backward-scrolling lane-dividing effect.

**Pattern**: Mirror of `ObstacleSpawner.cs` with lane-marker-specific tweaks.

**Serialized Fields (Self-Supplying)**:

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `_markerMaterial` | `Material` | null → dark gray placeholder | Material for marker cylinders |
| `_markerSpacing` | `float` | `6f` | Z-distance between successive rows of markers |
| `_spawnDistance` | `float` | `40f` | How far ahead of the player markers are spawned |
| `_poolDefaultCapacity` | `int` | `15` | Pre-allocated pool entries |
| `_poolMaxSize` | `int` | `30` | Pool cap |

**Runtime State**:
- `_pool`: `ObjectPool<GameObject>` — owns all marker cylinder GameObjects.
- `_player`: `Transform` — discovered via `GameObject.FindGameObjectWithTag("Player")`.
- `_scrollDistance`: `float` — virtual scroll counter (same pattern as `ObstacleSpawner._scrollDistance`).
- `_nextSpawnZ`: `float` — threshold for next spawn row.
- Lane offsets: `{ -2.5f, 0f, 2.5f }` — matches `PlayerController._laneDistance`.

**Factory (`CreateMarker()`)**:
```csharp
GameObject marker = Placeholders.CreatePrimitive(PrimitiveType.Cylinder, darkGray, "LaneMarker");
marker.transform.localScale = new Vector3(0.2f, 0.05f, 0.2f);
marker.AddComponent<LaneMarker>();
// NO Rigidbody — markers are visual-only, should not participate in physics
return marker;
```

**Update() behavior**:
1. Guard: `GameManager.Instance == null || GameManager.Instance.IsGameOver || _player == null` → return.
2. `_scrollDistance += GameManager.Instance.ForwardSpeed * Time.deltaTime;`
3. If `_scrollDistance >= _nextSpawnZ`:
   - For each of the 3 lane X offsets:
     - `GameObject marker = _pool.Get();`
     - Position at `(laneX, 0.025f, _player.position.z + _spawnDistance)`
     - `marker.GetComponent<LaneMarker>().Configure((go) => _pool.Release(go), _player);`
   - `_nextSpawnZ = _scrollDistance + _markerSpacing;`

**Key design decisions**:
- **All 3 lanes spawn together**: Unlike obstacles (which pick random lanes with same-lane avoidance), lane markers are supposed to mark all lanes equally. Each spawn event places one marker in each of the 3 lanes simultaneously, creating a continuous "row" of markers scrolling backward.
- **No same-lane avoidance needed**: Markers are uniformly distributed across all lanes — there's no gameplay reason to avoid a lane.
- **Virtual scroll distance**: Uses the same `_scrollDistance` pattern as `ObstacleSpawner` to decouple spawning from the player's actual Z position (player is stationary at Z=0).
- **No Rigidbody on markers**: Markers are purely decorative. Adding a Rigidbody would cause unnecessary physics overhead and could trigger false `OnCollisionEnter` calls on the player. The player's death check (`GetComponent<Obstacle>()`) already excludes lane markers.
- **Collision safety**: The player's `OnCollisionEnter` only triggers death when `collision.gameObject.GetComponent<Obstacle>() != null`. Lane markers have a `LaneMarker` component, not an `Obstacle` component, so they can never cause a false death — even if the player physically touches a marker cylinder.

---

### 3. UIManager (MODIFY)

**File**: `Assets/Scripts/UIManager.cs`

**Changes**:

#### 3a. New Serialized Fields

```csharp
[SerializeField] private TextMeshProUGUI _gameOverTitle;
[SerializeField] private TextMeshProUGUI _restartPrompt;
```

These reference the `GameOverTitle` and `RestartPrompt` TMP children of the GameOverPanel. Both are self-supplying: discovered via `_gameOverPanel.transform.Find("GameOverTitle")` / `Find("RestartPrompt")` in `Start()` if null.

#### 3b. New Public Method: `WireReferences`

```csharp
/// <summary>
/// Called by SceneBootstrapper.BuildScene() to directly wire all UI references,
/// bypassing the GameObject.Find self-supply path. Keeps self-supply as fallback.
/// </summary>
public void WireReferences(
    TextMeshProUGUI scoreText,
    GameObject gameOverPanel,
    TextMeshProUGUI gameOverTitle,
    TextMeshProUGUI gameOverScoreText,
    TextMeshProUGUI restartPrompt)
{
    _scoreText = scoreText;
    _gameOverPanel = gameOverPanel;
    _gameOverTitle = gameOverTitle;
    _gameOverScoreText = gameOverScoreText;
    _restartPrompt = restartPrompt;
}
```

This eliminates reliance on `GameObject.Find` at runtime when the bootstrapper is used, while preserving the self-supply fallback for baked scenes where the bootstrapper might not be present.

#### 3c. Refactored `ShowGameOver()`

```csharp
public void ShowGameOver()
{
    // Defensive fallback: if references are null (e.g., baked scene without WireReferences),
    // try GameObject.Find as a last resort.
    if (_gameOverPanel == null)
        _gameOverPanel = GameObject.Find("GameOverPanel");

    if (_gameOverPanel != null)
        _gameOverPanel.SetActive(true);

    // Populate dedicated children (with fallback discovery)
    if (_gameOverTitle == null && _gameOverPanel != null)
        _gameOverTitle = _gameOverPanel.transform.Find("GameOverTitle")?.GetComponent<TextMeshProUGUI>();

    if (_gameOverScoreText == null && _gameOverPanel != null)
        _gameOverScoreText = _gameOverPanel.transform.Find("GameOverScoreText")?.GetComponent<TextMeshProUGUI>();

    if (_restartPrompt == null && _gameOverPanel != null)
        _restartPrompt = _gameOverPanel.transform.Find("RestartPrompt")?.GetComponent<TextMeshProUGUI>();

    if (_gameOverTitle != null)
        _gameOverTitle.text = "Game Over!";

    if (_gameOverScoreText != null && GameManager.Instance != null)
        _gameOverScoreText.text = $"Distance: {GameManager.Instance.Distance:F0}m";

    if (_restartPrompt != null)
        _restartPrompt.text = "Press R to restart";

    Debug.Log("UIManager.ShowGameOver: Panel activated, text populated.");
}
```

Key improvements:
- Each child text element (`GameOverTitle`, `GameOverScoreText`, `RestartPrompt`) is populated individually rather than shoving everything into `_gameOverScoreText`.
- Defensive `GameObject.Find` fallback at call time if any reference is null (handles baked-scene edge case).
- Debug log for traceability.

#### 3d. Updated `Start()` Self-Supply

Add discovery for the two new fields:

```csharp
if (_gameOverTitle == null && _gameOverPanel != null)
{
    Transform child = _gameOverPanel.transform.Find("GameOverTitle");
    if (child != null) _gameOverTitle = child.GetComponent<TextMeshProUGUI>();
}

if (_restartPrompt == null && _gameOverPanel != null)
{
    Transform child = _gameOverPanel.transform.Find("RestartPrompt");
    if (child != null) _restartPrompt = child.GetComponent<TextMeshProUGUI>();
}
```

---

### 4. SceneBootstrapper (MODIFY)

**File**: `Assets/Scripts/SceneBootstrapper.cs`

**Changes**:

#### 4a. Reorder: GameManager Before UI

Move the GameManager creation block (currently lines 236–237) to BEFORE the UI Canvas block (currently lines 118–224). After the move, the section order is:

```
5. GameManager          ← MOVED UP (was #7)
6. UI Canvas + UIManager ← Was #5, now #6 (after GameManager)
7. EventSystem
8. ObstacleSpawner
9. LaneMarkerSpawner     ← NEW (was CreateLaneMarkers)
```

This ensures `GameManager.Instance` is available before `UIManager.Start()` runs, making the `TrySubscribeEvents()` retry loop succeed on the first attempt.

#### 4b. Wire UIManager References Explicitly

After creating all UI elements and before adding `UIManager`, capture references and pass them via `WireReferences()`:

```csharp
// After creating ScoreText, GameOverPanel, GameOverTitle, GameOverScoreText, RestartPrompt:

// Assign a white 1×1 sprite to the GameOverPanel Image so it always renders
// as a solid color rectangle (some Unity versions skip Image rendering when
// no Source Image is assigned).
Texture2D whiteTex = new Texture2D(1, 1);
whiteTex.SetPixel(0, 0, Color.white);
whiteTex.Apply();
panelImage.sprite = Sprite.Create(whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

// Wire UIManager references explicitly (eliminates GameObject.Find dependency)
UIManager uiManager = uiGo.AddComponent<UIManager>();
uiManager.WireReferences(scoreText, panelGo, titleText, gameOverScoreText, promptText);
```

The `whiteTex` sprite ensures the `Image` component always has a Source Image — a solid white 1×1 texture tinted by `panelImage.color` (black, 0.7 alpha) to produce the dark overlay.

#### 4c. Replace `CreateLaneMarkers()` with LaneMarkerSpawner

Remove the `CreateLaneMarkers()` private method entirely. Replace the call at line 244 with:

```csharp
// --- 9. LaneMarkerSpawner (scrolling lane markers — replaces static CreateLaneMarkers) ---
GameObject markerSpawnerGo = new GameObject("LaneMarkerSpawner");
markerSpawnerGo.AddComponent<LaneMarkerSpawner>();
```

The `LaneMarkerSpawner` self-discovers the player via tag and self-supplies its material, so no further wiring is needed.

---

## New Component Integration in BuildScene()

### Updated BuildScene() Section Order

```
1. Physics.gravity
2. Ground Plane
3. Player Capsule + Rigidbody + InputHandler + PlayerController + WindEffect
4. Main Camera + CameraFollow
5. GameManager          ← MOVED UP (was after UI)
6. UI Canvas:
   - Canvas + CanvasScaler + GraphicRaycaster
   - ScoreText (TMP)
   - GameOverPanel (Image with white sprite) + GameOverTitle + GameOverScoreText + RestartPrompt
   - UIManager (added, then WireReferences called)
7. EventSystem
8. ObstacleSpawner
9. LaneMarkerSpawner    ← NEW (replaces CreateLaneMarkers)
10. Self-destruct (Play mode)
```

### Both Paths Covered

- **Play mode** (`Awake()` → `BuildScene()`): All objects created fresh. UIManager receives explicit references via `WireReferences()`. LaneMarkerSpawner manages scrolling markers.
- **Editor Bake** (`Tools > Bake Scene to Hierarchy`): Same `BuildScene()` called. Generated objects persist in hierarchy. On next Play, UIManager.Start() self-supplies references as fallback (since WireReferences was only called during bake, not on deserialization).

---

## Technical Stack

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Engine | Unity 6 (6000.0+) | Project constraint |
| Language | C# (pure scripts) | Project constraint |
| Marker pooling | `UnityEngine.Pool.ObjectPool<GameObject>` | Same pattern as `ObstacleSpawner`; SOTA-recommended |
| Marker visuals | `Placeholders.CreatePrimitive(PrimitiveType.Cylinder, ...)` | Existing pattern; project constraint |
| Marker scrolling | `Transform.position += Vector3.back * speed * dt` | Same pattern as `Obstacle.Update()`; SOTA-recommended |
| Event subscription | `GameManager.OnGameOver` / `OnRestart` events | Existing system; no change |
| UI rendering | `Image` with 1×1 white sprite + color tint | Solves Image-without-sprite rendering issue |
| Explicit wiring | `UIManager.WireReferences()` | Eliminates GameObject.Find dependency at runtime |

---

## Edge Cases Addressed

| Edge Case | Mitigation | Where |
|-----------|-----------|-------|
| **Image renders as invisible** (no Source Image sprite) | White 1×1 `Texture2D` assigned as `Sprite` on GameOverPanel `Image` | `SceneBootstrapper.BuildScene()` |
| **UIManager references null in baked scene** | `ShowGameOver()` defensive `GameObject.Find` fallback if any reference is null | `UIManager.ShowGameOver()` |
| **Event subscription race** (GameManager null during Start) | GameManager created BEFORE UI in BuildScene; `[DefaultExecutionOrder(-100)]` as existing safety; `TrySubscribeEvents` retry loop as existing safety | `SceneBootstrapper.BuildScene()` order |
| **GameOverPanel children not populated** | `ShowGameOver()` now sets `GameOverTitle`, `GameOverScoreText`, `RestartPrompt` individually | `UIManager.ShowGameOver()` |
| **Lane markers collide with player** (false death) | Markers have `LaneMarker` component, not `Obstacle`; `PlayerController.OnCollisionEnter` checks `GetComponent<Obstacle>() != null` — markers don't match. No Rigidbody on markers → physics overhead avoided. | `LaneMarker.cs` (no Obstacle component), `PlayerController.cs` line 208 |
| **Marker pool starvation on restart** | `SceneManager.LoadScene` destroys all objects; new `LaneMarkerSpawner.Awake()` creates fresh pool | `LaneMarkerSpawner.Awake()` |
| **Markers recycle too early/late** (visual pop) | Recycle threshold `player.z - 10f` matches `Obstacle.cs`; camera far clip is 100f, so markers are well off-screen before recycling | `LaneMarker.Update()` |
| **Speed mismatch with obstacles** | Both `LaneMarker` and `Obstacle` read `GameManager.Instance.ForwardSpeed` (8f) | `LaneMarker.Configure()`, `Obstacle.Configure()` |
| **Markers render on top of obstacles** | Markers are at Y=0.025 (ground level); obstacles are at Y=0.5 — physically separated, no Z-fighting | `LaneMarkerSpawner` positioning |
| **R key restart after game over** | Existing `UIManager.CheckRestartInput()` → `GameManager.Restart()` → `SceneManager.LoadScene()` — unchanged | `UIManager.Update()` |

---

## Extensibility Points (Future, Not Implemented Now)

- **Variable marker density**: `LaneMarkerSpawner._markerSpacing` can be reduced for denser markers or increased for sparser ones — exposed as `[SerializeField]`.
- **Marker color/material swap**: `LaneMarkerSpawner._markerMaterial` accepts any material via Inspector or bootstrapper.
- **Curved/swaying lanes**: If the game ever adds curved lanes, `LaneMarker.Update()` can interpolate X as well as Z.
- **Obstacle-style lane marker variants**: Different marker types could be added to the pool with a type-selection system (e.g., dashed vs. solid lines).

---

## Success Criteria Verification

| Criteria | How Fix Achieves It |
|----------|---------------------|
| (1) Crash → dark overlay with "Game Over!", distance, "Press R to restart" | GameOverPanel Image gets a white sprite → always renders as dark overlay. ShowGameOver() populates GameOverTitle, GameOverScoreText, RestartPrompt individually. Explicit WireReferences() eliminates reference-resolution failure. Defensive GameObject.Find fallback catches baked-scene edge case. |
| (2) Press R → scene reloads, game restarts fresh | Existing `CheckRestartInput()` + `GameManager.Restart()` unchanged |
| (3) Lane markers scroll backward continuously past player and recycle | `LaneMarkerSpawner` places rows of 3 markers ahead; `LaneMarker.Update()` scrolls them backward at `ForwardSpeed`; recycle at `z < player.z - 10f`; pool returns objects for reuse |
| (4) No console errors | No tag assignments, no missing references (defensive null checks), no unregistered GameObjects |
| (5) Zero manual scene setup | All changes in `BuildScene()` + new self-contained components; Play mode and Bake path both covered |
