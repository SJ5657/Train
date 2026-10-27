using System.Collections.Generic;
using UnityEngine;

// 미니맵 (화면 오른쪽 아래): 거인 주변을 위에서 본 지도. 아직 부서지지 않은 건물만 그리고(부술 수 없는 초고층은 진하게),
// 거인 위치와 바라보는 방향을 화살표로 표시. 건물이 부서지면 지도에서도 사라짐. 아래에 남은 건물 수.
// 도시 전체를 한 장의 그림으로 그려 두고 거인 주변만 잘라서 보여줌 (건물이 부서질 때만 다시 그림).
[DefaultExecutionOrder(200)] // TallBuildings(100)가 건물 높이·레이어를 정한 뒤에 건물 목록을 만듦
public class Minimap : MonoBehaviour
{
    [Tooltip("지도 한 변 크기(px)")] public float size = 180f;
    [Tooltip("화면 가장자리에서 띄우는 거리(px)")] public float margin = 16f;
    [Tooltip("지도에 보이는 범위 (m, 한 변)")] public float viewMeters = 450f;
    [Tooltip("도시 전체 그림 해상도(px)")] public int resolution = 1024;
    [Tooltip("부서진 건물이 있는지 확인하는 간격(초)")] public float checkInterval = 0.25f;
    public Color groundColor = new Color(0.16f, 0.17f, 0.2f, 0.88f);
    [Tooltip("블록(인도) 색: 도로와 구분되게")] public Color blockColor = new Color(0.27f, 0.29f, 0.33f, 0.92f);
    public Color buildingColor = new Color(0.82f, 0.84f, 0.88f, 1f);
    [Tooltip("부술 수 없는 초고층 색")] public Color solidColor = new Color(0.42f, 0.5f, 0.66f, 1f);
    public Color giantColor = new Color(1f, 0.3f, 0.25f, 1f);

    class Bldg { public Transform t; public Collider[] cols; }
    readonly List<Bldg> buildings = new List<Bldg>();
    readonly List<Rect> blocks = new List<Rect>();
    Texture2D tex; Color32[] px;
    Rect world;                 // 그림에 담는 월드 범위 (x,z)
    float nextCheck;
    int remaining = -1, solidCount = -1;
    static Texture2D arrowTex;

    void Start()
    {
        var city = FindObjectOfType<CityGenerator>();
        if (!city) { enabled = false; return; }
        float pitch = city.blockSize + city.roadWidth;
        float sx = city.blocksX * pitch + city.roadWidth, sz = city.blocksZ * pitch + city.roadWidth;
        Vector3 c = city.transform.position;
        world = new Rect(c.x - sx * 0.5f, c.z - sz * 0.5f, sx, sz);
        foreach (var t in city.GetComponentsInChildren<Transform>(true))
        {
            // 도시가 만든 건물 본체 (이름 "Building"). 날개·위층 등 자식 콜라이더도 함께 그림
            if (t.name == "Building") buildings.Add(new Bldg { t = t, cols = t.GetComponentsInChildren<Collider>(true) });
            // 블록 바닥(인도): 도로와 구분되게 살짝 밝게
            else if (t.name == "Sidewalk" && t.TryGetComponent<Collider>(out var col))
                blocks.Add(Rect.MinMaxRect(col.bounds.min.x, col.bounds.min.z, col.bounds.max.x, col.bounds.max.z));
        }
        tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        px = new Color32[resolution * resolution];
        CheckRedraw(true);
    }

    void Update()
    {
        if (Time.unscaledTime >= nextCheck) { nextCheck = Time.unscaledTime + checkInterval; CheckRedraw(false); }
    }

    // 남은 건물 수(또는 초고층이 부술 수 있게 바뀐 수)가 달라졌을 때만 다시 그림
    void CheckRedraw(bool force)
    {
        int breakable = LayerMask.NameToLayer("Building");
        int alive = 0, solid = 0;
        for (int i = buildings.Count - 1; i >= 0; i--)
        {
            var b = buildings[i];
            if (!b.t) { buildings.RemoveAt(i); continue; }
            if (!b.t.gameObject.activeInHierarchy) continue; // 부서지는 중
            alive++;
            if (b.t.gameObject.layer != breakable) solid++;
        }
        if (!force && alive == remaining && solid == solidCount) return;
        remaining = alive; solidCount = solid;
        Redraw(breakable);
    }

