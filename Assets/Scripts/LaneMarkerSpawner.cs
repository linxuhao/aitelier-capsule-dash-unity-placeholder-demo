using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Manages an ObjectPool of scrolling lane marker cylinders that continuously
/// appear ahead of the player across all three lanes and scroll backward to
/// convey forward motion.
///
/// Mirrors ObstacleSpawner.cs with lane-marker-specific adaptations:
/// - ALL 3 lanes spawn together each time (not random single lane)
/// - No same-lane avoidance needed
/// - No Rigidbody on markers (purely visual)
/// - Dark gray material instead of red
/// - Constant spacing between marker rows instead of random gaps
///
/// Self-discovers the player via GameObject.FindGameObjectWithTag("Player") and
/// self-supplies its marker material via Placeholders.CreateMaterial(Color) fallback
/// if no material is assigned in the Inspector.
///
/// Spawning uses a virtual _scrollDistance counter (incremented each frame by
/// ForwardSpeed * dt) rather than the player's actual Z position, matching the
/// ObstacleSpawner pattern. This decouples spawn timing from player movement,
/// so markers spawn correctly even when the player is stationary at Z=0.
/// </summary>
public class LaneMarkerSpawner : MonoBehaviour
{
    // --- Serialized Fields (Self-Supplying) ---

    /// <summary>
    /// Material applied to spawned lane marker cylinders. If null at Awake, a dark
    /// gray placeholder material is created via Placeholders.CreateMaterial(new Color(0.15f, 0.15f, 0.15f)).
    /// </summary>
    [SerializeField] private Material _markerMaterial;

    /// <summary>
    /// Z-distance in virtual scroll units between successive rows of lane markers.
    /// Smaller values produce denser markers; larger values produce sparser markers.
    /// </summary>
    [SerializeField] private float _markerSpacing = 6f;

    /// <summary>
    /// Distance in world units ahead of the player at which new marker rows are placed.
    /// </summary>
    [SerializeField] private float _spawnDistance = 40f;

    /// <summary>
    /// Default capacity for the ObjectPool. Pre-allocates this many pool entries
    /// to avoid runtime allocations.
    /// </summary>
    [SerializeField] private int _poolDefaultCapacity = 15;

    /// <summary>
    /// Maximum number of pool entries. Prevents unbounded pool growth.
    /// If the pool exceeds this size, the oldest returned objects are destroyed.
    /// </summary>
    [SerializeField] private int _poolMaxSize = 30;

    // --- Runtime State ---

    /// <summary>
    /// The ObjectPool that owns all lane marker cylinder GameObjects.
    /// Created in Awake() and used in Update() for spawning and recycling.
    /// </summary>
    private ObjectPool<GameObject> _pool;

    /// <summary>
    /// Reference to the player's Transform, discovered in Awake() via tag.
    /// Used to compute spawn positions ahead of the player.
    /// </summary>
    private Transform _player;

    /// <summary>
    /// Virtual scroll distance counter. Increments each frame by
    /// GameManager.Instance.ForwardSpeed * Time.deltaTime, conceptually
    /// representing "how far the world has scrolled." Mirrors the same pattern
    /// used by ObstacleSpawner and GameManager.Distance accumulation.
    /// Default value 0f by C# field initialisation.
    /// </summary>
    private float _scrollDistance;

    /// <summary>
    /// Virtual scroll distance threshold for the next marker row spawn. When
    /// _scrollDistance exceeds _nextSpawnZ, a new row of markers (one per lane)
    /// is spawned and _nextSpawnZ is advanced by _markerSpacing.
    /// Initialised to 0f so the first row spawns immediately as _scrollDistance
    /// passes 0 on the first frame of gameplay.
    /// </summary>
    private float _nextSpawnZ = 0f;

    /// <summary>
    /// Lateral spacing between lanes in world units. Mirrors PlayerController._laneDistance
    /// and ObstacleSpawner._laneSpacing. Lane X offsets computed as (laneIndex - 1) * this value.
    /// </summary>
    private const float _laneSpacing = 2.5f;

    // --- Lifecycle ---

