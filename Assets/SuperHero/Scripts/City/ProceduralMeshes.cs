using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>건물/청크용 박스 메쉬를 코드로 생성하고 캐시한다. 창문 텍스처는 한 칸(3m)이 UV 1.0.</summary>
    public static class ProceduralMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t0 = new List<int>();
            public readonly List<int> t1 = new List<int>();

            // 바깥에서 봤을 때 bl, tl, tr, br 순서
            public void Quad(List<int> tris, Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 normal, float u, float vv)
            {
                int b = v.Count;
                v.Add(bl); v.Add(tl); v.Add(tr); v.Add(br);
                n.Add(normal); n.Add(normal); n.Add(normal); n.Add(normal);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, vv)); uv.Add(new Vector2(u, vv)); uv.Add(new Vector2(u, 0));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            /// <summary>sides*: 측면 UV 반복, sidesTris: 측면 submesh, topTris: 윗면/아랫면 submesh. bottom=false면 아랫면 생략.</summary>
            public void Box(Vector3 min, Vector3 max, float uX, float uZ, float vRep, List<int> sideTris, List<int> topTris, bool bottom)
            {
                float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
                Quad(sideTris, new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.forward, uX, vRep);
                Quad(sideTris, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, uX, vRep);
                Quad(sideTris, new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, uZ, vRep);
                Quad(sideTris, new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), Vector3.left, uZ, vRep);
                Quad(topTris, new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, 1, 1);
                if (bottom)
                    Quad(topTris, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), Vector3.down, 1, 1);
            }

            public Mesh ToMesh(string meshName, bool twoSubmeshes)
            {
                var m = new Mesh { name = meshName };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                if (twoSubmeshes)
                {
                    m.subMeshCount = 2;
                    m.SetTriangles(t0, 0); m.SetTriangles(t1, 1);
                }
                else
                {
                    var all = new List<int>(t0); all.AddRange(t1);
                    m.SetTriangles(all, 0);
                }
                m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>1x1x1 큐브 (모든 면 UV 0..1). 파편용.</summary>
        public static Mesh UnitCube()
        {
            Mesh m;
            if (cache.TryGetValue("unit", out m) && m != null) return m;
            var b = new Builder();
            b.Box(-Vector3.one * 0.5f, Vector3.one * 0.5f, 1, 1, 1, b.t0, b.t0, true);
            m = b.ToMesh("UnitCube", false);
            cache["unit"] = m;
            return m;
        }

        /// <summary>벽 패널: 셀의 +Z 쪽 가장자리에 붙은 얇은 판. 중심은 셀 중심.</summary>
        public static Mesh Wall(float cell, float rowH, float thick)
        {
            string key = "wall" + cell + "_" + rowH + "_" + thick;
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var b = new Builder();
            float h = cell * 0.5f;
            b.Box(new Vector3(-h, -rowH * 0.5f, h - thick), new Vector3(h, rowH * 0.5f, h), 1, 1, rowH / cell, b.t0, b.t0, true);
            m = b.ToMesh(key, false);
            cache[key] = m;
            return m;
        }

        /// <summary>모서리 기둥: 꽉 찬 큐브.</summary>
        public static Mesh Corner(float cell, float rowH)
        {
            string key = "corner" + cell + "_" + rowH;
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var b = new Builder();
            float h = cell * 0.5f;
            b.Box(new Vector3(-h, -rowH * 0.5f, -h), new Vector3(h, rowH * 0.5f, h), 1, 1, rowH / cell, b.t0, b.t0, true);
            m = b.ToMesh(key, false);
            cache[key] = m;
            return m;
        }

        /// <summary>바닥/지붕 슬래브. top=true면 셀 윗쪽, false면 셀 아랫쪽에 붙는다.</summary>
        public static Mesh Slab(float cell, float rowH, float thick, bool top)
        {
            string key = "slab" + cell + "_" + rowH + "_" + thick + top;
            Mesh m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            var b = new Builder();
            float h = cell * 0.5f;
            float y0 = top ? rowH * 0.5f - thick : -rowH * 0.5f;
            b.Box(new Vector3(-h, y0, -h), new Vector3(h, y0 + thick, h), 1, 1, 1, b.t0, b.t0, true);
            m = b.ToMesh(key, false);
            cache[key] = m;
            return m;
        }

        /// <summary>
        /// 온전한 건물 외형. submesh0 = 벽(스타일 머티리얼), submesh1 = 지붕(콘크리트).
        /// insets[row]는 그 층의 외곽 축소 칸 수(계단식 타워).
        /// </summary>
        public static Mesh Building(int cx, int cz, int rows, float rowH, float cell, int[] insets)
        {
            var b = new Builder();
            int r = 0;
            while (r < rows)
            {
                int ins = insets[r];
                int r1 = r;
                while (r1 + 1 < rows && insets[r1 + 1] == ins) r1++;
                float x0 = (ins - cx * 0.5f) * cell, x1 = (cx * 0.5f - ins) * cell;
                float z0 = (ins - cz * 0.5f) * cell, z1 = (cz * 0.5f - ins) * cell;
                float y0 = r * rowH, y1 = (r1 + 1) * rowH;
                float vRep = (y1 - y0) / cell;
                b.Box(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), cx - 2 * ins, cz - 2 * ins, vRep, b.t0, b.t1, false);
                r = r1 + 1;
            }
            return b.ToMesh("Building", true);
        }

        /// <summary>보도 블록 / 바닥 판 (상단 UV는 면적에 맞춰 반복).</summary>
        public static Mesh FlatBox(float sx, float sy, float sz, float tileSize)
        {
            var b = new Builder();
            var min = new Vector3(-sx * 0.5f, 0, -sz * 0.5f);
            var max = new Vector3(sx * 0.5f, sy, sz * 0.5f);
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            b.Quad(b.t0, new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, sx / tileSize, sz / tileSize);
            b.Quad(b.t0, new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.forward, sx / tileSize, 0.05f);
            b.Quad(b.t0, new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, sx / tileSize, 0.05f);
            b.Quad(b.t0, new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, sz / tileSize, 0.05f);
            b.Quad(b.t0, new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), Vector3.left, sz / tileSize, 0.05f);
            return b.ToMesh("FlatBox", false);
        }

        /// <summary>바닥에 깔리는 얇은 사각형 (도로 구간). u = 폭 방향 0..1, v = 길이 방향 반복.</summary>
        public static void AddFlatQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris, float x0, float z0, float x1, float z1, float y, bool alongX, float vRepeat)
        {
            int b = verts.Count;
            verts.Add(new Vector3(x0, y, z0)); verts.Add(new Vector3(x0, y, z1)); verts.Add(new Vector3(x1, y, z1)); verts.Add(new Vector3(x1, y, z0));
            if (alongX)
            {   // 길이가 X 방향: u = z, v = x
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, vRepeat)); uvs.Add(new Vector2(0, vRepeat));
            }
            else
            {   // 길이가 Z 방향: u = x, v = z
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, vRepeat)); uvs.Add(new Vector2(1, vRepeat)); uvs.Add(new Vector2(1, 0));
            }
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }
    }
}
