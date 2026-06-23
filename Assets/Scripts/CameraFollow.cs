using UnityEngine;

/// <summary>
/// Smooth 3D perspective follow camera that trails behind and above the player capsule.
/// Attached to the Main Camera GameObject by SceneBootstrapper.BuildScene().
///
/// Uses LateUpdate with Vector3.Lerp for jitter-free following and LookAt with
/// look-ahead so the player can see upcoming obstacles.
///
/// Discovers the player Transform via GameObject.FindGameObjectWithTag("Player")
/// in Awake, with a lazy retry in LateUpdate to handle edge cases where the
/// player GameObject is created after this camera's Awake runs.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    // --- Serialized Fields ---

    /// <summary>
    /// Position offset relative to the player. Negative Z trails behind,
    /// positive Y looks down from above.
    /// Default: 5 units above, 8 units behind.
    /// </summary>
    [SerializeField] private Vector3 _offset = new Vector3(0f, 5f, -8f);

    /// <summary>
    /// Speed of the smooth position interpolation (Lerp factor per second).
    /// Higher values snap the camera closer to the target position more quickly.
    /// </summary>
    [SerializeField] private float _smoothSpeed = 8f;

    /// <summary>
    /// Distance in world units ahead of the player that the camera looks toward.
    /// Provides a view of what's coming rather than staring at the back of the capsule.
    /// </summary>
    [SerializeField] private float _lookAheadZ = 3f;

    // --- Runtime State ---

    /// <summary>
    /// Reference to the player capsule's Transform. Discovered via tag lookup in Awake.
    /// </summary>
    private Transform _player;

    // --- Unity Lifecycle ---

    private void Awake()
    {
        // Discover the player Transform via tag lookup
        _player = GameObject.FindGameObjectWithTag("Player")?.transform;

        // Configure the Camera component
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
        }
        else
        {
            Debug.LogWarning("CameraFollow.Awake: No Camera component found on this GameObject.");
        }
    }

    private void LateUpdate()
    {
        // Lazy re-acquire: if the player reference was lost or not yet available,
        // try finding it again. This handles edge cases where the player GameObject
        // is created after this component's Awake.
        if (_player == null)
        {
            _player = GameObject.FindGameObjectWithTag("Player")?.transform;
        }

        // If the player still doesn't exist, skip camera movement this frame
        if (_player == null)
        {
            return;
        }

        // Calculate the target camera position: player position + offset
        Vector3 targetPosition = _player.position + _offset;

        // Smoothly interpolate toward the target position
        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            _smoothSpeed * Time.deltaTime
        );

        // Calculate a look target slightly ahead of the player (along +Z)
        // to let the player see upcoming obstacles
        Vector3 lookTarget = _player.position + Vector3.forward * _lookAheadZ;

        // Rotate the camera to face the look target
        transform.LookAt(lookTarget);
    }
}