    /// <summary>
    /// Initialises the spawner: discovers the player Transform, creates the
    /// marker material (with Placeholders fallback), and sets up the ObjectPool.
    /// </summary>
    private void Awake()
    {
        // Discover player by tag
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            _player = playerObj.transform;
            Debug.Log($"LaneMarkerSpawner: Found player '{playerObj.name}'.");
        }
        else
        {
            Debug.LogWarning("LaneMarkerSpawner: No GameObject with tag 'Player' found. Spawning disabled.");
        }

        // Self-supply marker material if not assigned
        if (_markerMaterial == null)
        {
            Color darkGray = new Color(0.15f, 0.15f, 0.15f);
            _markerMaterial = Placeholders.CreateMaterial(darkGray);
            if (_markerMaterial != null)
            {
                Debug.Log("LaneMarkerSpawner: Using fallback dark gray placeholder material for lane markers.");
            }
            else
            {
                Debug.LogError("LaneMarkerSpawner: Failed to create placeholder material. Lane markers may be invisible.");
            }
        }

        // Initialise the marker pool
        _pool = new ObjectPool<GameObject>(
            createFunc: CreateMarker,
            actionOnGet: (go) => go.SetActive(true),
            actionOnRelease: (go) => go.SetActive(false),
            actionOnDestroy: (go) => Destroy(go),
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize: _poolMaxSize
        );

        Debug.Log($"LaneMarkerSpawner: Pool initialised (capacity={_poolDefaultCapacity}, maxSize={_poolMaxSize}).");
    }

    /// <summary>
    /// Factory method called by the ObjectPool when a new lane marker is needed.
    /// Creates a dark gray cylinder primitive with a flat aspect ratio, adds the
    /// LaneMarker component for per-frame scrolling and pool-return logic, and
    /// intentionally does NOT add a Rigidbody — lane markers are purely visual
    /// and must not participate in collision detection with the player.
    ///
    /// PlayerController.OnCollisionEnter uses component-based detection
    /// (GetComponent<Obstacle>() != null), so lane markers (which carry a
    /// LaneMarker component, not an Obstacle component) can never cause a false
    /// death even if the player physically touches a marker cylinder.
    /// </summary>
    /// <returns>A new pooled lane marker GameObject.</returns>
    private GameObject CreateMarker()
    {
        Color color = _markerMaterial != null ? _markerMaterial.color : new Color(0.15f, 0.15f, 0.15f);
        GameObject marker = Placeholders.CreatePrimitive(PrimitiveType.Cylinder, color, "LaneMarker");
        marker.transform.localScale = new Vector3(0.2f, 0.05f, 0.2f);
        marker.AddComponent<LaneMarker>();
        // NO Rigidbody — markers are visual-only, should not participate in physics
        return marker;
    }

    /// <summary>
    /// Called every frame. Guards against null GameManager, GameOver state, and
    /// missing player reference. Advances the virtual scroll distance counter and,
    /// when _scrollDistance exceeds _nextSpawnZ, spawns a row of markers in ALL 3
    /// lanes simultaneously, configures each with a pool-release callback and player
    /// reference, and advances the spawn threshold by _markerSpacing.
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
        _scrollDistance += GameManager.Instance.ForwardSpeed * Time.deltaTime;

        // Virtual-distance-based spawn check — spawn a row of markers when threshold reached
        if (_scrollDistance >= _nextSpawnZ)
        {
            // Spawn one marker in each of the 3 lanes simultaneously
            for (int lane = 0; lane < 3; lane++)
            {
                // Retrieve marker from pool
                GameObject marker = _pool.Get();

                // Position ahead of the player: compute X from lane index, Y just above ground
                float xPos = (lane - 1) * _laneSpacing;
                marker.transform.position = new Vector3(xPos, 0.025f, _player.position.z + _spawnDistance);

                // Configure marker with release callback and player reference
                LaneMarker laneMarkerComponent = marker.GetComponent<LaneMarker>();
                if (laneMarkerComponent != null)
                {
                    laneMarkerComponent.Configure((go) => _pool.Release(go), _player);
                }
                else
                {
                    Debug.LogError("LaneMarkerSpawner: Pooled marker is missing LaneMarker component!");
                }
            }

            // Advance spawn threshold by the fixed marker spacing
            _nextSpawnZ = _scrollDistance + _markerSpacing;
        }
    }
}
