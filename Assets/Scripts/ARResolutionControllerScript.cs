using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARResolutionController : MonoBehaviour
{
    [SerializeField] private ARCameraManager arCameraManager;

    // void Awake()
    // {
    //     arCameraManager = FindAnyObjectByType<ARCameraManager>();
    // }    

    void OnEnable()
    {
        arCameraManager.frameReceived += OnFirstFrameReceived;
    }

    void OnDisable()
    {
        arCameraManager.frameReceived -= OnFirstFrameReceived;
    }

    private void OnFirstFrameReceived(ARCameraFrameEventArgs args)
    {
        arCameraManager.frameReceived -= OnFirstFrameReceived; // Unsubscribe after the first frame is received

        SetLowResolution();
    }

    private void SetLowResolution()
    {
        if(arCameraManager.subsystem == null)
        {
            return;
        }

        using (var configurations = arCameraManager.subsystem.GetConfigurations(Unity.Collections.Allocator.Temp))
        {
            if(!configurations.IsCreated || configurations.Length == 0)
            {
                return;
            }

            XRCameraConfiguration lowResConfig = configurations[0]; // Default to the first configuration
            for(int i = 1; i < configurations.Length; i++)
            {
                if(configurations[i].width < lowResConfig.width || configurations[i].height < lowResConfig.height)
                {
                    lowResConfig = configurations[i];
                }
            }

            arCameraManager.currentConfiguration = lowResConfig; // Set the camera to the lowest resolution configuration
            Debug.Log($"Camera resolution set to: {lowResConfig.width}x{lowResConfig.height}.");
        }
    }

}
