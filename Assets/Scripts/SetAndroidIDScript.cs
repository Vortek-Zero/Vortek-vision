using UnityEngine;
using Unity.RenderStreaming;

public class SetAndroidIDScript : MonoBehaviour
{
    [Header("Broadcast Handler")]
    public SingleConnection androidBroadcastHandler;

    [Header("Connection ID")]
    public string connectionId = "android_vr";

    void Start()
    {
        // Newer Render Streaming versions do not expose `.id` on VideoStreamReceiver/InputSender.
        // The connection is identified when opening signaling with the target connectionId.
        if (androidBroadcastHandler == null)
        {
            Debug.LogWarning("[SetAndroidIDScript] Android handler is not assigned.");
            return;
        }

        if (string.IsNullOrWhiteSpace(connectionId))
        {
            Debug.LogWarning("[SetAndroidIDScript] connectionId is empty.");
            return;
        }

        androidBroadcastHandler.CreateConnection(connectionId);
        Debug.Log($"[SetAndroidIDScript] Requested connectionId: {connectionId}");
    }
}