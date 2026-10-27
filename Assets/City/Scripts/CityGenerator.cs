using System.Collections.Generic;
using UnityEngine;

// 윤곽선 스타일의 거대 도시를 절차적으로 생성.
// 일부 블록은 이웃 블록과 붙여 일자·ㄴ/ㄱ자 큰 블록으로 만듦 (사이 도로는 없어지고 closedRoads에 기록 → 차량은 그 길로 안 다님).
// 블록을 부지로 나눈 뒤 일부는 이웃 부지와 합쳐 일자(긴 건물)나 ㄴ/ㄱ자(본체 + 날개) 건물로 세움.
// 인스펙터 우클릭(⋮) 메뉴 → Generate City / Clear City
public class CityGenerator : MonoBehaviour
{
    [Header("크기")]
    public int blocksX = 16;
    public int blocksZ = 16;
    public float blockSize = 80f;      // 블록 한 변 (m)
    public float roadWidth = 15f;      // 도로 폭 (m)
    public float sidewalkHeight = 0.3f;

    [Header("건물")]
    public float minLot = 18f;         // 건물 부지 최소 크기
    public float maxLot = 50f;
    public float setback = 2.5f;       // 부지 경계에서 들여쓰기
    public float minHeight = 8f;
    public float maxHeight = 160f;
    [Range(0, 1)] public float towerTierChance = 0.35f; // 위층이 좁아지는 계단형 건물 비율
    [Range(0, 1)] public float parkChance = 0.07f;
    [Tooltip("이웃 부지와 합칠 확률: 양 끝이 맞으면 일자(긴 건물), 한쪽 끝만 맞으면 ㄴ/ㄱ자 건물")] [Range(0, 1)] public float mergeChance = 0.3f;
    [Tooltip("블록을 이웃 블록과 붙여 일자(2~3개)나 ㄴ/ㄱ자(3개) 큰 블록으로 만들 확률. 사이 도로는 없어지고 건물이 들어섬")] [Range(0, 1)] public float blockMergeChance = 0.25f;
    public int seed = 12345;

    // 블록을 붙이느라 없어진 도로 구간: (x, y) = 교차로 번호, z = 0이면 +X쪽 다음 교차로까지, 1이면 +Z쪽 다음 교차로까지
    [HideInInspector] public List<Vector3Int> closedRoads = new List<Vector3Int>();
    HashSet<Vector3Int> closedSet;

    [Header("인도 (블록 가장자리, 도로와 맞닿은 부분)")]
    public float walkwayWidth = 3.5f;  // 건물은 블록 가장자리에서 2 + setback 만큼 안쪽에 있으므로 그보다 좁게
    public float walkwayHeight = 0.45f;
    public Color walkwayColor = new Color(0.80f, 0.78f, 0.74f);

    [Header("머티리얼")]
    public Material buildingMat;
    public Material sidewalkMat;
    public Material parkMat;
    public Material groundMat;

    Mesh cube;
    System.Random rng;

    float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    [ContextMenu("Clear City")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
    }

