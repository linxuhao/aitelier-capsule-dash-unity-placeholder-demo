using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Monobehaviour that polls UnityEngine.InputSystem devices directly each frame
/// and exposes four boolean properties: LeftPressed, RightPressed, JumpPressed, RestartPressed.
///
/// This is the SOLE point of contact with InputSystem device queries in the project.
/// All other components (PlayerController, UIManager) consume these properties only.
///
/// No .inputactions assets, no PlayerInput component, no InputActionMap — pure C# polling.
/// </summary>
public class InputHandler : MonoBehaviour
{
    // --- Public Properties ---
    // Properties are reset to false at the start of each Update() and set true for
    // exactly one frame when the corresponding input event occurs (wasPressedThisFrame semantics).

    public bool LeftPressed { get; private set; }
    public bool RightPressed { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool RestartPressed { get; private set; }

    // --- Touch State (stretch goal) ---
    private Vector2 _touchStartPos;
    private float _touchStartTime;
    private bool _touchActive;

    private const float SwipeThreshold = 50f;      // minimum pixel distance for a swipe
    private const float SwipeMaxDuration = 0.3f;    // max duration for a swipe gesture
    private const float TapMaxDuration = 0.2f;      // max duration for a tap gesture
    private const float TapMaxDistance = 25f;       // max pixel distance for a tap (half of SwipeThreshold)

    private void Update()
    {
        // Reset all properties at the start of each frame
        LeftPressed = false;
        RightPressed = false;
        JumpPressed = false;
        RestartPressed = false;

        // --- Keyboard Polling ---
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
                LeftPressed = true;

            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                RightPressed = true;

            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
                JumpPressed = true;

            if (keyboard.rKey.wasPressedThisFrame)
                RestartPressed = true;
        }

        // --- Touch Polling (stretch goal) ---
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            ProcessTouch(touchscreen);
        }
    }

    /// <summary>
    /// Processes touch input from the Touchscreen device.
    /// Gestures are detected on the Ended phase to avoid mid-swipe jitter:
    ///   - Swipe left/right (horizontal delta > SwipeThreshold) → LeftPressed / RightPressed
    ///   - Swipe up (negative Y delta) → JumpPressed
    ///   - Tap (short duration, small delta) → RestartPressed
    /// </summary>
    /// <param name="touchscreen">The active Touchscreen device (guaranteed non-null by caller).</param>
    private void ProcessTouch(Touchscreen touchscreen)
    {
        TouchState touch = touchscreen.primaryTouch;

        switch (touch.phase.ReadValue())
        {
            case UnityEngine.InputSystem.TouchPhase.Began:
                // Record the starting position and time of the touch
                _touchStartPos = touch.position.ReadValue();
                _touchStartTime = Time.unscaledTime;
                _touchActive = true;
                break;

            case UnityEngine.InputSystem.TouchPhase.Moved:
                // We wait for Ended to classify gestures, but keep tracking if active
                break;

            case UnityEngine.InputSystem.TouchPhase.Ended:
            case UnityEngine.InputSystem.TouchPhase.Canceled:
                if (!_touchActive)
                    break;

                Vector2 endPos = touch.position.ReadValue();
                Vector2 delta = endPos - _touchStartPos;
                float duration = Time.unscaledTime - _touchStartTime;
                _touchActive = false;

                // Tap detection: short duration and small movement
                if (duration < TapMaxDuration && delta.magnitude < TapMaxDistance)
                {
                    RestartPressed = true;
                    return;
                }

                // Swipe detection: must complete within the max swipe duration
                if (duration > SwipeMaxDuration)
                    return;

                // Determine swipe direction
                // Swipe up → negative Y delta (finger moves upward on screen)
                if (delta.y < -SwipeThreshold)
                {
                    JumpPressed = true;
                }

                // Swipe right → positive X delta
                if (delta.x > SwipeThreshold)
                {
                    RightPressed = true;
                }

                // Swipe left → negative X delta
                if (delta.x < -SwipeThreshold)
                {
                    LeftPressed = true;
                }
                break;

            default:
                // Stationary or other phases: no action
                break;
        }
    }
}
