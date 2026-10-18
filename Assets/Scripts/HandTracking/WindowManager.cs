using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Estado + comandos REAIS das janelas do PC (Etapa 3/5):
///   GET  /windows                     -> lista (wmctrl/XWayland)
///   POST /window/focus?id=0x...       -> foca a janela real
///   POST /window/minimize?id=...      -> minimiza a janela real
///   POST /window/restore?id=...       -> restaura
///   POST /window/close?id=...         -> WM_CLOSE (graceful; não mata processo)
/// Ações espaciais (HOME/LAYOUT/CONFIG) são locais. Fechar/Minimizar atuam na
/// janela REAL só quando o usuário escolheu uma na lista; caso contrário a
/// representação espacial é apenas OCULTA (sem afirmar que a janela fechou).
/// </summary>
public class WindowManager : MonoBehaviour
{
    public static WindowManager I;

    [Header("Servidor (server.js no PC)")]
    public string serverUrl = "http://192.168.1.102";

    private Camera cam;
    private Transform panel;
    private Vector3 homePos;
    private Quaternion homeRot;
    private Vector3 homeScale;

    public string AssociatedWindowId { get; private set; }
    public List<(string id, string title)> LastWindows { get; private set; } = new List<(string, string)>();

    void Awake()
    {
        if (I != null && I != this) { Destroy(this); return; }
        I = this;
    }

    public void Setup(Camera camera, Transform panelTransform, Vector3 initialPos,
                      Quaternion initialRot, Vector3 initialScale)
    {
        cam = camera;
        panel = panelTransform;
        homePos = initialPos;
        homeRot = initialRot;
        homeScale = initialScale;
    }

    // ---------------- Ações da hotbar ----------------
    public void Home()
    {
        if (panel == null) return;
        panel.position = homePos;
        panel.rotation = homeRot;
        panel.localScale = homeScale;
    }

    public void Layout()
    {
        // 1 janela = central e na frente (multi-janelas: grade, Etapa 4)
        if (panel == null || cam == null) return;
        panel.position = cam.transform.position + cam.transform.forward * 1.5f;
        panel.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up)
                         * Quaternion.Euler(10f, 0f, 0f);
    }

    public void Config()
    {
        FindAnyObjectByType<HandTrackingDiagnostics>()?.Toggle();
    }

    public void ToggleWindowList(SpatialHotbar hotbar)
    {
        if (hotbar == null) return;
        if (hotbar.ListVisible) { hotbar.HideWindowList(); return; }
        StartCoroutine(FetchWindows(hotbar));
    }

    public void PickWindow(string id)
    {
        AssociatedWindowId = id;
        Debug.Log($"[WinMgr] janela associada: {id}");
        StartCoroutine(Op("focus", id));
    }

    // ---------------- Ações dos controles externos da janela ----------------
    public void ChromeAction(string action)
    {
        switch (action)
        {
            case "win_close":
                if (panel != null) panel.gameObject.SetActive(false);  // oculta a representação
                if (!string.IsNullOrEmpty(AssociatedWindowId))
                    StartCoroutine(Op("close", AssociatedWindowId));   // e a janela REAL (se escolhida)
                else
                    Debug.Log("[WinMgr] fechar: só ocultou a janela espacial (nenhuma janela do PC associada — use JANELAS p/ escolher)");
                break;
            case "win_min":
                if (panel != null) panel.gameObject.SetActive(false);
                if (!string.IsNullOrEmpty(AssociatedWindowId))
                    StartCoroutine(Op("minimize", AssociatedWindowId));
                break;
            case "win_restore":
                if (panel != null) panel.gameObject.SetActive(true);
                if (!string.IsNullOrEmpty(AssociatedWindowId))
                    StartCoroutine(Op("restore", AssociatedWindowId));
                break;
            case "win_focus":
                if (panel != null) panel.gameObject.SetActive(true);
                if (!string.IsNullOrEmpty(AssociatedWindowId))
                    StartCoroutine(Op("focus", AssociatedWindowId));
                break;
        }
    }

    public void ShowPanel()
    {
        if (panel != null) panel.gameObject.SetActive(true);
    }

    // ---------------- HTTP ----------------
    private IEnumerator FetchWindows(SpatialHotbar hotbar)
    {
        using (var req = UnityWebRequest.Get($"{serverUrl}/windows"))
        {
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[WinMgr] /windows falhou: {req.error}");
                yield break;
            }
            var wins = ParseWindows(req.downloadHandler.text);
            LastWindows = wins;
            hotbar.ShowWindowList(wins, PickWindow);
        }
    }

    private IEnumerator Op(string op, string id)
    {
        using (var req = new UnityWebRequest($"{serverUrl}/window/{op}?id={UnityWebRequest.EscapeURL(id)}", "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(new byte[0]);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 5;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[WinMgr] {op} falhou: {req.error}");
            else
                Debug.Log($"[WinMgr] {op} ok: {req.downloadHandler.text}");
        }
    }

    private static List<(string, string)> ParseWindows(string json)
    {
        var list = new List<(string, string)>();
        try
        {
            var data = JsonUtility.FromJson<WindowsDto>(json);
            if (data != null && data.windows != null)
                foreach (var w in data.windows) list.Add((w.id, w.title));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WinMgr] parse /windows: {e.Message}");
        }
        return list;
    }

    [System.Serializable] private class WindowsDto { public WindowDto[] windows; public int ok; }
    [System.Serializable] private class WindowDto { public string id; public string title; public string wmClass; }
}
