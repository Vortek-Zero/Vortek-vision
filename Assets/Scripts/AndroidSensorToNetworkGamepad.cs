using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class AndroidSensorToNetworkGamepad : MonoBehaviour
{
    private Gamepad _virtualGamepad;

    void Awake()
    {
        // Add a virtual gamepad. Render Streaming's InputSender natively synchronizes Gamepads.
        _virtualGamepad = InputSystem.AddDevice<Gamepad>("NetworkSensorGamepad");
        if (_virtualGamepad != null)
        {
            Debug.Log("[AndroidSensorToNetworkGamepad] Virtual Gamepad created.");
        }
    }

    void Start()
    {
        // Enable sensors
        if (AttitudeSensor.current != null)
        {
            InputSystem.EnableDevice(AttitudeSensor.current);
            Debug.Log("[AndroidSensorToNetworkGamepad] AttitudeSensor enabled.");
        }
        else
        {
            Debug.LogWarning("[AndroidSensorToNetworkGamepad] AttitudeSensor NOT found on this device!");
        }
            
        if (UnityEngine.InputSystem.Gyroscope.current != null)
            InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
    }

    private float _nextLogTime = 0f;

    void Update()
    {
        if (AttitudeSensor.current == null) return;
        if (_virtualGamepad == null) return;

        // 1. Read absolute rotation
        Quaternion q = AttitudeSensor.current.attitude.ReadValue();

        if (Time.time > _nextLogTime)
        {
            Debug.Log($"[AndroidSensorToNetworkGamepad] Sending Quat: {q} | Euler: {q.eulerAngles}");
            _nextLogTime = Time.time + 1f; // log every 1 second
        }

        // 2. Pack the Quaternion (x, y, z, w) into the Gamepad's two joysticks
        var state = new GamepadState();
        
        // Input system sticks clamp between -1 and 1, which perfectly matches Quaternion ranges
        state.leftStick = new Vector2(q.x, q.y);
        state.rightStick = new Vector2(q.z, q.w);

        // 3. Queue the event. InputSender will pick this up and send it over WebRTC!
        InputSystem.QueueStateEvent(_virtualGamepad, state);
    }

    void OnDestroy()
    {
        if (_virtualGamepad != null)
            InputSystem.RemoveDevice(_virtualGamepad);
    }
}