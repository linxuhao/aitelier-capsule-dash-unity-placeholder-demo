using UnityEngine;
using TMPro;

/// <summary>
/// Manages the runtime UI: displays the running distance score and toggles a
/// Game Over panel with restart prompt.
///
/// Attached to the "UI" Canvas GameObject by SceneBootstrapper.BuildScene().
///
/// Subscribes to GameManager.OnGameOver and GameManager.OnRestart events to
/// show/hide the Game Over panel. Uses a lazy re-subscription pattern
/// (TrySubscribeEvents) that retries each frame until subscription succeeds,
/// handling the edge case where GameManager.Instance is momentarily null
/// during Start() in baked scenes.
///
/// Polls InputHandler.RestartPressed (via FindObjectOfType) to detect restart
/// input when the game is over.
///
/// Uses the self-supplying SerializeField pattern: fields can be assigned by
/// the bootstrapper or discovered via GameObject.Find in Start().
/// </summary>
public class UIManager : MonoBehaviour
{
    // --- Serialized Fields (Self-Supplying) ---

    /// <summary>
    /// Reference to the TextMeshProUGUI component that displays the running
    /// distance score. If null at Start, discovered via GameObject.Find("ScoreText").
    /// </summary>
    [SerializeField] private TextMeshProUGUI _scoreText;

    /// <summary>
    /// The root GameObject of the Game Over panel (contains score text and
    /// restart prompt child objects). Initially inactive. If null at Start,
    /// discovered via GameObject.Find("GameOverPanel").
    /// </summary>
    [SerializeField] private GameObject _gameOverPanel;

    /// <summary>
    /// Reference to the TextMeshProUGUI within the Game Over panel that shows
    /// the final score. If null at Start, discovered via
    /// _gameOverPanel.transform.Find("GameOverScoreText").
    /// </summary>
    [SerializeField] private TextMeshProUGUI _gameOverScoreText;

    // --- Runtime State ---

    /// <summary>
    /// Cached reference to an InputHandler in the scene, used to poll for
    /// RestartPressed when the game is over.
    /// </summary>
    private InputHandler _inputHandler;

    /// <summary>
    /// Guard flag indicating whether GameManager events have been successfully
    /// subscribed. Defaults to false. Set to true in TrySubscribeEvents() once
    /// subscription completes. Resets to false on scene load (new UIManager instance).
    /// </summary>
    private bool _eventsSubscribed;

    // --- Unity Lifecycle ---

    private void Start()
    {
        // Self-supply: discover references if not assigned by the bootstrapper
        if (_scoreText == null)
        {
            GameObject scoreGO = GameObject.Find("ScoreText");
            if (scoreGO != null)
            {
                _scoreText = scoreGO.GetComponent<TextMeshProUGUI>();
            }
        }

        if (_gameOverPanel == null)
        {
            _gameOverPanel = GameObject.Find("GameOverPanel");
        }

        if (_gameOverScoreText == null && _gameOverPanel != null)
        {
            Transform child = _gameOverPanel.transform.Find("GameOverScoreText");
            if (child != null)
            {
                _gameOverScoreText = child.GetComponent<TextMeshProUGUI>();
            }
        }

        // Detect an InputHandler in the scene for restart polling
        _inputHandler = FindObjectOfType<InputHandler>();

        // Attempt event subscription (may fail if GameManager is not yet ready
        // in baked scenes — Update() will retry each frame until it succeeds).
        TrySubscribeEvents();

        // Start with the Game Over panel hidden
        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(false);
        }
    }

    private void Update()
    {
        // Lazy re-subscription: if events weren't subscribed in Start()
        // (e.g., GameManager.Instance was null during Start in a baked scene),
        // retry each frame until subscription succeeds.
        if (!_eventsSubscribed)
        {
            TrySubscribeEvents();
        }

        // Guard: ensure GameManager exists
        if (GameManager.Instance == null)
        {
            return;
        }

        // While the game is playing, update the distance score text
        if (!GameManager.Instance.IsGameOver)
        {
            UpdateScoreText();
        }
        else
        {
            // When game is over, check for restart input via InputHandler
            CheckRestartInput();
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver -= ShowGameOver;
            GameManager.Instance.OnRestart -= HideGameOver;
        }
    }

    // --- Event Subscription ---

    /// <summary>
    /// Idempotent event subscription method. Attempts to subscribe to
    /// GameManager.OnGameOver and GameManager.OnRestart if not yet subscribed.
    /// Returns immediately if already subscribed or if GameManager.Instance
    /// is null. Logs a warning when the singleton is unavailable so developers
    /// are informed of ordering issues.
    ///
    /// Designed to be called from Start() and re-called from Update() in a
    /// lazy retry pattern, ensuring subscription succeeds even when
    /// GameManager.Instance is momentarily null during Start() in baked scenes.
    /// </summary>
    private void TrySubscribeEvents()
    {
        if (_eventsSubscribed)
            return;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameOver += ShowGameOver;
            GameManager.Instance.OnRestart += HideGameOver;
            _eventsSubscribed = true;
        }
        else
        {
            Debug.LogWarning("UIManager.TrySubscribeEvents: GameManager.Instance is null. " +
                             "Retrying on next frame. Ensure GameManager component exists in the scene.");
        }
    }

    // --- Public Event Handlers (called by GameManager events) ---

    /// <summary>
    /// Shows the Game Over panel and displays the final distance score.
    /// Called by GameManager.OnGameOver event.
    /// </summary>
    public void ShowGameOver()
    {
        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(true);
        }

        if (_gameOverScoreText != null && GameManager.Instance != null)
        {
            _gameOverScoreText.text = $"Game Over!\nDistance: {GameManager.Instance.Distance:F0}m\nPress R to restart";
        }
    }

    /// <summary>
    /// Hides the Game Over panel. Called by GameManager.OnRestart event
    /// (fires before scene reload, providing visual cleanup).
    /// </summary>
    public void HideGameOver()
    {
        if (_gameOverPanel != null)
        {
            _gameOverPanel.SetActive(false);
        }
    }

    // --- Private Helpers ---

    /// <summary>
    /// Updates the distance score text on the HUD with the current running distance.
    /// </summary>
    private void UpdateScoreText()
    {
        if (_scoreText != null && GameManager.Instance != null)
        {
            _scoreText.text = $"Distance: {GameManager.Instance.Distance:F0}m";
        }
    }

    /// <summary>
    /// Checks if the player has pressed the restart key (via any InputHandler
    /// in the scene) and triggers a restart if so.
    ///
    /// UIManager is on a separate GameObject from the player's InputHandler,
    /// so we use FindObjectOfType to locate any active InputHandler.
    /// </summary>
    private void CheckRestartInput()
    {
        if (_inputHandler == null)
        {
            // Re-acquire if the reference was lost (e.g., after re-creation)
            _inputHandler = FindObjectOfType<InputHandler>();
        }

        if (_inputHandler != null && _inputHandler.RestartPressed)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Restart();
            }
        }
    }
}
