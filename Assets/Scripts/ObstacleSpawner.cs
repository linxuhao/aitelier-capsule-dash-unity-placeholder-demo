using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Spawns cube obstacles ahead of the player using virtual-distance-based intervals and
/// an ObjectPool<GameObject> for zero-allocation reuse. Obstacles are placed in
/// one of three lanes (left, center, right) with same-lane avoidance.
///
/// Self-discovers the player via GameObject.FindGameObjectWithTag("Player") and
/// self-supplies its obstacle material via Placeholders.CreateMaterial(Color.red)
/// fallback if no material is assigned in the Inspector.
///
/// Spawning uses a virtual _scrollDistance counter (incremented each frame by
/// ForwardSpeed * dt) rather than the player's actual Z position. This decouples
/// spawn timing from player movement, so obstacles spawn correctly even when the
/// player is stationary at Z=0 (player-stationary refactor). The virtual distance
/// conceptually represents "how far the world has scrolled" and mirrors
/// GameManager.Distance accumulation.
/// </summary>
public class ObstacleSpawner : MonoBehaviour
{
    // --- Serialized Fields (Self-Supplying) ---

    /// <summary>
    /// Material applied to spawned obstacle cubes. If null at Awake, a red
    /// placeholder material is created via Placeholders.CreateMaterial(Color.red).
    /// </summary>
    [SerializeField] private Material _obstacleMaterial;

    /// <summary>
    /// Distance in world units ahead of the player at which obstacles are spawned.
    /// </summary>
    [SerializeField] private float _spawnDistance = 35f;

    /// <summary>
    /// Minimum gap (in virtual scroll distance units) between consecutive obstacle spawns.
    /// Used with _maxSpawnGap to randomise spacing.
    /// </summary>
    [SerializeField] private float _minSpawnGap = 6f;

    /// <summary>
    /// Maximum gap (in virtual scroll distance units) between consecutive obstacle spawns.
    /// Used with _minSpawnGap to randomise spacing.
    /// </summary>
    [SerializeField] private float _maxSpawnGap = 16f;

    /// <summary>
    /// Default capacity for the ObjectPool. Pre-allocates this many pool entries
    /// to avoid runtime allocations.
    /// </summary>
    [SerializeField] private int _poolDefaultCapacity = 10;

    /// <summary>
    /// Maximum number of pool entries. Prevents unbounded pool growth.
    /// If the pool exceeds this size, the oldest returned objects are destroyed.
    /// </summary>
    [SerializeField] private int _poolMaxSize = 25;

    // --- Runtime State ---

    /// <summary>
    /// The ObjectPool that owns all obstacle cube GameObjects.
    /// Created in Awake() and used in Update() for spawning and recycling.
    /// </summary>
    private ObjectPool<GameObject> _pool;

    /// <summary>
    /// Reference to the player's Transform, discovered in Awake() via tag.
    /// Used to compute spawn positions and despawn thresholds.
    /// </summary>
    private Transform _player;

    /// <summary>
    /// Virtual scroll distance threshold for the next obstacle spawn. When this
    /// virtual distance (which represents "how far the world has scrolled")
    /// exceeds _nextSpawnZ, a new obstacle is spawned and _nextSpawnZ is advanced.
    /// Initialised to 0f so the first obstacle spawns immediately as _scrollDistance
    /// passes 0 on the first frame of gameplay.
    /// </summary>
    private float _nextSpawnZ = 0f;

    /// <summary>
    /// Virtual scroll distance counter. Increments each frame by
    /// GameManager.Instance.ForwardSpeed * Time.deltaTime, conceptually
    /// representing "how far the world has scrolled." Replaces the old
    /// _player.position.z-based spawn trigger, decoupling spawning from
    /// actual player position so obstacles spawn correctly when the player
    /// is stationary at Z=0.
    /// Default value 0f by C# field initialisation.
    /// </summary>
    private float _scrollDistance;

    /// <summary>
    /// Last lane an obstacle was placed in (0=left, 1=center, 2=right).
    /// Used to avoid placing two obstacles in the same lane consecutively.
    /// Initialised to -1 so the first spawn is unrestricted.
    /// </summary>
    private int _lastLane = -1;

    /// <summary>
    /// Lateral spacing between lanes in world units. Matches PlayerController._laneDistance.
    /// </summary>
    private const float _laneSpacing = 2.5f;

    // --- Lifecycle ---

