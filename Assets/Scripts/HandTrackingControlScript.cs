using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.RenderStreaming;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Camada de TRANSPORTE + coordenadas do hand tracking.
///
/// Responsabilidades (SOMENTE estas):
///  - capturar frames da câmera AR e enviar JPEG ao servidor (o CÉREBRO) no PC,
///  - receber o JSON de COMANDO pronto e repassá-lo via evento OnCommand,
///  - fornecer o mapeamento imagem -> tela (ImageToScreen) usando o
///    displayMatrix do ARCore + flags de flip vindas do servidor,
///  - auto-colocar a janela no ponto de mira quando a stream começa.
///
/// A lógica de gesto/modo NÃO fica aqui: ela é 100% do servidor
/// ("web app/handtracking/server.py"). O celular só executa o comando.
/// Ver HandPanelInteractionController (HandTracking/) = executor.
/// </summary>
public class HandTrackingControlScript : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The Quad GameObject that displays the stream")]
    [SerializeField] private GameObject targetQuad;
    [Tooltip("ARCameraManager (used to capture frames for hand tracking)")]
    [SerializeField] private ARCameraManager arCameraManager;
    [Tooltip("VideoStreamReceiver - used to detect stream start and auto-place the window")]
    [SerializeField] private VideoStreamReceiver videoReceiver;

    [Header("Hand tracking server")]
    [SerializeField] private string serverUrl = "ws://192.168.1.102:8787";
    [SerializeField] private int captureWidth = 480;
    [SerializeField] private int jpegQuality = 50;
    [SerializeField] private float captureFps = 12f;

    [Header("Auto-place")]
    [Tooltip("Degrees the window is tilted back (monitor-like)")]
    [SerializeField] private float tiltDegrees = 10f;
    [Tooltip("Distance (m) from camera where the window appears if no plane is hit")]
    [SerializeField] private float fallbackDistance = 1.5f;
    [Tooltip("Enable verbose logs")]
    [SerializeField] private bool verboseLogs = false;

    /// <summary>Comando pronto do servidor (o executor consome).</summary>
    public event Action<HandCommand> OnCommand;
    /// <summary>Disparado quando a janela é auto-colocada.</summary>
    public event Action OnWindowPlaced;

    public GameObject Panel => targetQuad;
    public bool HasPlaced => hasPlaced;

    // ---- (JSON é parseado direto no HandCommand: os campos batem com server.py) ----

    // ---- WebSocket plumbing ----
    private ClientWebSocket ws;
    private CancellationTokenSource cts;
    private volatile bool wsOpen;
    private volatile bool sendingFrame;
    private readonly ConcurrentQueue<string> incomingJson = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<byte[]> outgoingFrames = new ConcurrentQueue<byte[]>();

    // ---- depuração da cadeia de coordenadas (§9D: mostra cada etapa no overlay) ----
    public float LastU, LastV;                 // normalizado vindo do servidor (origem topo-esq)
    public float LastNormX, LastNormY;         // pós-matrix/flips, pré-pixel (0..1)
    public bool LastUsedMatrix;                // false = caiu no fallback (suspeito nº 1)
    public Vector2 LastScreen;                 // pixels p/ ScreenPointToRay (origem embaixo)

    // ---- Frame capture ----
    private Texture2D cameraTex;
    private Matrix4x4? displayMatrix;
    private float lastCaptureTime;
    private ARRaycastManager raycastManager;
    private ARPlaneManager planeManager;
    private ARAnchorManager anchorManager;
    private Camera arCamera;
    private bool hasPlaced;
    private bool streamStarted;
    private int flipX, flipY; // enviados pelo servidor (correção sem rebuild)
    private static readonly System.Collections.Generic.List<ARRaycastHit> hits =
        new System.Collections.Generic.List<ARRaycastHit>();

    void Awake()
    {
        raycastManager = FindAnyObjectByType<ARRaycastManager>();
        planeManager = FindAnyObjectByType<ARPlaneManager>();
        anchorManager = FindAnyObjectByType<ARAnchorManager>();
        arCamera = Camera.main;
    }

    void OnEnable()
    {
        if (arCameraManager != null)
            arCameraManager.frameReceived += OnCameraFrame;
        if (videoReceiver != null)
            videoReceiver.OnUpdateReceiveTexture += OnReceiveTexture;
    }

    void OnDisable()
    {
        if (arCameraManager != null)
            arCameraManager.frameReceived -= OnCameraFrame;
        if (videoReceiver != null)
            videoReceiver.OnUpdateReceiveTexture -= OnReceiveTexture;
    }

    void Start() => StartWebSocket();

    void OnDestroy()
    {
        try { cts?.Cancel(); } catch { }
        try { ws?.Dispose(); } catch { }
    }

    // ------------------------------------------------------------------
    //  WebSocket
    // ------------------------------------------------------------------
    private void StartWebSocket()
    {
        cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(serverUrl), cts.Token);
                    wsOpen = true;
                    Debug.Log($"[HandTrack] conectado: {serverUrl}");

                    while (outgoingFrames.TryDequeue(out byte[] queued))
                        await ws.SendAsync(new ArraySegment<byte>(queued),
                            WebSocketMessageType.Binary, true, cts.Token);

                    _ = Task.Run(async () =>
                    {
                        while (wsOpen && !cts.IsCancellationRequested)
                        {
                            if (outgoingFrames.TryDequeue(out byte[] frame))
                            {
                                sendingFrame = true;
                                try
                                {
                                    await ws.SendAsync(new ArraySegment<byte>(frame),
                                        WebSocketMessageType.Binary, true, cts.Token);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning("[HandTrack] send falhou: " + e.Message);
                                    wsOpen = false;
                                }
                                sendingFrame = false;
                            }
                            else
                            {
                                await Task.Delay(5, CancellationToken.None);
                            }
                        }
                    }, CancellationToken.None);

                    byte[] buffer = new byte[256 * 1024];
                    while (wsOpen && !cts.IsCancellationRequested)
                    {
                        var sb = new System.Text.StringBuilder();
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                wsOpen = false;
                                break;
                            }
                            sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
                        } while (!result.EndOfMessage);

                        if (wsOpen)
                            incomingJson.Enqueue(sb.ToString());
                    }
                }
                catch (Exception e)
                {
                    if (!cts.IsCancellationRequested)
                        Debug.LogWarning("[HandTrack] WebSocket: " + e.Message);
                }
                wsOpen = false;
                await Task.Delay(2000, CancellationToken.None);
            }
        }, cts.Token);
    }

    // ------------------------------------------------------------------
    //  Camera capture (throttled)
    // ------------------------------------------------------------------
    private void OnCameraFrame(ARCameraFrameEventArgs args)
    {
        if (args.displayMatrix.HasValue)
            displayMatrix = args.displayMatrix.Value;
    }

    private void CaptureAndSendFrame()
    {
        if (arCameraManager == null || !wsOpen || sendingFrame) return;
        if (outgoingFrames.Count > 1) return;

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
            return;

        using (image)
        {
            int w = captureWidth;
            int h = Mathf.Max(2, (image.height * w) / image.width);
            h -= h % 2;

            var conversion = new XRCpuImage.ConversionParams(image, TextureFormat.RGBA32,
                XRCpuImage.Transformation.None);
            conversion.outputDimensions = new Vector2Int(w, h);

            int size = image.GetConvertedDataSize(conversion);
            var buffer = new NativeArray<byte>(size, Allocator.Temp);
            try
            {
                image.Convert(conversion, new NativeSlice<byte>(buffer));

                if (cameraTex == null || cameraTex.width != w || cameraTex.height != h)
                {
                    if (cameraTex != null) Destroy(cameraTex);
                    cameraTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                }
                cameraTex.LoadRawTextureData(buffer);
                cameraTex.Apply(false, false);

                byte[] jpg = cameraTex.EncodeToJPG(jpegQuality);
                if (jpg != null && jpg.Length > 0)
                    outgoingFrames.Enqueue(jpg);
            }
            finally
            {
                buffer.Dispose();
            }
        }
    }

    // ------------------------------------------------------------------
    //  Stream start -> auto place window at gaze point
    // ------------------------------------------------------------------
    private void OnReceiveTexture(Texture receiveTexture)
    {
        if (streamStarted || receiveTexture == null) return;
        streamStarted = true;
        Debug.Log("[HandTrack] stream iniciada - posicionando janela no ponto de mira.");
        PlaceAtGaze();
    }

    private void PlaceAtGaze()
    {
        if (targetQuad == null || arCamera == null || hasPlaced) return;

        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Pose? chosen = null;

        if (raycastManager != null && raycastManager.Raycast(center, hits, TrackableType.PlaneWithinPolygon))
        {
            int best = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                var plane = planeManager != null ? planeManager.GetPlane(hits[i].trackableId) : null;
                if (plane != null && plane.alignment == PlaneAlignment.Vertical)
                {
                    best = i;
                    break;
                }
            }
            chosen = hits[best].pose;
        }

        Pose pose;
        if (chosen.HasValue)
        {
            pose = chosen.Value;
        }
        else
        {
            var camT = arCamera.transform;
            pose = new Pose(camT.position + camT.forward * fallbackDistance,
                Quaternion.LookRotation(-camT.forward, Vector3.up));
        }

        Quaternion rotation = Quaternion.LookRotation(-pose.up, Vector3.up)
                             * Quaternion.Euler(tiltDegrees, 0f, 0f);

        if (anchorManager != null && targetQuad.transform.parent == null)
        {
            var anchorGO = new GameObject("WindowAnchor");
            anchorGO.transform.SetPositionAndRotation(pose.position, rotation);
            anchorGO.AddComponent<ARAnchor>();
            targetQuad.transform.SetParent(anchorGO.transform, true);
        }

        targetQuad.transform.SetPositionAndRotation(pose.position, rotation);
        targetQuad.SetActive(true);
        hasPlaced = true;
        HidePlaneVisuals();
        OnWindowPlaced?.Invoke();
        Debug.Log($"[HandTrack] janela colocada em {pose.position:F2}");
    }

    private void HidePlaneVisuals()
    {
        if (planeManager != null)
            foreach (var plane in planeManager.trackables)
                plane.gameObject.SetActive(false);

        var pointCloudManager = FindAnyObjectByType<ARPointCloudManager>();
        if (pointCloudManager != null)
        {
            foreach (var cloud in pointCloudManager.trackables)
                cloud.gameObject.SetActive(false);
            pointCloudManager.enabled = false;
        }
    }

    // ------------------------------------------------------------------
    //  Main loop: captura + repasse do frame de gestos
    // ------------------------------------------------------------------
    void Update()
    {
        if (Time.unscaledTime - lastCaptureTime >= 1f / Mathf.Max(1f, captureFps))
        {
            lastCaptureTime = Time.unscaledTime;
            CaptureAndSendFrame();
        }

        HandCommand latest = null;
        while (incomingJson.TryDequeue(out string json))
        {
            try { latest = JsonUtility.FromJson<HandCommand>(json); }
            catch { /* frame malformado: ignora */ }
        }

        if (latest == null) return;

        flipX = latest.fx;
        flipY = latest.fy;

        if (verboseLogs)
            Debug.Log($"[HandTrack] mode={latest.mode} count={latest.count} " +
                      $"u={latest.u:F2} v={latest.v:F2} present={latest.present} state={latest.state}");

        OnCommand?.Invoke(latest);
    }

    // ------------------------------------------------------------------
    //  Mapeamento imagem -> tela (o controlador usa)
    // ------------------------------------------------------------------
    /// <summary>
    /// Converte um ponto normalizado da imagem (0..1) para pixels de tela
    /// (origem embaixo, convenção do ScreenPointToRay).
    /// </summary>
    public Vector2 ImageToScreen(float u, float v)
    {
        float x, y;
        bool usedMatrix = displayMatrix.HasValue;
        if (usedMatrix)
        {
            Vector3 m = displayMatrix.Value.MultiplyPoint3x4(new Vector3(u, v, 1f));
            x = m.x;
            y = m.y;
        }
        else
        {
            // Fallback retrato: rotaciona coords do sensor 90 graus.
            x = v;
            y = 1f - u;
        }

        if (flipX != 0) x = 1f - x;
        if (flipY != 0) y = 1f - y;

        x = Mathf.Clamp01(x);
        y = Mathf.Clamp01(y);

        // registra a cadeia p/ o overlay de diagnóstico (§9D)
        LastU = u; LastV = v;
        LastNormX = x; LastNormY = y;
        LastUsedMatrix = usedMatrix;
        LastScreen = new Vector2(x * Screen.width, (1f - y) * Screen.height);
        return LastScreen;
    }
}
