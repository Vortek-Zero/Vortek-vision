using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// AR paper-style interaction for the floating stream quad:
///  - Tap on a vertical plane/wall  -> anchor the quad there (original behavior).
///  - Single-finger drag ON the quad -> move it freely, like holding a paper
///    (slides in a plane parallel to the camera, keeps grab offset).
///  - Two-finger pinch               -> resize the quad (clamped).
///
/// Unlike the original version, this script (and the AR managers) stay enabled
/// after placement so the window can be repositioned at any time.
/// </summary>
public class PlaceDisplayOnWallScript : MonoBehaviour
{
    [SerializeField] private GameObject targetQuad; // Assign the existing Quad from your scene here

    [Header("Paper Feel - Move")]
    [Tooltip("Camera used for drag math. Auto-found (main camera) if empty.")]
    [SerializeField] private Camera arCamera;

    [Header("Paper Feel - Resize")]
    [Tooltip("Minimum size relative to the scene-authored quad scale.")]
    [SerializeField] private float minScaleMultiplier = 0.25f;
    [Tooltip("Maximum size relative to the scene-authored quad scale.")]
    [SerializeField] private float maxScaleMultiplier = 4f;

    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    static List<ARRaycastHit> hits = new List<ARRaycastHit>(); // Reusable list to avoid allocations

    // Drag state
    private int dragFingerIndex = -1;
    private Plane dragPlane;
    private Vector3 dragOffset;

    // Pinch state
    private int pinchFingerA = -1;
    private int pinchFingerB = -1;
    private float pinchPrevDistance;
    private float pinchPrevAngle;
    private bool pinchAngleValid;

    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCaptured;
    private bool hasPlaced;

    void OnEnable()
    {
        EnhancedTouchSupport.Enable();
        Touch.onFingerDown += OnFingerDown;
        Touch.onFingerMove += OnFingerMove;
        Touch.onFingerUp += OnFingerUp;
        if (planeManager != null)
            planeManager.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        Touch.onFingerDown -= OnFingerDown;
        Touch.onFingerMove -= OnFingerMove;
        Touch.onFingerUp -= OnFingerUp;
        if (planeManager != null)
            planeManager.planesChanged -= OnPlanesChanged;
        EnhancedTouchSupport.Disable();
    }

    void Awake()
    {
        // Use FindAnyObjectByType so it finds them wherever they are in the scene (usually on XR Origin)
        raycastManager = FindAnyObjectByType<ARRaycastManager>();
        planeManager   = FindAnyObjectByType<ARPlaneManager>();

        if (arCamera == null)
        {
            arCamera = Camera.main;
        }

        if (targetQuad != null)
        {
            // The quad needs a collider so we can tell "grabbed the paper"
            // apart from "tapped the wall". Added at runtime: no scene edit needed.
            if (targetQuad.GetComponent<Collider>() == null)
            {
                var meshFilter = targetQuad.GetComponent<MeshFilter>();
                var meshCollider = targetQuad.AddComponent<MeshCollider>();
                if (meshFilter != null)
                {
                    meshCollider.sharedMesh = meshFilter.sharedMesh;
                }
                meshCollider.convex = false;
            }

            if (!baseScaleCaptured)
            {
                baseScale = targetQuad.transform.localScale;
                baseScaleCaptured = true;
            }
        }
    }

    private void OnFingerDown(Finger finger)
    {
        if (targetQuad == null || arCamera == null) return;

        // Second finger down -> switch to pinch, cancel any drag.
        if (dragFingerIndex != -1 && finger.index != dragFingerIndex)
        {
            pinchFingerA = dragFingerIndex;
            pinchFingerB = finger.index;
            pinchPrevDistance = PinchDistance();
            pinchPrevAngle = PinchAngle();
            pinchAngleValid = true;
            dragFingerIndex = -1;
            Debug.Log("[PlaceDisplay] Pinch started.");
            return;
        }

        if (dragFingerIndex != -1 || pinchFingerA != -1) return; // already tracking

        // Did the finger land ON the quad (grab the paper)?
        Ray ray = arCamera.ScreenPointToRay(finger.screenPosition);
        if (targetQuad.activeInHierarchy && Physics.Raycast(ray, out RaycastHit hit)
            && hit.transform == targetQuad.transform)
        {
            // Begin drag: slide in the camera-parallel plane through the grab point.
            dragFingerIndex = finger.index;
            dragPlane = new Plane(-arCamera.transform.forward, hit.point);
            dragOffset = targetQuad.transform.position - hit.point;
            Debug.Log("[PlaceDisplay] Drag started.");
        }
        else
        {
            // Tapped empty space/wall -> try to anchor there (original behavior).
            PerformRaycast(finger.screenPosition);
        }
    }

