# 3D Endless Runner — Resources & Setup Guide

## Unity Version

- **Required**: Unity 2022.3 LTS or later
- **Recommended**: Unity 6 (6000.0 LTS) or later
- **Render Pipeline**: Universal Render Pipeline (URP) — default in modern Unity templates; the project also works with Built-in Render Pipeline and HDRP (placeholders handle shader fallbacks automatically).

## Required UPM Packages

Both packages are **included by default** in Unity 2022.3+ and Unity 6. No manual package installation is required.

| Package | Role | Notes |
|---|---|---|
| **Input System** (`com.unity.inputsystem`) | Cross-platform input (keyboard, touch) | Installed by default in Unity 6; may need to be added via **Window > Package Manager** in older Unity 2022.3 LTS templates |
| **TextMeshPro** (`com.unity.textmeshpro`) | UI text rendering | Installed by default; provides `TextMeshProUGUI` and default LiberationSans SDF font |

### Checking / Installing Packages

1. Open **Window > Package Manager**
2. In the top-left dropdown, select **Unity Registry**
3. Search for "Input System" — if not listed as "Installed", click **Install**
4. Search for "TextMeshPro" — if not listed as "Installed", click **Install**
5. If prompted to import TMP Essentials, click **Import TMP Essentials** (this adds the default LiberationSans SDF font)

> **Note**: The Input System package enables `UnityEngine.InputSystem` APIs. The default Unity 6 template sets **Active Input Handling** to **Input System Package (New)** automatically. If you see `InvalidOperationException` at runtime about `UnityEngine.Input`, ensure Active Input Handling is set correctly (see **Project Settings > Player > Active Input Handling**). This project uses only the new Input System — never `UnityEngine.Input`.

## How to Run the Game

### Zero-Setup Play (No Manual Scene Setup)

1. Create a **new empty 3D project** in Unity
2. Copy all `Assets/Scripts/` files into your project's `Assets/Scripts/` folder
3. Create an **empty GameObject** in the default scene (e.g., rename "SampleScene" root to "Bootstrapper")
4. Add the `SceneBootstrapper` component to it via **Add Component > SceneBootstrapper**
5. Press **Play**

The bootstrapper will:
- Set physics gravity
- Create the ground plane, player capsule, camera, UI canvas, EventSystem, GameManager, ObstacleSpawner, and lane markers
- Wire all components together
- Destroy itself after building

That's it — **no prefabs, no materials, no scene setup required**.

> **Important**: If Active Input Handling is set to "Both" or "Input System Package (New)", the project works. If set to "Input Manager (Old)", the Input System APIs won't be available. Verify in **Project Settings > Player > Active Input Handling**.

## How to Replace Placeholder Visuals with Real Art

All visual components use the **self-supplying SerializeField pattern**: they expose `[SerializeField]` fields in the Inspector that can be assigned real materials, sprites, or fonts. If a field is left null, the component creates a placeholder at runtime via `Placeholders.CreateMaterial()` or `TMP_Settings.defaultFontAsset`.

### After Baking the Scene

1. Run **Tools > Bake Scene to Hierarchy** (see next section)
2. Save the scene (**Ctrl+S**)
3. Select the baked GameObject in the Hierarchy (e.g., "Player", "ObstacleSpawner")
4. In the Inspector, drag real materials / assets into the exposed fields:

| Component | Field | Placeholder Default | Replace With |
|---|---|---|---|
| `PlayerController` | `_playerMaterial` | Blue (`Color.blue`) | Your player material |
| `ObstacleSpawner` | `_obstacleMaterial` | Red (`Color.red`) | Your obstacle material |
| `CameraFollow` | `_offset`, `_smoothSpeed`, `_lookAheadZ` | Configurable | Tweak camera position / feel |
| `UIManager` | `_scoreText`, `_gameOverPanel`, `_gameOverScoreText` | Self-discovered via GameObject.Find | Assign TMP texts / panel directly |
| `PlayerController` | `_laneDistance`, `_laneSwitchSpeed`, `_jumpForce`, `_groundCheckDistance` | Configurable | Tweak gameplay feel |

## Tools Menu: Bake Scene to Hierarchy

### What is Baking?

