using UnityEngine;

/// <summary>
/// Monobehaviour attached to each pooled obstacle cube. Handles per-frame scrolling
/// toward the player (along negative Z) and self-return to the object pool when the
/// obstacle passes behind the player.
///
/// Configured by ObstacleSpawner via Configure() when the obstacle is retrieved from
/// the pool. Includes an OnDisable() safety net to return the game object to the pool
/// if it's deactivated without being properly returned (e.g., during scene reload).
/// </summary>
public class Obstacle : MonoBehaviour
{
    // --- Private Fields ---

    /// <summary>
    /// Forward speed of the game, cached from GameManager.Instance.ForwardSpeed
    /// at configuration time. Obstacles scroll backward at this speed.
    /// </summary>
    private float _scrollSpeed;

    /// <summary>
    /// Reference to the player's Transform, used to determine when this obstacle
    /// has passed behind the player and should return to the pool.
    /// </summary>
    private Transform _player;

    /// <summary>
    /// Callback to return this game object to the object pool.
    /// Injected by ObstacleSpawner.Configure() via pool.Release().
    /// </summary>
    private System.Action<GameObject> _releaseAction;

    /// <summary>
    /// Tracks whether Configure() has been called on this instance.
    /// Used in OnDisable() to avoid releasing an unconfigured object.
    /// </summary>
    private bool _configured;

    // --- Public API ---

    /// <summary>
    /// Configures this obstacle with a pool-release callback and a reference to the
    /// player's Transform. Must be called after the obstacle is retrieved from the pool
    /// and before it is expected to scroll. Also caches the current forward speed from
    /// GameManager.
    /// </summary>
    /// <param name="releaseAction">
    /// A callback that returns this GameObject to the object pool (typically
    /// <c>(go) => pool.Release(go)</c>).
    /// </param>
    /// <param name="player">
    /// The player's Transform, used to detect when this obstacle should be recycled.
    /// </param>
    public void Configure(System.Action<GameObject> releaseAction, Transform player)
    {
        _releaseAction = releaseAction;
        _player = player;

        if (GameManager.Instance != null)
        {
            _scrollSpeed = GameManager.Instance.ForwardSpeed;
        }
        else
        {
            _scrollSpeed = 8f; // fallback default
            Debug.LogWarning("Obstacle.Configure: GameManager.Instance is null, using fallback scroll speed of 8.");
        }

        _configured = true;
    }

    // --- Unity Callbacks ---

    /// <summary>
    /// Called every frame. Moves the obstacle toward the player along negative Z.
    /// If the obstacle has passed behind the player (z < player.z - 10f), returns
    /// the game object to the pool via the release callback.
    ///
    /// Silently returns if the obstacle has not been configured yet or if the player
    /// reference is null.
    /// </summary>
    private void Update()
    {
        if (!_configured || _player == null || _releaseAction == null)
        {
            return;
        }

        // Scroll toward the player along negative Z
        transform.position += Vector3.back * _scrollSpeed * Time.deltaTime;

        // Despawn check: if obstacle has passed behind the player, return to pool
        if (transform.position.z < _player.position.z - 10f)
        {
            _configured = false;
            _releaseAction(gameObject);
        }
    }

    /// <summary>
    /// Safety net: if this game object is deactivated without being explicitly returned
    /// to the pool (e.g., scene reload, external disable), release it back to the pool
    /// so the pool count remains consistent.
    ///
    /// Only releases if Configure() was previously called (guarded by _configured flag).
    /// </summary>
    private void OnDisable()
    {
        if (_configured && _releaseAction != null)
        {
            _configured = false;
            _releaseAction(gameObject);
        }
    }
}