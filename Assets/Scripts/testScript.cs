using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.RenderStreaming;
using UnityEngine;

[DisallowMultipleComponent]
public class testScript : SignalingHandlerBase,
	ICreatedConnectionHandler, IConnectHandler,
	IDisconnectHandler, IDeletedConnectionHandler
{
	[Header("UI")]
	[SerializeField] private TextMeshProUGUI statusLabel;
	[SerializeField] private bool autoAssignTextMesh = true;
	[TextArea] [SerializeField] private string idleMessage = "Waiting for Render Streaming peer...";
	[SerializeField] private Color idleColor = new Color(0.8f, 0.8f, 0.8f);

	[Header("Status Colors")]
	[SerializeField] private Color negotiatingColor = new Color(1f, 0.65f, 0f);
	[SerializeField] private Color connectedColor = Color.green;
	[SerializeField] private Color disconnectedColor = new Color(1f, 0.4f, 0.2f);
	[SerializeField] private Color closedColor = Color.gray;

	[Header("Signaling")]
	[SerializeField] private SignalingManager signalingManager;
	[SerializeField] private bool autoRegisterWithManager = true;

	private readonly Dictionary<string, ConnectionEntry> connectionEntries = new();
	private readonly StringBuilder textBuilder = new();
	private Color lastAppliedColor;
	private bool hasLastColor;
	private bool hasWarnedMissingLabel;
	private bool hasScreenBeenTouched;
	private bool lastRunningState;

	private enum ConnectionPhase
	{
		Negotiating,
		Connected,
		Disconnected,
		Closed
	}

	private sealed class ConnectionEntry
	{
		public ConnectionPhase Phase;
		public DateTime Timestamp;
	}

	private void Reset()
	{
		CacheStatusLabel();
	}

	private void Awake()
	{
		CacheStatusLabel();
		RenderStatusText();
	}

	private void OnEnable()
	{
		CacheStatusLabel();
		TryAutoRegister();
		
		if (!UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
		{
			UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
		}

		lastRunningState = signalingManager != null && signalingManager.Running;
		RenderStatusText();
	}

	private void Update()
	{
		bool needsRender = false;
		
		if (!hasScreenBeenTouched && DidTouchBeginThisFrame())
		{
			hasScreenBeenTouched = true;
			needsRender = true;
		}

		bool currentRunning = signalingManager != null && signalingManager.Running;
		if (currentRunning != lastRunningState)
		{
			lastRunningState = currentRunning;
			needsRender = true;
		}

		if (needsRender)
		{
			RenderStatusText();
		}
	}

	private bool DidTouchBeginThisFrame()
	{
		if (UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled)
		{
			var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
			for (int i = 0; i < touches.Count; i++)
			{
				if (touches[i].phase == UnityEngine.InputSystem.TouchPhase.Began) return true;
			}
		}

		var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
		if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame) return true;


#if UNITY_EDITOR || UNITY_STANDALONE
		var keyboard = UnityEngine.InputSystem.Keyboard.current;
		if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) return true;
#endif

		return false;
	}

	private void OnDisable()
	{
		if (autoRegisterWithManager && signalingManager != null)
		{
			signalingManager.RemoveSignalingHandler(this);
		}
	}

	private void OnValidate()
	{
		if (!Application.isPlaying)
		{
			CacheStatusLabel();
			RenderStatusText();
		}
	}

	public void OnCreatedConnection(SignalingEventData data)
	{
		UpdateConnectionEntry(data.connectionId, ConnectionPhase.Negotiating, negotiatingColor);
	}

	public void OnConnect(SignalingEventData data)
	{
		UpdateConnectionEntry(data.connectionId, ConnectionPhase.Connected, connectedColor);
	}

	public void OnDisconnect(SignalingEventData data)
	{
		UpdateConnectionEntry(data.connectionId, ConnectionPhase.Disconnected, disconnectedColor);
	}

	public void OnDeletedConnection(SignalingEventData data)
	{
		UpdateConnectionEntry(data.connectionId, ConnectionPhase.Closed, closedColor);
	}

	private void UpdateConnectionEntry(string connectionId, ConnectionPhase phase, Color color)
	{
		if (string.IsNullOrEmpty(connectionId))
			return;

		connectionEntries[connectionId] = new ConnectionEntry
		{
			Phase = phase,
			Timestamp = DateTime.UtcNow
		};

		lastAppliedColor = color;
		hasLastColor = true;
		RenderStatusText();
	}

	private void RenderStatusText()
	{
		if (statusLabel == null)
		{
			if (!hasWarnedMissingLabel)
			{
				// Debug.LogWarning($"{nameof(testScript)} on {name} is missing a TextMeshProUGUI reference.");
				hasWarnedMissingLabel = true;
			}
			return;
		}

		hasWarnedMissingLabel = false;
		textBuilder.Clear();
		
		textBuilder.AppendLine($"[Input] Screen Touched: {(hasScreenBeenTouched ? "<color=green>Yes</color>" : "<color=#ffaaaa>No</color>")}");
		textBuilder.AppendLine($"[Signaling] Triggered: {(lastRunningState ? "<color=green>Yes</color>" : "<color=yellow>Waiting...</color>")}");
		textBuilder.AppendLine("-----------------------------");

		if (connectionEntries.Count == 0)
		{
			textBuilder.Append(idleMessage);
			statusLabel.color = idleColor;
		}
		else
		{
			foreach (var entry in connectionEntries)
			{
				textBuilder.AppendLine($"{entry.Key}: {FormatEntry(entry.Value)}");
			}

			statusLabel.color = hasLastColor ? lastAppliedColor : idleColor;
		}

		statusLabel.text = textBuilder.ToString().TrimEnd();
	}

	private string FormatEntry(ConnectionEntry entry)
	{
		string phaseText = entry.Phase switch
		{
			ConnectionPhase.Negotiating => "Negotiating",
			ConnectionPhase.Connected => "Connected",
			ConnectionPhase.Disconnected => "Disconnected",
			ConnectionPhase.Closed => "Closed",
			_ => entry.Phase.ToString()
		};

		return $"{phaseText} @ {entry.Timestamp.ToLocalTime():HH:mm:ss}";
	}

	private void CacheStatusLabel()
	{
		if (statusLabel != null || !autoAssignTextMesh)
			return;

		statusLabel = GetComponentInChildren<TextMeshProUGUI>(true);
	}

	private void TryAutoRegister()
	{
		if (!autoRegisterWithManager)
			return;

		if (signalingManager == null)
		{
			signalingManager = FindSignalingManagerInstance();
		}

		if (signalingManager == null)
		{
			// Debug.LogWarning($"{nameof(testScript)} could not find a SignalingManager to register with.");
			return;
		}

		signalingManager.AddSignalingHandler(this);
	}

	private static SignalingManager FindSignalingManagerInstance()
	{
#if UNITY_2023_1_OR_NEWER
		return FindAnyObjectByType<SignalingManager>();
#else
		return FindObjectOfType<SignalingManager>();
#endif
	}
}