    /// <summary>
    /// Initialises the spawner: discovers the player Transform, creates the
    /// obstacle material (with Placeholders fallback), and sets up the ObjectPool.
    /// </summary>
    private void Awake()
    {
        // Discover player by tag
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            _player = playerObj.transform;
            Debug.Log($"ObstacleSpawner: Found player '{playerObj.name}'.");
        }
        else
        {
            Debug.LogWarning("ObstacleSpawner: No GameObject with tag 'Player' found. Spawning disabled.");
        }

        // Self-supply obstacle material if not assigned
        if (_obstacleMaterial == null)
        {
            _obstacleMaterial = Placeholders.CreateMaterial(Color.red);
            if (_obstacleMaterial != null)
            {
                Debug.Log("ObstacleSpawner: Using fallback red placeholder material for obstacles.");
            }
            else
            {
                Debug.LogError("ObstacleSpawner: Failed to create placeholder material. Obstacles may be invisible.");
            }
        }

        // Initialise the obstacle pool
        _pool = new ObjectPool<GameObject>(
            createFunc: CreateObstacle,
            actionOnGet: (go) => go.SetActive(true),
            actionOnRelease: (go) => go.SetActive(false),
            actionOnDestroy: (go) => Destroy(go),
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize: _poolMaxSize
        );

        Debug.Log($"ObstacleSpawner: Pool initialised (capacity={_poolDefaultCapacity}, maxSize={_poolMaxSize}).");
    }

    /// <summary>
    /// Factory method called by the ObjectPool when a new obstacle is needed.
    /// Creates a red cube primitive, tags it as "Obstacle", and adds the Obstacle
    /// component for per-frame scroll and pool-return logic.
    /// </summary>
    /// <returns>A new pooled obstacle GameObject.</returns>
    private GameObject CreateObstacle()
    {
        Color color = _obstacleMaterial != null ? _obstacleMaterial.color : Color.red;
        GameObject cube = Placeholders.CreatePrimitive(PrimitiveType.Cube, color, "Obstacle");
        cube.tag = "Obstacle";
        cube.AddComponent<Obstacle>();
        return cube;
    }

    /// <summary>
    /// Called every frame. Guards against null GameManager, GameOver state, and
    /// missing player reference. Advances the virtual scroll distance counter and,
    /// when _scrollDistance exceeds _nextSpawnZ, spawns an obstacle in a random
    /// lane (with same-lane avoidance), configures it, and advances the spawn
    /// threshold using virtual distance.
    /// </summary>
    private void Update()
    {
        // Guard: GameManager must exist and game must be playing
        if (GameManager.Instance == null || GameManager.Instance.IsGameOver)
            return;

        // Guard: player must be known
        if (_player == null)
            return;

        // Advance virtual scroll distance (decoupled from actual player position)
        // This conceptually represents "how far the world has scrolled" and mirrors
        // GameManager.Distance accumulation. Replaces the old _player.position.z
        // check so obstacles spawn correctly when player is stationary at Z=0.
        _scrollDistance += GameManager.Instance.ForwardSpeed * Time.deltaTime;

        // Virtual-distance-based spawn check (was: _player.position.z >= _nextSpawnZ)
        if (_scrollDistance >= _nextSpawnZ)
        {
            // Random lane selection with same-lane avoidance
            int lane = Random.Range(0, 3);
            if (lane == _lastLane)
            {
                lane = (lane + 1) % 3;
            }

            // Retrieve obstacle from pool
            GameObject obstacle = _pool.Get();

            // Position ahead of the player (formula unchanged — with stationary player
            // at Z=0, obstacles spawn at world Z=_spawnDistance and scroll toward player)
            float xPos = (lane - 1) * _laneSpacing;
            obstacle.transform.position = new Vector3(xPos, 0.5f, _player.position.z + _spawnDistance);

            // Configure obstacle with release callback and player reference
            Obstacle obstacleComponent = obstacle.GetComponent<Obstacle>();
            if (obstacleComponent != null)
            {
                obstacleComponent.Configure((go) => _pool.Release(go), _player);
            }
            else
            {
                Debug.LogError("ObstacleSpawner: Pooled obstacle is missing Obstacle component!");
            }

            _lastLane = lane;

            // Advance spawn threshold by a random gap from the CURRENT virtual distance
            // (was: _player.position.z + Random.Range(...))
            _nextSpawnZ = _scrollDistance + Random.Range(_minSpawnGap, _maxSpawnGap);

            Debug.Log($"Spawned obstacle in lane {lane} at Z={obstacle.transform.position.z:F1}");
        }
    }
}
