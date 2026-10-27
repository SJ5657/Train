using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 배틀필드식 구조 파괴 건물.
    ///  - 평소엔 메쉬 1개 + 콜라이더로 가볍게 존재하다가, 처음 맞는 순간 3m 격자 청크(벽 패널/기둥/바닥/지붕)로 쪼개진다.
    ///  - 피해는 공격 지점의 구 안쪽 청크부터 깎여 나가며(거리순으로 약간 지연) 바깥으로 날아간다.
    ///  - 부서진 뒤에는 "지지 검사"를 돌려, 지면까지 이어지지 못한 부분(허공에 매달린 벽, 잘려나간 윗층)을 찾아
    ///    파괴 지점에서 가까운 곳부터 순서대로 무너뜨린다.
    /// </summary>
    public class DestructibleBuilding : MonoBehaviour
    {
        public static readonly List<DestructibleBuilding> All = new List<DestructibleBuilding>();

        // 생성기가 채우는 값
        [HideInInspector] public int cx, cz, rows, styleIndex, slabStride = 1;
        [HideInInspector] public float cell = 3f, rowH = 3f, toughness = 1f;
        [HideInInspector] public int[] insets;

        public const float WallThick = 0.9f, SlabThick = 0.5f;
        [Tooltip("지면에서 이어진 기둥으로부터 옆으로 이만큼(칸)까지만 매달려 버틴다")]
        public int maxOverhang = 2;

        const byte KNone = 0, KWall = 1, KCorner = 2, KSlab = 3, KRoof = 4;

        byte[] kind, side, dist;
        float[] hp;
        bool[] alive;
        GameObject[] objs;
        Material wallMat;
        Transform chunkRoot;
        MeshRenderer introRenderer;
        readonly List<Collider> introCols = new List<Collider>();
        Bounds wb;

        struct BreakJob { public int idx; public float time; public Vector3 from; public float force; }
        class Collapse { public List<int> cells; public float time; public Vector3 away; }

        readonly List<BreakJob> breaks = new List<BreakJob>();
        readonly List<Collapse> pending = new List<Collapse>();
        bool stabilityDirty;
        float stabilityTime;
        Vector3 lastImpact;
        int aliveCount;

        public bool Chunked { get; private set; }
        public Bounds WorldBounds { get { return wb; } }

        // ───────────────────────── 초기화 ─────────────────────────

        public void Init(MeshRenderer renderer, List<Collider> cols)
        {
            introRenderer = renderer;
            if (cols != null) introCols.AddRange(cols);
            wallMat = CityMaterials.Style(styleIndex);
            wb = new Bounds(transform.position + Vector3.up * (rows * rowH * 0.5f), new Vector3(cx * cell, rows * rowH, cz * cell));
            All.Add(this);
            enabled = false;
        }

        void OnDestroy() { All.Remove(this); }

        public float SqrDistanceTo(Vector3 p) { return wb.SqrDistance(p); }

        Vector3 CellLocal(int x, int y, int z)
        {
            return new Vector3((x + 0.5f - cx * 0.5f) * cell, (y + 0.5f) * rowH, (z + 0.5f - cz * 0.5f) * cell);
        }

        int Index(int x, int y, int z) { return (y * cz + z) * cx + x; }

        void BuildGrid()
        {
            int n = cx * cz * rows;
            kind = new byte[n]; side = new byte[n]; dist = new byte[n];
            hp = new float[n]; alive = new bool[n]; objs = new GameObject[n];
            for (int y = 0; y < rows; y++)
            {
                int ins = insets[y];
                int x0 = ins, x1 = cx - 1 - ins, z0 = ins, z1 = cz - 1 - ins;
                bool tierTop = y == rows - 1 || insets[y + 1] != insets[y];
                bool slabRow = tierTop || (y % Mathf.Max(1, slabStride) == 0);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = Index(x, y, z);
                        bool edgeX = x == x0 || x == x1, edgeZ = z == z0 || z == z1;
                        if (edgeX || edgeZ)
                        {
                            if (edgeX && edgeZ) { kind[i] = KCorner; hp[i] = 190f * toughness; }
                            else
                            {
                                kind[i] = KWall; hp[i] = 105f * toughness;
                                side[i] = (byte)(z == z1 ? 0 : z == z0 ? 1 : x == x1 ? 2 : 3);
                            }
                        }
                        else if (tierTop) { kind[i] = KRoof; hp[i] = 70f * toughness; }
                        else if (slabRow) { kind[i] = KSlab; hp[i] = 70f * toughness; }
                    }
            }
        }

        // ───────────────────────── 청크 생성 ─────────────────────────

        void Chunkify()
        {
            if (Chunked) return;
            Chunked = true;
            BuildGrid();
            chunkRoot = new GameObject("Chunks").transform;
            chunkRoot.SetParent(transform, false);
            for (int y = 0; y < rows; y++)
                for (int z = 0; z < cz; z++)
                    for (int x = 0; x < cx; x++)
                    {
                        int i = Index(x, y, z);
                        if (kind[i] == KNone) continue;
                        objs[i] = MakeChunk(i, x, y, z);
                        alive[i] = true;
                        aliveCount++;
                    }
            if (introRenderer != null) introRenderer.enabled = false;
            for (int i = 0; i < introCols.Count; i++) if (introCols[i] != null) introCols[i].enabled = false;
            enabled = true;
        }

        GameObject MakeChunk(int i, int x, int y, int z)
        {
            var go = new GameObject("c");
            var t = go.transform;
            t.SetParent(chunkRoot, false);
            t.localPosition = CellLocal(x, y, z);
            Mesh mesh; Material mat;
            switch (kind[i])
            {
                case KWall:
                    mesh = ProceduralMeshes.Wall(cell, rowH, WallThick); mat = wallMat;
                    t.localRotation = Quaternion.Euler(0f, side[i] == 0 ? 0f : side[i] == 1 ? 180f : side[i] == 2 ? 90f : 270f, 0f);
                    break;
                case KCorner: mesh = ProceduralMeshes.Corner(cell, rowH); mat = wallMat; break;
                case KRoof: mesh = ProceduralMeshes.Slab(cell, rowH, SlabThick, true); mat = CityMaterials.Concrete; break;
                default: mesh = ProceduralMeshes.Slab(cell, rowH, SlabThick, false); mat = CityMaterials.Concrete; break;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = mesh.bounds.center; bc.size = mesh.bounds.size;
            go.layer = GameLayers.Building;
            return go;
        }

        // ───────────────────────── 피해 ─────────────────────────

        /// <summary>
        /// 구 범위 피해. 반환값 = 새로 부서지기 시작한 청크 수.
        /// minDist: 이 거리 안쪽은 건너뜀 (확장하는 충격파 고리용).
        /// </summary>
        public int ApplyDamage(Vector3 point, float radius, float damage, float force, float minDist = 0f)
        {
            if (wb.SqrDistance(point) > radius * radius) return 0;
            if (!Chunked) Chunkify();

            Vector3 lp = point - transform.position;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((lp.x - radius) / cell + cx * 0.5f));
            int x1 = Mathf.Min(cx - 1, Mathf.FloorToInt((lp.x + radius) / cell + cx * 0.5f));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((lp.z - radius) / cell + cz * 0.5f));
            int z1 = Mathf.Min(cz - 1, Mathf.FloorToInt((lp.z + radius) / cell + cz * 0.5f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((lp.y - radius) / rowH));
            int y1 = Mathf.Min(rows - 1, Mathf.FloorToInt((lp.y + radius) / rowH));
            Vector3 half = new Vector3(cell * 0.5f, rowH * 0.5f, cell * 0.5f);
            float now = Time.time;
            int broke = 0;

            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = Index(x, y, z);
                        if (!alive[i]) continue;
                        Vector3 c = CellLocal(x, y, z);
                        float dx = Mathf.Max(0f, Mathf.Abs(lp.x - c.x) - half.x);
                        float dy = Mathf.Max(0f, Mathf.Abs(lp.y - c.y) - half.y);
                        float dz = Mathf.Max(0f, Mathf.Abs(lp.z - c.z) - half.z);
                        float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                        if (d >= radius || d < minDist) continue;
                        float f = 1f - 0.75f * (d / radius);
                        hp[i] -= damage * f;
                        if (hp[i] > 0f) continue;

                        alive[i] = false; aliveCount--;
                        float delay = radius > 5f ? Mathf.Min(d * 0.012f, 0.45f) : 0f;
                        breaks.Add(new BreakJob { idx = i, time = now + delay, from = point, force = force * (0.5f + 0.5f * f) });
                        broke++;
                    }

            if (broke > 0)
            {
                lastImpact = point;
                stabilityDirty = true;
                stabilityTime = now + 0.3f;
                enabled = true;
            }
            return broke;
        }

        void Update()
        {
            float now = Time.time;

            for (int k = breaks.Count - 1; k >= 0; k--)
            {
                if (breaks[k].time > now) continue;
                DoBreak(breaks[k]);
                breaks.RemoveAt(k);
            }

            if (stabilityDirty && breaks.Count == 0 && now >= stabilityTime)
            {
                stabilityDirty = false;
                CheckStability();
            }

            for (int k = pending.Count - 1; k >= 0; k--)
            {
                if (pending[k].time > now) continue;
                ExecuteCollapse(pending[k]);
                pending.RemoveAt(k);
            }

            if (breaks.Count == 0 && pending.Count == 0 && !stabilityDirty) enabled = false;
        }

        void DoBreak(BreakJob job)
        {
            var go = objs[job.idx];
            objs[job.idx] = null;
            if (go == null) return;
            Vector3 pos = go.transform.position;
            Vector3 dir = pos - job.from;
            float len = dir.magnitude;
            dir = len > 0.05f ? dir / len : Random.onUnitSphere;
            dir = (dir + Vector3.up * 0.25f).normalized;
            float speed = Mathf.Clamp(job.force * Random.Range(0.7f, 1.25f), 2.5f, 38f);
            MakeDebris(go, dir * speed, Random.insideUnitSphere * 7f, 60f, true);
            Fx.Dust(pos, 3f, 1, 3.5f);
        }

        // ───────────────────────── 지지(구조) 검사 ─────────────────────────

        static readonly int[] DX = { 1, -1, 0, 0, 0, 0 };
        static readonly int[] DY = { 0, 0, 0, 0, 1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 0, 0 };

        bool Link(int a, int b, int dy, out int cost)
        {
            cost = 0;
            if (!alive[b]) return false;
            bool aS = kind[a] >= KSlab, bS = kind[b] >= KSlab;
            if (dy == 0) { cost = bS ? 0 : 1; return true; }
            if (aS && bS) return false;       // 바닥판끼리는 위아래로 안 이어짐
            cost = dy > 0 ? 0 : 1;            // 위로는 기둥을 타고 공짜, 아래로 매달리면 1
            return true;
        }

        void CheckStability()
        {
            int n = alive.Length;
            int max = maxOverhang;
            for (int i = 0; i < n; i++) dist[i] = 255;

            var buckets = new List<int>[max + 1];
            for (int b = 0; b <= max; b++) buckets[b] = new List<int>();
            int layer = cx * cz;
            for (int i = 0; i < layer; i++)
                if (alive[i]) { dist[i] = 0; buckets[0].Add(i); }

            for (int d = 0; d <= max; d++)
            {
                var list = buckets[d];
                for (int k = 0; k < list.Count; k++)
                {
                    int i = list[k];
                    if (dist[i] != d) continue;
                    int x = i % cx, z = (i / cx) % cz, y = i / layer;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        int nx = x + DX[dir], ny = y + DY[dir], nz = z + DZ[dir];
                        if (nx < 0 || nx >= cx || nz < 0 || nz >= cz || ny < 0 || ny >= rows) continue;
                        int j = Index(nx, ny, nz);
                        int cost;
                        if (!Link(i, j, DY[dir], out cost)) continue;
                        int nd = d + cost;
                        if (nd > max || nd >= dist[j]) continue;
                        dist[j] = (byte)nd;
                        buckets[nd].Add(j);
                    }
                }
            }

            // 지지 못 받는 셀을 이어진 덩어리끼리 묶는다
            List<int> unsupported = null;
            for (int i = 0; i < n; i++)
                if (alive[i] && dist[i] > max)
                {
                    if (unsupported == null) unsupported = new List<int>();
                    unsupported.Add(i);
                }
            if (unsupported == null) return;

            var visited = new HashSet<int>();
            var unsupSet = new HashSet<int>(unsupported);
            float now = Time.time;
            for (int u = 0; u < unsupported.Count; u++)
            {
                int start = unsupported[u];
                if (visited.Contains(start)) continue;
                var comp = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start); visited.Add(start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    comp.Add(i);
                    int x = i % cx, z = (i / cx) % cz, y = i / layer;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        int nx = x + DX[dir], ny = y + DY[dir], nz = z + DZ[dir];
                        if (nx < 0 || nx >= cx || nz < 0 || nz >= cz || ny < 0 || ny >= rows) continue;
                        int j = Index(nx, ny, nz);
                        if (!unsupSet.Contains(j) || visited.Contains(j)) continue;
                        int c;
                        if (!Link(i, j, DY[dir], out c)) continue;
                        visited.Add(j); stack.Push(j);
                    }
                }

                // 파괴 지점에서 가장 가까운 셀까지의 거리로 붕괴 시간을 정한다 (가까운 곳부터 순서대로)
                float nearest = float.MaxValue;
                Vector3 centroid = Vector3.zero;
                for (int k = 0; k < comp.Count; k++)
                {
                    int i = comp[k];
                    int x = i % cx, z = (i / cx) % cz, y = i / layer;
                    Vector3 w = transform.position + CellLocal(x, y, z);
                    centroid += w;
                    nearest = Mathf.Min(nearest, Vector3.Distance(w, lastImpact));
                    alive[i] = false; aliveCount--;
                }
                centroid /= comp.Count;
                Vector3 away = centroid - lastImpact; away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere;
                pending.Add(new Collapse { cells = comp, time = now + 0.35f + nearest * 0.014f + Random.Range(0f, 0.3f), away = away });
            }
            enabled = true;
        }

        void ExecuteCollapse(Collapse c)
        {
            var gos = new List<GameObject>(c.cells.Count);
            for (int k = 0; k < c.cells.Count; k++)
            {
                int i = c.cells[k];
                if (objs[i] != null) { gos.Add(objs[i]); objs[i] = null; }
            }
            if (gos.Count == 0) return;
            gos.Sort((a, b) => a.transform.position.y.CompareTo(b.transform.position.y));

            if (gos.Count <= 2)
            {
                foreach (var g in gos)
                    MakeDebris(g, c.away * Random.Range(1f, 3f) + Vector3.down, Random.insideUnitSphere * 2f, 60f, true);
                return;
            }
            const int MaxIsland = 200;
            for (int s = 0; s < gos.Count; s += MaxIsland)
                CreateIsland(gos.GetRange(s, Mathf.Min(MaxIsland, gos.Count - s)), c.away);
        }

        void CreateIsland(List<GameObject> gos, Vector3 away)
        {
            Vector3 center = Vector3.zero;
            foreach (var g in gos) center += g.transform.position;
            center /= gos.Count;

            var island = new GameObject("Island");
            island.transform.position = center;
            island.transform.SetParent(DestructionManager.Instance.DebrisRoot, true);
            var rb = island.AddComponent<Rigidbody>();
            rb.mass = gos.Count * 150f;
            rb.drag = 0.02f; rb.angularDrag = 0.4f;
            foreach (var g in gos)
            {
                g.transform.SetParent(island.transform, true);
                g.layer = GameLayers.Debris;
            }
            // 파괴 지점 반대쪽으로 천천히 기울며 떨어진다
            Vector3 axis = Vector3.Cross(Vector3.up, away);
            rb.velocity = away * Random.Range(0.5f, 2.5f) + Vector3.down * 1.5f;
            rb.angularVelocity = axis * Random.Range(0.15f, 0.5f) + Random.insideUnitSphere * 0.1f;
            island.AddComponent<ChunkIsland>().Init(rb);
            Fx.Dust(center, 6f, 3, 5f);
            Fx.Shake(center, 0.35f, 300f);
            Sfx.Crumble(center, 0.7f);
        }

        // ───────────────────────── 공용: 파편화 ─────────────────────────

        /// <summary>청크 GameObject를 자유 낙하 리지드바디 파편으로 바꾼다.</summary>
        public static Rigidbody MakeDebris(GameObject go, Vector3 vel, Vector3 ang, float life, bool canShatter)
        {
            var dm = DestructionManager.Instance;
            go.transform.SetParent(dm.DebrisRoot, true);
            go.layer = GameLayers.Debris;
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            var bc = go.GetComponent<BoxCollider>();
            Vector3 sz = bc != null ? Vector3.Scale(bc.size, go.transform.lossyScale) : Vector3.one * 2f;
            rb.mass = Mathf.Clamp(sz.x * sz.y * sz.z * 40f, 6f, 800f);
            rb.drag = 0.04f; rb.angularDrag = 0.15f;
            rb.velocity = vel; rb.angularVelocity = ang;
            var dc = go.GetComponent<DebrisChunk>();
            if (dc == null) dc = go.AddComponent<DebrisChunk>();
            dc.canShatter = canShatter;
            dc.size = sz;
            var mr = go.GetComponent<MeshRenderer>();
            dc.mat = mr != null ? mr.sharedMaterial : null;
            DestructionManager.Register(go, rb, life);
            return rb;
        }
    }
}
