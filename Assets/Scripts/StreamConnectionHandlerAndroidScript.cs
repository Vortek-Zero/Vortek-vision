using System.Collections;
using UnityEngine;
using Unity.RenderStreaming;
using UnityEngine.UI;

/// <summary>
/// Handles receiving the WebRTC desktop video stream on Android and mapping it
/// to a RawImage or 3D MeshRenderer (Quad/Wall screen). Includes connection state tracking,
/// disconnect event handling, and automatic recovery.
/// </summary>
public class StreamConnectionHandlerAndroidScript : SignalingHandlerBase, 
    ICreatedConnectionHandler, IConnectHandler, IDisconnectHandler, IDeletedConnectionHandler
{
    [Header("Stream Components")]
    [Tooltip("Drag the Video Stream Receiver component here (or auto-assigned if empty)")]
    public VideoStreamReceiver videoReceiver;

    [Tooltip("Drag the SingleConnection component here (or auto-assigned if empty)")]
    public SingleConnection singleConnection;

    [Tooltip("Optional SignalingManager reference for event monitoring")]
    public SignalingManager signalingManager;

    [Header("Render Targets")]
    [Tooltip("UI RawImage to display the video (e.g. Canvas HUD display)")]
    public RawImage targetRawImage;

    [Tooltip("MeshRenderer (e.g., Quad display in 3D world space)")]
    public MeshRenderer targetMeshRenderer;

    [Header("Connection Configuration")]
    [Tooltip("The Connection ID to receive from")]
    public string connectionId = "windowsStream";

    private bool streamReceivedLogged = false;
    private bool isConnectionActive = false;
    private float lastInitiatedTime;
    private Coroutine monitorCoroutine;

    void Start()
    {
        if (singleConnection == null)
        {
            singleConnection = GetComponent<SingleConnection>();
        }

        if (videoReceiver == null)
        {
            videoReceiver = GetComponent<VideoStreamReceiver>();
        }

        if (signalingManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            signalingManager = FindAnyObjectByType<SignalingManager>();
#else
            signalingManager = FindObjectOfType<SignalingManager>();
#endif
        }

        if (signalingManager != null)
        {
            signalingManager.AddSignalingHandler(this);
        }

        InitiateConnection();

        if (videoReceiver != null)
        {
            videoReceiver.OnUpdateReceiveTexture += OnUpdateReceiveTexture;
            
            // If VideoReceiver is using RenderTexture mode (Target Texture assigned), link it to RawImage immediately
            if (targetRawImage != null && videoReceiver.targetTexture != null)
            {
                targetRawImage.texture = videoReceiver.targetTexture;
                Debug.Log($"[StreamConnectionHandlerAndroid] Assigned RenderTexture '{videoReceiver.targetTexture.name}' to Target RawImage.");
            }
        }

        monitorCoroutine = StartCoroutine(MonitorStreamHealthRoutine());
    }

    public void InitiateConnection()
    {
        // Don't spam CreateConnection if it's already active or requested in the last 2 seconds
        if (isConnectionActive && streamReceivedLogged) return;
        if (Time.time - lastInitiatedTime < 2f) return;

        lastInitiatedTime = Time.time;

        if (singleConnection != null && !string.IsNullOrEmpty(connectionId))
        {
            singleConnection.CreateConnection(connectionId);
            Debug.Log($"[StreamConnectionHandlerAndroid] Requested connectionId: '{connectionId}'");
        }
        else
        {
            Debug.LogWarning("[StreamConnectionHandlerAndroid] SingleConnection or connectionId is unassigned.");
        }
    }

    public void OnCreatedConnection(SignalingEventData eventData)
    {
        if (eventData != null && (string.IsNullOrEmpty(eventData.connectionId) || eventData.connectionId == connectionId))
        {
            isConnectionActive = true;
            Debug.Log($"[StreamConnectionHandlerAndroid] WebRTC connection created for '{connectionId}'.");
        }
    }

    public void OnConnect(SignalingEventData eventData)
    {
        if (eventData != null && (string.IsNullOrEmpty(eventData.connectionId) || eventData.connectionId == connectionId))
        {
            isConnectionActive = true;
            Debug.Log($"[StreamConnectionHandlerAndroid] WebRTC Connected successfully for '{connectionId}'!");
        }
    }

    public void OnDisconnect(SignalingEventData eventData)
    {
        if (eventData != null && (string.IsNullOrEmpty(eventData.connectionId) || eventData.connectionId == connectionId))
        {
            Debug.LogWarning($"[StreamConnectionHandlerAndroid] Disconnect event received for connection '{eventData.connectionId}'.");
            isConnectionActive = false;
            streamReceivedLogged = false;
            InitiateConnection();
        }
    }

    public void OnDeletedConnection(SignalingEventData eventData)
    {
        if (eventData != null && (string.IsNullOrEmpty(eventData.connectionId) || eventData.connectionId == connectionId))
        {
            Debug.LogWarning($"[StreamConnectionHandlerAndroid] Connection deleted for '{eventData.connectionId}'.");
            isConnectionActive = false;
            streamReceivedLogged = false;
            InitiateConnection();
        }
    }

    private void OnUpdateReceiveTexture(Texture receiveTexture)
    {
        if (receiveTexture == null) return;

        isConnectionActive = true;
        receiveTexture.filterMode = FilterMode.Bilinear;

        if (!streamReceivedLogged)
        {
            Debug.Log($"[StreamConnectionHandlerAndroid] Stream received successfully! Resolution: {receiveTexture.width}x{receiveTexture.height}");
            streamReceivedLogged = true;
        }

        // Apply texture to RawImage UI
        if (targetRawImage != null)
        {
            targetRawImage.texture = receiveTexture;
        }

        // Apply texture to 3D MeshRenderer material
        if (targetMeshRenderer != null)
        {
            targetMeshRenderer.material.mainTexture = receiveTexture;
        }
    }

    private IEnumerator MonitorStreamHealthRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            // ONLY retry if connection is NOT active
            if (!isConnectionActive && !streamReceivedLogged)
            {
                Debug.Log($"[StreamConnectionHandlerAndroid] Stream not active yet. Retrying connection '{connectionId}'...");
                InitiateConnection();
            }
        }
    }

    void OnDestroy()
    {
        if (signalingManager != null)
        {
            signalingManager.RemoveSignalingHandler(this);
        }

        if (videoReceiver != null)
        {
            videoReceiver.OnUpdateReceiveTexture -= OnUpdateReceiveTexture;
        }

        if (monitorCoroutine != null)
        {
            StopCoroutine(monitorCoroutine);
        }
    }
}

