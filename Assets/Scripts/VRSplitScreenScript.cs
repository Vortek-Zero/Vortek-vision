using UnityEngine;

public class VRSplitScreen : MonoBehaviour
{
    [Tooltip("Drag your LeftEyeCamera (or main AR camera) here, or auto-assigned if empty.")]
    public Transform leftEyeCamera;
    
    [Tooltip("Average human interpupillary distance (IPD) is ~0.064 meters (64mm).")]
    [Range(0.04f, 0.08f)]
    public float ipd = 0.064f; 

    [Tooltip("Toggle stereoscopic offset on/off at runtime.")]
    public bool enableStereo = true;

    void Start()
    {
        if (leftEyeCamera == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null && mainCam.transform != transform)
            {
                leftEyeCamera = mainCam.transform;
            }
        }
    }

    void LateUpdate()
    {
        if (leftEyeCamera != null)
        {
            // Match rotation of the tracked camera
            transform.rotation = leftEyeCamera.rotation;
            
            // Offset right eye by IPD along local right axis
            if (enableStereo)
            {
                transform.position = leftEyeCamera.position + (leftEyeCamera.right * ipd);
            }
            else
            {
                transform.position = leftEyeCamera.position;
            }
        }
    }
}