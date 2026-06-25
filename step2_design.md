# Technical Architecture Design — Fix Obstacle Collision Death Detection (Tag → Component Check)

## Overview

This design addresses a single bug in the Capsule Dash 3D endless runner: the player fails to die on obstacle collision because the `"Obstacle"` Unity tag is not registered in the project's `TagManager.asset` (`tags: []` — zero custom tags). The fix replaces tag-based collision detection (`CompareTag("Obstacle")`) with component-based detection (`GetComponent<Obstacle>() != null`), which is already guaranteed to work because every obstacle has the `Obstacle` MonoBehaviour attached by `ObstacleSpawner.CreateObstacle()`.

The fix is a **one-line change** in `PlayerController.OnCollisionEnter()`, plus an **optional one-line cleanup** in `ObstacleSpawner.CreateObstacle()` to remove the dead-code tag assignment that generates the `"Tag: Obstacle is not defined"` console error.

No new files, no new components, no SceneBootstrapper changes, no asset changes. Two existing files are modified.

---

## Architecture Diagram (Text)

```
┌──────────────────────────────────────────────────────────────────────┐
│                       SceneBootstrapper                              │
│  BuildScene(): wires Player + ObstacleSpawner + GameManager          │
│  (NO CHANGE — already correctly attaches all components)             │
└───────┬──────────────────────────────────────────────────────────────┘
        │
        ▼
┌──────────────────────────────────────────────────────────────────────┐
│  ┌────────────────────┐    ┌────────────────────┐                    │
│  │     Player         │    │  ObstacleSpawner   │                    │
│  │   (Capsule)        │    │  (standalone GO)   │                    │
│  │  ┌───────────────┐ │    │  ┌───────────────┐ │                    │
│  │  │ PlayerControl │ │    │  │ ObjectPool    │ │                    │
│  │  │               │ │    │  │ <GameObject>  │ │                    │
│  │  │ OnCollision   │ │    │  └───────┬───────┘ │                    │
│  │  │ Enter()       │ │    │          │          │                    │
│  │  │  ┌──────────┐ │ │    │  CreateObstacle()  │                    │
│  │  │  │ FIX:     │ │ │    │   ┌──────────────┐ │                    │
│  │  │  │ GetComp  │─┼─┼────┼──→│ Obstacle     │ │                    │
│  │  │  │ onent<>  │ │ │    │   │ component    │ │                    │
│  │  │  │ instead  │ │ │    │   └──────────────┘ │                    │
│  │  │  │ of       │ │ │    │   ┌──────────────┐ │                    │
│  │  │  │ Compare  │ │ │    │   │ CLEANUP:     │ │                    │
│  │  │  │ Tag()    │ │ │    │   │ remove tag=  │ │                    │
│  │  │  └──────────┘ │ │    │   │ "Obstacle"   │ │                    │
│  │  └───────────────┘ │    │   └──────────────┘ │                    │
│  └────────┬───────────┘    └────────────────────┘                    │
│           │                                                           │
│  ┌────────┴──────────────────────────────────────────────────┐       │
│  │                    GameManager                             │       │
│  │  GameOver() → OnGameOver event → UIManager                 │       │
│  │  (NO CHANGE)                                               │       │
│  └───────────────────────────────────────────────────────────┘       │
└──────────────────────────────────────────────────────────────────────┘
```

### Data Flow (Collision → Death)

```
ObstacleSpawner.CreateObstacle()
  └─ Placeholders.CreatePrimitive(Cube, red, "Obstacle")
  └─ cube.AddComponent<Obstacle>()          ← Guarantees Obstacle component exists
  └─ [cube.tag = "Obstacle";]               ← REMOVED (dead code, generates error)

PlayerController.OnCollisionEnter(Collision collision)
  ├─ if (_isDead) return;                   ← Existing double-trigger guard
  ├─ OLD: if (collision.gameObject.CompareTag("Obstacle"))
  │        → Always returns false (tag not registered in TagManager)
  │
  └─ NEW: if (collision.gameObject.GetComponent<Obstacle>() != null)
           → Returns true for obstacles (component exists)
           → Returns false for ground/walls/lane-markers (no component)
     ├─ _isDead = true;
     ├─ renderer.material.color = Color.red;  ← Existing death visual
     └─ GameManager.Instance.GameOver();      ← Existing death notification
```

---

## File Structure

```
Assets/
  Scripts/
    PlayerController.cs       — (MODIFY) Line 204: CompareTag → GetComponent<Obstacle>
    ObstacleSpawner.cs        — (MODIFY) Line 163: remove cube.tag = "Obstacle"
    Obstacle.cs               — (NO CHANGE) Existing MonoBehaviour, used for GetComponent check
    GameManager.cs            — (NO CHANGE)
    SceneBootstrapper.cs      — (NO CHANGE)
    Placeholders.cs           — (NO CHANGE)
    InputHandler.cs           — (NO CHANGE)
    CameraFollow.cs           — (NO CHANGE)
    UIManager.cs              — (NO CHANGE)
    WindEffect.cs             — (NO CHANGE)
  Editor/
    SceneBaker.cs             — (NO CHANGE)
```

