using System;
using System.Linq;
using System.Collections;
using UnityEngine;
using TMPro;
using Unity.RenderStreaming;

public class WebServerIPScript : SignalingHandlerBase, IConnectHandler, IDisconnectHandler
{
    [Header("Default Server Address")]
    [SerializeField] private string defaultWebSocketUrl = "192.170.2.137:80";

    [Header("New URL Input")]
    [SerializeField] private string playerPrefsKey = "WebSocketServerUrl";

    [SerializeField] private TMP_InputField serverAddressInputField;
    [SerializeField] private SignalingManager signalingManager;
    [SerializeField] private bool restartSignalingWhenAddressChanges = true;

    [Tooltip("If true, the script loads the previously saved URL from PlayerPrefs at startup.")]
    [SerializeField] private bool loadSavedUrlOnStart = true;

    [Header("Auto Reconnect Settings")]
    [SerializeField] private bool autoReconnectOnDisconnect = true;
    [SerializeField] private float reconnectRetryInterval = 5f;

    [Header("Status UI")]
    [Tooltip("Text element to show connection status.")]
    [SerializeField] private TMP_Text connectionStatusText;
    [Tooltip("How long to show the status text before disappearing.")]
    [SerializeField] private float statusDisplayDuration = 3f;

    [Tooltip("Current WebSocket URL used by the app.")]
    public string webServerIP;

    private Coroutine statusCoroutine;
    private Coroutine reconnectCoroutine;
    private bool isConnected;

    private void Start()
    {
        if (signalingManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            signalingManager = FindAnyObjectByType<SignalingManager>();
#else
            signalingManager = FindObjectOfType<SignalingManager>();
#endif
        }

        InitializeServerUrl();
        
        if (signalingManager != null)
        {
            signalingManager.AddSignalingHandler(this);
        }

        if (serverAddressInputField != null)
        {
            serverAddressInputField.onEndEdit.RemoveListener(OnInputFieldEndEdit);
            serverAddressInputField.onEndEdit.AddListener(OnInputFieldEndEdit);
        }
    }

    private void OnDestroy()
    {
        if (signalingManager != null)
        {
            signalingManager.RemoveSignalingHandler(this);
        }

        if (serverAddressInputField != null)
        {
            serverAddressInputField.onEndEdit.RemoveListener(OnInputFieldEndEdit);
        }
    }

    private void OnInputFieldEndEdit(string value)
    {
        ApplyAddressFromInputField();
    }