    // 바닥 → 블록 → 남아 있는 건물의 바닥 모양(콜라이더 범위) 순서로 칠함
    void Redraw(int breakable)
    {
        if (!tex) return;
        Color32 ground = groundColor, block = blockColor, normal = buildingColor, solid = solidColor;
        for (int i = 0; i < px.Length; i++) px[i] = ground;
        foreach (var r in blocks) Fill(r.xMin, r.yMin, r.xMax, r.yMax, block, 0f);
        float inset = world.width / resolution * 0.6f; // 붙어 있는 건물 사이에 틈이 보이게 살짝 줄여서 칠함
        foreach (var b in buildings)
        {
            if (!b.t || !b.t.gameObject.activeInHierarchy) continue;
            Color32 col = b.t.gameObject.layer == breakable ? normal : solid; // Building 레이어가 아니면 부술 수 없는 초고층
            foreach (var c in b.cols)
            {
                if (!c) continue;
                Bounds bb = c.bounds;
                Fill(bb.min.x, bb.min.z, bb.max.x, bb.max.z, col, inset);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false);
    }

    void Fill(float x0, float z0, float x1, float z1, Color32 col, float inset)
    {
        if (x1 - x0 > inset * 3f) { x0 += inset; x1 -= inset; }
        if (z1 - z0 > inset * 3f) { z0 += inset; z1 -= inset; }
        int n = resolution;
        int i0 = Mathf.Clamp(Mathf.FloorToInt((x0 - world.xMin) / world.width * n), 0, n - 1);
        int i1 = Mathf.Clamp(Mathf.CeilToInt((x1 - world.xMin) / world.width * n) - 1, 0, n - 1);
        int j0 = Mathf.Clamp(Mathf.FloorToInt((z0 - world.yMin) / world.height * n), 0, n - 1);
        int j1 = Mathf.Clamp(Mathf.CeilToInt((z1 - world.yMin) / world.height * n) - 1, 0, n - 1);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++) px[j * n + i] = col;
    }

    // 위쪽을 향한 흰 삼각형 화살표 (가장자리 어둡게)
    static Texture2D ArrowTexture()
    {
        if (arrowTex) return arrowTex;
        const int n = 32;
        arrowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float v = (n - 1 - y) / (float)(n - 1);              // 0 = 위 꼭짓점, 1 = 아래 밑변
                float half = v * 0.42f * n, d = Mathf.Abs(x - (n - 1) * 0.5f);
                float edge = Mathf.Min(half - d, (1f - v) * n * 0.9f, v * n);
                float a = Mathf.Clamp01(edge / 1.5f), inner = Mathf.Clamp01((edge - 2.5f) / 1.5f);
                arrowTex.SetPixel(x, y, new Color(inner, inner, inner, a));
            }
        arrowTex.Apply();
        return arrowTex;
    }

    void OnGUI()
    {
        if (!tex || GameStartMenu.InMenu) return;
        var hp = GiantHealth.Instance;
        var box = new Rect(Screen.width - size - margin, Screen.height - size - margin - 20f, size, size);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(box.x - 3, box.y - 3, box.width + 6, box.height + 6), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 거인을 가운데에 두고 viewMeters 범위만 잘라서 보여줌 (도시 끝에서는 지도 안쪽으로 밀어 넣음). 북쪽(+z)이 위
        Vector3 p = hp ? hp.transform.position : new Vector3(world.center.x, 0f, world.center.y);
        float vw = Mathf.Min(1f, viewMeters / world.width), vh = Mathf.Min(1f, viewMeters / world.height);
        float u = (p.x - world.xMin) / world.width, v = (p.z - world.yMin) / world.height;
        var view = new Rect(Mathf.Clamp(u - vw * 0.5f, 0f, 1f - vw), Mathf.Clamp(v - vh * 0.5f, 0f, 1f - vh), vw, vh);
        GUI.DrawTextureWithTexCoords(box, tex, view);

        if (hp)
        {
            Vector2 gp = new Vector2(box.x + (u - view.x) / view.width * box.width, box.yMax - (v - view.y) / view.height * box.height);
            Vector3 f = hp.transform.forward;
            float ang = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; // 북쪽 기준 시계 방향
            var old = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, gp);
            GUI.color = giantColor;
            GUI.DrawTexture(new Rect(gp.x - 9f, gp.y - 10f, 18f, 20f), ArrowTexture());
            GUI.matrix = old;
            GUI.color = Color.white;
        }

        var st = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        var lr = new Rect(box.x, box.yMax + 3f, box.width, 18f);
        st.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(lr.x + 1f, lr.y + 1f, lr.width, lr.height), $"남은 건물  {remaining}", st);
        st.normal.textColor = Color.white;
        GUI.Label(lr, $"남은 건물  {remaining}", st);
    }
}