---

## Component Specifications

### 1. PlayerController (MODIFY — Collision Detection Fix)

**File**: `Assets/Scripts/PlayerController.cs`

**Change**: One-line replacement in `OnCollisionEnter()`, at line 204.

**Before** (line 204):
```csharp
if (collision.gameObject.CompareTag("Obstacle"))
```

**After**:
```csharp
if (collision.gameObject.GetComponent<Obstacle>() != null)
```

**Rationale**:
- `CompareTag("Obstacle")` requires the tag to be registered in `ProjectSettings/TagManager.asset`. The project's TagManager has `tags: []` — the tag is unregistered, so `CompareTag()` always returns `false`.
- `GetComponent<Obstacle>()` is a pure C# type-check against the `Obstacle` MonoBehaviour. It requires no Unity Editor configuration, no tag registration, and works identically in Play mode, baked scenes, and built players.
- Every obstacle cube already has the `Obstacle` component attached — guaranteed by `ObstacleSpawner.CreateObstacle()` calling `cube.AddComponent<Obstacle>()` immediately after primitive creation (line 164).
- Ground (`"Ground"` tag), lane markers, walls, and the player itself have no `Obstacle` component, so `GetComponent<Obstacle>()` naturally returns `null` for them. No false-positive death triggers.
- The existing `_isDead` guard (line 200) and death behavior (lines 206–219: material color change to red, `GameManager.GameOver()`) are preserved unchanged.

**No other changes to PlayerController.** Lane switching, jumping, ground check, Rigidbody configuration (including CCD which was added in a prior fix), and the debug log line remain as-is.

### 2. ObstacleSpawner (MODIFY — Optional Cleanup)

**File**: `Assets/Scripts/ObstacleSpawner.cs`

**Change**: Remove the tag assignment on line 163.

**Before** (lines 161–165):
```csharp
Color color = _obstacleMaterial != null ? _obstacleMaterial.color : Color.red;
GameObject cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, color, "Obstacle");
cube.tag = "Obstacle";
cube.AddComponent<Obstacle>();
return cube;
```

**After**:
```csharp
Color color = _obstacleMaterial != null ? _obstacleMaterial.color : Color.red;
GameObject cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, color, "Obstacle");
cube.AddComponent<Obstacle>();
return cube;
```

**Rationale**:
- Once `PlayerController` no longer uses `CompareTag("Obstacle")`, setting the tag is dead code.
- The `cube.tag = "Obstacle"` assignment is the sole source of the `"Tag: Obstacle is not defined"` console error. Removing it silences the error entirely.
- A grep of the codebase confirms no other script references the `"Obstacle"` tag — neither `GameObject.FindGameObjectWithTag("Obstacle")` nor any `CompareTag("Obstacle")` remain after the PlayerController fix.
- This cleanup is **optional** within the project goals (the brief only requires changing PlayerController), but strongly recommended for a clean console.

---

## Component Interaction Matrix (Unchanged)

| From → To | GameManager | PlayerController | ObstacleSpawner | Obstacle | UIManager |
|------------|:---:|:---:|:---:|:---:|:---:|
| **GameManager** | — | — | — | — | events |
| **PlayerController** | `.IsGameOver` `.GameOver()` | — | — | `GetComponent<Obstacle>()` | — |
| **ObstacleSpawner** | `.IsGameOver` `.ForwardSpeed` | `.position.z` | — | `.Configure()` | — |
| **Obstacle** | `.ForwardSpeed` (cached) | `.position.z` | — | — | — |
| **UIManager** | `.Distance` `.OnGameOver` `.OnRestart` | — | — | — | — |

The only change: `PlayerController → Obstacle` now uses `GetComponent<Obstacle>()` (component type reference) instead of `CompareTag("Obstacle")` (string-based tag lookup). This is a compile-time dependency on the `Obstacle` class — same as the existing dependency on `GameManager`, `InputHandler`, and `Placeholders`.

---

## Integration in SceneBootstrapper.BuildScene()

**No changes needed.** The `SceneBootstrapper.BuildScene()` method already:

1. Creates the Player capsule with `PlayerController` attached (lines 77–94). `PlayerController.Awake()` configures the Rigidbody and self-supplies materials — the `OnCollisionEnter` change is internal to the component.
2. Creates the `ObstacleSpawner` (lines 241–242). `ObstacleSpawner.Awake()` creates the object pool — the tag-removal in `CreateObstacle()` is internal to the spawner.

Both the runtime path (`Awake()` → `BuildScene()`) and the Editor bake path (`Tools > Bake Scene to Hierarchy` → `BuildScene()`) call the same `BuildScene()` method, so the fix works identically in both modes without any bootstrapper changes.

