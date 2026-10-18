using UnityEngine;

/// <summary>
/// CURSOR espacial (redesign):
///   - anel fino no ponto de impacto (não círculo grande cobrindo o conteúdo);
///   - ponto central pequeno;
///   - raio de apontamento fino e translúcido, terminando EXATAMENTE no hit;
///   - hover realça o anel; clique/pinch dá um pulso breve de pressão;
///   - cor por modo suave (sem neon).
/// O cursor é visualmente distinto de qualquer representação de mão.
/// </summary>
public class HandCursorFeedback : MonoBehaviour
{
    [Header("Referências (preenchidas pelo executor)")]
    [SerializeField] private Camera arCamera;
    [SerializeField] private Transform panel;

    [Header("Métricas (m)")]
    [SerializeField] private float ringRadius = 0.011f;
    [SerializeField] private float ringWidth = 0.0016f;
    [SerializeField] private float dotSize = 0.0035f;
    [SerializeField] private float fingertipDepth = 0.40f;

    [Header("Cores por modo (sutis)")]
    [SerializeField] private Color colorCursor = new Color(0.45f, 0.78f, 1.00f, 0.95f);
    [SerializeField] private Color colorMove   = new Color(1.00f, 0.68f, 0.35f, 0.95f);
    [SerializeField] private Color colorPlace  = new Color(0.50f, 0.90f, 0.60f, 0.95f);
    [SerializeField] private Color colorScale  = new Color(0.72f, 0.55f, 1.00f, 0.95f);
    [SerializeField] private Color colorIdle   = new Color(0.70f, 0.74f, 0.80f, 0.70f);
    [SerializeField] private Color colorLocked = new Color(0.95f, 0.30f, 0.30f, 0.90f);
    [SerializeField] private Color outlineBase = new Color(0.45f, 0.80f, 1.00f, 0.45f);
    [SerializeField] private Color outlineActive = new Color(1.00f, 0.75f, 0.35f, 0.80f);

    private GameObject ringGo;
    private LineRenderer ring;
    private GameObject dotGo;
    private Renderer dotRenderer;
    private LineRenderer beam;
    private LineRenderer outline;
    private bool lockedFb;
    private bool hoverFb;
    private float pressAt = -10f;
    private readonly Vector3[] outlineCorners = new Vector3[4];
    private readonly Vector3[] beamPoints = new Vector3[2];

    public void Init(Camera camera, Transform panelTransform)
    {
        arCamera = camera;
        panel = panelTransform;
        EnsureCursor();
        EnsureBeam();
        EnsureOutline();
    }

