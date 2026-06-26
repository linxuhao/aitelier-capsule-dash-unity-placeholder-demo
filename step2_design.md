# Technical Architecture Design — Fix Collision GameOver & Undefined "Ground" Tag

## Overview

This design addresses two bugs in the 3D endless runner:

1. **Player-obstacle collision does not trigger Game Over**: Obstacles are moved via `Transform.position += Vector3.back * speed * dt` in `Obstacle.Update()` but have **no Rigidbody component**. Unity's physics engine treats them as static colliders (Collider with no Rigidbody), and moving a static collider into a dynamic Rigidbody does NOT reliably fire `OnCollisionEnter` on the dynamic body. The fix: add a **kinematic Rigidbody** to each obstacle so the physics engine tracks its movement and properly triggers collision callbacks.

2. **"Tag: Ground is not defined" console error**: `SceneBootstrapper.BuildScene()` line 74 sets `ground.tag = "Ground"`, but "Ground" is not a built-in Unity tag and not registered in `ProjectSettings/TagManager.asset` (`tags: []`). The fix: **remove the tag assignment** — no code in the project checks for the "Ground" tag.

Both fixes are single-line changes in existing files. No new files, no new components, no SceneBootstrapper wiring changes needed.

---

## Architecture Diagram (Text)

```
┌──────────────────────────────────────────────────────────────────────┐
│                       SceneBootstrapper                              │
│  BuildScene(): wires Player + ObstacleSpawner + GameManager + UI     │
│                                                                       │
│  FIX #2: Remove ground.tag = "Ground";  ← Line 74 — dead code,       │
│           no code depends on this tag, and it's not registered        │
└───────┬──────────────────────────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────────────────────────────────┐
│  ┌────────────────────┐    ┌────────────────────────────────────┐    │
│  │     Player         │    │  ObstacleSpawner                   │    │
│  │   (Capsule)        │    │  (standalone GO)                   │    │
│  │  ┌───────────────┐ │    │  ┌──────────────────────────────┐  │    │
│  │  │ Rigidbody     │ │    │  │ ObjectPool<GameObject>       │  │    │
│  │  │ (dynamic,     │ │    │  │                              │  │    │
│  │  │  CCD=Cont.Dyn)│ │    │  │ CreateObstacle():            │  │    │
│  │  └───────┬───────┘ │    │  │   cube = CreatePrimitive()   │  │    │
│  │          │          │    │  │   cube.AddComponent<Obstacle>│  │    │
│  │  ┌───────┴───────┐ │    │  │                               │  │    │
│  │  │ CapsuleCollider│ │    │  │ FIX #1:                      │  │    │
│  │  └───────┬───────┘ │    │  │   cube.AddComponent<Rigidbody> │  │    │
│  │          │          │    │  │     .isKinematic = true;     │  │    │
│  │  ┌───────┴───────┐ │    │  │   ← One line added            │  │    │
│  │  │ PlayerControl │ │    │  └──────────────────────────────┘  │    │
│  │  │               │ │    │                                    │    │
│  │  │ OnCollision   │ │    │ Obstacle.Update():                 │    │
│  │  │ Enter()       │ │    │   transform.position +=            │    │
│  │  │  (UNCHANGED)  │ │    │     Vector3.back * speed * dt;    │    │
│  │  │  GetComponent │ │    │   ← Transform movement unchanged   │    │
│  │  │  <Obstacle>() │ │    │                                    │    │
│  │  └───────┬───────┘ │    └────────────────────────────────────┘    │
│  │          │          │                                              │
│  └──────────┼──────────┘                                              │
│             │                                                         │
│  ┌──────────┴──────────────────────────────────────────────────┐     │
│  │                    GameManager                               │     │
│  │  GameOver() → IsGameOver=true, OnGameOver event             │     │
│  │  (NO CHANGE)                                                 │     │
│  └──────────┬──────────────────────────────────────────────────┘     │
│             │                                                         │
│  ┌──────────┴──────────┐                                              │
│  │      UIManager       │                                             │
│  │  ShowGameOver()      │                                             │
│  │  (NO CHANGE)         │                                             │
│  └─────────────────────┘                                              │
└──────────────────────────────────────────────────────────────────────┘
```