    private void OnFingerMove(Finger finger)
    {
        if (targetQuad == null || arCamera == null) return;

        // Pinch resize + twist rotate (like turning a paper on a table).
        if ((finger.index == pinchFingerA || finger.index == pinchFingerB)
            && pinchFingerA != -1 && pinchFingerB != -1)
        {
            float dist = PinchDistance();
            if (pinchPrevDistance > 0f && dist > 0f)
            {
                float ratio = dist / pinchPrevDistance;
                Vector3 newScale = targetQuad.transform.localScale * ratio;
                float currentMultiplier = newScale.x / baseScale.x;
                float clamped = Mathf.Clamp(currentMultiplier, minScaleMultiplier, maxScaleMultiplier);
                targetQuad.transform.localScale = baseScale * clamped;
            }
            pinchPrevDistance = dist;

            float ang = PinchAngle();
            if (pinchAngleValid)
            {
                // Signed screen-space twist; positive (CCW fingers) rotates the
                // paper CCW as seen by the viewer.
                float delta = Mathf.DeltaAngle(pinchPrevAngle, ang);
                targetQuad.transform.Rotate(arCamera.transform.forward, delta, Space.World);
            }
            pinchPrevAngle = ang;
            pinchAngleValid = true;
            return;
        }

        // Paper drag.
        if (finger.index == dragFingerIndex)
        {
            Ray ray = arCamera.ScreenPointToRay(finger.screenPosition);
            if (dragPlane.Raycast(ray, out float enter))
            {
                targetQuad.transform.position = ray.GetPoint(enter) + dragOffset;
            }
        }
    }

    private void OnFingerUp(Finger finger)
    {
        if (finger.index == dragFingerIndex)
        {
            dragFingerIndex = -1;
            Debug.Log("[PlaceDisplay] Drag ended.");
        }
        if (finger.index == pinchFingerA || finger.index == pinchFingerB)
        {
            pinchFingerA = -1;
            pinchFingerB = -1;
            pinchPrevDistance = 0f;
            pinchAngleValid = false;
            Debug.Log("[PlaceDisplay] Pinch ended.");
        }
    }

    private float PinchDistance()
    {
        Vector2? a = null, b = null;
        foreach (var t in Touch.activeTouches)
        {
            if (t.finger.index == pinchFingerA) a = t.screenPosition;
            if (t.finger.index == pinchFingerB) b = t.screenPosition;
        }
        if (a == null || b == null) return 0f;
        return Vector2.Distance(a.Value, b.Value);
    }

    private float PinchAngle()
    {
        Vector2? a = null, b = null;
        foreach (var t in Touch.activeTouches)
        {
            if (t.finger.index == pinchFingerA) a = t.screenPosition;
            if (t.finger.index == pinchFingerB) b = t.screenPosition;
        }
        if (a == null || b == null) return 0f;
        Vector2 d = b.Value - a.Value;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

#if UNITY_EDITOR
    void Update()
    {
        if (UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame)
        {
            PerformRaycast(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        }
    }
#endif

    private void PerformRaycast(Vector2 screenPosition)
    {
        if (raycastManager == null || planeManager == null) return;

        if(raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon))
        {
            var hitPose = hits[0].pose;
            var hitTrackableId = hits[0].trackableId;
            var plane = planeManager.GetPlane(hitTrackableId);

            if (plane != null && plane.alignment == PlaneAlignment.Vertical)
            {
                PlaceQuad(hitPose);
            }
        }
    }

    void PlaceQuad(Pose hitPose)
    {
        // Move and activate the existing Quad
        targetQuad.transform.position = hitPose.position;

        // Use the wall's normal (hitPose.up) to orient the Quad.
        // We use Quaternion.LookRotation to ensure its 'up' vector remains perfectly parallel to the world's gravity (Vector3.up).
        // A Unity default Quad faces the -Z axis. Pointing its +Z axis INTO the wall (-hitPose.up)
        // makes the viewable side face outward comfortably towards the user, removing any tilted angle.
        targetQuad.transform.rotation = Quaternion.LookRotation(-hitPose.up, Vector3.up);

        // Depending on your prefab, you may need a slight adjustment so it doesn't z-fight with the wall:
        // targetQuad.transform.position += hitPose.up * 0.01f;

        targetQuad.SetActive(true);
        Debug.Log($"[PlaceDisplay] Quad anchored at {hitPose.position}.");

        // Once the window has a home, clear the tracking visuals (dots/planes).
        // Managers stay ON: raycasts still hit the (hidden) planes, so the
        // window can be re-anchored with another tap at any time.
        hasPlaced = true;
        HidePlaneVisuals();

        // NOTE: intentionally NOT disabling planes / raycast / this script anymore,
        // so the window can be re-anchored, dragged and pinched at any time.
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!hasPlaced) return; // still aiming: leave the dots visible
        foreach (var plane in args.added)
            plane.gameObject.SetActive(false);
        foreach (var plane in args.updated)
            plane.gameObject.SetActive(false);
    }

    private void HidePlaneVisuals()
    {
        if (planeManager != null)
        {
            foreach (var plane in planeManager.trackables)
                plane.gameObject.SetActive(false);
        }

        var pointCloudManager = FindAnyObjectByType<ARPointCloudManager>();
        if (pointCloudManager != null)
        {
            foreach (var cloud in pointCloudManager.trackables)
                cloud.gameObject.SetActive(false);
            pointCloudManager.enabled = false;
            Debug.Log("[PlaceDisplay] Plane/point-cloud visuals hidden.");
        }
    }
}
