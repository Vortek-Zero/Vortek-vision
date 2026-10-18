using UnityEngine;
using Unity.RenderStreaming;

[RequireComponent(typeof(VideoStreamSender))]
public class highSpeedVideoStreamingScript : MonoBehaviour
{
    public VideoStreamSender videoStreamSender;

    // Minimum and Maximum Bitrate in Kbps
    public uint minBitrateKbps = 8000;   // 8 Mbps
    public uint maxBitrateKbps = 15000;  // 15 Mbps

    void Start()
    {
        if (videoStreamSender == null)
        {
            videoStreamSender = GetComponent<VideoStreamSender>();
        }

        if (videoStreamSender != null)
        {
            try
            {
                videoStreamSender.SetBitrate(minBitrateKbps, maxBitrateKbps);
                Debug.Log($"[HighSpeedScript] Bitrate set: Min {minBitrateKbps}Kbps, Max {maxBitrateKbps}Kbps.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HighSpeedScript] Failed to set bitrate: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning("[HighSpeedScript] VideoStreamSender component missing!");
        }
    }
}

