using Unity.RenderStreaming;
using UnityEngine;
using UnityEngine.InputSystem;
// using UnityEngine.InputSystem.EnhancedTouch;
// using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;  

/* ________________Note: uncomment all the commented lines in this script to use Enhanced Touch__________________ */

public class ManualStart : MonoBehaviour
{
    [SerializeField] private SignalingManager signalingManager;
    // [SerializeField] private bool useEnhancedTouchInput = true; // for android phone

    // private bool ownsEnhancedTouchSupport; // Track if this script enabled Enhanced Touch Support so we can disable it later

    // private void OnEnable()
    // {
    //     if (useEnhancedTouchInput && !EnhancedTouchSupport.enabled)
    //     {
    //         EnhancedTouchSupport.Enable();
    //         ownsEnhancedTouchSupport = true;
    //     }
    // }

    // private void OnDisable()
    // {
    //     if (ownsEnhancedTouchSupport && EnhancedTouchSupport.enabled)
    //     {
    //         EnhancedTouchSupport.Disable();
    //         ownsEnhancedTouchSupport = false;
    //     }
    // }

    private void Update()
    {
        bool keyboardPressed = false;
        
#if UNITY_EDITOR || UNITY_STANDALONE     // allow spacebar to start in editor and standalone builds for easier testing
        var keyboard = Keyboard.current; // Check if the keyboard is available (it won't be on mobile devices)
        if (keyboard != null)
        {
            keyboardPressed = keyboard.spaceKey.wasPressedThisFrame;
        }
#endif

        if (keyboardPressed || DidTouchBeginThisFrame())
        {
            TriggerSignaling();
        }
    }

    private void TriggerSignaling()
    {
        if (signalingManager == null)
        {
            // Debug.LogWarning("ManualStart has no SignalingManager assigned.");
            return;
        }

        if (signalingManager.Running)
        {
            return;
        }

        signalingManager.Run();
        // Debug.Log("Signaling Started!");
    }

    private bool DidTouchBeginThisFrame()
    {
        // if (useEnhancedTouchInput && EnhancedTouchSupport.enabled)
        // {
        //     var touches = Touch.activeTouches; // Get the list of active touches from the Enhanced Touch system
        //     for (int i = 0; i < touches.Count; i++)
        //     {
        //         if (touches[i].phase == UnityEngine.InputSystem.TouchPhase.Began) // Check if any touch began this frame
        //         {
        //             return true;
        //         }
        //     }
        // }

        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            return true;
        }

        return false;
    }
}