---

## Technical Stack

| Concern | Technology | Rationale |
|---------|-----------|-----------|
| Engine | Unity 6 (6000.0+) | Required by project |
| Language | C# (pure scripts) | Project constraint |
| Collision detection | `MonoBehaviour.OnCollisionEnter(Collision)` | Existing callback, unchanged |
| Death condition | `GameObject.GetComponent<Obstacle>() != null` | Replaces `CompareTag("Obstacle")` — zero-config, works everywhere |
| Death response | `GameManager.Instance.GameOver()` + material color change | Existing behavior, preserved |
| Obstacle identification | `Obstacle` MonoBehaviour (marker component) | Already attached to every pooled obstacle |

---

## Bug Fix Traceability

| Issue | Root Cause | Fix | File(s) Changed | Lines |
|-------|-----------|-----|-----------------|-------|
| Player doesn't die on obstacle collision | `CompareTag("Obstacle")` always returns `false` because the `"Obstacle"` tag is not registered in `ProjectSettings/TagManager.asset` (`tags: []`). The tag assignment `cube.tag = "Obstacle"` in `CreateObstacle()` silently fails at runtime. | Replace `CompareTag("Obstacle")` with `GetComponent<Obstacle>() != null` — a type-based check that requires no tag registration. | `PlayerController.cs` | 204 |
| Console error: "Tag: Obstacle is not defined" | `ObstacleSpawner.CreateObstacle()` sets `cube.tag = "Obstacle"` on every spawned cube, but the tag doesn't exist in TagManager. | Remove the `cube.tag = "Obstacle"` line — dead code after the PlayerController fix. | `ObstacleSpawner.cs` | 163 |

---

## Edge Cases Addressed

| Edge Case | Mitigation | Where |
|-----------|-----------|-------|
| Double-death trigger (simultaneous collisions) | Existing `_isDead` guard at top of `OnCollisionEnter` — returns immediately if already dead | `PlayerController.cs` line 200 |
| Ground collision false-positive | Ground plane has no `Obstacle` component → `GetComponent<Obstacle>()` returns `null` → death not triggered | Implicit via component absence |
| Lane marker collision false-positive | Lane markers are Cylinder primitives with no `Obstacle` component → `GetComponent<Obstacle>()` returns `null` | Implicit via component absence |
| Obstacle pool guarantees component presence | `CreateObstacle()` calls `cube.AddComponent<Obstacle>()` unconditionally on every new pooled object | `ObstacleSpawner.cs` line 164 |
| Obstacle child colliders (future) | Not applicable — obstacles are simple Cube primitives with no children. If future obstacles had children with colliders, `GetComponentInParent<Obstacle>()` would be needed on `collision.gameObject`. Current design uses `GetComponent<Obstacle>()` on `collision.gameObject` (the root), which is correct for the current obstacle structure. | N/A (future concern) |
| Baked scene compatibility | The fix uses only `GetComponent<T>()`, a core Unity API that works identically in Edit mode and Play mode. Baked scenes have the same component structure as runtime-built scenes. | `PlayerController.cs` |
| Player Rigidbody CCD already enabled | A prior fix already added `_rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic` (line 112). This prevents bullet-through-paper tunneling at high speeds — a separate concern from the tag check, but complementary. | `PlayerController.cs` line 112 (existing) |

---

## Extensibility Points (Future, Not Implemented Now)

- **Obstacle-type variants**: If new obstacle types are introduced (e.g., `TallObstacle : Obstacle`), `GetComponent<Obstacle>()` naturally matches all subclasses via polymorphism. No code change needed.
- **Death effects**: The `_isDead = true` block in `OnCollisionEnter` can be extended with particle effects, screen shake, or audio — all within the same callback, without touching the detection logic.
- **Non-obstacle kill triggers**: If a new hazard type uses a different component (e.g., `SpikeTrap`), add an `else if (collision.gameObject.GetComponent<SpikeTrap>() != null)` branch.

---

## RESOURCES.md Update Notes

The existing `RESOURCES.md` already documents the obstacle spawning architecture. No structural changes are needed — the component-based detection is an internal implementation detail transparent to users replacing materials or creating prefabs.

One note to add to the Troubleshooting section (optional):

> ### Player doesn't die on obstacle collision
> - This was fixed in the latest update. Obstacle detection now uses component-based checking (`GetComponent<Obstacle>()`) instead of tag-based checking (`CompareTag("Obstacle")`). Ensure `ObstacleSpawner.CreateObstacle()` calls `AddComponent<Obstacle>()` on every spawned cube.

---

## Linter Manifest

Only `.cs` files are modified in this project. C# compilation is handled automatically by the Unity build system. The existing `linter_manifest.json` covers non-C# files (`.md`, `.json`) with `basic` linting — no changes needed.
