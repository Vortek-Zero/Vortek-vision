using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tokens de design do Vortek Vision (identidade própria, linguagem Quest-like:
/// superfícies translúcidas escuras, acento frio, sem neon).
/// Métricas em metros no espaço; todo ícone/texto é limitado por PAD.
/// </summary>
public static class SpatialTheme
{
    // cores
    public static readonly Color Panel      = new Color(0.055f, 0.065f, 0.085f, 0.80f);
    public static readonly Color PanelEdge  = new Color(0.35f, 0.42f, 0.55f, 0.35f);
    public static readonly Color Button     = new Color(0.10f, 0.115f, 0.15f, 0.92f);
    public static readonly Color ButtonHover= new Color(0.16f, 0.30f, 0.48f, 0.96f);
    public static readonly Color ButtonPress= new Color(0.22f, 0.42f, 0.68f, 1.00f);
    public static readonly Color Accent     = new Color(0.36f, 0.72f, 1.00f, 1.00f);
    public static readonly Color Danger     = new Color(0.62f, 0.16f, 0.16f, 0.94f);
    public static readonly Color Text       = new Color(0.92f, 0.95f, 1.00f, 0.95f);
    public static readonly Color TextDim    = new Color(0.62f, 0.68f, 0.78f, 0.90f);
    public static readonly Color Ok         = new Color(0.14f, 0.62f, 0.32f, 0.95f);

    // métricas (m)
    public const float BtnSize   = 0.050f;   // botão quadrado padrão
    public const float Gap       = 0.010f;
    public const float Pad       = 0.011f;   // padding interno (ícone nunca passa disso)
    public const float IconFrac  = 0.52f;    // ícone ocupa no máx. 52% da altura
    public const float CapFrac   = 0.24f;    // legenda: 24% da altura
    public const float RadiusFrac= 0.26f;    // raio dos cantos (fração do lado)
    public const float EdgeW     = 0.0016f;  // espessura da borda do raio
    public const float BeamW     = 0.0028f;  // espessura do raio de apontamento
}

/// <summary>Ícones desenhados como polilinhas (nunca extravasam: geometria própria).</summary>
public enum SpatialIcon { None, Close, Minus, Square, Circle, Home, Windows, Layout, Config, Status }

public static class IconShapes
{
    /// <summary>Polilinhas em unidade normalizada (-0.5..0.5, y pra cima).</summary>
    public static List<Vector2[]> Get(SpatialIcon icon)
    {
        var L = new List<Vector2[]>();
        switch (icon)
        {
            case SpatialIcon.Close:
                L.Add(new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f) });
                L.Add(new[] { new Vector2(-0.5f, 0.5f), new Vector2(0.5f, -0.5f) });
                break;
            case SpatialIcon.Minus:
                L.Add(new[] { new Vector2(-0.55f, 0f), new Vector2(0.55f, 0f) });
                break;
            case SpatialIcon.Square:
                L.Add(SquareAt(0f, 0f, 0.5f));
                break;
            case SpatialIcon.Circle:
                L.Add(Circle(0.5f, 22));
                break;
            case SpatialIcon.Home:
                L.Add(new[] { new Vector2(-0.55f, -0.45f), new Vector2(-0.55f, 0.02f),
                               new Vector2(0f, 0.5f), new Vector2(0.55f, 0.02f),
                               new Vector2(0.55f, -0.45f), new Vector2(-0.55f, -0.45f) });
                break;
            case SpatialIcon.Windows:
                L.Add(SquareAt(-0.15f, -0.15f, 0.55f));
                L.Add(SquareAt(0.15f, 0.15f, 0.55f));
                break;
            case SpatialIcon.Layout:
                L.Add(SquareAt(-0.24f, -0.24f, 0.42f));
                L.Add(SquareAt(0.24f, -0.24f, 0.42f));
                L.Add(SquareAt(-0.24f, 0.24f, 0.42f));
                L.Add(SquareAt(0.24f, 0.24f, 0.42f));
                break;
            case SpatialIcon.Config:
                L.Add(new[] { new Vector2(-0.55f, 0.35f), new Vector2(0.55f, 0.35f) });
                L.Add(new[] { new Vector2(-0.55f, 0f),    new Vector2(0.55f, 0f) });
                L.Add(new[] { new Vector2(-0.55f, -0.35f),new Vector2(0.55f, -0.35f) });
                L.Add(CircleAt(0.15f, 0.35f, 0.13f, 10));
                L.Add(CircleAt(-0.2f, 0f, 0.13f, 10));
                L.Add(CircleAt(0.25f, -0.35f, 0.13f, 10));
                break;
            case SpatialIcon.Status:
                L.Add(Circle(0.45f, 18));
                break;
        }
        return L;
    }

    private static Vector2[] Circle(float r, int n)
    {
        var pts = new Vector2[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }
        return pts;
    }

    private static Vector2[] CircleAt(float cx, float cy, float r, int n)
    {
        var c = Circle(r, n);
        for (int i = 0; i < c.Length; i++) c[i] += new Vector2(cx, cy);
        return c;
    }

    private static Vector2[] SquareAt(float cx, float cy, float half)
    {
        return new[]
        {
            new Vector2(cx - half, cy - half), new Vector2(cx + half, cy - half),
            new Vector2(cx + half, cy + half), new Vector2(cx - half, cy + half),
            new Vector2(cx - half, cy - half),
        };
    }
}

