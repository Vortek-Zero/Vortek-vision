using UnityEngine;

/// <summary>
/// Diagnóstico (§9 da spec).
///
/// Overlay discreto de depuração (tecla D). Mostra o MODO ativo, contagem de
/// dedos, latência do servidor e texto de estado vindo do servidor (cérebro).
/// Só LÊ API pública (não interfere no controle).
/// </summary>
public class HandTrackingDiagnostics : MonoBehaviour
{
    [Tooltip("Mostrar o overlay de depuração")]
    [SerializeField] private bool show = false;
    [SerializeField] private HandPanelInteractionController controller;
    [SerializeField] private HandTrackingControlScript source;

    // Último comando recebido
    private float lastServerMs;
    private int lastMode;
    private int lastCount;
    private int lastPresent;
    private string lastState = "";
    private float lastFrameAt;
    private int framesReceived;
    private float fps;

    void Awake()
    {
        if (controller == null) controller = FindAnyObjectByType<HandPanelInteractionController>();
        if (source == null) source = FindAnyObjectByType<HandTrackingControlScript>();
    }

    void Update()
    {
        // Atalho: tecla D liga/desliga o overlay (não afeta a experiência normal).
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.dKey.wasPressedThisFrame)
            show = !show;

        if (show) UpdateCornerGrid();   // grade de teste 4 cantos + centro
        else if (gridMarks != null) foreach (var g in gridMarks) if (g != null) g.SetActive(false);
    }

    /// <summary>Alterna o overlay (usado pelo botão CONFIG da hotbar).</summary>
    public void Toggle()
    {
        show = !show;
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
        lastServerMs = c.ms;
        lastMode = c.mode;
        lastCount = c.count;
        lastPresent = c.present;
        lastState = c.state ?? "";

        framesReceived++;
        float now = Time.unscaledTime;
        if (lastFrameAt > 0f)
        {
            float inst = 1f / Mathf.Max(0.0001f, now - lastFrameAt);
            fps = Mathf.Lerp(fps, inst, 0.1f);
        }
        lastFrameAt = now;
    }

    void OnGUI()
    {
        if (!show) return;

        const float w = 320f;
        GUILayout.BeginArea(new Rect(10, 10, w, 320), GUI.skin.box);
        GUILayout.Label("<b>Hand Tracking — Diagnóstico</b>", Rich(GUI.skin.label));

        GUILayout.Label($"Modo: {(HandMode)lastMode}   dedos: {lastCount}");
        GUILayout.Label($"Trava: {(controller != null && controller.IsLocked ? "LIGADA" : "livre")}" +
                        (controller != null ? $"   punho: {controller.FistHold01 * 100f:F0}%" : ""));
        GUILayout.Label($"Mão presente: {(lastPresent == 1 ? "SIM" : "não")}");
        GUILayout.Label($"Servidor: {lastServerMs:F1} ms   FPS: {fps:F0}");
        GUILayout.Label($"Estado servidor: {lastState}");
        GUILayout.Label($"Frames recebidos: {framesReceived}");
        if (source != null)
        {
            GUILayout.Label($"dedo uv: ({source.LastU:F2},{source.LastV:F2})" +
                            $" -> norm: ({source.LastNormX:F2},{source.LastNormY:F2})" +
                            (source.LastUsedMatrix ? " [matrix]" : " [FALLBACK]"));
            GUILayout.Label($"tela px: ({source.LastScreen.x:F0},{source.LastScreen.y:F0})");
        }
        if (controller != null)
        {
            GUILayout.Label($"estado: <b>{controller.CurrentState}</b>   destino: {controller.CursorDest}");
            GUILayout.Label($"hitUV: ({controller.CursorUv.x:F2},{controller.CursorUv.y:F2})   " +
                            $"mãos: {controller.HandsCount}  ({controller.HandRoles})");
        }

        GUILayout.Space(6);
        GUILayout.Label("<i>grade: 4 cantos + centro da janela (cursor deve bater em todos)</i>", Rich(GUI.skin.label));
        GUILayout.Label("<i>1=cursor 2=mover 3=mirar 4=escala 0=ocioso</i>", Rich(GUI.skin.label));
        GUILayout.Label("<i>lógica no server.py -> calibrar sem build</i>", Rich(GUI.skin.label));

        GUILayout.Space(6);
        if (GUILayout.Button("Fechar overlay"))
            show = false;
        GUILayout.EndArea();
    }

    private static GUIStyle rich;
    private static GUIStyle Rich(GUIStyle baseStyle)
    {
        if (rich == null)
        {
            rich = new GUIStyle(baseStyle);
            rich.richText = true;
        }
        return rich;
    }

    // ------------------------------------------------------------------
    //  Grade de teste (4 cantos + centro) — só visível com o overlay aberto
    // ------------------------------------------------------------------
    private GameObject[] gridMarks;

    private void UpdateCornerGrid()
    {
        var panel = controller != null ? controller.PanelTransform : null;
        if (panel == null) return;
        if (gridMarks == null)
        {
            gridMarks = new GameObject[5];
            var mat = new Material(Shader.Find("Sprites/Default"))
            { color = new Color(1f, 0.85f, 0.2f, 0.85f) };
            Vector2[] local =
            {
                new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
                new Vector2(0.5f, 0.5f),   new Vector2(-0.5f, 0.5f),
                new Vector2(0f, 0f),
            };
            for (int i = 0; i < 5; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.name = "gridMark" + i;
                go.transform.localScale = new Vector3(0.035f, 0.035f, 1f);
                go.GetComponent<MeshRenderer>().material = mat;
                gridMarks[i] = go;
            }
            gridLocal = local;
        }
        for (int i = 0; i < gridMarks.Length; i++)
        {
            gridMarks[i].transform.position =
                panel.TransformPoint(new Vector3(gridLocal[i].x, gridLocal[i].y, -0.002f));
            gridMarks[i].transform.rotation = panel.rotation;
            gridMarks[i].SetActive(show);
        }
    }

    private Vector2[] gridLocal;

    void OnDestroy()
    {
        if (gridMarks == null) return;
        foreach (var g in gridMarks) if (g != null) Destroy(g);
    }
}
