using UnityEngine;
using Unity.RenderStreaming;

public class SetConnectionIDs : MonoBehaviour
{
    [Header("PC to Android Settings")]
    public SignalingHandlerBase androidConnectionHandler;
    public string androidConnectionId = "windowsStream";

    [Header("Browser to PC Settings")]
    public SignalingHandlerBase browserConnectionHandler;
    public string browserConnectionId = "ScreenStream";

    [Header("Input Stream Settings")]
    public SignalingHandlerBase inputConnectionHandler;
    public string inputConnectionId = "InputStream";

    void Start()
    {
        if (androidConnectionHandler == null || browserConnectionHandler == null || inputConnectionHandler == null)
        {
            Debug.LogError("[SetConnectionIDs] Handlers are missing! Please assign Android (SingleConnection), Browser (SingleConnection), and Input (SingleConnection) in the Inspector.");
            return;
        }

        // Connection separation is done at the signaling handler level.
        CreateConnectionIfAssigned(androidConnectionHandler, androidConnectionId, "Android");
        CreateConnectionIfAssigned(browserConnectionHandler, browserConnectionId, "Browser");
        CreateConnectionIfAssigned(inputConnectionHandler, inputConnectionId, "Input");

        Debug.Log($"Connection IDs Set: Android={androidConnectionId}, Browser={browserConnectionId}, Input={inputConnectionId}");
    }

    static void CreateConnectionIfAssigned(SignalingHandlerBase handler, string connectionId, string label)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            Debug.LogWarning($"[SetConnectionIDs] {label} connection ID is empty.");
            return;
        }

        handler.CreateConnection(connectionId);
        Debug.Log($"[SetConnectionIDs] {label} is initiating connection to: {connectionId}");
    }
}
