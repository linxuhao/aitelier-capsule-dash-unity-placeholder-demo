using UnityEngine;

/// <summary>
/// Code-driven wind / speed-line particle effect that visually conveys forward
/// motion while the player capsule is stationary at Z=0.
///
/// Creates and configures a ParticleSystem in Awake() using the self-supplying
/// SerializeField pattern: all visual parameters have defaults that are resolved
/// at runtime if not explicitly assigned via the Inspector.
///
/// The effect emits thin stretched particles from a box-shaped region ahead of
/// the player, flowing backward (along -Z) in world space. This produces the
/// appearance of speed lines / dust rushing past a moving character.
///
/// Attached as a child GameObject of the Player by SceneBootstrapper.BuildScene().
/// No Update() loop required — the ParticleSystem auto-emits with playOnAwake
/// and loop = true.
/// </summary>
public class WindEffect : MonoBehaviour
{
    // --- Serialized Fields (Self-Supplying) ---

    /// <summary>
    /// Material applied to wind particle billboards.
    /// If null at Awake, a white semi-transparent placeholder material is created
    /// via Placeholders.CreateMaterial().
    /// </summary>
    [SerializeField] private Material _particleMaterial;

    /// <summary>
    /// Speed at which particles move backward (world units per second).
    /// Defaults to 8f. If set to 0 or negative at Awake, resolves from
    /// GameManager.Instance.ForwardSpeed as a fallback.
    /// </summary>
    [SerializeField] private float _particleSpeed = 8f;

    /// <summary>
    /// Number of particles emitted per second. Higher values produce denser
    /// speed-line visuals.
    /// </summary>
    [SerializeField] private float _emissionRate = 50f;

    /// <summary>
    /// Lifetime of each particle in seconds. Longer lifetimes mean particles
    /// travel further before fading.
    /// </summary>
    [SerializeField] private float _particleLifetime = 2f;

    /// <summary>
    /// Width of the box-shaped emission volume along the X axis (across lanes).
    /// </summary>
    [SerializeField] private float _emissionWidth = 8f;

    /// <summary>
    /// Height of the box-shaped emission volume along the Y axis.
    /// </summary>
    [SerializeField] private float _emissionHeight = 1f;

    /// <summary>
    /// Depth of the box-shaped emission volume along the Z axis (along the
    /// run direction / forward axis).
    /// </summary>
    [SerializeField] private float _emissionDepth = 3f;

    /// <summary>
    /// Z offset of the box-shaped emission volume center from the player's
    /// origin. Positive values place the emission region ahead of the player,
    /// so particles are already in motion as they pass by.
    /// </summary>
    [SerializeField] private float _emissionZOffset = 5f;

    // --- Unity Lifecycle ---

    private void Awake()
    {
        // Self-supply material if not assigned via Inspector
        if (_particleMaterial == null)
        {
            // White, semi-transparent — particles look like dust / speed lines
            _particleMaterial = Placeholders.CreateMaterial(new Color(1f, 1f, 1f, 0.4f));
        }

        // Resolve particle speed from GameManager if not explicitly configured
        if (_particleSpeed <= 0f && GameManager.Instance != null)
        {
            _particleSpeed = GameManager.Instance.ForwardSpeed;
        }

        // Add and configure the ParticleSystem
        ParticleSystem ps = gameObject.AddComponent<ParticleSystem>();
        ConfigureParticleSystem(ps);
    }

    // --- Particle System Configuration ---

    /// <summary>
    /// Configures all modules of the given ParticleSystem to produce a
    /// speed-line / wind effect:
    ///
    /// - Main: thin particles (0.05f size), semi-transparent white, world-space
    ///   simulation, loops continuously.
    /// - Emission: constant stream at _emissionRate particles per second.
    /// - Shape: box volume positioned ahead of the player.
    /// - VelocityOverLifetime: particles flow backward along -Z at _particleSpeed.
    /// - Renderer: stretched billboard mode for speed-line appearance.
    ///
    /// Module structs are cached in local variables before modification, as
    /// required by the Unity 6 ParticleSystem API.
    /// </summary>
    /// <param name="ps">The ParticleSystem component to configure.</param>
    private void ConfigureParticleSystem(ParticleSystem ps)
    {
        // ---- Main Module ----
        var main = ps.main;
        main.startLifetime = _particleLifetime;
        main.startSize = 0.05f;                        // Thin lines
        main.startColor = new Color(1f, 1f, 1f, 0.5f); // Semi-transparent white
        main.simulationSpace = ParticleSystemSimulationSpace.World; // Particles in world space
        main.playOnAwake = true;
        main.loop = true;
        main.startSpeed = 0f; // Velocity controlled via VelocityOverLifetime module

        // ---- Emission Module ----
        var emission = ps.emission;
        emission.rateOverTime = _emissionRate;

        // ---- Shape Module ----
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(_emissionWidth, _emissionHeight, _emissionDepth);
        shape.position = new Vector3(0f, 0f, _emissionZOffset);

        // ---- Velocity Over Lifetime Module ----
        // Negative Z produces backward flow (toward / past the player)
        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.z = -_particleSpeed;

        // ---- Renderer Module ----
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch; // Speed-line stretching
        renderer.lengthScale = 2f;      // Base stretch length
        renderer.velocityScale = 0.15f; // Stretch proportional to velocity
        renderer.material = _particleMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
}
