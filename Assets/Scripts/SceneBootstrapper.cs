using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Single-source-of-truth MonoBehaviour that constructs the full playable 3D Endless Runner
/// scene at runtime. Called both from Awake() — enabling "press Play and play" — and from
/// the Editor Bake menu (Tools > Bake Scene to Hierarchy) in Edit mode to persist objects.
///
/// BuildScene() creates and wires ALL GameObjects, components, and references. Every new
/// component that must exist in the running scene is created here; no component relies on
/// Awake-order assumptions (GameManager uses [DefaultExecutionOrder(-100)] for safety).
///
/// After building in Play mode, the bootstrapper destroys itself. In Edit mode (bake),
/// the caller (SceneBaker) destroys the temporary bootstrapper GameObject.
/// </summary>
public class SceneBootstrapper : MonoBehaviour
{
    // --- Serialized Fields ---

    /// <summary>
    /// If true, BuildScene() is called automatically in Awake().
    /// Set to false when invoking BuildScene() from the Editor Bake menu
    /// to avoid double-build when the temporary bootstrapper GameObject is created.
    /// </summary>
    [SerializeField] private bool _buildOnAwake = true;

    // --- Unity Lifecycle ---

    private void Awake()
    {
        if (_buildOnAwake)
        {
            BuildScene();
        }
    }

    // --- Public API ---

    /// <summary>
    /// Constructs the full playable scene. Idempotent: if a GameManager already exists
    /// in the scene (from a previous build or bake), the method returns immediately.
    ///
    /// Called by:
    /// - Awake() when _buildOnAwake is true (Play mode, zero setup)
    /// - SceneBaker Editor menu item (Edit mode, to persist objects for asset replacement)
    /// </summary>
    public void BuildScene()
    {
        // Guard: if GameManager already exists, scene is already built
        if (FindObjectOfType<GameManager>() != null)
        {
            Debug.Log("SceneBootstrapper.BuildScene: GameManager already exists — scene already built. Skipping.");
            return;
        }

        Debug.Log("SceneBootstrapper.BuildScene: Building playable scene...");

        // --- 1. Physics ---
        Physics.gravity = new Vector3(0f, -25f, 0f);

        // --- 2. Ground Plane ---
        GameObject ground = Placeholders.CreatePrimitive(
            PrimitiveType.Plane,
            new Color(0.3f, 0.3f, 0.3f),
            "Ground"
        );
        ground.transform.position = new Vector3(0f, 0f, 10f);
        ground.transform.localScale = new Vector3(3f, 1f, 20f);
        ground.tag = "Ground";

        // --- 3. Player Capsule ---
        GameObject player = Placeholders.CreatePrimitive(
            PrimitiveType.Capsule,
            Color.blue,
            "Player"
        );
        player.transform.position = new Vector3(0f, 1f, 0f);
        player.tag = "Player";

        // Rigidbody for physics-based auto-run and jumping
        Rigidbody rb = player.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.drag = 0f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Input and control components
        player.AddComponent<InputHandler>();
        player.AddComponent<PlayerController>();

        // --- 4. Main Camera ---
        // Use existing MainCamera if present; otherwise create one
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGo = new GameObject("MainCamera");
            cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        // Position behind and above the player
        cam.transform.position = player.transform.position + new Vector3(0f, 5f, -8f);

        // Add the smooth follow component
        cam.gameObject.AddComponent<CameraFollow>();

        // --- 5. UI Canvas ---
        GameObject uiGo = new GameObject("UI");

        // Canvas (Screen Space Overlay)
        Canvas canvas = uiGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Canvas Scaler (responsive UI)
        CanvasScaler scaler = uiGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Graphic Raycaster (needed for UI interaction, even if minimal)
        uiGo.AddComponent<GraphicRaycaster>();

        // --- Score Text (top-left corner) ---
        GameObject scoreTextGo = new GameObject("ScoreText");
        scoreTextGo.transform.SetParent(uiGo.transform, false);

        TextMeshProUGUI scoreText = scoreTextGo.AddComponent<TextMeshProUGUI>();
        scoreText.text = "Distance: 0m";
        scoreText.fontSize = 36f;
        scoreText.color = Color.white;
        scoreText.alignment = TextAlignmentOptions.TopLeft;

        // Use default TMP font if available
        TryAssignDefaultFont(scoreText);

        // Anchor to top-left corner
        RectTransform scoreRt = scoreTextGo.GetComponent<RectTransform>();
        scoreRt.anchorMin = new Vector2(0f, 1f);
        scoreRt.anchorMax = new Vector2(0f, 1f);
        scoreRt.pivot = new Vector2(0f, 1f);
        scoreRt.anchoredPosition = new Vector2(20f, -20f);

        // --- Game Over Panel (initially hidden) ---
        GameObject panelGo = new GameObject("GameOverPanel");
        panelGo.transform.SetParent(uiGo.transform, false);

        // Dark semi-transparent background
        Image panelImage = panelGo.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.7f);

        // Stretch to fill entire screen
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.sizeDelta = Vector2.zero;

        // Start hidden
        panelGo.SetActive(false);

        // --- Game Over Title ---
        GameObject titleGo = new GameObject("GameOverTitle");
        titleGo.transform.SetParent(panelGo.transform, false);