### Data Flow (Collision → Game Over)

```
ObstacleSpawner.CreateObstacle()
  └─ Placeholders.CreatePrimitive(Cube, red, "Obstacle")
  └─ cube.AddComponent<Obstacle>()
  └─ cube.AddComponent<Rigidbody>().isKinematic = true;    ← FIX #1: kinematic Rigidbody added

Obstacle.Update()
  └─ transform.position += Vector3.back * _scrollSpeed * dt;
     ← Physics engine NOW tracks this movement because Rigidbody exists (even though isKinematic)
     ← Collision events fire reliably between this GameObject and the player's dynamic Rigidbody

Player Collision Event (Physics Engine)
  └─ PlayerController.OnCollisionEnter(Collision collision)
     ├─ if (_isDead) return;                     ← Existing double-trigger guard
     ├─ if (collision.gameObject.GetComponent<Obstacle>() != null)  ← Existing check (component-based)
     │   ├─ _isDead = true;
     │   ├─ renderer.material.color = Color.red;  ← Existing death visual
     │   └─ GameManager.Instance.GameOver();       ← Existing death notification
     │       └─ GameManager.OnGameOver event
     │           └─ UIManager.ShowGameOver()       ← Panel visible, final distance shown
     │
     └─ else (ground, lane markers, walls): nothing — no Obstacle component
```

---

## File Structure

```
Assets/
  Scripts/
    ObstacleSpawner.cs        — (MODIFY) Add kinematic Rigidbody in CreateObstacle()
    SceneBootstrapper.cs      — (MODIFY) Remove ground.tag = "Ground";
    Obstacle.cs               — (NO CHANGE) Transform-based movement preserved
    PlayerController.cs       — (NO CHANGE) OnCollisionEnter already uses GetComponent<Obstacle>()
    GameManager.cs            — (NO CHANGE)
    Placeholders.cs           — (NO CHANGE)
    InputHandler.cs           — (NO CHANGE)
    CameraFollow.cs           — (NO CHANGE)
    UIManager.cs              — (NO CHANGE)
    WindEffect.cs             — (NO CHANGE)
  Editor/
    SceneBaker.cs             — (NO CHANGE)
RESOURCES.md                  — (MODIFY) Remove "Ground" tag from troubleshooting
```

---

## Component Specifications

### 1. ObstacleSpawner (MODIFY — Bug 1: Collision Fix)

**File**: `Assets/Scripts/ObstacleSpawner.cs`

**Change**: Add one line in `CreateObstacle()` — a kinematic Rigidbody on the obstacle cube.

**Before** (lines 165–170):
```csharp
private GameObject CreateObstacle()
{
    Color color = _obstacleMaterial != null ? _obstacleMaterial.color : Color.red;
    GameObject cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, color, "Obstacle");
    cube.AddComponent<Obstacle>();
    return cube;
}
```

**After**:
```csharp
private GameObject CreateObstacle()
{
    Color color = _obstacleMaterial != null ? _obstacleMaterial.color : Color.red;
    GameObject cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, color, "Obstacle");
    cube.AddComponent<Obstacle>();
    cube.AddComponent<Rigidbody>().isKinematic = true;
    return cube;
}
```

**Rationale**:

- **Root cause**: Without a Rigidbody, Unity treats the obstacle's `BoxCollider` as a **static collider**. When a static collider is moved via `Transform.position` (as `Obstacle.Update()` does), the physics engine does NOT track its per-frame movement, and `OnCollisionEnter` on a dynamic Rigidbody (the player) is **not reliably called**. This is the fundamental reason player-obstacle collisions fail to trigger Game Over.

- **Why kinematic Rigidbody**: `isKinematic = true` tells the physics engine "this body moves via script, not physics forces, but please track its position each frame for collision purposes." Kinematic bodies are specifically designed for Transform-moved objects that need to participate in collision detection with dynamic Rigidbodies.

