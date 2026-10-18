using UnityEngine;
using Unity.RenderStreaming;

public class WebRTCOptimizerScript : MonoBehaviour
{
    [Header("Bitrate Settings (kbps)")]
    [Tooltip("Minimum bitrate in kbps")]
    public uint minBitrateKbps = 3000;

    [Tooltip("Target max bitrate in kbps")]
    [Min(1)]
    public uint targetBitrateKbps = 15000;

    [Header("Optional References")]
    [Tooltip("Assign specific senders, or leave empty to auto-find all VideoStreamSender components in the scene.")]
    [SerializeField] private VideoStreamSender[] videoSenders;

    [Tooltip("If assigned, delegate bitrate management to dynamic quality monitor.")]
    public DynamicStreamQualityManager dynamicQualityManager;

    void Start()
    {
        if (dynamicQualityManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            dynamicQualityManager = FindAnyObjectByType<DynamicStreamQualityManager>();
#else
            dynamicQualityManager = FindObjectOfType<DynamicStreamQualityManager>();
#endif
        }

        if (dynamicQualityManager != null)
        {
            Debug.Log("[WebRTCOptimizerScript] DynamicStreamQualityManager detected. Bitrate optimization delegated to dynamic monitor.");
            return;
        }

        if (videoSenders == null || videoSenders.Length == 0)
        {
#if UNITY_2023_1_OR_NEWER
            videoSenders = FindObjectsByType<VideoStreamSender>(FindObjectsSortMode.None);
#else
            videoSenders = FindObjectsOfType<VideoStreamSender>();
#endif
        }

        if (videoSenders == null || videoSenders.Length == 0)
        {
            Debug.LogWarning("[WebRTCOptimizerScript] No VideoStreamSender found in scene.");
            return;
        }

        for (int i = 0; i < videoSenders.Length; i++)
        {
            var sender = videoSenders[i];
            if (sender == null) continue;

            try
            {
                sender.SetBitrate(minBitrateKbps, targetBitrateKbps);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[WebRTCOptimizerScript] Failed setting bitrate: {ex.Message}");
            }
        }

        Debug.Log($"[WebRTCOptimizerScript] Applied bitrate range: {minBitrateKbps} - {targetBitrateKbps} kbps");
    }
}