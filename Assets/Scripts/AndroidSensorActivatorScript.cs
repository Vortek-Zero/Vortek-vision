using UnityEngine;
using UnityEngine.InputSystem;

using TMPro;

public class AndroidSensorActivatorScript : MonoBehaviour
{
    [Header("UI Status")]
    public TMP_Text statusText;

    void Start()
    {
        // Enable the Gyroscope on the Android device 
        // For a standard 3DoF VR experience on a phone, 
        // you only need the rotation (orientation) of the device.
        if (AttitudeSensor.current != null)
            InputSystem.EnableDevice(AttitudeSensor.current);
            
        // Also enable normal Accelerometer if you want to use it for additional motion input, 
        // but it's not strictly necessary for basic VR orientation tracking________
        // if (Accelerometer.current != null)
        //     InputSystem.EnableDevice(Accelerometer.current);
            
        // Enable Gyroscope 
        // if (UnityEngine.InputSystem.Gyroscope.current != null)
        //     InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
    }
    
    /*
    void Update()
    {
        if (statusText != null)
        {
            if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            {
                Quaternion q = AttitudeSensor.current.attitude.ReadValue();
                statusText.text = $"[Sensor Status]\nValid: YES\nX:{q.x:F3} Y:{q.y:F3} Z:{q.z:F3} W:{q.w:F3}";
            }
            else
            {
                statusText.text = $"[Sensor Status]\nValid: NO\nNot reading anything!";
            }
        }
    }

    */
}