- **Movement code unchanged**: `Obstacle.Update()` continues to use `transform.position += Vector3.back * _scrollSpeed * Time.deltaTime`. Kinematic Rigidbodies are expected to be moved via Transform — this is the idiomatic Unity pattern.

- **Performance**: At most 25 pooled obstacles exist at any time. Adding a kinematic Rigidbody to each has zero measurable performance impact.

- **No interference with the pool**: The ObjectPool's `actionOnGet`/`actionOnRelease` callbacks (SetActive true/false) work identically with or without a Rigidbody component.

- **Compatibility**: Works identically in Play mode, baked scenes (Editor), and built players. The Rigidbody is a standard Unity component with no external dependencies.

### 2. SceneBootstrapper (MODIFY — Bug 2: Undefined "Ground" Tag)

**File**: `Assets/Scripts/SceneBootstrapper.cs`

**Change**: Remove the `ground.tag = "Ground";` assignment on line 74.

**Before** (lines 64–74):
```csharp
// --- 2. Ground Plane ---
GameObject ground = Placeholders.CreatePrimitive(
    PrimitiveType.Plane,
    new Color(0.3f, 0.3f, 0.3f),
    "Ground"
);
ground.transform.position = new Vector3(0f, 0f, 10f);
ground.transform.localScale = new Vector3(3f, 1f, 200f);
ground.tag = "Ground";
```

**After**:
```csharp
// --- 2. Ground Plane ---
GameObject ground = Placeholders.CreatePrimitive(
    PrimitiveType.Plane,
    new Color(0.3f, 0.3f, 0.3f),
    "Ground"
);
ground.transform.position = new Vector3(0f, 0f, 10f);
ground.transform.localScale = new Vector3(3f, 1f, 200f);
```

**Rationale**:

- **Root cause**: `ground.tag = "Ground"` sets a tag that does not exist in Unity's tag database. Unity only provides "Player", "MainCamera", "Untagged", "Respawn", "Finish", and "EditorOnly" as built-in tags. "Ground" is a custom string with no corresponding entry in `ProjectSettings/TagManager.asset` (`tags: []`), so Unity logs a console error at runtime.

