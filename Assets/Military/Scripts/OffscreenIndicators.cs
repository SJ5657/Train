using System.Collections.Generic;
using UnityEngine;

// 화면 밖에 있는 적(탱크/헬기/전투기)의 방향을 화면 가장자리에 작은 화살표로 표시.
// 가까운 적일수록 화살표가 크고 진하게 표시됨.
[RequireComponent(typeof(Camera))]
public class OffscreenIndicators : MonoBehaviour
{
    public float edgeMargin = 30f;
    public float arrowSize = 24f;
    public float maxDistance = 1500f;       // 이보다 먼 적은 가장 작고 흐리게
    public Color tankColor = new Color(1f, 0.85f, 0.2f);
    public Color heliColor = new Color(1f, 0.55f, 0.15f);
    public Color jetColor = new Color(1f, 0.2f, 0.2f);

    Camera cam;
    Texture2D arrow;
    readonly List<(Transform t, Color c, float yOffset)> targets = new List<(Transform, Color, float)>();
    float nextScan;

    void Awake()
    {
        cam = GetComponent<Camera>();
        arrow = MakeArrow(64);
    }

    void Rescan()
    {
        targets.Clear();
        foreach (var x in FindObjectsOfType<TankAI>()) if (!x.IsDead) targets.Add((x.transform, tankColor, 5f));
        foreach (var x in FindObjectsOfType<HeliAI>()) if (!x.IsDead) targets.Add((x.transform, heliColor, 0f));
        foreach (var x in FindObjectsOfType<JetAI>()) if (!x.IsDead) targets.Add((x.transform, jetColor, 0f));
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || GameStartMenu.HideHud) return; // 시작 메뉴 중이거나 HUD 숨김이면 그리지 않음
        if (Event.current.type != EventType.Repaint || !cam) return;
        if (Time.unscaledTime >= nextScan) { Rescan(); nextScan = Time.unscaledTime + 0.25f; }

        float W = Screen.width, H = Screen.height;
        Vector2 center = new Vector2(W * 0.5f, H * 0.5f);
        var oldMatrix = GUI.matrix;
        var oldColor = GUI.color;

        foreach (var (t, col, yOff) in targets)
        {
            if (!t) continue;
            Vector3 world = t.position + Vector3.up * yOff;
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool behind = sp.z < 0f;
            if (behind) { sp.x = W - sp.x; sp.y = H - sp.y; }
            if (!behind && sp.x > 0 && sp.x < W && sp.y > 0 && sp.y < H) continue; // 화면 안이면 표시 안 함

            Vector2 d = new Vector2(sp.x - center.x, sp.y - center.y);
            if (d.sqrMagnitude < 1f) d = Vector2.down;
            float hw = center.x - edgeMargin, hh = center.y - edgeMargin;
            float k = Mathf.Min(hw / Mathf.Max(Mathf.Abs(d.x), 0.001f), hh / Mathf.Max(Mathf.Abs(d.y), 0.001f));
            Vector2 edge = center + d * k;
            Vector2 guiPos = new Vector2(edge.x, H - edge.y);           // GUI 좌표는 y가 아래로
            float angle = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg;

            float dist = Vector3.Distance(cam.transform.position, world);
            float near = 1f - Mathf.Clamp01(dist / maxDistance);
            float size = arrowSize * Mathf.Lerp(0.7f, 1.25f, near);

            GUI.matrix = oldMatrix;
            GUIUtility.RotateAroundPivot(angle, guiPos);
            GUI.color = new Color(0, 0, 0, 0.5f);
            GUI.DrawTexture(new Rect(guiPos.x - size * 0.5f + 1.5f, guiPos.y - size * 0.5f + 1.5f, size, size), arrow);
            GUI.color = new Color(col.r, col.g, col.b, Mathf.Lerp(0.55f, 1f, near));
            GUI.DrawTexture(new Rect(guiPos.x - size * 0.5f, guiPos.y - size * 0.5f, size, size), arrow);
        }
        GUI.matrix = oldMatrix;
        GUI.color = oldColor;
    }

    // 오른쪽을 가리키는 흰색 삼각형 화살표 텍스처 (가장자리 부드럽게)
    static Texture2D MakeArrow(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Vector2 a = new Vector2(n * 0.92f, n * 0.5f), b = new Vector2(n * 0.12f, n * 0.1f), c = new Vector2(n * 0.12f, n * 0.9f);
        Vector2 notch = new Vector2(n * 0.35f, n * 0.5f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // 화살촉 모양 = 삼각형(a,b,c)에서 뒤쪽 홈(b,notch,c) 제외
                float d1 = EdgeDist(p, a, b, c);
                float d2 = EdgeDist(p, b, notch, c);
                float alpha = Mathf.Clamp01(d1 + 0.5f) * (1f - Mathf.Clamp01(d2 + 0.5f));
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        tex.Apply();
        return tex;
    }

    // 점이 삼각형 안쪽이면 양수(가장자리까지 거리), 밖이면 음수
    static float EdgeDist(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s = Mathf.Sign(Cross(b - a, c - a));
        return Mathf.Min(s * Side(p, a, b), Mathf.Min(s * Side(p, b, c), s * Side(p, c, a)));
    }
    static float Side(Vector2 p, Vector2 a, Vector2 b) => Cross(b - a, p - a) / (b - a).magnitude;
    static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
}
