using UnityEngine;
using UnityEngine.InputSystem;

public class RemoteRotationHandlerScript : MonoBehaviour
{
    // Drag the Quaternion-producing Input Action from your Input Actions asset here.
    public InputActionReference rotationAction;

    // Cached action reference to avoid repeated property lookups and simplify unsubscribe.
    InputAction _rotationInput;
    private float _nextDebugLog = 0f;

    void OnEnable()
    {
        // Resolve the runtime action from the serialized action reference.
        _rotationInput = rotationAction?.action;
        if (_rotationInput == null)
        {
            Debug.LogWarning("[RemoteRotationHandler] Action is null! Please assign a valid InputActionReference.");
            return;
        }

        Debug.Log($"[RemoteRotationHandler] Subscribed to action: {_rotationInput.name}");
        _rotationInput.Enable();
    }

    void OnDisable()
    {
        if (_rotationInput == null)
            return;

        Debug.Log($"[RemoteRotationHandler] Unsubscribed from action: {_rotationInput.name}");
        _rotationInput.Disable();
    }

    void Update()
    {
        if (_rotationInput == null) return;

        // Polling is often more reliable for continuous sensors than event-driven callbacks.
        Quaternion newRot = _rotationInput.ReadValue<Quaternion>();

        if (Time.time > _nextDebugLog)
        {
            Debug.Log($"[RemoteRotationHandler] Polled Raw Quat: {newRot} | Euler: {newRot.eulerAngles}");
            _nextDebugLog = Time.time + 1f;
        }

        // Only apply valid rotations (a zero quaternion will collapse your object's scale/rotation and ruin rendering)
        if (Mathf.Abs(newRot.x) > 0.01f || Mathf.Abs(newRot.y) > 0.01f || Mathf.Abs(newRot.z) > 0.01f || Mathf.Abs(newRot.w) > 0.01f)
        {
            transform.localRotation = newRot;
        }
    }
}