/// <summary>Malha de retângulo arredondado (convex fan). Visível dos 2 lados
/// porque o shader Sprites/Default não faz cull.</summary>
public static class RoundedRectMesh
{
    public static Mesh Build(float w, float h, float radiusFrac, int seg = 5)
    {
        float r = Mathf.Min(w, h) * 0.5f * Mathf.Clamp01(radiusFrac);
        float hw = w * 0.5f - r, hh = h * 0.5f - r;
        var outline = new List<Vector2>();
        Vector2[] corners = { new Vector2(hw, hh), new Vector2(-hw, hh),
                              new Vector2(-hw, -hh), new Vector2(hw, -hh) };
        float[] startDeg = { 0f, 90f, 180f, 270f };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = (startDeg[c] + i * 90f / seg) * Mathf.Deg2Rad;
                outline.Add(corners[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }

        int n = outline.Count;
        var verts = new Vector3[n + 1];
        var tris = new int[n * 3];
        verts[0] = Vector3.zero;
        for (int i = 0; i < n; i++) verts[i + 1] = new Vector3(outline[i].x, outline[i].y, 0f);
        for (int i = 0; i < n; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % n + 1;
        }
        var mesh = new Mesh { vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>Botão espacial com estados (normal/hover/pressed), ícone vetorial
/// limitado ao padding e legenda com tamanho derivado do botão (nunca
/// extravia, em nenhuma escala espacial).</summary>
public class SpatialButton : MonoBehaviour
{
    public string id;
    public System.Action onClick;

    private MeshRenderer bg;
    private readonly List<LineRenderer> iconLines = new List<LineRenderer>();
    private TextMesh caption;
    private Color baseColor;
    private Color baseInk;
    private bool hover;
    private float pressAt = -10f;
    private Vector3 baseScale;

    public void Setup(string buttonId, SpatialIcon icon, string label, Color color,
                      Color ink, System.Action action, float btnH)
    {
        id = buttonId;
        onClick = action;
        baseColor = color;
        baseInk = ink;
        baseScale = transform.localScale;
        bg = GetComponent<MeshRenderer>();
        bg.material = new Material(Shader.Find("Sprites/Default")) { color = color };

        if (!string.IsNullOrEmpty(label)) BuildCaption(label, ink, btnH);   // antes do ícone
        if (icon != SpatialIcon.None) BuildIcon(icon, ink, btnH);
        ApplyState();
    }

    private void BuildIcon(SpatialIcon icon, Color ink, float btnH)
    {
        float iconSize = btnH * SpatialTheme.IconFrac;
        bool hasCaption = GetComponentInChildren<TextMesh>() != null;
        float cy = hasCaption ? btnH * 0.10f : 0f;
        foreach (var poly in IconShapes.Get(icon))
        {
            var go = new GameObject("icon");
            go.transform.SetParent(transform, false);
            // normaliza escala: 1 unidade local = 1 metro de mundo (sem distorção)
            go.transform.localPosition = new Vector3(0f, cy, -0.51f);
            go.transform.localScale = new Vector3(1f / transform.localScale.x,
                                                  1f / transform.localScale.y, 1f);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = poly.Length;
            for (int i = 0; i < poly.Length; i++)
                lr.SetPosition(i, new Vector3(poly[i].x * iconSize, poly[i].y * iconSize, 0f));
            lr.loop = poly.Length > 2;
            lr.startWidth = SpatialTheme.EdgeW * 1.6f;
            lr.endWidth = SpatialTheme.EdgeW * 1.6f;
            lr.numCapVertices = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = ink;
            lr.endColor = ink;
            iconLines.Add(lr);
        }
    }

    private void BuildCaption(string label, Color ink, float btnH)
    {
        // legenda limitada à largura do botão: nunca truncada nem cortada
        float maxW = transform.localScale.x * 0.86f;
        float charH = btnH * SpatialTheme.CapFrac;
        float charW = charH * 0.62f;
        if (label.Length * charW > maxW)
            charH *= maxW / (label.Length * charW);

        var go = new GameObject("caption");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, -btnH * 0.27f, -0.51f);
        go.transform.localScale = new Vector3(1f / transform.localScale.x,
                                              1f / transform.localScale.y, 1f);
        caption = go.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            caption.font = font;
            go.GetComponent<MeshRenderer>().material = font.material;
        }
        caption.text = label;
        caption.fontSize = 64;
        caption.characterSize = charH;      // em metros: cabe sempre no botão
        caption.anchor = TextAnchor.MiddleCenter;
        caption.alignment = TextAlignment.Center;
        caption.color = ink;
    }

    public void SetHover(bool h)
    {
        if (hover == h) return;
        hover = h;
        ApplyState();
    }

    /// <summary>Pulso de pressão (curto) ao confirmar.</summary>
    public void Press()
    {
        pressAt = Time.unscaledTime;
    }

    public void SetColor(Color c)
    {
        baseColor = c;
        ApplyState();
    }

    private void ApplyState()
    {
        if (bg == null) return;
        Color c = hover ? Color.Lerp(baseColor, SpatialTheme.ButtonHover, 0.75f) : baseColor;
        bg.material.color = c;
        Color ink = hover ? SpatialTheme.Text : baseInk;
        foreach (var lr in iconLines) { lr.startColor = ink; lr.endColor = ink; }
        if (caption != null) caption.color = ink;
    }

    private void Update()
    {
        float p = Mathf.Clamp01(1f - (Time.unscaledTime - pressAt) / 0.18f);
        transform.localScale = baseScale * (1f - 0.10f * p);
    }
}

/// <summary>Construção de painéis/botões com os tokens do tema.</summary>
public static class SpatialUIFactory
{
    private static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

    public static Material UiMaterial(Color c)
    {
        string key = c.ToString();
        if (!matCache.TryGetValue(key, out var m))
        {
            m = new Material(Shader.Find("Sprites/Default")) { color = c };
            matCache[key] = m;
        }
        return m;
    }

    /// <summary>Painel arredondado translúcido com borda sutil.</summary>
    public static GameObject MakePanel(Transform parent, Vector2 size, Vector3 localPos)
    {
        var go = new GameObject("panel");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().mesh =
            RoundedRectMesh.Build(1f, 1f, SpatialTheme.RadiusFrac);
        go.AddComponent<MeshRenderer>().material = UiMaterial(SpatialTheme.Panel);

        var edge = new GameObject("edge");
        edge.transform.SetParent(go.transform, false);
        edge.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        edge.transform.localScale = new Vector3(1.04f, 1.10f, 1f);
        edge.AddComponent<MeshFilter>().mesh =
            RoundedRectMesh.Build(1f, 1f, SpatialTheme.RadiusFrac);
        edge.AddComponent<MeshRenderer>().material = UiMaterial(SpatialTheme.PanelEdge);
        return go;
    }

    /// <summary>Botão: fundo arredondado + ícone vetorial + legenda opcional.</summary>
    public static SpatialButton MakeButton(Transform parent, string label, string id,
                                           Vector3 localPos, Vector2 size, Color color,
                                           System.Action onClick, SpatialIcon icon = SpatialIcon.None,
                                           Color? ink = null)
    {
        var go = new GameObject("btn:" + id);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.AddComponent<MeshFilter>().mesh =
            RoundedRectMesh.Build(1f, 1f, SpatialTheme.RadiusFrac);
        var box = go.AddComponent<BoxCollider>();
        box.size = Vector3.one;

        var btn = go.AddComponent<SpatialButton>();
        btn.Setup(id, icon, label, color, ink ?? SpatialTheme.TextDim, onClick, size.y);
        return btn;
    }
}

/// <summary>
/// HOTBAR persistente ancorada à câmera (sempre acessível; não acompanha
/// painéis manipulados). Ícones + legenda curta, estados reais.
/// </summary>
public class SpatialHotbar : MonoBehaviour
{
    private Camera cam;
    private readonly List<SpatialButton> buttons = new List<SpatialButton>();
    private GameObject listPanel;
    private System.Action<string> onAction;

    public void Init(Camera camera, System.Action<string> action)
    {
        cam = camera;
        onAction = action;
        transform.SetParent(cam.transform, false);

        var defs = new (string label, string id, SpatialIcon icon)[]
        {
            ("HOME", "home", SpatialIcon.Home),
            ("JANELAS", "windows", SpatialIcon.Windows),
            ("LAYOUT", "layout", SpatialIcon.Layout),
            ("CONFIG", "config", SpatialIcon.Config),
            ("PC", "status", SpatialIcon.Status),
        };
        float s = SpatialTheme.BtnSize, gap = SpatialTheme.Gap;
        float totalW = defs.Length * (s + gap) - gap;
        float pad = SpatialTheme.Pad * 1.6f;
        SpatialUIFactory.MakePanel(transform,
            new Vector2(totalW + pad * 2f, s + pad * 2f), Vector3.zero);

        for (int i = 0; i < defs.Length; i++)
        {
            float x = -totalW / 2f + (s + gap) * (i + 0.5f);
            string id = defs[i].id;
            var b = SpatialUIFactory.MakeButton(transform, defs[i].label, id,
                new Vector3(x, 0f, -0.004f), new Vector2(s, s), SpatialTheme.Button,
                () => onAction?.Invoke(id), defs[i].icon);
            buttons.Add(b);
        }
        SetStatus(false);
        ApplyPose();
    }

    /// <summary>Lista de janelas do PC acima da barra (seletor real).</summary>
    public void ShowWindowList(List<(string id, string title)> wins, System.Action<string> onPick)
    {
        HideWindowList();
        listPanel = new GameObject("WindowList");
        listPanel.transform.SetParent(transform, false);
        listPanel.transform.localPosition = new Vector3(0f, 0.085f, 0f);
        int n = Mathf.Min(wins.Count, 6);
        float rowH = SpatialTheme.BtnSize * 0.86f;
        float w = 0.58f;
        SpatialUIFactory.MakePanel(listPanel.transform,
            new Vector2(w, 0.03f + n * (rowH + 0.006f)), Vector3.zero);
        for (int i = 0; i < n; i++)
        {
            var win = wins[i];
            string label = string.IsNullOrEmpty(win.title) ? win.id : win.title;
            SpatialUIFactory.MakeButton(listPanel.transform, label, "win:" + win.id,
                new Vector3(0f, (n - 1) * 0.5f * (rowH + 0.006f) - i * (rowH + 0.006f), -0.004f),
                new Vector2(w - 0.02f, rowH), SpatialTheme.Button,
                () => { onPick?.Invoke(win.id); HideWindowList(); },
                SpatialIcon.Square);
        }
    }

    public void HideWindowList()
    {
        if (listPanel != null) { Destroy(listPanel); listPanel = null; }
    }

    public bool ListVisible => listPanel != null;

    public void SetStatus(bool connected)
    {
        var st = buttons.Find(b => b.id == "status");
        st?.SetColor(connected ? SpatialTheme.Ok : SpatialTheme.Danger);
    }

    public SpatialButton HitTest(Ray ray)
    {
        foreach (var b in buttons)
            if (b != null && b.GetComponent<Collider>().Raycast(ray, out _, 100f)) return b;
        if (listPanel != null)
            foreach (var c in listPanel.GetComponentsInChildren<Collider>())
                if (c.Raycast(ray, out _, 100f))
                    return c.GetComponent<SpatialButton>();
        return null;
    }

    void LateUpdate()
    {
        ApplyPose();
    }

    private void ApplyPose()
    {
        transform.localPosition = new Vector3(0f, -0.29f, 0.50f);
        transform.localRotation = Quaternion.Euler(26f, 0f, 0f);
    }
}

/// <summary>
/// CONTROLES EXTERNOS da janela (fora do conteúdo transmitido):
/// fechar/minimizar/restaurar/focar. Seguem a janela com tamanho próprio.
/// </summary>
public class WindowChrome : MonoBehaviour
{
    private Transform panel;
    private readonly List<SpatialButton> buttons = new List<SpatialButton>();

    public void Init(Transform panelTransform, System.Action<string> action)
    {
        panel = panelTransform;
        var defs = new (string id, SpatialIcon icon, Color c)[]
        {
            ("win_close", SpatialIcon.Close, SpatialTheme.Danger),
            ("win_min", SpatialIcon.Minus, SpatialTheme.Button),
            ("win_restore", SpatialIcon.Square, SpatialTheme.Button),
            ("win_focus", SpatialIcon.Circle, SpatialTheme.Button),
        };
        float s = SpatialTheme.BtnSize * 0.92f, gap = SpatialTheme.Gap;
        float totalW = defs.Length * (s + gap) - gap;
        for (int i = 0; i < defs.Length; i++)
        {
            float x = -totalW / 2f + (s + gap) * (i + 0.5f);
            string id = defs[i].id;
            var b = SpatialUIFactory.MakeButton(transform, null, id,
                new Vector3(x, 0f, 0f), new Vector2(s, s), defs[i].c,
                () => action?.Invoke(id), defs[i].icon, SpatialTheme.Text);
            buttons.Add(b);
        }
    }

    void LateUpdate()
    {
        if (panel == null) { Destroy(gameObject); return; }
        bool active = panel.gameObject.activeInHierarchy;
        foreach (var b in buttons) if (b != null) b.gameObject.SetActive(active);
        if (!active) return;
        float sy = Mathf.Max(1e-3f, panel.lossyScale.y);
        float h = sy * 0.5f;
        transform.position = panel.TransformPoint(new Vector3(0f, (h + 0.05f) / sy, -0.05f));
        transform.rotation = panel.rotation;
        transform.localScale = Vector3.one;
    }

    public SpatialButton HitTest(Ray ray)
    {
        if (panel == null || !panel.gameObject.activeInHierarchy) return null;
        foreach (var b in buttons)
            if (b != null && b.GetComponent<Collider>().Raycast(ray, out _, 100f)) return b;
        return null;
    }
}
