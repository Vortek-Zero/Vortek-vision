using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Networking;

/// <summary>
/// EXECUTOR fino da interação por mãos. Não reconhece gesto — o servidor
/// (cérebro) manda o MODO (contagem de dedos) e os valores; aqui só executa:
///
///   1 dedo  (Cursor) -> cursor com RAYCAST: gruda no ponto onde o raio bate na janela
///   2 dedos (Move)   -> RAYCAST p/ agarrar: precisa APONTAR p/ a janela; o CENTRO
///                       dela segue o MEIO dos 2 dedos (u,v do servidor já é o
///                       ponto médio indicador+médio). Sem acerto = não move.
///   3 dedos (Place)  -> a janela acompanha a VISÃO e o ÂNGULO do celular
///                       (você mira onde quer a tela; soltar = fica ali)
///   4 dedos (Scale)  -> escala por slider vertical (servidor manda scaleMul)
///   0      (Idle)    -> solta: a janela fica onde está
///   PUNHO (0 c/ mão presente, segurado 0.8s) -> TRAVA/confirma: congela a janela;
///                       outro punho segurado destrava. Vale p/ 3 e 4 dedos
///                       (evita ajuste errado pelo flap 3<->4).
///   TOQUE na tela do celular (com cursor sobre a janela) -> CLIQUE no PC,
///                       no ponto do cursor (via POST /click no servidor).
/// </summary>
/// <summary>
/// Estados de interação EXPLÍCITOS (rastreáveis no diagnóstico):
///   Idle        — nenhuma interação (sem mão / trava)
///   Point       — apontando, sem alvo
///   Hover       — alvo interativo sob o ponteiro (UI ou janela)
///   UiPress     — clique confirmado em botão da UI (transitório)
///   RemoteClick — clique confirmado no conteúdo remoto (transitório)
///   WindowGrab  — janela agarrada (modo 2)
///   Drag        — arraste de conteúdo (toque + mover)
///   Scroll      — rolagem (toque de 2 dedos vertikal)
///   Release     — liberação segura (soltar botão/arraste; transitório)
/// </summary>
public enum InteractionState
{
    Idle, Point, Hover, UiPress, RemoteClick, WindowGrab, Drag, Scroll, Release
}

