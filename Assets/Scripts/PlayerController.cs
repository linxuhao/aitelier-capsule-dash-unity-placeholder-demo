using UnityEngine;

/// <summary>
/// Controls the player capsule character in the 3D endless runner.
/// Handles three-lane switching (smooth X interpolation via Transform, not physics),
/// jumping (Rigidbody impulse), ground detection (raycast), and collision death detection.
///
/// The player is stationary on Z (never auto-runs forward) — obstacles scroll toward
/// the player instead. This fixes the finite-runway bug (#3) where the player would
/// fall off the edge of the ground plane.
///
/// Input is consumed from the sibling InputHandler component. Game state queries
/// and death notification go through GameManager.Instance.
///
/// Requires Rigidbody for physics movement and collision detection.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    // --- Serialized Fields (Self-Supplying) ---

    /// <summary>
    /// Material applied to the player capsule mesh. If null at Awake, a blue
    /// placeholder material is created via Placeholders.CreateMaterial(Color.blue).
    /// On death, the material color is changed to red.
    /// </summary>
    [SerializeField] private Material _playerMaterial;

    /// <summary>
    /// Horizontal distance between lanes (world units). Center lane is at X=0,
    /// left lane at X=-_laneDistance, right lane at X=+_laneDistance.
    /// </summary>
    [SerializeField] private float _laneDistance = 2.5f;

    /// <summary>
    /// Speed of the smooth interpolation when switching lanes (units per second).
    /// Higher values produce snappier lane changes.
    /// </summary>
    [SerializeField] private float _laneSwitchSpeed = 12f;

    /// <summary>
    /// Magnitude of the upward impulse applied when jumping.
    /// Applied via Rigidbody.AddForce with ForceMode.Impulse.
    /// </summary>
    [SerializeField] private float _jumpForce = 10f;

    /// <summary>
    /// Length of the downward raycast used to detect whether the player is grounded.
    /// Should be slightly longer than half the capsule collider height.
    /// </summary>
    [SerializeField] private float _groundCheckDistance = 1.2f;

    // --- Runtime State ---

    /// <summary>
    /// Reference to the sibling InputHandler component, resolved in Awake.
    /// Used each frame to read LeftPressed, RightPressed, and JumpPressed.
    /// </summary>
    private InputHandler _input;

    /// <summary>
    /// Reference to the Rigidbody component, resolved in Awake.
    /// Used for jump impulse (Y) and lane-switch suppression.
    /// Z velocity is explicitly set to 0 — player is stationary on the run axis.
    /// </summary>
    private Rigidbody _rb;

    /// <summary>
    /// Current lane index: 0 = left, 1 = center, 2 = right.
    /// Updated by input in Update().
    /// </summary>
    private int _currentLane = 1;

    /// <summary>
    /// Target X position derived from _currentLane and _laneDistance.
    /// The player Transform smoothly interpolates toward this each frame.
    /// </summary>
    private float _targetX;

    /// <summary>
    /// Whether the player is currently touching the ground.
    /// Updated each frame via Physics.Raycast downward.
    /// Used to gate jumping.
    /// </summary>
    private bool _isGrounded;

    /// <summary>
    /// Whether the player has died from collision with an obstacle.
    /// When true, input and movement are suppressed.
    /// </summary>
    private bool _isDead;

    // --- Unity Lifecycle ---

    private void Awake()
    {
        // Resolve required components
        _rb = GetComponent<Rigidbody>();
        _input = GetComponent<InputHandler>();

        // Configure Rigidbody for physics movement
        _rb.mass = 1f;
        _rb.linearDamping = 0f;
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Enable Continuous Dynamic CCD to prevent fast-moving obstacles (static
        // colliders at 8 units/s via Transform) from tunneling through the player
        // between physics frames (bullet-through-paper effect). This is the root
        // cause of the obstacle collision death bug — the OnCollisionEnter logic
        // itself was correct, but discrete detection missed collisions at speed.
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Self-supply material if not assigned in Inspector
        MeshRenderer renderer = GetComponent<MeshRenderer>();
        if (_playerMaterial == null && renderer != null)
        {
            _playerMaterial = Placeholders.CreateMaterial(Color.blue);
            if (_playerMaterial != null)
            {
                renderer.material = _playerMaterial;
            }
        }
        else if (_playerMaterial != null && renderer != null)
        {
            renderer.material = _playerMaterial;
        }

        // Start in the center lane
        _targetX = 0f;
    }

    private void Update()
    {
        // Suppress all input and movement after death
        if (_isDead)
            return;

        // --- Lane Switching ---

        // Guard against missing InputHandler
        if (_input != null)
        {
            if (_input.LeftPressed && _currentLane > 0)
            {
                _currentLane--;
            }

            if (_input.RightPressed && _currentLane < 2)
            {
                _currentLane++;
            }
        }

        // Calculate target X from current lane
        _targetX = (_currentLane - 1) * _laneDistance;

        // Smoothly interpolate X toward target (Transform, not physics)
        Vector3 pos = transform.position;
        pos.x = Mathf.Lerp(pos.x, _targetX, _laneSwitchSpeed * Time.deltaTime);
        transform.position = pos;

        // --- Ground Check ---
        // Raycast downward from the player's position
        _isGrounded = Physics.Raycast(transform.position, Vector3.down, _groundCheckDistance);

        // --- Jump ---
        if (_input != null && _input.JumpPressed && _isGrounded)
        {
            _rb.AddForce(Vector3.up * _jumpForce, ForceMode.Impulse);
        }
    }

    private void FixedUpdate()
    {
        // Suppress physics movement after death
        if (_isDead)
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        // Player is stationary on Z — obstacles scroll toward the player instead.
        // Z velocity is explicitly 0f so no auto-run moves the player forward.
        // Y velocity is preserved for gravity/jump arc.
        // X velocity is zero — lane switching is handled via Transform in Update.
        // The forwardSpeed lookup is retained (though unused for velocity) to
        // minimise the diff footprint and preserve the GameManager.Instance guard.

        _rb.velocity = new Vector3(0f, _rb.velocity.y, 0f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Debug log to verify collision detection is firing (temporary aid).
        // Remove after confirming death works correctly.
        Debug.Log($"Player.OnCollisionEnter: collided with '{collision.gameObject.name}' (tag={collision.gameObject.tag})");

        // Ignore collisions after death (prevents double-trigger)
        if (_isDead)
            return;

        // Only obstacles trigger death — ground, walls, and lane markers do not
        if (collision.gameObject.CompareTag("Obstacle"))
        {
            _isDead = true;

            // Visual feedback: change material color to red
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = Color.red;
            }

            // Notify GameManager of death
            if (GameManager.Instance != null)
            {
                GameManager.Instance.GameOver();
            }
        }
    }
}