    [ContextMenu("Generate City")]
    public void Generate()
    {
        Clear();
        rng = new System.Random(seed);
        EnsureCube();

        float pitch = blockSize + roadWidth;
        float sizeX = blocksX * pitch + roadWidth;
        float sizeZ = blocksZ * pitch + roadWidth;
        Vector3 origin = transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;

        // 바닥(도로)
        Box("Ground", transform, origin + new Vector3(sizeX / 2, -0.5f, sizeZ / 2), new Vector3(sizeX + 400, 1f, sizeZ + 400), groundMat, null);

        var blocksRoot = new GameObject("Blocks").transform;
        blocksRoot.SetParent(transform, false);
        var mpb = new MaterialPropertyBlock();
        Vector2 center = new Vector2(sizeX, sizeZ) * 0.5f;
        float maxDist = center.magnitude;
        int count = 0;
        int buildingLayer = Mathf.Max(0, LayerMask.NameToLayer("Building"));

        // 공원 블록을 먼저 정하고, 나머지 블록 일부를 이웃과 붙임 (없어질 도로 구간 기록)
        var park = new bool[blocksX, blocksZ];
        for (int bx = 0; bx < blocksX; bx++)
            for (int bz = 0; bz < blocksZ; bz++) park[bx, bz] = rng.NextDouble() < parkChance;
        PlanBlockMerges(park);

        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            float x0 = roadWidth + bx * pitch, z0 = roadWidth + bz * pitch;
            var block = new GameObject($"Block_{bx}_{bz}").transform;
            block.SetParent(blocksRoot, false);
            Vector3 bc = origin + new Vector3(x0 + blockSize / 2, 0, z0 + blockSize / 2);

            Box("Sidewalk", block, bc + Vector3.up * sidewalkHeight / 2, new Vector3(blockSize, sidewalkHeight, blockSize), park[bx, bz] ? parkMat : sidewalkMat, null);
            AddWalkways(block, origin + new Vector3(x0, 0, z0));
            if (park[bx, bz]) continue;

            var lots = new List<Rect>();
            Split(new Rect(x0 + 2f, z0 + 2f, blockSize - 4f, blockSize - 4f), lots, 0);
            count += PlaceLots(block, lots, origin, Downtown(new Vector2(x0 + blockSize / 2, z0 + blockSize / 2), center, maxDist), mpb, buildingLayer);
        }

