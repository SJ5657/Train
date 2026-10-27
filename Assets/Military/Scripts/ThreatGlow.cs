using UnityEngine;

// 날아오는 포탄·로켓을 둘러싼 붉은 테두리 원. 항상 카메라를 향하고,
// 멀리 있어도 화면에서 일정 크기 이상으로 보이며, 깜빡여서 눈에 잘 띔.
public class ThreatGlow : MonoBehaviour
{
    public static readonly Color DefaultColor = new Color(1f, 0.15f, 0.1f, 0.8f); // 테두리만 그리므로 알파는 높게

    static Texture2D glowTex;
    static Mesh quad;

    Transform glow;
    float baseSize, minScreenFraction;
    float seed;

    // size = 가까이서 보이는 후광 지름(m), minScreenFraction = 카메라 거리에 대한 최소 지름 비율 (멀리서도 보이게)
    // color를 주면 그 색 테두리 (보급상자 등). 안 주면 위험 신호인 붉은색
    public static ThreatGlow Add(Transform parent, float size, float minScreenFraction = 0.03f, Color? color = null)
    {
        var fx = CombatFX.Instance;
        if (!fx || !fx.fireMaterial) return null;
        if (!glowTex) glowTex = MakeGlowTexture();
        if (!quad) quad = MakeQuad(DefaultColor);

        var go = new GameObject("ThreatGlow");
        go.transform.SetParent(parent, false);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = color.HasValue ? MakeQuad(color.Value) : quad;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = new Material(fx.fireMaterial) { mainTexture = glowTex };
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var t = go.AddComponent<ThreatGlow>();
        t.glow = go.transform; t.baseSize = size; t.minScreenFraction = minScreenFraction;
        t.seed = Random.value * 10f;
        return t;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (!cam) return;
        Vector3 toCam = cam.transform.position - glow.position;
        float dist = toCam.magnitude;
        glow.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
        float pulse = 1f + 0.1f * Mathf.Sin((Time.time + seed) * 18f);
        float s = Mathf.Max(baseSize, dist * minScreenFraction) * pulse;
        glow.localScale = Vector3.one * s / Mathf.Max(0.0001f, glow.parent ? glow.parent.lossyScale.x : 1f);
    }

    // 가운데는 비어 있고 테두리만 있는 원 (안쪽 포탄이 그대로 보임)
    static Texture2D MakeGlowTexture()
    {
        const int n = 128;
        const float ringCenter = 0.82f, ringHalfWidth = 0.07f; // 반지름 대비 위치·두께
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f - 0.5f, n / 2f - 0.5f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - ringCenter) / ringHalfWidth);
                a = a * a * (3f - 2f * a);       // 테두리 양쪽을 부드럽게
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }

    static Mesh MakeQuad(Color c)
    {
        var m = new Mesh { name = "ThreatGlowQuad" };
        m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.colors = new[] { c, c, c, c };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; // 양면
        m.RecalculateBounds();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);
        return m;
    }
}
