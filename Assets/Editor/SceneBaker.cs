#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO; // For Directory.CreateDirectory belt-and-suspenders

/// <summary>
/// Editor-only static class that provides the "Tools / Bake Scene to Hierarchy" menu item.
///
/// Calls SceneBootstrapper.BuildScene() in Edit mode to persist the runtime-generated
/// GameObjects (player, ground, camera, UI, obstacles, etc.) into the scene hierarchy.
/// After baking, the scene can be saved (Ctrl+S) and the generated objects can be
/// manually edited, have assets replaced via Inspector slots, or be used as a starting
/// point for further scene authoring — all without any binary/prefab assets.
///
/// The Bake menu and the runtime "press Play and play" path both use the same
/// SceneBootstrapper.BuildScene() method — a single source of truth for scene composition.
/// </summary>
public static class SceneBaker
{
    private const string MENU_PATH = "Tools/Bake Scene to Hierarchy";
    private const string TEMP_NAME = "___Baker";

    /// <summary>
    /// Entry point for the Editor menu item "Tools > Bake Scene to Hierarchy".
    ///
    /// Flow:
    /// 1. Guard against Play mode — show dialog if in Play mode.
    /// 2. Guard against already-baked scene — warn if GameManager exists.
    /// 3. Ensure the Assets/Editor/ directory exists (belt-and-suspenders).
    /// 4. Show confirmation dialog to prevent accidental bake.
    /// 5. Find existing SceneBootstrapper in scene, or create a temporary one.
    /// 6. Disable auto-build on the bootstrapper via SerializedObject.
    /// 7. Call bootstrapper.BuildScene() to construct all GameObjects.
    /// 8. Destroy the temporary bootstrapper GameObject if we created one.
    /// 9. Mark the scene dirty so the user knows to save.
    /// 10. Log a success message.
    /// </summary>
    [MenuItem(MENU_PATH)]
    public static void BakeScene()
    {
        // --- 1. Play-mode guard ---
        // BuildScene() creates persistent GameObjects in Edit mode only.
        // In Play mode, objects created would be transient and destroyed on exit.
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Bake Scene",
                "Cannot bake while in Play mode.\nPlease stop Play mode first.",
                "OK"
            );
            return;
        }

        // --- 2. Already-baked guard ---
        // SceneBootstrapper.BuildScene() has its own idempotent guard that silently
        // returns if GameManager exists. We add an explicit dialog so the user
        // understands why nothing happened.
        if (Object.FindObjectOfType<GameManager>() != null)
        {
            EditorUtility.DisplayDialog(
                "Bake Scene",
                "Scene already contains baked objects.\n" +
                "Clear the scene (File > New Scene or delete baked objects) before re-baking.",
                "OK"
            );
            return;
        }

        // --- 3. Ensure the Assets/Editor/ directory exists ---
        // In a fresh project, this folder might not exist yet. Directory.CreateDirectory
        // is idempotent — it does nothing if the folder already exists. This is a
        // defensive belt-and-suspenders measure.
        Directory.CreateDirectory("Assets/Editor");

        // --- 4. Confirmation dialog ---
        // Prevents accidental bake from a mis-click on the menu item.
        bool proceed = EditorUtility.DisplayDialog(
            "Bake Scene",
            "This will build all runtime GameObjects into the scene hierarchy.\n" +
            "Proceed?",
            "Bake",
            "Cancel"
        );
        if (!proceed) return;

        // --- 5. Find or create a temporary SceneBootstrapper ---
        // If there's already a SceneBootstrapper in the scene (e.g., user added one
        // to an empty scene but hasn't played yet), reuse it. Otherwise create a
        // temporary one solely for the bake call.
        SceneBootstrapper bootstrapper = Object.FindObjectOfType<SceneBootstrapper>();
        bool createdTemp = false;

        if (bootstrapper == null)
        {
            GameObject go = new GameObject(TEMP_NAME);
            bootstrapper = go.AddComponent<SceneBootstrapper>();
            createdTemp = true;
        }

        // --- 6. Disable auto-build via SerializedObject ---
        // The _buildOnAwake field is [SerializeField] private. Using SerializedObject
        // is cleaner than raw reflection and works with Unity's serialization system.
        SerializedObject so = new SerializedObject(bootstrapper);
        SerializedProperty buildProp = so.FindProperty("_buildOnAwake");
        if (buildProp != null)
        {
            buildProp.boolValue = false;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[SceneBaker] Could not find _buildOnAwake field on SceneBootstrapper. " +
                             "Proceeding anyway — Awake may double-build.");
        }

        // --- 7. Build the scene ---
        // This creates all GameObjects (ground, player, camera, UI, GameManager,
        // ObstacleSpawner, lane markers, EventSystem) in Edit mode. Because we are
        // NOT in Play mode, these objects become persistent in the scene hierarchy
        // and can be saved.
        bootstrapper.BuildScene();

        // --- 8. Clean up temporary bootstrapper ---
        // If we created a temporary GameObject just for baking, destroy it now.
        // The built objects remain in the scene.
        if (createdTemp && bootstrapper != null && bootstrapper.gameObject != null)
        {
            Object.DestroyImmediate(bootstrapper.gameObject);
        }

        // --- 9. Mark scene dirty ---
        // This causes the asterisk to appear in the Hierarchy tab title, reminding
        // the user to save (Ctrl+S) to persist the baked objects.
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        // --- 10. Log success ---
        Debug.Log("[SceneBaker] Scene baked successfully. Save the scene (Ctrl+S) to persist.");
    }
}
#endif