public class HandPanelInteractionController : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private HandTrackingControlScript source;
    [SerializeField] private Camera arCamera;
    [SerializeField] private GameObject targetQuad;

    [Header("Movimento / escala")]
    [Tooltip("Suavização de follow (maior = mais coladinho)")]
    [SerializeField] private float followSpeed = 18f;
    [Tooltip("Distância (m) da janela quando você mira com 3 dedos")]
    [SerializeField] private float placeDistance = 1.5f;
    [Tooltip("Tilt (graus) da janela ao posicionar (tipo monitor)")]
    [SerializeField] private float tiltDegrees = 10f;
    [Tooltip("Zona morta (m): variação menor que isso NÃO move a janela (jitter de pose não faz ela andar sozinha)")]
    [SerializeField] private float posDeadzone = 0.035f;
    [Tooltip("Zona morta de rotação (graus) no posicionar")]
    [SerializeField] private float rotDeadzoneDeg = 2f;

    [Header("Trava por punho + clique no PC")]
    [Tooltip("Segurar o punho por esse tempo (s) TRAVA/destrava a janela")]
    [SerializeField] private float fistHoldS = 0.8f;
    [Tooltip("POST de clique no PC (endpoint /click do server.js)")]
    [SerializeField] private string clickUrl = "http://192.168.1.102/click";
    [Tooltip("POST de rolagem no PC (endpoint /scroll do server.js)")]
    [SerializeField] private string scrollUrl = "http://192.168.1.102/scroll";
    [Tooltip("Tempo máx (s) de um toque p/ valer como clique")]
    [SerializeField] private float tapMaxTime = 0.5f;
    [Tooltip("Distância máx (px) do toque p/ valer como clique")]
    [SerializeField] private float tapMaxDistPx = 40f;
    [Tooltip("Anti-duplo-clique (s)")]
    [SerializeField] private float tapCooldown = 0.8f;

    [Header("Diagnostics")]
    [SerializeField] private bool verbose = false;

    private HandCursorFeedback feedback;
    private PlaceDisplayOnWallScript touchControl;

    // Interface espacial (Etapa 3): hotbar + controles externos da janela
    private WindowManager wm;
    private SpatialHotbar hotbar;
    private WindowChrome chrome;
    private SpatialButton hoverBtn;
    private float lastCmdAt = -10f;

    // Último comando do servidor
    private HandCommand cmd;
    private bool hasCmd;
    private HandMode lastMode = HandMode.Idle;

    // Escala (modo 4)
    private float baseMul = 1f;
    private Vector3 baseScaleVec;

    // Mover (modo 2): agarrado por raycast + profundidade do acerto
    private bool grabbed;
    private float moveDepth = 1.5f;

    // Trava por punho (confirma): congela Move/Place/Scale
    private bool locked;
    private float fistTime;
    private bool fistArmed;

    // Cursor sobre a quad (p/ clique): UV do vídeo + throttle
    private Vector2 cursorUv;
    private bool cursorOnQuad;
    private float lastClickAt = -10f;

    // Confirmação temporal do clique: cursor precisa estar parado
    private Vector2 stableRef;
    private float stableSince = -10f;
    private const float STABLE_PX = 25f;
    private const float STABLE_S = 0.25f;

    public HandMode Mode => cmd?.Mode ?? HandMode.Idle;
    public bool IsLocked => locked;
    public float FistHold01 => Mathf.Clamp01(fistTime / Mathf.Max(0.01f, fistHoldS));
    public string CursorDest => locked ? "TRAVADA" : (cursorOnQuad ? "JANELA" : "FORA");
    public Vector2 CursorUv => cursorUv;
    public InteractionState CurrentState { get; private set; } = InteractionState.Idle;
    public int HandsCount => cmd != null ? cmd.hands : 0;
    public string HandRoles => cmd != null && cmd.hands >= 2
        ? $"esq={cmd.handMode} dir={cmd.handPtr}" : "1 mão (2 papéis)";
    public Transform PanelTransform => targetQuad != null ? targetQuad.transform : null;

    void Awake()
    {
        if (source == null) source = FindAnyObjectByType<HandTrackingControlScript>();
        if (arCamera == null) arCamera = Camera.main;
        if (targetQuad == null && source != null) targetQuad = source.Panel;
        touchControl = FindAnyObjectByType<PlaceDisplayOnWallScript>();

        EnhancedTouchSupport.Enable();
        if (targetQuad != null && targetQuad.GetComponent<Collider>() == null)
            targetQuad.AddComponent<MeshCollider>();   // p/ raycast de agarrar/cursor/clique

        feedback = FindAnyObjectByType<HandCursorFeedback>();
        if (feedback == null)
            feedback = new GameObject("HandFeedback").AddComponent<HandCursorFeedback>();
        feedback.Init(arCamera, targetQuad != null ? targetQuad.transform : null);

        // interface espacial própria (hotbar + chrome), criada em runtime
        wm = FindAnyObjectByType<WindowManager>();
        if (wm == null) wm = new GameObject("WindowManager").AddComponent<WindowManager>();
        hotbar = FindAnyObjectByType<SpatialHotbar>();
        if (hotbar == null) hotbar = new GameObject("SpatialHotbar").AddComponent<SpatialHotbar>();
        hotbar.Init(arCamera, OnSpatialAction);
        chrome = FindAnyObjectByType<WindowChrome>();
        if (chrome == null) chrome = new GameObject("WindowChrome").AddComponent<WindowChrome>();
        chrome.Init(targetQuad.transform, OnSpatialAction);
    }

    void Start()
    {
        if (wm != null && targetQuad != null)
            wm.Setup(arCamera, targetQuad.transform, targetQuad.transform.position,
                     targetQuad.transform.rotation, targetQuad.transform.localScale);
    }

    private void OnSpatialAction(string id)
    {
        if (wm == null) return;
        switch (id)
        {
            case "home": wm.Home(); break;
            case "windows": wm.ToggleWindowList(hotbar); break;
            case "layout": wm.Layout(); break;
            case "config": wm.Config(); break;
            case "status": wm.ShowPanel(); break;   // status = reapresenta a janela
            default:
                if (id.StartsWith("win_")) wm.ChromeAction(id);
                break;
        }
    }

    void OnEnable()
    {
        if (source != null) source.OnCommand += OnCommand;
    }

    void OnDisable()
    {
        if (source != null) source.OnCommand -= OnCommand;
    }

    private void OnCommand(HandCommand c)
    {
        cmd = c;
        hasCmd = true;
        lastCmdAt = Time.unscaledTime;
        if (hotbar != null) hotbar.SetStatus(true);
    }

    void Update()
    {
        if (source == null || targetQuad == null || arCamera == null) return;
        if (!hasCmd) { feedback?.Hide(); return; }

        // status da hotbar: sem comando há 2s = cérebro desconectado
        if (hotbar != null && Time.unscaledTime - lastCmdAt > 2f)
            hotbar.SetStatus(false);

        HandMode mode = cmd.Mode;

        // ---- Punho = TRAVA/confirma (mão presente + idle + 0 dedos, segurado) ----
        bool isFist = cmd.present == 1 && mode == HandMode.Idle && cmd.count == 0;
        if (isFist)
        {
            fistTime += Time.deltaTime;
            if (fistTime >= fistHoldS && !fistArmed)
            {
                fistArmed = true;
                locked = !locked;
                feedback?.SetLocked(locked);
                Debug.Log($"[HandCtrl] TRAVA {(locked ? "LIGADA — janela congelada" : "desligada")}");
            }
        }
        else
        {
            fistTime = 0f;
            fistArmed = false;
        }

        // ---- Captura de referências na entrada dos modos ----
        if (mode == HandMode.Move && lastMode != HandMode.Move)
        {
            moveDepth = PanelDepth();      // chute inicial; o grab ajusta no acerto
            SetTouchEnabled(false);
        }
        if (mode == HandMode.Scale && lastMode != HandMode.Scale)
        {
            baseScaleVec = targetQuad.transform.localScale;
            baseMul = Mathf.Max(1e-4f, cmd.scaleMul);
            SetTouchEnabled(false);
        }
        if (lastMode == HandMode.Move && mode != HandMode.Move)
        {
            grabbed = false;
            SetTouchEnabled(true);
        }
        if (lastMode == HandMode.Scale && mode != HandMode.Scale)
            SetTouchEnabled(true);

        // ---- Posição do cursor ----
        Vector2 screenPos = source.ImageToScreen(cmd.u, cmd.v);
        float cursorDepth = PanelDepth();
        bool handPresent = cmd.present == 1 && mode != HandMode.Idle;

        // ---- Executa o modo (a trava congela Move/Place/Scale) ----
        if (!locked)
        {
            switch (mode)
            {
                case HandMode.Cursor:
                    break;                        // só o cursor se move

                case HandMode.Move:
                    MovePanelGrab(screenPos);     // agarra por raycast e segue
                    break;

                case HandMode.Place:
                    PlaceAtGaze();                // acompanha visão + ângulo do celular
                    break;

                case HandMode.Scale:
                    ApplyScale(cmd.scaleMul);
                    break;

                case HandMode.Idle:
                default:
                    break;
            }

            ProcessTouches();                      // toque: UI / clique / arraste / rolagem
        }

        // ---- Feedback (cursor gruda na janela por raycast) ----
        UpdateFeedbackEx(screenPos, cursorDepth, mode, handPresent);

        UpdateInteractionState();

        if (mode != lastMode)
        {
            if (verbose) Debug.Log($"[HandCtrl] modo: {lastMode} -> {mode} (dedos={cmd.count})");
            lastMode = mode;
        }
    }

    // ------------------------------------------------------------------
    //  Modo 2 — agarrar por RAYCAST e seguir (centro no meio dos 2 dedos)
    // ------------------------------------------------------------------
    private void MovePanelGrab(Vector2 screenPos)
    {
        Ray r = arCamera.ScreenPointToRay(screenPos);
        if (!grabbed)
        {
            // precisa APONTAR p/ a janela p/ agarrar (diferente do modo 3)
            if (RayHitQuad(r, out RaycastHit hit, out _))
            {
                grabbed = true;
                Vector3 fwd0 = arCamera.transform.forward;
                moveDepth = Mathf.Max(0.3f,
                    Vector3.Dot(hit.point - arCamera.transform.position, fwd0));
            }
            else return;
        }

        Vector3 fwd = arCamera.transform.forward;
        Vector3 planePoint = arCamera.transform.position + fwd * moveDepth;

        float denom = Vector3.Dot(r.direction, fwd);
        if (Mathf.Abs(denom) < 1e-4f) return;
        float t = Vector3.Dot(planePoint - r.origin, fwd) / denom;
        Vector3 target = r.origin + r.direction * t;   // centro segue o raio

        float smooth = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        // zona morta: só arrasta quando o alvo andou de verdade
        if ((target - targetQuad.transform.position).magnitude > posDeadzone)
            targetQuad.transform.position =
                Vector3.Lerp(targetQuad.transform.position, target, smooth);
        // rotação e escala ficam intactas
    }

    /// <summary>Raio contra a quad da janela (collider próprio). Devolve a UV do vídeo.</summary>
    private bool RayHitQuad(Ray r, out RaycastHit hit, out Vector2 uv)
    {
        hit = default;
        uv = default;
        if (targetQuad == null) return false;
        var col = targetQuad.GetComponent<Collider>();
        if (col == null || !col.enabled) return false;
        if (col.Raycast(r, out hit, 100f))
        {
            uv = hit.textureCoord;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    //  Camada de TOQUE na tela (dedão que segura o celular):
    //    1 toque curto sem mover = clique (UI espacial ou conteúdo remoto)
    //    1 toque + mover sobre o conteúdo = ARRASTE (mouse down...up)
    //    2 toques + arrastar vertical = ROLAGEM; 2 toques curtos = botão direito
    // ------------------------------------------------------------------
    private bool dragging;
    private float lastMovePost;
    private bool twoFinger;
    private bool twoFingerMoved;
    private Vector2 twoFingerLast;
    private float twoFingerStartT;
    private float scrollAccum;
    private float lastScrollPost;
    private const float SCROLL_PX_PER_NOTCH = 22f;

    private void ProcessTouches()
    {
        var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
        int n = touches.Count;

        if (n == 0)
        {
            if (dragging)   // soltou durante arraste: liberação segura
            {
                dragging = false;
                StartCoroutine(PostButton(cursorUv, "left", "up"));
                SetTransient(InteractionState.Release);
            }
            if (twoFinger && !twoFingerMoved
                && Time.unscaledTime - twoFingerStartT <= tapMaxTime && cursorOnQuad)
            {
                lastClickAt = Time.unscaledTime;
                StartCoroutine(PostButton(cursorUv, "right", "click"));   // botão direito
                SetTransient(InteractionState.RemoteClick);
                feedback?.Press();
            }
            twoFinger = false;
            twoFingerMoved = false;
            scrollAccum = 0f;
            return;
        }

        if (n >= 2)
        {
            if (!twoFinger)
            {
                twoFinger = true;
                twoFingerMoved = false;
                twoFingerLast = AvgPos(touches);
                twoFingerStartT = Time.unscaledTime;
                scrollAccum = 0f;
                if (dragging)   // 2 dedos cancela arraste com release seguro
                {
                    dragging = false;
                    StartCoroutine(PostButton(cursorUv, "left", "up"));
                }
            }
            Vector2 avg = AvgPos(touches);
            float dy = avg.y - twoFingerLast.y;         // tela: descer = rolar p/ baixo
            twoFingerLast = avg;
            if (Mathf.Abs(dy) > 2f)
            {
                twoFingerMoved = true;
                if (cursorOnQuad)                       // rolagem só sobre o conteúdo
                {
                    scrollAccum += -dy / SCROLL_PX_PER_NOTCH;
                    if (Mathf.Abs(scrollAccum) >= 1f
                        && Time.unscaledTime - lastScrollPost > 0.06f)
                    {
                        lastScrollPost = Time.unscaledTime;
                        StartCoroutine(PostScroll(Mathf.Clamp(scrollAccum, -4f, 4f)));
                        scrollAccum = 0f;
                        CurrentState = InteractionState.Scroll;
                    }
                }
            }
            return;
        }

        // ---- 1 toque ----
        var t = touches[0];
        if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended)
        {
            if (dragging)
            {
                dragging = false;
                StartCoroutine(PostButton(cursorUv, "left", "up"));
                SetTransient(InteractionState.Release);
                return;
            }
            float dt = (float)(t.time - t.startTime);
            float dist = (t.screenPosition - t.startScreenPosition).magnitude;
            if (dt <= tapMaxTime && dist <= tapMaxDistPx
                && Time.unscaledTime - lastClickAt >= tapCooldown
                && Time.unscaledTime - stableSince >= STABLE_S)
            {
                lastClickAt = Time.unscaledTime;
                if (hoverBtn != null)
                {
                    var b = hoverBtn;
                    if (verbose) Debug.Log($"[HandCtrl] UI: {b.id}");
                    b.onClick?.Invoke();
                    b.Press();
                    SetTransient(InteractionState.UiPress);
                }
                else if (cursorOnQuad)
                {
                    StartCoroutine(PostClick(cursorUv));
                    SetTransient(InteractionState.RemoteClick);
                }
                feedback?.Press();
            }
            return;
        }

        // arraste: toque que sai da área de toque sobre o conteúdo
        if (!dragging && cursorOnQuad
            && (t.screenPosition - t.startScreenPosition).magnitude > tapMaxDistPx)
        {
            dragging = true;
            StartCoroutine(PostButton(cursorUv, "left", "down"));
            SetTransient(InteractionState.Drag);
        }
        if (dragging && Time.unscaledTime - lastMovePost > 0.05f)  // segue o cursor (20 Hz)
        {
            lastMovePost = Time.unscaledTime;
            StartCoroutine(PostButton(cursorUv, "left", "move"));
        }
    }

    private static Vector2 AvgPos(
        in UnityEngine.InputSystem.Utilities.ReadOnlyArray<UnityEngine.InputSystem.EnhancedTouch.Touch> touches)
    {
        Vector2 s = Vector2.zero;
        for (int i = 0; i < touches.Count; i++) s += touches[i].screenPosition;
        return s / Mathf.Max(1, touches.Count);
    }

    // ------------------------------------------------------------------
    //  Máquina de estados (explícita; aparece no diagnóstico)
    // ------------------------------------------------------------------
    private InteractionState transientState;
    private float transientUntil = -10f;

    private void SetTransient(InteractionState st)
    {
        transientState = st;
        transientUntil = Time.unscaledTime + 0.25f;
    }

    private void UpdateInteractionState()
    {
        if (Time.unscaledTime < transientUntil)
        {
            CurrentState = transientState;
            return;
        }
        HandMode mode = Mode;
        if (locked || cmd == null || cmd.present != 1)
            CurrentState = InteractionState.Idle;
        else if (dragging)
            CurrentState = InteractionState.Drag;
        else if (mode == HandMode.Move && grabbed)
            CurrentState = InteractionState.WindowGrab;
        else if (hoverBtn != null || cursorOnQuad)
            CurrentState = InteractionState.Hover;
        else
            CurrentState = InteractionState.Point;
    }

    // ------------------------------------------------------------------
    //  POST no PC (server.js): clique/botão/rolagem — JSON compartilhado
    // ------------------------------------------------------------------
    private IEnumerator PostClick(Vector2 uv)
    {
        yield return PostJson(clickUrl,
            $"{{\"u\":{uv.x:F4},\"v\":{uv.y:F4},\"button\":\"left\",\"action\":\"click\"}}");
    }

    private IEnumerator PostButton(Vector2 uv, string button, string action)
    {
        yield return PostJson(clickUrl,
            $"{{\"u\":{uv.x:F4},\"v\":{uv.y:F4},\"button\":\"{button}\",\"action\":\"{action}\"}}");
    }

    private IEnumerator PostScroll(float notches)
    {
        yield return PostJson(scrollUrl, $"{{\"dy\":{notches:F1}}}");
    }

    private IEnumerator PostJson(string url, string json)
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[HandCtrl] POST falhou: {req.error}");
            else if (verbose)
                Debug.Log($"[HandCtrl] {json} -> {req.downloadHandler.text}");
        }
    }

    // ------------------------------------------------------------------
    //  Modo 3 — acompanha a VISÃO e o ÂNGULO do celular (posicionar)
    // ------------------------------------------------------------------
    private void PlaceAtGaze()
    {
        var camT = arCamera.transform;
        Vector3 pos = camT.position + camT.forward * placeDistance;
        // alinha a janela de frente com o usuário (a partir do ângulo do celular)
        // A face visível da Quad é a -Z. Para ela encarar a câmera, o +Z precisa
        // apontar PRA LONGE da câmera (= camT.forward), igual à parede usa
        // -hitPose.up. Com -camT.forward a face -Z virava pra longe => backface/invisível.
        Quaternion rot = Quaternion.LookRotation(camT.forward, Vector3.up)
                         * Quaternion.Euler(tiltDegrees, 0f, 0f);

        float smooth = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        var tr = targetQuad.transform;
        // zona morta: jitter de pose não anda com a janela
        if ((pos - tr.position).magnitude > posDeadzone)
            tr.position = Vector3.Lerp(tr.position, pos, smooth);
        if (Quaternion.Angle(tr.rotation, rot) > rotDeadzoneDeg)
            tr.rotation = Quaternion.Slerp(tr.rotation, rot, smooth);
    }

    // ------------------------------------------------------------------
    //  Modo 4 — escala por slider vertical (scaleMul vem do servidor)
    // ------------------------------------------------------------------
    private void ApplyScale(float scaleMul)
    {
        float relative = scaleMul / baseMul;      // 1.0 = tamanho na entrada
        Vector3 target = baseScaleVec * relative;
        float smooth = 1f - Mathf.Exp(-12f * Time.deltaTime);
        targetQuad.transform.localScale =
            Vector3.Lerp(targetQuad.transform.localScale, target, smooth);
    }

    // ------------------------------------------------------------------
    //  Feedback — cursor com RAYCAST (gruda na janela) + contorno
    // ------------------------------------------------------------------
    private void UpdateFeedbackEx(Vector2 screenPos, float depth, HandMode mode, bool handPresent)
    {
        if (feedback == null) return;

        // confirmação temporal: marca quando o cursor parou
        if ((screenPos - stableRef).magnitude > STABLE_PX)
        {
            stableRef = screenPos;
            stableSince = Time.unscaledTime;
        }

        cursorOnQuad = false;

        // UI espacial primeiro: chrome (perto da janela) e hotbar têm DESTINO
        // próprio — nunca geram clique no PC (§5: alvos distintos).
        SpatialButton btn = null;
        if (handPresent)
        {
            Ray uiRay = arCamera.ScreenPointToRay(screenPos);
            btn = chrome?.HitTest(uiRay);
            if (btn == null) btn = hotbar?.HitTest(uiRay);
        }
        if (hoverBtn != btn)
        {
            hoverBtn?.SetHover(false);
            btn?.SetHover(true);
            hoverBtn = btn;
        }

        if (handPresent && btn == null)
        {
            Ray r = arCamera.ScreenPointToRay(screenPos);
            if (RayHitQuad(r, out RaycastHit hit, out Vector2 uv))
            {
                cursorOnQuad = true;
                cursorUv = uv;
                feedback.UpdateCursorWorld(r, hit.point, mode, true);
            }
            else
            {
                feedback.UpdateCursor(screenPos, depth, mode, true);
            }
        }
        else if (handPresent)
        {
            feedback.UpdateCursor(screenPos, depth, mode, true);   // sobre UI: cursor normal
        }
        else
        {
            feedback.UpdateCursor(screenPos, depth, mode, false);
        }

        bool overPanel = cursorOnQuad || hoverBtn != null
                         || (handPresent && IsOverPanel(screenPos));
        feedback.SetHover(handPresent && (hoverBtn != null || cursorOnQuad));
        feedback.SetOutline(overPanel || mode == HandMode.Place || mode == HandMode.Scale);
        feedback.SetOutlineColor(mode == HandMode.Scale || mode == HandMode.Move);
    }

    private float PanelDepth()
    {
        if (targetQuad.activeInHierarchy)
            return Vector3.Distance(arCamera.transform.position, targetQuad.transform.position);
        return placeDistance;
    }

    private bool IsOverPanel(Vector2 screenPos)
    {
        if (targetQuad == null || !targetQuad.activeInHierarchy) return false;

        Vector3[] local = {
            new Vector3(-0.5f,-0.5f,0f), new Vector3(0.5f,-0.5f,0f),
            new Vector3(0.5f,0.5f,0f),   new Vector3(-0.5f,0.5f,0f)
        };
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var l in local)
        {
            Vector3 world = targetQuad.transform.TransformPoint(l);
            Vector3 scr = arCamera.WorldToScreenPoint(world);
            if (scr.z <= 0f) return false;
            minX = Mathf.Min(minX, scr.x); maxX = Mathf.Max(maxX, scr.x);
            minY = Mathf.Min(minY, scr.y); maxY = Mathf.Max(maxY, scr.y);
        }

        float mx = Mathf.Max((maxX - minX) * 0.12f, 40f);
        float my = Mathf.Max((maxY - minY) * 0.12f, 40f);
        return screenPos.x >= minX - mx && screenPos.x <= maxX + mx
            && screenPos.y >= minY - my && screenPos.y <= maxY + my;
    }

    private void SetTouchEnabled(bool enabled)
    {
        if (touchControl != null && touchControl.enabled != enabled)
            touchControl.enabled = enabled;
    }
}
