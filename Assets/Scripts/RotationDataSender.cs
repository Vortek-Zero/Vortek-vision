using UnityEngine;
using UnityEngine.InputSystem;
using Unity.RenderStreaming;

[RequireComponent(typeof(SingleConnection))]
public class RotationDataSender : DataChannelBase
{
    [Header("Connection Settings")]
    [Tooltip("The Connection ID to send data to")]
    public string connectionId = "InputStream";

    public SingleConnection _singleConnection;

    [Header("Send Settings")]
    [Tooltip("Target send rate in Hz (messages per second, e.g. 60)")]
    public float sendRateHz = 60f;

    private float _nextLogTime;
    private float _nextSendTime;

    void Start()
    {
        if (_singleConnection == null)
        {
            _singleConnection = GetComponent<SingleConnection>();
        }

        EnableSensors();
        InitiateDataConnection();
    }

    public void InitiateDataConnection()
    {
        if (_singleConnection != null && !string.IsNullOrEmpty(connectionId))
        {
            _singleConnection.CreateConnection(connectionId);
            Debug.Log($"[RotationDataSender] Requesting connectionId: '{connectionId}'");
        }
    }

    private void EnableSensors()
    {
        // Enable legacy gyro for Android/iOS devices
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            Debug.Log("[RotationDataSender] Legacy Input.gyro enabled.");
        }

        if (AttitudeSensor.current != null)
        {
            InputSystem.EnableDevice(AttitudeSensor.current);
            Debug.Log("[RotationDataSender] AttitudeSensor enabled via InputSystem.");
        }

        if (UnityEngine.InputSystem.Gyroscope.current != null)
        {
            InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
        }
    }

    void Update()
    {
        if (!IsConnected)
        {
            if (Time.time > _nextLogTime)
            {
                Debug.Log($"[RotationDataSender] Waiting for DataChannel '{connectionId}' to connect. Retrying...");
                _nextLogTime = Time.time + 3f;
                InitiateDataConnection();
            }
            return;
        }

        if (Time.time < _nextSendTime) return;
        _nextSendTime = Time.time + (1f / sendRateHz);

        Quaternion q = Quaternion.identity;

        // Try reading from AttitudeSensor first
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
        {
            q = AttitudeSensor.current.attitude.ReadValue();
        }

        // Fallback to legacy Gyro if AttitudeSensor returned identity or zero
        if ((q == Quaternion.identity || IsZeroQuaternion(q)) && Input.gyro.enabled)
        {
            q = Input.gyro.attitude;
        }

        // Ignore invalid zero quaternions
        if (IsZeroQuaternion(q)) return;

        // Compact 4-float string serialization
        string msg = $"{q.x:F5},{q.y:F5},{q.z:F5},{q.w:F5}";
        Send(msg);
    }

    private static bool IsZeroQuaternion(Quaternion q)
    {
        return Mathf.Approximately(q.x, 0f) && Mathf.Approximately(q.y, 0f) &&
               Mathf.Approximately(q.z, 0f) && Mathf.Approximately(q.w, 0f);
    }

    protected override void OnOpen(string connectionId)
    {
        base.OnOpen(connectionId);
        Debug.Log($"[RotationDataSender] WebRTC DataChannel Opened for '{connectionId}'! Transmitting rotation data.");
    }

    protected override void OnClose(string connectionId)
    {
        base.OnClose(connectionId);
        Debug.LogWarning($"[RotationDataSender] WebRTC DataChannel Closed for '{connectionId}'");
    }
}
