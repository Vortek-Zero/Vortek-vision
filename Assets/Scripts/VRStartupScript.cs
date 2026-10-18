using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

public class VRStartupScript : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(StartXR());
    }

    IEnumerator StartXR()
    {
        // Check if the XR manager is ready
        if (XRGeneralSettings.Instance.Manager.activeLoader == null)
        {
            Debug.Log("Initializing XR...");
            yield return XRGeneralSettings.Instance.Manager.InitializeLoader();
        }

        if (XRGeneralSettings.Instance.Manager.activeLoader != null)
        {
            Debug.Log("Starting XR Subsystems...");
            XRGeneralSettings.Instance.Manager.StartSubsystems();
        }
        else
        {
            Debug.LogError("Failed to initialize XR. Check your Project Settings!");
        }
    }
}