    private void EnsureCursor()
    {
        if (ringGo != null) return;
        ringGo = new GameObject("CursorRing");
        ring = ringGo.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        const int N = 28;
        ring.positionCount = N;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f);
        }
        ring.startWidth = ringWidth / ringRadius;   // world = ringWidth
        ring.endWidth = ringWidth / ringRadius;
        ring.numCapVertices = 2;
        ring.material = new Material(Shader.Find("Sprites/Default"));
        ringGo.SetActive(false);

        dotGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var col = dotGo.GetComponent<Collider>();
        if (col != null) Destroy(col);
        dotGo.name = "CursorDot";
        dotGo.transform.localScale = Vector3.one * dotSize;
        dotRenderer = dotGo.GetComponent<Renderer>();
        dotRenderer.material = new Material(Shader.Find("Sprites/Default"));
        dotGo.SetActive(false);
    }

    private void EnsureBeam()
    {
        if (beam != null) return;
        var go = new GameObject("HandBeam");
        beam = go.AddComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.startWidth = SpatialTheme.BeamW;
        beam.endWidth = SpatialTheme.BeamW * 0.45f;
        beam.material = new Material(Shader.Find("Sprites/Default"));
        beam.numCapVertices = 2;
        beam.enabled = false;
    }

    private void EnsureOutline()
    {
        if (outline != null || panel == null) return;
        var go = new GameObject("PanelOutline");
        outline = go.AddComponent<LineRenderer>();
        outline.loop = true;
        outline.useWorldSpace = true;
        outline.positionCount = 4;
        outline.startWidth = SpatialTheme.EdgeW * 3.2f;
        outline.endWidth = SpatialTheme.EdgeW * 3.2f;
        outline.material = new Material(Shader.Find("Sprites/Default"));
        outline.startColor = outlineBase;
        outline.endColor = outlineBase;
        outline.enabled = false;
    }

    /// <summary>Cursor comum (sem hit): impacto no plano interativo.</summary>
    public void UpdateCursor(Vector2 screenPos, float depth, HandMode mode, bool handPresent)
    {
        if (!handPresent || arCamera == null) { HideCursor(); return; }
        Ray r = arCamera.ScreenPointToRay(screenPos);
        Vector3 hit = r.GetPoint(Mathf.Max(0.1f, depth));
        Draw(r, hit, mode, false);
    }

    /// <summary>Cursor no ponto EXATO do raycast (anel colado no impacto).</summary>
    public void UpdateCursorWorld(Ray r, Vector3 hit, HandMode mode, bool handPresent)
    {
        if (!handPresent || arCamera == null) { HideCursor(); return; }
        Draw(r, hit, mode, true);
    }

    private void Draw(Ray r, Vector3 hit, HandMode mode, bool onTarget)
    {
        EnsureCursor();
        EnsureBeam();

        ringGo.transform.position = hit;
        dotGo.transform.position = hit;

        // hover: anel cresce ~30% e clareia; pressão: encolhe brevemente
        float scale = ringRadius * 2f;                 // anel desenhado com raio 0.5
        if (hoverFb) scale *= 1.3f;
        float press = Mathf.Clamp01(1f - (Time.unscaledTime - pressAt) / 0.16f);
        scale *= 1f - 0.35f * press;
        ringGo.transform.localScale = Vector3.one * scale;

        ringGo.SetActive(true);
        dotGo.SetActive(true);

        Vector3 origin = r.GetPoint(Mathf.Max(0.05f, fingertipDepth));
        beamPoints[0] = origin;
        beamPoints[1] = hit;
        beam.SetPositions(beamPoints);
        beam.enabled = true;

        Color c = ColorForMode(mode);
        if (hoverFb) c = Color.Lerp(c, Color.white, 0.35f);
        ring.startColor = c; ring.endColor = c;
        dotRenderer.material.color = c;
        Color beamIn = new Color(c.r, c.g, c.b, 0.20f);
        Color beamEnd = new Color(c.r, c.g, c.b, onTarget ? 0.85f : 0.45f);
        beam.startColor = beamIn; beam.endColor = beamEnd;
    }

    private void HideCursor()
    {
        if (ringGo != null) ringGo.SetActive(false);
        if (dotGo != null) dotGo.SetActive(false);
        if (beam != null) beam.enabled = false;
    }

    /// <summary>Cursor encara a câmera (anel sempre legível).</summary>
    private void LateUpdate()
    {
        if (ringGo != null && ringGo.activeInHierarchy && arCamera != null)
            ringGo.transform.rotation = arCamera.transform.rotation;

        if (outline != null && outline.enabled && panel != null)
        {
            outlineCorners[0] = panel.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
            outlineCorners[1] = panel.TransformPoint(new Vector3(0.5f, -0.5f, 0f));
            outlineCorners[2] = panel.TransformPoint(new Vector3(0.5f, 0.5f, 0f));
            outlineCorners[3] = panel.TransformPoint(new Vector3(-0.5f, 0.5f, 0f));
            outline.SetPositions(outlineCorners);
        }
    }

    private Color ColorForMode(HandMode mode)
    {
        switch (mode)
        {
            case HandMode.Cursor: return colorCursor;
            case HandMode.Move:   return colorMove;
            case HandMode.Place:  return colorPlace;
            case HandMode.Scale:  return colorScale;
            default:              return colorIdle;
        }
    }

    /// <summary>Hover sobre alvo interativo (UI ou janela).</summary>
    public void SetHover(bool hover) { hoverFb = hover; }

    /// <summary>Pulso breve de pressão (clique/pinch confirmado).</summary>
    public void Press() { pressAt = Time.unscaledTime; }

    /// <summary>Trava por punho: contorno vermelho sempre visível.</summary>
    public void SetLocked(bool isLocked)
    {
        lockedFb = isLocked;
        if (outline != null && isLocked)
        {
            outline.startColor = colorLocked;
            outline.endColor = colorLocked;
            outline.enabled = panel != null;
        }
    }

    /// <summary>Mostra/esconde o contorno do painel (alvo sob o ponteiro).</summary>
    public void SetOutline(bool visible)
    {
        EnsureOutline();
        if (outline == null) return;
        if (lockedFb) { outline.enabled = panel != null; return; }
        outline.enabled = visible && panel != null;
    }

    public void SetOutlineColor(bool activeManipulation)
    {
        if (outline == null || lockedFb) return;
        Color c = activeManipulation ? outlineActive : outlineBase;
        outline.startColor = c;
        outline.endColor = c;
    }

    public void Hide()
    {
        HideCursor();
        if (outline != null) outline.enabled = false;
    }
}
