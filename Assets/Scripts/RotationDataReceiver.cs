using UnityEngine;
using Unity.RenderStreaming;
using System.Text;
using TMPro;

[RequireComponent(typeof(SingleConnection))]
public class RotationDataReceiver : DataChannelBase
{
    [Header("Target Camera Object")]
    [Tooltip("Stereo Camera or XR Camera transform to rotate")]
    public Transform stereoCameraTarget;

    [Header("Calibration & Smoothing")]
    [Tooltip("Adjust starting direction (e.g. (90,0,0) for horizontal phone orientation)")]
    public Vector3 initialRotationOffset = new Vector3(90, 0, 0);

    [Tooltip("Smoothing factor for rotation interpolation (0 = instant, 15-25 = smooth VR tracking)")]
    [Range(0f, 50f)]
    public float smoothFactor = 20f;

    [Tooltip("Check this if Up/Down rotation is inverted.")]
    public bool invertPitch = true;
    [Tooltip("Check this if Left/Right rotation is inverted.")]
    public bool invertYaw = true;
    [Tooltip("Check this if Tilt/Roll is inverted.")]
    public bool invertRoll = false;

    [Header("UI Input Fields (Dynamic Offset)")]
    [Tooltip("Assign the TMPro Input Fields from your Canvas here")]
    // public TMP_InputField inputFieldX;
    public TMP_InputField inputFieldY;
    // public TMP_InputField inputFieldZ;


    [Header("Connection Settings")]
    public string connectionId = "InputStream";
    public SingleConnection _singleConnection;

    private Quaternion _targetRot = Quaternion.identity;
    private Quaternion _currentRot = Quaternion.identity;
    private float _nextLogTime;
    private bool _hasInitiated;

    void Start()
    {
        if (_singleConnection == null)
        {
            _singleConnection = GetComponent<SingleConnection>();
        }

        InitializeUIFields();

        if (inputFieldY != null) inputFieldY.onEndEdit.AddListener(UpdateOffsetFromUI);

        InitiateConnection();
    }

    public void InitiateConnection()
    {
        if (_singleConnection != null && !string.IsNullOrEmpty(connectionId))
        {
            _singleConnection.CreateConnection(connectionId);
            _hasInitiated = true;
            Debug.Log($"[RotationDataReceiver] Requested connectionId: '{connectionId}'");
        }
    }



    void Update()
    {
        if (!IsConnected)
        {
            if (Time.time > _nextLogTime)
            {
                Debug.Log($"[RotationDataReceiver] Waiting for DataChannel '{connectionId}' to connect. Retrying...");
                _nextLogTime = Time.time + 3f;
                InitiateConnection();
            }
            return;
        }

        // Apply rotation with optional Slerp smoothing
        if (smoothFactor > 0f)
        {
            _currentRot = Quaternion.Slerp(_currentRot, _targetRot, Time.deltaTime * smoothFactor);
        }
        else
        {
            _currentRot = _targetRot;
        }

        Quaternion finalRotation = Quaternion.Euler(initialRotationOffset) * _currentRot;

        if (stereoCameraTarget != null)
        {
            stereoCameraTarget.localRotation = finalRotation;
        }
        else
        {
            transform.localRotation = finalRotation;
        }
    }


    protected override void OnMessage(byte[] bytes)
    {
        base.OnMessage(bytes);

        if (bytes == null || bytes.Length == 0) return;
        
        string msg = Encoding.UTF8.GetString(bytes);

        string[] parts = msg.Split(',');
        if (parts.Length == 4)
        {
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z) &&
                float.TryParse(parts[3], out float w))
            {
                Quaternion rawRot = new Quaternion(-x, -y, -z, -w);
                Vector3 euler = rawRot.eulerAngles;

                float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
                float yaw = euler.y > 180f ? euler.y - 360f : euler.y;
                float roll = euler.z > 180f ? euler.z - 360f : euler.z;

                if (invertPitch) pitch = -pitch;
                if (invertYaw) yaw = -yaw;
                if (invertRoll) roll = -roll;

                _targetRot = Quaternion.Euler(pitch, yaw, roll);
            }
        }
    }

    protected override void OnOpen(string connectionId)
    {
        base.OnOpen(connectionId);
        Debug.Log($"[RotationDataReceiver] WebRTC DataChannel Opened for '{connectionId}'");
    }

    protected override void OnClose(string connectionId)
    {
        base.OnClose(connectionId);
        _hasInitiated = false;
        Debug.LogWarning($"[RotationDataReceiver] WebRTC DataChannel Closed for '{connectionId}'");
    }

    public void RecalibrateBaseline()
    {
        _targetRot = Quaternion.identity;
        _currentRot = Quaternion.identity;
        Debug.Log("[RotationDataReceiver] Recalibrated head orientation baseline.");
    }

    private void InitializeUIFields()
    {
        if (inputFieldY != null) inputFieldY.text = initialRotationOffset.y.ToString();
    }

    public void UpdateOffsetFromUI(string rawInputText)
    {
        if (float.TryParse(rawInputText, out float parsedY))
        {
            initialRotationOffset.y = parsedY;
            Debug.Log($"[RotationDataReceiver] Dynamic Y-offset changed to: {parsedY}");
        }
        else
        {
            Debug.LogWarning($"[RotationDataReceiver] Failed to parse input text '{rawInputText}' into a number.");
        }
    }
}
