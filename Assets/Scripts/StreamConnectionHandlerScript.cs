using UnityEngine;
using Unity.RenderStreaming;

/// <summary>
/// In newer versions of Unity Render Streaming, handshaking (WebRTC Offers/Answers)
/// is automatically handled by the `SingleConnection` or `Broadcast` components.
/// 
/// Below is how you receive the actual Video Texture and apply it.
/// </summary>
public class StreamConnectionHandler : MonoBehaviour
{
    [Tooltip("Drag the Video Stream Receiver component here")]
    public VideoStreamReceiver videoReceiver;

    [Tooltip("Drag the MeshRenderer (e.g., Quad) that will display the video")]
    public MeshRenderer targetRenderer;

    void Start()
    {
        if (videoReceiver != null)
        {
            // Subscribe to the event when the video texture updates
            videoReceiver.OnUpdateReceiveTexture += OnUpdateReceiveTexture;
        }
    }

    private bool streamReceivedLogged = false;

    private void OnUpdateReceiveTexture(Texture receiveTexture)
    {
        if (!streamReceivedLogged && receiveTexture != null)
        {
            Debug.Log($"[StreamConnectionHandler] Stream successfully received! Texture Size: {receiveTexture.width}x{receiveTexture.height}");
            streamReceivedLogged = true;
        }

        // Apply the received video texture to the material of the target renderer
        if (targetRenderer != null && receiveTexture != null)
        {
            targetRenderer.material.mainTexture = receiveTexture;
        }
    }

    void OnDestroy()
    {
        if (videoReceiver != null)
        {
            // Always unsubscribe to prevent memory leaks when the behavior is destroyed
            videoReceiver.OnUpdateReceiveTexture -= OnUpdateReceiveTexture;
        }
    }
}