    public void OnConnect(SignalingEventData eventData)
    {
        isConnected = true;
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }
        ShowStatusMessage("Connected to Server!", Color.green);
    }

    public void OnDisconnect(SignalingEventData eventData)
    {
        isConnected = false;
        ShowStatusMessage("Disconnected from Server", Color.red);
        
        if (autoReconnectOnDisconnect && gameObject.activeInHierarchy && reconnectCoroutine == null)
        {
            reconnectCoroutine = StartCoroutine(AutoReconnectRoutine());
        }
    }

    private IEnumerator AutoReconnectRoutine()
    {
        while (!isConnected)
        {
            yield return new WaitForSeconds(reconnectRetryInterval);
            if (!isConnected && signalingManager != null)
            {
                ShowStatusMessage("Attempting Reconnection...", Color.yellow);
                if (!signalingManager.Running)
                {
                    signalingManager.Run();
                }
                else
                {
                    ApplyUrlToSignalingManager();
                }
            }
        }
        reconnectCoroutine = null;
    }

    private void InitializeServerUrl()
    {
        string candidateUrl = defaultWebSocketUrl;

        if (loadSavedUrlOnStart && PlayerPrefs.HasKey(playerPrefsKey))
        {
            string saved = PlayerPrefs.GetString(playerPrefsKey);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                candidateUrl = saved;
            }
        }

        if (!TryNormalizeWebSocketUrl(candidateUrl, out webServerIP))
        {
            TryNormalizeWebSocketUrl(defaultWebSocketUrl, out webServerIP);
            Debug.LogWarning($"Invalid saved URL '{candidateUrl}'. Falling back to default '{webServerIP}'.");
        }

        Debug.Log($"WebSocket Server URL initialized to: {webServerIP}");

        ApplyUrlToSignalingManager();

        if (serverAddressInputField != null)
        {
            serverAddressInputField.text = ToHostPortDisplay(webServerIP);
        }
    }

    public bool SetWebSocketAddress(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            ShowStatusMessage("Failed: Input is empty", Color.red);
            return false;
        }

        if (!TryNormalizeWebSocketUrl(userInput, out string normalizedUrl))
        {
            Debug.LogWarning($"Invalid input: '{userInput}'. Expected IP or IP:Port (e.g. 192.168.1.50:80 or ws://192.168.1.50:80)");
            ShowStatusMessage("Failed: Invalid IP/URL format", Color.red);
            return false;
        }

        webServerIP = normalizedUrl;
        PlayerPrefs.SetString(playerPrefsKey, webServerIP);
        PlayerPrefs.Save();

        Debug.Log($"Updated WebSocket Server URL to: {webServerIP}");

        ApplyUrlToSignalingManager();

        string displayAddress = ToHostPortDisplay(webServerIP);
        if (serverAddressInputField != null && serverAddressInputField.text != displayAddress)
        {
            serverAddressInputField.text = displayAddress;
        }

        return true;
    }

    public void ApplyAddressFromInputField()
    {
        if (serverAddressInputField == null)
        {
            Debug.LogWarning("Server address input field is not assigned.");
            return;
        }

        SetWebSocketAddress(serverAddressInputField.text);
    }

    public void ResetToDefaultAddress()
    {
        if (TryNormalizeWebSocketUrl(defaultWebSocketUrl, out string normalizedDefault))
        {
            webServerIP = normalizedDefault;
            PlayerPrefs.SetString(playerPrefsKey, webServerIP);
            PlayerPrefs.Save();
            ApplyUrlToSignalingManager();
            if (serverAddressInputField != null)
            {
                serverAddressInputField.text = ToHostPortDisplay(webServerIP);
            }
            Debug.Log($"Reset WebSocket Server URL to default: {webServerIP}");
        }
        else
        {
            Debug.LogError($"Default WebSocket URL is invalid: '{defaultWebSocketUrl}'");
        }
    }

    public static bool TryNormalizeWebSocketUrl(string input, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string candidate = input.Trim();

        // Strip leading http/https/ws/wss if present to extract pure host/port or re-format cleanly
        if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "ws://" + candidate.Substring(7);
        }
        else if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "wss://" + candidate.Substring(8);
        }
        else if (!candidate.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && 
                 !candidate.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = "ws://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        string scheme = uri.Scheme;
        if (scheme != "ws" && scheme != "wss")
        {
            scheme = "ws";
        }

        int port = uri.Port;
        if (port == -1)
        {
            port = 80; // default signaling port
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Port = port
        };

        normalizedUrl = builder.Uri.ToString().TrimEnd('/');
        return true;
    }

    private static string ToHostPortDisplay(string wsUrl)
    {
        if (string.IsNullOrWhiteSpace(wsUrl))
        {
            return string.Empty;
        }

        string value = wsUrl.Trim();
        if (value.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(5);
        }
        else if (value.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(6);
        }

        return value.TrimEnd('/');
    }

    private void ApplyUrlToSignalingManager()
    {
        if (signalingManager == null)
        {
            return;
        }

        IceServer[] clonedIceServers = null;
        SignalingSettings currentSettings = signalingManager.GetSignalingSettings();
        if (currentSettings is WebSocketSignalingSettings wsSettings)
        {
            if (AreUrlsEquivalent(wsSettings.url, webServerIP))
            {
                if (!signalingManager.Running)
                {
                    signalingManager.Run();
                }
                return;
            }

            if (wsSettings.iceServers != null)
            {
                clonedIceServers = wsSettings.iceServers.Select(server => server.Clone()).ToArray();
            }
        }

        bool wasRunning = signalingManager.Running;

        if (wasRunning && restartSignalingWhenAddressChanges)
        {
            signalingManager.Stop();
        }

        try
        {
            signalingManager.SetSignalingSettings(new WebSocketSignalingSettings(webServerIP, clonedIceServers));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Exception setting signaling settings: {ex.Message}");
        }

        if ((wasRunning || !signalingManager.Running) && restartSignalingWhenAddressChanges)
        {
            signalingManager.Run();
        }

        ShowStatusMessage($"Connecting to: {ToHostPortDisplay(webServerIP)}", Color.yellow);
    }

    private void ShowStatusMessage(string message, Color color)
    {
        if (connectionStatusText != null)
        {
            if (statusCoroutine != null)
            {
                StopCoroutine(statusCoroutine);
            }
            connectionStatusText.color = color;
            statusCoroutine = StartCoroutine(ShowStatusRoutine(message));
        }
    }

    private IEnumerator ShowStatusRoutine(string message)
    {
        connectionStatusText.text = message;
        connectionStatusText.gameObject.SetActive(true);

        yield return new WaitForSeconds(statusDisplayDuration);

        connectionStatusText.gameObject.SetActive(false);
    }

    private static bool AreUrlsEquivalent(string left, string right)
    {
        if (!TryNormalizeWebSocketUrl(left, out string leftNormalized))
        {
            return false;
        }

        if (!TryNormalizeWebSocketUrl(right, out string rightNormalized))
        {
            return false;
        }

        return string.Equals(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase);
    }
}

