using System.Collections;
using UnityEngine;
using Unity.RenderStreaming;

/// <summary>
/// DynamicStreamQualityManager automatically monitors stream performance and network stability,
/// dynamically scaling video bitrate up or down.
/// If connection starts poor, stream starts at conservative bitrate. If connection improves,
/// it automatically upgrades to high quality (up to max Bitrate, e.g. 15 Mbps / 60 FPS).
/// </summary>
public class DynamicStreamQualityManager : MonoBehaviour
{
    [Header("Target Bitrate Limits (Kbps)")]
    [Tooltip("Minimum bitrate for weak connections (e.g. 2000 Kbps = 2 Mbps)")]
    public uint minBitrateKbps = 30000; //5000; // 5000 

    [Tooltip("Maximum bitrate for strong connections (e.g. 15000 Kbps = 15 Mbps)")]
    public uint maxBitrateKbps = 100000; //10000;

    [Tooltip("Initial bitrate tier to start with on connection")]
    public uint initialBitrateKbps = 70000; //7500;

    [Header("Quality Adjustment Controls")]
    [Tooltip("Step size (Kbps) when upgrading quality after stable connection")]
    public uint stepUpKbps = 25000; //2000;

    [Tooltip("Step size (Kbps) when dropping quality due to lag or frame drops")]
    public uint stepDownKbps = 25000; //3000;

    [Tooltip("Evaluation interval in seconds")]
    public float evaluationIntervalSeconds = 3f;

    [Tooltip("FPS threshold below which stream bitrate is stepped down")]
    public float lowFpsThreshold = 25f;

    [Tooltip("FPS threshold above which stream bitrate can step up")]
    public float highFpsThreshold = 55f;

    [Tooltip("Number of consecutive stable checks required before stepping up quality")]
    public int requiredStableChecksToStepUp = 2;

    [Header("Targets")]
    [Tooltip("Assign VideoStreamSender components, or leave empty to auto-find in scene")]
    public VideoStreamSender[] videoSenders;

    public uint CurrentBitrateKbps { get; private set; }

    private float fpsBuffer;
    private int frameCounter;
    private float lastSampleTime;
    private int consecutiveStableChecks;

    void Start()
    {
        CurrentBitrateKbps = initialBitrateKbps;

        if (videoSenders == null || videoSenders.Length == 0)
        {
            FindVideoSenders();
        }

        ApplyCurrentBitrate();
        StartCoroutine(QualityMonitoringLoop());
    }

    public void FindVideoSenders()
    {
#if UNITY_2023_1_OR_NEWER
        videoSenders = FindObjectsByType<VideoStreamSender>(FindObjectsSortMode.None);
#else
        videoSenders = FindObjectsOfType<VideoStreamSender>();
#endif
    }

    private IEnumerator QualityMonitoringLoop()
    {
        lastSampleTime = Time.time;
        frameCounter = 0;

        while (true)
        {
            yield return new WaitForSecondsRealtime(evaluationIntervalSeconds);

            if (videoSenders == null || videoSenders.Length == 0)
            {
                FindVideoSenders();
                if (videoSenders != null && videoSenders.Length > 0)
                {
                    ApplyCurrentBitrate();
                }
                else
                {
                    continue;
                }
            }
            else
            {
                // Re-apply bitrate periodically to ensure newly negotiated WebRTC transceivers get high quality limits
                ApplyCurrentBitrate();
            }

            float elapsedTime = Time.time - lastSampleTime;
            if (elapsedTime <= 0f) continue;


            float currentFps = frameCounter / elapsedTime;
            frameCounter = 0;
            lastSampleTime = Time.time;

            EvaluateAndUpdateQuality(currentFps);
        }
    }

    void Update()
    {
        frameCounter++;
    }

    private void EvaluateAndUpdateQuality(float currentFps)
    {
        if (currentFps < lowFpsThreshold)
        {
            consecutiveStableChecks = 0;
            if (CurrentBitrateKbps > minBitrateKbps)
            {
                uint newBitrate = (uint)Mathf.Max(minBitrateKbps, (long)CurrentBitrateKbps - stepDownKbps);
                if (newBitrate != CurrentBitrateKbps)
                {
                    CurrentBitrateKbps = newBitrate;
                    Debug.LogWarning($"[DynamicStreamQualityManager] Low performance detected (FPS: {currentFps:F1}). Stepping down bitrate to {CurrentBitrateKbps} Kbps.");
                    ApplyCurrentBitrate();
                }
            }
        }
        else if (currentFps >= highFpsThreshold)
        {
            consecutiveStableChecks++;
            if (consecutiveStableChecks >= requiredStableChecksToStepUp)
            {
                consecutiveStableChecks = 0;
                if (CurrentBitrateKbps < maxBitrateKbps)
                {
                    uint newBitrate = (uint)Mathf.Min(maxBitrateKbps, (long)CurrentBitrateKbps + stepUpKbps);
                    if (newBitrate != CurrentBitrateKbps)
                    {
                        CurrentBitrateKbps = newBitrate;
                        Debug.Log($"[DynamicStreamQualityManager] Connection stable (FPS: {currentFps:F1}). Upgrading bitrate to {CurrentBitrateKbps} Kbps.");
                        ApplyCurrentBitrate();
                    }
                }
            }
        }
        else
        {
            consecutiveStableChecks = 0;
        }
    }

    public void ApplyCurrentBitrate()
    {
        if (videoSenders == null) return;

        foreach (var sender in videoSenders)
        {
            if (sender == null) continue;
            try
            {
                sender.SetBitrate(minBitrateKbps, CurrentBitrateKbps);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DynamicStreamQualityManager] Failed setting bitrate on sender: {ex.Message}");
            }
        }
    }

    public void ForceMaxQuality()
    {
        CurrentBitrateKbps = maxBitrateKbps;
        ApplyCurrentBitrate();
        Debug.Log($"[DynamicStreamQualityManager] Forced Maximum Bitrate: {maxBitrateKbps} Kbps");
    }
}
