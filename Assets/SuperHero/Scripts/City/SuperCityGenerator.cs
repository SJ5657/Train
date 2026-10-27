using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 플레이 시작 때 거대한 격자형 도시를 생성한다 (기본 36x36 블록 ≈ 3.6km x 3.6km).
    ///  - 도시 중심부는 초고층, 바깥으로 갈수록 저층 주택가. 공원/계단식 타워 포함.
    ///  - 모든 건물은 DestructibleBuilding (처음 맞기 전까지는 메쉬 1개라 가볍다).
    ///  - 건물/보도는 구역(district)별 정적 배칭, 자동차/가로수/가로등은 영웅 주변에만 배치.
    /// </summary>
    public class SuperCityGenerator : MonoBehaviour
    {
        public static SuperCityGenerator Instance { get; private set; }
        public static bool Ready { get; private set; }
        public static float Progress { get; private set; }

        [Header("Layout")]
        public int blocks = 36;
        public float blockSize = 76f;
        public float roadWidth = 24f;
        public int seed = 20261025;
        public int districtBlocks = 6;

        [Header("Props (SimplePoly)")]
        public GameObject[] carPrefabs;
        public GameObject[] treePrefabs;
        public GameObject[] lightPrefabs;
        public float propRadius = 1000f;
        public int maxCars = 650;

        const float SidewalkH = 0.2f;
        const float Cell = 3f;

        float pitch, half;
        Transform worldRoot;
        int buildingCount;
        readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        readonly Dictionary<Material, Material> instMats = new Dictionary<Material, Material>();

        public float HalfSize { get { return half; } }
        public Vector3 SpawnPoint { get { return new Vector3(0f, 1.2f, 0f); } }
        public int BuildingCount { get { return buildingCount; } }

        void Awake() { Instance = this; Ready = false; Progress = 0f; }

        IEnumerator Start()
        {
            pitch = blockSize + roadWidth;
            half = blocks * pitch * 0.5f;
            worldRoot = new GameObject("CityWorld").transform;
            worldRoot.SetParent(transform, false);

            MakeGround();
            MakeRoads();
            yield return null;

            int dn = Mathf.CeilToInt(blocks / (float)districtBlocks);
            for (int dj = 0; dj < dn; dj++)
            {
                for (int di = 0; di < dn; di++) BuildDistrict(di, dj);
                Progress = (dj + 1f) / (dn + 1f);
                yield return null;
            }

            MakeCars();
            Progress = 1f;
            Ready = true;
            Debug.Log("[SuperHero] City ready: " + buildingCount + " buildings, " + (blocks * pitch) + " m wide");
        }

        // ───────────────────────── 지면 / 도로 ─────────────────────────

        void MakeGround()
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(worldRoot, false);
            go.transform.position = new Vector3(0f, -10f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.FlatBox(14000f, 10f, 14000f, 24f);
            go.AddComponent<MeshRenderer>().sharedMaterial = CityMaterials.Grass;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 5f, 0f); bc.size = new Vector3(14000f, 10f, 14000f);
        }

        void MakeRoads()
        {
            var vs = new List<Vector3>(); var us = new List<Vector2>(); var ts = new List<int>();
            var vc = new List<Vector3>(); var uc = new List<Vector2>(); var tc = new List<int>();
            float rw = roadWidth, y = 0.06f;
            for (int j = 0; j <= blocks; j++)
            {
                float lz = -half + j * pitch;
                for (int i = 0; i < blocks; i++)
                {
                    float x0 = -half + i * pitch + rw * 0.5f, x1 = -half + (i + 1) * pitch - rw * 0.5f;
                    ProceduralMeshes.AddFlatQuad(vs, us, ts, x0, lz - rw * 0.5f, x1, lz + rw * 0.5f, y, true, (x1 - x0) / 16f);
                }
            }
            for (int i = 0; i <= blocks; i++)
            {
                float lx = -half + i * pitch;
                for (int j = 0; j < blocks; j++)
                {
                    float z0 = -half + j * pitch + rw * 0.5f, z1 = -half + (j + 1) * pitch - rw * 0.5f;
                    ProceduralMeshes.AddFlatQuad(vs, us, ts, lx - rw * 0.5f, z0, lx + rw * 0.5f, z1, y, false, (z1 - z0) / 16f);
                }
                for (int j = 0; j <= blocks; j++)
                {
                    float lz = -half + j * pitch;
                    ProceduralMeshes.AddFlatQuad(vc, uc, tc, lx - rw * 0.5f, lz - rw * 0.5f, lx + rw * 0.5f, lz + rw * 0.5f, y, false, 1f);
                }
            }
            MakeFlatMesh("RoadSegments", vs, us, ts, CityMaterials.RoadSegment);
            MakeFlatMesh("RoadCrossings", vc, uc, tc, CityMaterials.RoadCross);
        }

        void MakeFlatMesh(string meshName, List<Vector3> v, List<Vector2> uv, List<int> tris, Material mat)
        {
            var m = new Mesh { name = meshName };
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            var n = new Vector3[v.Count];
            for (int i = 0; i < n.Length; i++) n[i] = Vector3.up;
            m.normals = n;
            m.RecalculateBounds();
            var go = new GameObject(meshName);
            go.transform.SetParent(worldRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ───────────────────────── 구역 / 블록 ─────────────────────────

        Vector3 BlockCenter(int bi, int bj)
        {
            return new Vector3(-half + bi * pitch + pitch * 0.5f, 0f, -half + bj * pitch + pitch * 0.5f);
        }

        void BuildDistrict(int di, int dj)
        {
            var droot = new GameObject("District_" + di + "_" + dj).transform;
            droot.SetParent(worldRoot, false);
            var proot = new GameObject("Props_" + di + "_" + dj).transform;
            proot.SetParent(worldRoot, false);

            for (int bj = dj * districtBlocks; bj < Mathf.Min(blocks, (dj + 1) * districtBlocks); bj++)
                for (int bi = di * districtBlocks; bi < Mathf.Min(blocks, (di + 1) * districtBlocks); bi++)
                    BuildBlock(bi, bj, droot, proot);

            StaticBatchingUtility.Combine(droot.gameObject);
        }

        static float R(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        void BuildBlock(int bi, int bj, Transform droot, Transform proot)
        {
            var rnd = new System.Random(seed * 73856093 ^ bi * 19349663 ^ bj * 83492791);
            Vector3 c = BlockCenter(bi, bj);
            float r = Mathf.Sqrt(c.x * c.x + c.z * c.z) / half;
            float noise = Mathf.PerlinNoise(bi * 0.23f + seed * 0.013f, bj * 0.23f + 11.7f);
            float zr = r - (noise - 0.5f) * 0.30f;
            int zone = zr < 0.20f ? 0 : zr < 0.50f ? 1 : 2;
            bool center = Mathf.Abs(c.x) < pitch && Mathf.Abs(c.z) < pitch;
            if (center) zone = 0;

            bool park = !center && rnd.NextDouble() < (zone == 2 ? 0.14 : zone == 1 ? 0.10 : 0.05);

            // 보도(블록) 판
            var slab = new GameObject("Block_" + bi + "_" + bj);
            slab.transform.SetParent(droot, false);
            slab.transform.position = c;
            slab.AddComponent<MeshFilter>().sharedMesh = BlockMesh();
            slab.AddComponent<MeshRenderer>().sharedMaterial = park ? CityMaterials.Park : CityMaterials.Sidewalk;
            var sc = slab.AddComponent<BoxCollider>();
            sc.center = new Vector3(0f, SidewalkH * 0.5f, 0f); sc.size = new Vector3(blockSize, SidewalkH, blockSize);

            bool nearHero = c.magnitude < propRadius;

            if (park)
            {
                if (nearHero) PlantPark(c, proot, rnd);
                return;
            }

            // 구획 나누기
            const float U = 66f;
            var lots = new List<Vector4>();   // x,z 중심 / w,d
            double roll = rnd.NextDouble();
            float singleP = zone == 0 ? 0.55f : zone == 1 ? 0.3f : 0.12f;
            float twoP = zone == 0 ? 0.25f : zone == 1 ? 0.3f : 0.28f;
            if (center || roll < singleP) lots.Add(new Vector4(0, 0, U, U));
            else if (roll < singleP + twoP)
            {
                if (rnd.NextDouble() < 0.5) { lots.Add(new Vector4(-17, 0, 30, U)); lots.Add(new Vector4(17, 0, 30, U)); }
                else { lots.Add(new Vector4(0, -17, U, 30)); lots.Add(new Vector4(0, 17, U, 30)); }
            }
            else
            {
                lots.Add(new Vector4(-17, -17, 30, 30)); lots.Add(new Vector4(17, -17, 30, 30));
                lots.Add(new Vector4(-17, 17, 30, 30)); lots.Add(new Vector4(17, 17, 30, 30));
            }

            for (int k = 0; k < lots.Count; k++)
            {
                if (zone == 2 && lots.Count == 4 && rnd.NextDouble() < 0.12) continue;     // 빈 필지
                BuildLot(bi, bj, k, c, lots[k], zone, center, rnd, droot);
            }

            if (nearHero) PlaceStreetProps(c, proot, rnd);
        }

        Mesh blockMeshCached;
        Mesh BlockMesh()
        {
            if (blockMeshCached == null) blockMeshCached = ProceduralMeshes.FlatBox(blockSize, SidewalkH, blockSize, 6f);
            return blockMeshCached;
        }

        void BuildLot(int bi, int bj, int k, Vector3 blockCenter, Vector4 lot, int zone, bool center, System.Random rnd, Transform droot)
        {
            int maxX = Mathf.FloorToInt(lot.z / Cell), maxZ = Mathf.FloorToInt(lot.w / Cell);
            int cap = zone == 2 ? 12 : 11;
            maxX = Mathf.Min(maxX, cap); maxZ = Mathf.Min(maxZ, cap);
            int floors;
            if (center) floors = rnd.Next(38, 72);
            else if (zone == 0) floors = Mathf.RoundToInt(Mathf.Lerp(22f, 68f, Mathf.Pow((float)rnd.NextDouble(), 1.4f)));
            else if (zone == 1) floors = rnd.Next(7, 30);
            else floors = rnd.Next(2, 9);

            int minC = floors > 14 ? 6 : 4;
            int cxC = rnd.Next(Mathf.Min(minC, maxX), maxX + 1);
            int czC = rnd.Next(Mathf.Min(minC, maxZ), maxZ + 1);
            float ox = (lot.z - cxC * Cell) * 0.5f * R(rnd, -0.8f, 0.8f);
            float oz = (lot.w - czC * Cell) * 0.5f * R(rnd, -0.8f, 0.8f);
            Vector3 pos = blockCenter + new Vector3(lot.x + ox, 0f, lot.y + oz);

            int rowFloors = Mathf.Max(1, Mathf.CeilToInt(floors / 24f));
            float rowH = rowFloors * Cell;
            int rows = Mathf.CeilToInt(floors / (float)rowFloors);

            var insets = new int[rows];
            if (floors >= 18 && cxC >= 6 && czC >= 6 && rnd.NextDouble() < 0.55)
            {
                int s1 = Mathf.RoundToInt(rows * R(rnd, 0.55f, 0.7f));
                for (int y = s1; y < rows; y++) insets[y] = 1;
                if (floors >= 36 && cxC >= 8 && czC >= 8 && rnd.NextDouble() < 0.55)
                {
                    int s2 = Mathf.RoundToInt(rows * R(rnd, 0.8f, 0.9f));
                    for (int y = Mathf.Max(s2, s1 + 1); y < rows; y++) insets[y] = 2;
                }
            }

            int[] pool = zone == 0 ? new[] { 2, 5, 6, 4, 2, 5, 1 } : zone == 1 ? new[] { 1, 3, 4, 7, 2, 0, 7 } : new[] { 0, 0, 3, 7, 1, 3 };
            int style = pool[rnd.Next(pool.Length)];
            float tough = zone == 0 ? 1.4f : zone == 1 ? 1.0f : 0.7f;
            int slabStride = rows <= 9 ? 1 : Mathf.CeilToInt(rows / 9f);

            var go = new GameObject("B_" + bi + "_" + bj + "_" + k);
            go.transform.SetParent(droot, false);
            go.transform.position = pos + Vector3.up * SidewalkH;
            go.layer = GameLayers.Building;

            string key = cxC + "_" + czC + "_" + rows + "_" + rowH + "_" + string.Join(",", insets);
            Mesh mesh;
            if (!meshCache.TryGetValue(key, out mesh))
            {
                mesh = ProceduralMeshes.Building(cxC, czC, rows, rowH, Cell, insets);
                meshCache[key] = mesh;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { CityMaterials.Style(style), CityMaterials.Concrete };

            var cols = new List<Collider>();
            int r0 = 0;
            while (r0 < rows)
            {
                int ins = insets[r0], r1 = r0;
                while (r1 + 1 < rows && insets[r1 + 1] == ins) r1++;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3((cxC - 2 * ins) * Cell, (r1 - r0 + 1) * rowH, (czC - 2 * ins) * Cell);
                bc.center = new Vector3(0f, (r0 + r1 + 1) * rowH * 0.5f, 0f);
                cols.Add(bc);
                r0 = r1 + 1;
            }

            var b = go.AddComponent<DestructibleBuilding>();
            b.cx = cxC; b.cz = czC; b.rows = rows; b.cell = Cell; b.rowH = rowH;
            b.insets = insets; b.styleIndex = style; b.toughness = tough; b.slabStride = slabStride;
            b.Init(mr, cols);
            buildingCount++;
        }

        // ───────────────────────── 소품 ─────────────────────────

        Material Instanced(Material m)
        {
            if (m == null) return null;
            Material inst;
            if (!instMats.TryGetValue(m, out inst))
            {
                inst = new Material(m) { enableInstancing = true };
                instMats[m] = inst;
            }
            return inst;
        }

        GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent, float scale = 1f)
        {
            var go = Instantiate(prefab, pos, rot, parent);
            go.transform.localScale = Vector3.one * scale;
            foreach (var rd in go.GetComponentsInChildren<Renderer>())
            {
                var mats = rd.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Instanced(mats[i]);
                rd.sharedMaterials = mats;
            }
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            return go;
        }

        void PlantPark(Vector3 c, Transform proot, System.Random rnd)
        {
            if (treePrefabs == null || treePrefabs.Length == 0) return;
            int n = rnd.Next(16, 30);
            for (int i = 0; i < n; i++)
            {
                var p = c + new Vector3(R(rnd, -31f, 31f), SidewalkH, R(rnd, -31f, 31f));
                Spawn(treePrefabs[rnd.Next(treePrefabs.Length)], p, Quaternion.Euler(0f, R(rnd, 0f, 360f), 0f), proot, R(rnd, 0.9f, 1.5f));
            }
        }

        void PlaceStreetProps(Vector3 c, Transform proot, System.Random rnd)
        {
            float e = blockSize * 0.5f - 2.2f;
            if (treePrefabs != null && treePrefabs.Length > 0)
            {
                for (int edge = 0; edge < 4; edge++)
                    for (float t = -e + 6f; t < e - 4f; t += 13f)
                    {
                        if (rnd.NextDouble() > 0.5) continue;
                        Vector3 p = edge == 0 ? new Vector3(t, 0, e) : edge == 1 ? new Vector3(t, 0, -e) : edge == 2 ? new Vector3(e, 0, t) : new Vector3(-e, 0, t);
                        Spawn(treePrefabs[rnd.Next(treePrefabs.Length)], c + p + Vector3.up * SidewalkH, Quaternion.Euler(0f, R(rnd, 0f, 360f), 0f), proot, R(rnd, 0.8f, 1.15f));
                    }
            }
            if (lightPrefabs != null && lightPrefabs.Length > 0)
            {
                float le = blockSize * 0.5f - 1.2f;
                for (int edge = 0; edge < 4; edge++)
                    for (float t = -le + 10f; t < le - 6f; t += 28f)
                    {
                        // 가로등 팔(+X)이 도로 쪽을 향하도록 회전
                        Vector3 p; float yaw;
                        if (edge == 0) { p = new Vector3(t, 0, le); yaw = -90f; }
                        else if (edge == 1) { p = new Vector3(t, 0, -le); yaw = 90f; }
                        else if (edge == 2) { p = new Vector3(le, 0, t); yaw = 0f; }
                        else { p = new Vector3(-le, 0, t); yaw = 180f; }
                        Spawn(lightPrefabs[rnd.Next(lightPrefabs.Length)], c + p + Vector3.up * SidewalkH, Quaternion.Euler(0f, yaw, 0f), proot);
                    }
            }
        }

        void MakeCars()
        {
            if (carPrefabs == null || carPrefabs.Length == 0) return;
            var rnd = new System.Random(seed);
            var carRoot = new GameObject("Cars").transform;
            carRoot.SetParent(worldRoot, false);
            int made = 0;
            float[] lanes = { -8.4f, -2.9f, 2.9f, 8.4f };
            int radius = Mathf.CeilToInt(propRadius / pitch);
            int mid = blocks / 2;
            for (int pass = 0; pass < 2 && made < maxCars; pass++)
                for (int a = Mathf.Max(0, mid - radius); a <= Mathf.Min(blocks, mid + radius) && made < maxCars; a++)        // 도로선 번호
                    for (int s = Mathf.Max(0, mid - radius); s < Mathf.Min(blocks, mid + radius) && made < maxCars; s++)   // 구간 번호
                    {
                        int count = rnd.Next(0, 3);
                        for (int n = 0; n < count && made < maxCars; n++)
                        {
                            float along = -half + s * pitch + roadWidth * 0.5f + 8f + (float)rnd.NextDouble() * (blockSize - 16f);
                            float lane = lanes[rnd.Next(4)];
                            float line = -half + a * pitch;
                            Vector3 pos; float yaw;
                            if (pass == 0) { pos = new Vector3(along, 0.06f, line + lane); yaw = lane < 0 ? 90f : 270f; }   // X 방향 도로
                            else { pos = new Vector3(line + lane, 0.06f, along); yaw = lane < 0 ? 0f : 180f; }              // Z 방향 도로
                            if (pos.magnitude > propRadius) continue;
                            MakeCar(carPrefabs[rnd.Next(carPrefabs.Length)], pos, yaw, carRoot);
                            made++;
                        }
                    }
        }

        void MakeCar(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            var go = Spawn(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            // 로컬 공간 바운드로 박스 콜라이더 구성
            Bounds lb = new Bounds(); bool first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                Vector3 ctr = go.transform.InverseTransformPoint(mf.transform.TransformPoint(mb.center));
                var b2 = new Bounds(ctr, mb.size);
                if (first) { lb = b2; first = false; } else lb.Encapsulate(b2);
            }
            if (first) { Destroy(go); return; }
            var bc = go.AddComponent<BoxCollider>();
            bc.center = lb.center; bc.size = lb.size;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.mass = 1400f; rb.drag = 0.1f; rb.angularDrag = 0.3f;
            go.AddComponent<ReactiveProp>();
        }
    }
}