- **No code depends on this tag**: A grep of the entire codebase confirms the "Ground" tag is never read — no `CompareTag("Ground")`, no `GameObject.FindGameObjectWithTag("Ground")`, no tag-based filtering on raycasts. The `PlayerController._isGrounded` check uses `Physics.Raycast(transform.position, Vector3.down, _groundCheckDistance)` with **no tag filter** — it hits any collider below the player (including the ground Plane's MeshCollider), so removing the tag has zero effect on gameplay.

- **Zero side effects**: The ground plane retains its default "Untagged" tag. The plane is visually identical, physically identical (MeshCollider intact), and functionally identical.

- **Why not register the tag**: Registering "Ground" in TagManager.asset would require (a) editing a binary `.asset` file (violates project constraints — deliver scripts only), or (b) Editor-only `UnityEditorInternal` APIs (doesn't work in builds). Removing the dead-code assignment is simpler, safer, and build-safe.

### 3. RESOURCES.md (MODIFY — Accuracy Update)

**File**: `RESOURCES.md`

**Change**: Update the troubleshooting entry that incorrectly references the "Ground" tag.

**Before** (line 159):
```
- If this occurs, ensure the ground's tag is "Ground" and the player's Rigidbody has `useGravity = true`
```

**After**:
```
- If this occurs, ensure the player's Rigidbody has `useGravity = true` and the ground Plane has a MeshCollider (Unity primitives include this by default)
```

**Rationale**: Since the "Ground" tag is no longer assigned, the troubleshooting guide should not instruct users to check for it. The correct check is that the ground Plane has its default MeshCollider (always true for Unity primitives) and the player has gravity enabled.

### 4. PlayerController (NO CHANGE — Already Correct)

**File**: `Assets/Scripts/PlayerController.cs`

No modifications needed. The existing code is correct:

- `OnCollisionEnter` (line 193) uses `GetComponent<Obstacle>() != null` (line 208) — component-based detection, no tag dependency.
- `_isDead` guard (line 200) prevents double-trigger.
- Death behavior (lines 210–223): material color → red, `GameManager.Instance.GameOver()`.
- `CollisionDetectionMode.ContinuousDynamic` (line 112) prevents bullet-through-paper tunneling.
- Player Rigidbody is dynamic (non-kinematic) — set up in `Awake()` (lines 102–112) and `SceneBootstrapper.BuildScene()` (lines 87–90).

The only reason `OnCollisionEnter` was not firing is that obstacles lacked a Rigidbody — fixed by the ObstacleSpawner change above.

---

## Component Interaction Matrix (Unchanged)

| From → To | GameManager | PlayerController | ObstacleSpawner | Obstacle | UIManager |
|------------|:---:|:---:|:---:|:---:|:---:|
| **GameManager** | — | — | — | — | events |
| **PlayerController** | `.IsGameOver` `.GameOver()` | — | — | `GetComponent<Obstacle>()` | — |
| **ObstacleSpawner** | `.IsGameOver` `.ForwardSpeed` | `.position.z` | — | `.Configure()` | — |
| **Obstacle** | `.ForwardSpeed` (cached) | `.position.z` | — | — | — |
| **UIManager** | `.Distance` `.OnGameOver` `.OnRestart` | — | — | — | — |

All interactions are unchanged. The two fixes are purely internal to `ObstacleSpawner.CreateObstacle()` (adds a component) and `SceneBootstrapper.BuildScene()` (removes a dead line).

---

## Integration in SceneBootstrapper.BuildScene()

**No wiring changes needed.** The two fixes are self-contained:

1. **ObstacleSpawner** is created by `BuildScene()` at lines 241–242 (`spawnerGo.AddComponent<ObstacleSpawner>()`). The `ObstacleSpawner.Awake()` creates the ObjectPool, and `CreateObstacle()` (the factory callback) now includes the kinematic Rigidbody. No bootstrapper change required — the spawner's internal behavior is transparent to the rest of the system.

2. **Ground Plane** is created at lines 64–73. The `ground.tag = "Ground"` line is simply removed. The ground remains at the same position, scale, and with the same MeshCollider.

Both the runtime path (`Awake()` → `BuildScene()`) and the Editor bake path (`Tools > Bake Scene to Hierarchy` → `BuildScene()`) execute the same `BuildScene()` method, so both fixes work identically in both modes.

---

## Technical Stack

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Engine | Unity 6 (6000.0+) | Required by project |
| Language | C# (pure scripts) | Project constraint |
| Collision detection | `MonoBehaviour.OnCollisionEnter(Collision)` | Existing callback, now reliably triggered |
| Obstacle Rigidbody | `Rigidbody.isKinematic = true` | One-line addition, enables physics tracking of Transform-moved obstacles |
| Death condition | `GameObject.GetComponent<Obstacle>() != null` | Existing, unchanged — zero-config, build-safe |
| Ground collision | `Physics.Raycast` (no tag filter) | Existing, unchanged — hits any collider below player |
| Death response | `GameManager.Instance.GameOver()` + material color change | Existing, unchanged |

---

## Bug Fix Traceability

| Issue | Root Cause | Fix | File(s) Changed | Lines Affected |
|-------|-----------|-----|-----------------|----------------|
| Player doesn't die on obstacle collision | Obstacles have no Rigidbody → Unity treats them as static colliders. Moving a static collider via `Transform` does not reliably trigger `OnCollisionEnter` on a dynamic Rigidbody. The `PlayerController.OnCollisionEnter` logic and `GetComponent<Obstacle>()` check are correct — the collision event simply never fires. | Add `cube.AddComponent<Rigidbody>().isKinematic = true;` in `ObstacleSpawner.CreateObstacle()`. Kinematic Rigidbodies are tracked by the physics engine, so Transform-moved obstacles now properly trigger collision events on the player's dynamic Rigidbody. | `ObstacleSpawner.cs` | +1 line in `CreateObstacle()` |
| Console error: "Tag: Ground is not defined" | `SceneBootstrapper.BuildScene()` sets `ground.tag = "Ground"` (line 74), but "Ground" is not a built-in tag and not registered in `ProjectSettings/TagManager.asset` (`tags: []`). No code reads this tag. | Remove `ground.tag = "Ground";` — dead code with zero functional impact. The ground plane retains its default "Untagged" tag and all gameplay behavior is preserved. | `SceneBootstrapper.cs` | −1 line (line 74) |
| RESOURCES.md references non-existent "Ground" tag | Troubleshooting entry says "ensure the ground's tag is \"Ground\"" — misleading since the tag no longer exists. | Replace with accurate guidance: check gravity and MeshCollider presence. | `RESOURCES.md` | 1 line edit |

---

## Edge Cases Addressed

| Edge Case | Mitigation | Where |
|-----------|-----------|-------|
| **Double-death trigger** (simultaneous collisions) | Existing `_isDead` guard at top of `OnCollisionEnter` — returns immediately if already dead | `PlayerController.cs` line 200 |
| **Ground collision false-positive death** | Ground plane has no `Obstacle` component → `GetComponent<Obstacle>()` returns `null` → death not triggered | Implicit via component absence |
| **Lane marker collision false-positive death** | Lane markers are Cylinder primitives with no `Obstacle` component → `GetComponent<Obstacle>()` returns `null` | Implicit via component absence |
| **Bullet-through-paper tunneling** (8 units/s scroll speed) | Player already has `CollisionDetectionMode.ContinuousDynamic` (line 112). Obstacles now have Rigidbody, so CCD fully activates — both bodies are tracked by the physics engine at sub-frame granularity. | `PlayerController.cs` line 112 + `ObstacleSpawner.cs` kinematic Rigidbody |
| **Pool lifecycle with kinematic Rigidbody** | `ObjectPool.actionOnRelease` calls `SetActive(false)` — Rigidbody is disabled along with the GameObject. `actionOnGet` calls `SetActive(true)` — Rigidbody reactivates. No special handling needed. | `ObstacleSpawner.Awake()` pool setup (lines 140–148) |
| **Scene reload (R key restart)** | `SceneManager.LoadScene` destroys all objects, including pooled obstacles and their Rigidbodies. Fresh scene load creates new pool and new obstacles. No stale state. | `GameManager.Restart()` (line 116) |
| **Baked scene compatibility** | Kinematic Rigidbody is a standard Unity component serialized normally. Baked obstacles in Edit mode have the same component structure as runtime obstacles. | Edit-mode bake via `SceneBaker.cs` |
| **Obstacle child colliders (future)** | Not applicable — obstacles are simple Cube primitives with no children. `GetComponent<Obstacle>()` on `collision.gameObject` (the root) is correct. | N/A |

---

## Extensibility Points (Future, Not Implemented Now)

- **Variable obstacle mass/size**: The kinematic Rigidbody can be configured with different mass values for physics material-based bounce effects, though currently unused.
- **Obstacle-type variants**: If new obstacle types are introduced (e.g., `TallObstacle : Obstacle`), `GetComponent<Obstacle>()` naturally matches all subclasses via polymorphism. No code change needed.
- **Death effects**: The `_isDead = true` block in `OnCollisionEnter` can be extended with particle effects, screen shake, or audio — all within the same callback, without touching the detection logic.

---

## Success Criteria Verification

| Criteria | How Fix Achieves It |
|----------|---------------------|
| (a) Player-obstacle collision triggers Game Over panel with final distance | Kinematic Rigidbody on obstacles ensures `OnCollisionEnter` fires reliably → `GetComponent<Obstacle>()` check passes → `GameManager.GameOver()` invoked → `UIManager.ShowGameOver()` displays panel and distance score |
| (b) Pressing R restarts a fresh game | `UIManager.CheckRestartInput()` detects `InputHandler.RestartPressed` → `GameManager.Restart()` → `SceneManager.LoadScene()` — existing flow, unchanged |
| (c) Zero console errors about undefined tags on startup | `ground.tag = "Ground"` removed from `BuildScene()` — the only source of the "Tag: Ground is not defined" error is eliminated |