        // 붙은 블록 사이(없어진 도로 자리): 인도 바닥을 깔고 건물을 세움
        foreach (var e in closedRoads)
        {
            Rect r = StripRect(e);
            var link = new GameObject($"Link_{e.x}_{e.y}_{(e.z == 0 ? "X" : "Z")}").transform;
            link.SetParent(blocksRoot, false);
            Box("Sidewalk", link, origin + new Vector3(r.center.x, sidewalkHeight / 2, r.center.y), new Vector3(r.width, sidewalkHeight, r.height), sidewalkMat, null);
            var lots = new List<Rect>();
            Split(e.z == 1 ? new Rect(r.x, r.y + 2f, r.width, r.height - 4f) : new Rect(r.x + 2f, r.y, r.width - 4f, r.height), lots, 0);
            count += PlaceLots(link, lots, origin, Downtown(r.center, center, maxDist), mpb, buildingLayer);
        }
        closedSet = null;
        Debug.Log($"[CityGenerator] {blocksX}x{blocksZ} blocks, {count} buildings, {closedRoads.Count} road segments closed, {sizeX:F0} x {sizeZ:F0} m");
    }

    // 이미 만들어진 도시에 인도만 추가 (건물은 그대로). 인스펙터 우클릭(⋮) 메뉴 → Add Walkways
    [ContextMenu("Add Walkways")]
    public void AddWalkwaysToExisting()
    {
        EnsureCube();
        var blocksRoot = transform.Find("Blocks");
        if (!blocksRoot) return;
        float pitch = blockSize + roadWidth;
        Vector3 origin = transform.position - new Vector3(blocksX * pitch + roadWidth, 0, blocksZ * pitch + roadWidth) * 0.5f;
        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            var block = blocksRoot.Find($"Block_{bx}_{bz}");
            if (!block) continue;
            for (int i = block.childCount - 1; i >= 0; i--)
                if (block.GetChild(i).name == "Walkway") { var c = block.GetChild(i).gameObject; if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            AddWalkways(block, origin + new Vector3(roadWidth + bx * pitch, 0, roadWidth + bz * pitch));
        }
    }

    // 블록 네 변을 따라 인도 (min = 블록 모서리 월드 좌표)
    void AddWalkways(Transform block, Vector3 min)
    {
        float w = walkwayWidth, s = blockSize, y = transform.position.y + walkwayHeight / 2;
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor("_Color", walkwayColor);
        Box("Walkway", block, new Vector3(min.x + s / 2, y, min.z + w / 2), new Vector3(s, walkwayHeight, w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + s / 2, y, min.z + s - w / 2), new Vector3(s, walkwayHeight, w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + w / 2, y, min.z + s / 2), new Vector3(w, walkwayHeight, s - 2 * w), sidewalkMat, mpb);
        Box("Walkway", block, new Vector3(min.x + s - w / 2, y, min.z + s / 2), new Vector3(w, walkwayHeight, s - 2 * w), sidewalkMat, mpb);
    }

    void EnsureCube()
    {
        if (cube) return;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube = tmp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
    }

    void Split(Rect r, List<Rect> outLots, int depth)
    {
        bool canX = r.width > maxLot || (r.width > minLot * 2 && rng.NextDouble() < 0.5);
        bool canZ = r.height > maxLot || (r.height > minLot * 2 && rng.NextDouble() < 0.5);
        if (depth > 6 || (!canX && !canZ)) { outLots.Add(r); return; }
        bool splitX = canX && (!canZ || r.width >= r.height);
        if (splitX)
        {
            float s = R(minLot, r.width - minLot);
            Split(new Rect(r.x, r.y, s, r.height), outLots, depth + 1);
            Split(new Rect(r.x + s, r.y, r.width - s, r.height), outLots, depth + 1);
        }
        else
        {
            float s = R(minLot, r.height - minLot);
            Split(new Rect(r.x, r.y, r.width, s), outLots, depth + 1);
            Split(new Rect(r.x, r.y + s, r.width, r.height - s), outLots, depth + 1);
        }
    }

    // 도심일수록 높게 (p = 도시 모서리 기준 좌표)
    static float Downtown(Vector2 p, Vector2 center, float maxDist) => Mathf.Clamp01(1f - Vector2.Distance(p, center) / maxDist * 1.6f);

    // 부지마다 건물을 세움 (일부 부지는 합쳐서 일자 / ㄴ·ㄱ자). 세운 건물 수를 돌려줌
    int PlaceLots(Transform parent, List<Rect> lots, Vector3 origin, float downtown, MaterialPropertyBlock mpb, int buildingLayer)
    {
        int count = 0;
        foreach (var shape in MergeLots(lots))
        {
            var lot = shape.main;
            float w = lot.width - setback * 2, d = lot.height - setback * 2;
            if (w < 4 || d < 4) continue;
            float hMax = Mathf.Lerp(minHeight * 2.5f, maxHeight, downtown * downtown);
            float h = R(minHeight, hMax) * R(0.6f, 1.1f);
            h = Mathf.Max(minHeight, Mathf.Round(h / 3.5f) * 3.5f); // 층 단위(3.5m)
            Vector3 pos = origin + new Vector3(lot.center.x, sidewalkHeight + h / 2, lot.center.y);

            float g = R(0.9f, 1f);
            mpb.SetColor("_Color", new Color(g, g, g * R(0.97f, 1.02f), 1));
            var b = Box("Building", parent, pos, new Vector3(w, h, d), buildingMat, mpb);
            b.layer = buildingLayer;
            count++;

            // ㄴ/ㄱ자 건물: 이웃 부지 쪽 날개 (본체의 자식이라 위층처럼 한 건물로 함께 무너짐)
            if (shape.wing.width > 0f)
            {
                Rect wr = WingRect(shape.main, shape.wing);
                if (wr.width >= 4f && wr.height >= 4f)
                {
                    var wing = Box("Wing", b.transform, origin + new Vector3(wr.center.x, sidewalkHeight + h / 2, wr.center.y), new Vector3(wr.width, h, wr.height), buildingMat, mpb);
                    wing.layer = buildingLayer;
                }
            }

            // 계단형 상층부
            if (h > 30 && rng.NextDouble() < towerTierChance)
            {
                float tw = w * R(0.45f, 0.75f), td = d * R(0.45f, 0.75f), th = h * R(0.25f, 0.6f);
                Vector3 tp = origin + new Vector3(lot.center.x, sidewalkHeight + h + th / 2, lot.center.y);
                var top = Box("Tier", b.transform, tp, new Vector3(tw, th, td), buildingMat, mpb);
                top.layer = buildingLayer;
            }
        }
        return count;
    }

    // ───────────── 블록 붙이기 (일자 / ㄴ·ㄱ자 큰 블록) ─────────────
    // 모양 후보 (기준 블록에서의 상대 위치): 가로·세로 2개, 3개 일자 / 2x2에서 하나 빠진 ㄴ·ㄱ자 4가지
    static readonly Vector2Int[][] BlockShapes =
    {
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) },
        new[] { new Vector2Int(0, 0), new Vector2Int(0, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
        new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(0, 2) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
        new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
    };

    // 공원이 아닌 블록 일부를 이웃과 붙임. 블록 하나는 한 묶음에만 들어감 (교차로가 막혀 고립되지 않게)
    void PlanBlockMerges(bool[,] park)
    {
        closedRoads.Clear();
        var used = new bool[blocksX, blocksZ];
        var order = new List<int>();
        for (int bx = 0; bx < blocksX; bx++)
        for (int bz = 0; bz < blocksZ; bz++)
        {
            if (used[bx, bz] || park[bx, bz] || rng.NextDouble() >= blockMergeChance) continue;
            order.Clear();
            for (int k = 0; k < BlockShapes.Length; k++) order.Insert(rng.Next(order.Count + 1), k); // 모양을 무작위 순서로 시도
            foreach (int k in order)
            {
                var cells = BlockShapes[k];
                bool fits = true;
                foreach (var c in cells)
                {
                    int x = bx + c.x, z = bz + c.y;
                    if (x >= blocksX || z >= blocksZ || used[x, z] || park[x, z]) { fits = false; break; }
                }
                if (!fits) continue;
                foreach (var c in cells) used[bx + c.x, bz + c.y] = true;
                // 묶음 안에서 맞닿은 블록 사이 도로를 없앰
                foreach (var a in cells)
                foreach (var c in cells)
                {
                    if (c.x == a.x + 1 && c.y == a.y) closedRoads.Add(new Vector3Int(bx + a.x + 1, bz + a.y, 1)); // 세로 도로
                    if (c.y == a.y + 1 && c.x == a.x) closedRoads.Add(new Vector3Int(bx + a.x, bz + a.y + 1, 0)); // 가로 도로
                }
                break;
            }
        }
    }

    // 없어진 도로 구간의 자리 (도시 모서리 기준 XZ)
    Rect StripRect(Vector3Int e)
    {
        float pitch = blockSize + roadWidth;
        return e.z == 1 ? new Rect(e.x * pitch, roadWidth + e.y * pitch, roadWidth, blockSize)  // 세로 도로: 교차로 (x,y)→(x,y+1)
                        : new Rect(roadWidth + e.x * pitch, e.y * pitch, blockSize, roadWidth); // 가로 도로: 교차로 (x,y)→(x+1,y)
    }

    // 이웃한 두 교차로 사이 도로가 남아 있는지 (탱크·경찰차 이동용)
    public bool RoadOpen(int i, int j, int ni, int nj)
    {
        if (closedRoads == null || closedRoads.Count == 0) return true;
        if (closedSet == null) closedSet = new HashSet<Vector3Int>(closedRoads);
        var key = ni != i ? new Vector3Int(Mathf.Min(i, ni), j, 0) : new Vector3Int(i, Mathf.Min(j, nj), 1);
        return !closedSet.Contains(key);
    }

    // 월드 위치 p가 없어진 도로 자리 위인지
    public bool OnClosedRoad(Vector3 p)
    {
        if (closedRoads == null || closedRoads.Count == 0) return false;
        float pitch = blockSize + roadWidth;
        Vector3 o = transform.position - new Vector3(blocksX * pitch + roadWidth, 0, blocksZ * pitch + roadWidth) * 0.5f;
        var q = new Vector2(p.x - o.x, p.z - o.z);
        foreach (var e in closedRoads) if (StripRect(e).Contains(q)) return true;
        return false;
    }

    // ───────────── 부지 합치기 (일자 / ㄴ·ㄱ자) ─────────────
    struct LotShape { public Rect main, wing; } // wing.width == 0 이면 직사각형 건물
    const float Eps = 0.01f;

    // 나눠진 부지 중 일부를 이웃과 합침. 맞댄 변의 양 끝이 맞으면 하나의 긴 부지(일자),
    // 한쪽 끝만 맞으면 큰 쪽을 본체·작은 쪽을 날개로 하는 ㄴ/ㄱ자 부지
    List<LotShape> MergeLots(List<Rect> lots)
    {
        var res = new List<LotShape>();
        var used = new bool[lots.Count];
        var cands = new List<int>();
        for (int i = 0; i < lots.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            Rect a = lots[i];
            if (mergeChance <= 0f || rng.NextDouble() >= mergeChance) { res.Add(new LotShape { main = a }); continue; }
            cands.Clear();
            for (int j = 0; j < lots.Count; j++) if (!used[j] && Joinable(a, lots[j], out _)) cands.Add(j);
            if (cands.Count == 0) { res.Add(new LotShape { main = a }); continue; }
            int k = cands[rng.Next(cands.Count)];
            used[k] = true;
            Rect b = lots[k];
            Joinable(a, b, out bool straight);
            if (straight) res.Add(new LotShape { main = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax)) });
            else if (a.width * a.height >= b.width * b.height) res.Add(new LotShape { main = a, wing = b });
            else res.Add(new LotShape { main = b, wing = a });
        }
        return res;
    }

    // 두 부지가 변을 맞대고 적어도 한쪽 끝이 맞는지 (straight = 양 끝이 다 맞아 합치면 직사각형)
    static bool Joinable(Rect a, Rect b, out bool straight)
    {
        straight = false;
        bool sideX = Mathf.Abs(a.xMax - b.xMin) < Eps || Mathf.Abs(b.xMax - a.xMin) < Eps; // 좌우로 맞댐
        bool sideZ = Mathf.Abs(a.yMax - b.yMin) < Eps || Mathf.Abs(b.yMax - a.yMin) < Eps; // 앞뒤로 맞댐
        if (sideX)
        {
            bool lo = Mathf.Abs(a.yMin - b.yMin) < Eps, hi = Mathf.Abs(a.yMax - b.yMax) < Eps;
            straight = lo && hi;
            return lo || hi;
        }
        if (sideZ)
        {
            bool lo = Mathf.Abs(a.xMin - b.xMin) < Eps, hi = Mathf.Abs(a.xMax - b.xMax) < Eps;
            straight = lo && hi;
            return lo || hi;
        }
        return false;
    }

    // 날개 건물 자리: 날개 부지에서 들여쓰되, 본체와 맞댄 쪽은 본체 벽까지 이어 붙임
    Rect WingRect(Rect main, Rect wing)
    {
        float s = setback;
        float x0 = wing.xMin + s, x1 = wing.xMax - s, z0 = wing.yMin + s, z1 = wing.yMax - s;
        if (Mathf.Abs(wing.xMin - main.xMax) < Eps) x0 -= 2f * s;
        else if (Mathf.Abs(wing.xMax - main.xMin) < Eps) x1 += 2f * s;
        else if (Mathf.Abs(wing.yMin - main.yMax) < Eps) z0 -= 2f * s;
        else if (Mathf.Abs(wing.yMax - main.yMin) < Eps) z1 += 2f * s;
        return Rect.MinMaxRect(x0, z0, x1, z1);
    }

    GameObject Box(string name, Transform parent, Vector3 worldPos, Vector3 size, Material mat, MaterialPropertyBlock mpb)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = worldPos;
        // 부모 스케일 영향 제거
        Vector3 ps = parent.lossyScale;
        go.transform.localScale = new Vector3(size.x / ps.x, size.y / ps.y, size.z / ps.z);
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        if (mpb != null) mr.SetPropertyBlock(mpb);
        go.AddComponent<BoxCollider>();
        return go;
    }
}
