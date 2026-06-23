using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central singleton MonoBehaviour that owns game state (Playing/GameOver),
/// tracks the running distance score, and orchestrates GameOver / Restart flow.
///
/// Marked with [DefaultExecutionOrder(-100)] so Awake runs before all other
/// components, eliminating Awake-order bugs in both Play mode and baked scenes.
///
/// No DontDestroyOnLoad — scene reload handles cleanup naturally.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    // --- Singleton ---

    /// <summary>
    /// Global singleton instance. Accessed by PlayerController, ObstacleSpawner,
    /// Obstacle, UIManager, and CameraFollow. Guaranteed to be initialized by
    /// the time other components' Awake runs (due to execution order -100).
    /// </summary>
    public static GameManager Instance { get; private set; }

    // --- State ---

    /// <summary>
    /// Total distance the player has run since the start of the current run,
    /// in world units. Accumulated each frame while the game is playing.
    /// </summary>
    public float Distance { get; private set; }

    /// <summary>
    /// Whether the current run has ended. True after GameOver() is called,
    /// false at the start of a run. Once true, distance stops accumulating
    /// and GameOver() becomes a no-op.
    /// </summary>
    public bool IsGameOver { get; private set; }

    // --- Speed Constant ---

    /// <summary>
    /// Single source of truth for forward auto-run speed (world units per second).
    /// Consumed by PlayerController (velocity), ObstacleSpawner (spawn intervals),
    /// and Obstacle (scroll speed). Currently constant at 8f.
    /// </summary>
    public float ForwardSpeed => 8f;

    // --- Events ---

    /// <summary>
    /// Raised when a run ends (obstacle collision). Subscribers:
    /// UIManager — shows GameOver panel.
    /// PlayerController — applies death visual (material color change).
    /// </summary>
    public event System.Action OnGameOver;

    /// <summary>
    /// Raised when the player triggers a restart (presses R after GameOver).
    /// Subscribers: UIManager — hides GameOver panel.
    /// </summary>
    public event System.Action OnRestart;

    // --- Lifecycle ---

    private void Awake()
    {
        // Singleton enforcement: destroy any duplicate GameManager instances
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Distance = 0f;
        IsGameOver = false;
    }

    private void Update()
    {
        if (!IsGameOver)
        {
            Distance += ForwardSpeed * Time.deltaTime;
        }
    }

    // --- Public Methods ---

    /// <summary>
    /// Ends the current run. Marks state as GameOver, stops distance accumulation,
    /// and invokes the OnGameOver event for all subscribers.
    ///
    /// Idempotent: calling GameOver() multiple times has no effect after the first call.
    /// </summary>
    public void GameOver()
    {
        // Idempotent guard: prevent multiple GameOver invocations
        if (IsGameOver)
            return;

        IsGameOver = true;
        OnGameOver?.Invoke();
    }

    /// <summary>
    /// Restarts the game by reloading the current scene.
    /// Invokes OnRestart event before loading so subscribers can prepare/clean up.
    ///
    /// SceneManager.LoadScene destroys all runtime objects and re-runs Awake
    /// on a fresh scene load — no stale state survives.
    /// </summary>
    public void Restart()
    {
        OnRestart?.Invoke();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