        TextMeshProUGUI titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.text = "Game Over!";
        titleText.fontSize = 48f;
        titleText.color = Color.white;
        titleText.alignment = TextAlignmentOptions.Center;
        TryAssignDefaultFont(titleText);

        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = new Vector2(0f, 40f);

        // --- Game Over Score Text ---
        GameObject scoreTextGo2 = new GameObject("GameOverScoreText");
        scoreTextGo2.transform.SetParent(panelGo.transform, false);

        TextMeshProUGUI gameOverScoreText = scoreTextGo2.AddComponent<TextMeshProUGUI>();
        gameOverScoreText.text = "";
        gameOverScoreText.fontSize = 32f;
        gameOverScoreText.color = Color.white;
        gameOverScoreText.alignment = TextAlignmentOptions.Center;
        TryAssignDefaultFont(gameOverScoreText);

        RectTransform scoreRt2 = scoreTextGo2.GetComponent<RectTransform>();
        scoreRt2.anchorMin = new Vector2(0.5f, 0.5f);
        scoreRt2.anchorMax = new Vector2(0.5f, 0.5f);
        scoreRt2.pivot = new Vector2(0.5f, 0.5f);
        scoreRt2.anchoredPosition = new Vector2(0f, -10f);

        // --- Restart Prompt ---
        GameObject promptGo = new GameObject("RestartPrompt");
        promptGo.transform.SetParent(panelGo.transform, false);

        TextMeshProUGUI promptText = promptGo.AddComponent<TextMeshProUGUI>();
        promptText.text = "Press R to restart";
        promptText.fontSize = 28f;
        promptText.color = Color.gray;
        promptText.alignment = TextAlignmentOptions.Center;
        TryAssignDefaultFont(promptText);

        RectTransform promptRt = promptGo.GetComponent<RectTransform>();
        promptRt.anchorMin = new Vector2(0.5f, 0.5f);
        promptRt.anchorMax = new Vector2(0.5f, 0.5f);
        promptRt.pivot = new Vector2(0.5f, 0.5f);
        promptRt.anchoredPosition = new Vector2(0f, -60f);

        // --- UIManager (attached to the Canvas root) ---
        // The UIManager self-discovers ScoreText, GameOverPanel, and GameOverScoreText
        // references via GameObject.Find in its Start() — no manual wiring needed.
        uiGo.AddComponent<UIManager>();

        // --- 6. EventSystem ---
        // Required for UI raycasting (GraphicRaycaster needs an EventSystem in the scene)
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();
        }

        // --- 7. GameManager ---
        GameObject gmGo = new GameObject("GameManager");
        gmGo.AddComponent<GameManager>();

        // --- 8. ObstacleSpawner ---
        GameObject spawnerGo = new GameObject("ObstacleSpawner");
        spawnerGo.AddComponent<ObstacleSpawner>();

        // --- 9. Lane Markers (optional visual guides) ---
        CreateLaneMarkers();

        // --- 10. Self-destruct in Play mode ---
        if (Application.isPlaying)
        {
            Destroy(gameObject);
        }

        Debug.Log("SceneBootstrapper.BuildScene: Scene construction complete.");
    }

    // --- Private Helpers ---

    /// <summary>
    /// Creates small cylinder markers along the ground to visually indicate the three lanes.
    /// Markers are placed at five Z positions (5, 10, 15, 20, 25) for each lane X offset.
    /// </summary>
    private void CreateLaneMarkers()
    {
        // Three lanes at X offsets
        float[] laneOffsets = { -2.5f, 0f, 2.5f };
        // Z positions along the ground
        float[] zPositions = { 5f, 10f, 15f, 20f, 25f };

        Color markerColor = new Color(0.15f, 0.15f, 0.15f);

        foreach (float x in laneOffsets)
        {
            foreach (float z in zPositions)
            {
                GameObject marker = Placeholders.CreatePrimitive(
                    PrimitiveType.Cylinder,
                    markerColor,
                    "LaneMarker"
                );
                marker.transform.localScale = new Vector3(0.2f, 0.05f, 0.2f);
                marker.transform.position = new Vector3(x, 0.025f, z);
            }
        }
    }

    /// <summary>
    /// Attempts to assign the default TMP font to a TextMeshProUGUI component.
    /// Falls back gracefully if no font is available.
    /// </summary>
    /// <param name="text">The TMP text component to assign a font to.</param>
    private static void TryAssignDefaultFont(TMP_Text text)
    {
        if (text == null)
            return;

        // Prefer TMP_Settings.defaultFontAsset (available in Unity 2022.3+)
        if (TMP_Settings.defaultFontAsset != null)
        {
            text.font = TMP_Settings.defaultFontAsset;
            return;
        }

        // Fallback: try loading the LiberationSans SDF font that ships with TMP Essentials
        TMP_FontAsset fallbackFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (fallbackFont != null)
        {
            text.font = fallbackFont;
            return;
        }

        // If no font is available, TMP will use its internal fallback — still renders.
        Debug.LogWarning("SceneBootstrapper.TryAssignDefaultFont: No default TMP font found. " +
                         "Text will render with fallback font.");
    }
}