The **Bake** feature runs the same `SceneBootstrapper.BuildScene()` logic used at runtime, but in **Edit mode**, so the generated GameObjects become **persistent** in the scene hierarchy. This lets you:
- Inspect and modify generated GameObjects in the Inspector
- Drag real assets into SerializeField slots
- Save the scene with customised objects

### How to Use

1. In the Unity Editor, open the scene that has (or will have) the bootstrapper
2. From the top menu, select **Tools > Bake Scene to Hierarchy**
3. Wait for the build to complete (check the Console for "Scene construction complete")
4. **Save the scene** (**Ctrl+S** or **File > Save**)
5. The GameObjects now exist in the scene and can be edited

### What Happens During Bake

1. A temporary "___Baker" GameObject is created with `SceneBootstrapper` attached
2. `BuildScene()` is called (same code as runtime)
3. The temporary bootstrapper is destroyed
4. The scene is marked dirty (ready to save)

### Notes

- If you bake multiple times, the guard (`FindObjectOfType<GameManager>() != null`) prevents duplicates
- After baking, you can delete the original bootstrapper GameObject (it checks for existing GameManager on next Play and skips building)
- Bake works in Edit mode only. If you're in Play mode, the menu item will show a warning dialog

## How to Create Prefabs from Baked GameObjects

After baking and saving the scene, you can create prefabs from any baked GameObject:

1. In the Hierarchy, select the GameObject you want to prefab (e.g., "Player", "Obstacle")
2. Drag it from the Hierarchy into the **Project window** (into any folder, e.g., `Assets/Prefabs/`)
3. Rename the prefab asset
4. You can now drag the prefab back into the scene or use it in other scenes

> **Note**: Prefabs created this way are optional. The game runs fully without any prefabs — the bootstrapper creates everything at runtime.

## Project File Structure

```
Assets/
  Scripts/
    CameraFollow.cs         — Smooth 3D follow camera
    GameManager.cs          — Singleton state, score, game over/restart
    InputHandler.cs         — Input System polling (keyboard + touch)
    Obstacle.cs             — Per-obstacle scroll & pool return
    ObstacleSpawner.cs      — Object pool, spawn logic, lane selection
    Placeholders.cs         — Runtime primitive + material creation
    PlayerController.cs     — Auto-run, lane switch, jump, collision death
    SceneBootstrapper.cs    — Scene construction (Awake + Bake)
    UIManager.cs            — Score display, Game Over panel
  Editor/
    SceneBaker.cs           — [MenuItem] Tools > Bake Scene to Hierarchy
```

## Troubleshooting

### "Object reference not set to an instance of an object" at runtime
- Ensure **Active Input Handling** is set to **Input System Package (New)** or **Both** in **Project Settings > Player**
- Verify all `.cs` files are in `Assets/Scripts/` with matching class names

### Canvas text shows as pink/missing
- Import **TMP Essentials** (Window > TextMeshPro > Import TMP Essentials)
- This adds the default LiberationSans SDF font asset

### Objects appear in Hierarchy during Play but disappear when Play stops
- This is normal! Runtime-generated objects vanish when exiting Play mode
- Use **Tools > Bake Scene to Hierarchy** to persist them

### "The name 'Keyboard' does not exist in the current context"
- The Input System package is not installed
- Open **Window > Package Manager**, find **Input System** (Unity Registry), and click **Install**

### Two obstacles appear in the same lane consecutively
- The spawner's same-lane avoidance logic prevents this, but the initial `_lastLane = -1` means the first two spawns may overlap. This is a cosmetic edge case with no gameplay impact (the player still dodges or jumps).

### Player falls through the ground
- The ground Plane primitive has a `MeshCollider` by default
- The player Capsule primitive has a `CapsuleCollider` by default
- If this occurs, ensure the ground's tag is "Ground" and the player's Rigidbody has `useGravity = true`

## Extending the Project

### Adding New Obstacle Types
1. Create a new prefab variant or primitive type in `ObstacleSpawner.CreateObstacle()`
2. Add a new `ObjectPool<GameObject>` in `ObstacleSpawner`
3. Select obstacle type randomly when spawning

### Increasing Difficulty Over Time
- `GameManager.ForwardSpeed` is currently constant (8f)
- Modify `GameManager.Update()` to gradually increase `ForwardSpeed` over time

### Mobile Touch Controls
- `InputHandler` already supports touch input (swipe left/right/up, tap to restart)
- No additional configuration needed — works out of the box on mobile devices
