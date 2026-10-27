using System.Collections.Generic;
using UnityEngine;

namespace TrainGame
{
    /// <summary>
    /// VRoid 계열 모델은 옷 밑에 맨몸이 그대로 있어서 팔을 움직이면(겨드랑이·어깨) 살이 옷을 뚫고 나온다.
    /// 바인드 포즈에서 피부 정점의 법선 방향 바로 앞에 옷이 있으면 "옷에 덮인 피부"로 보고 그 삼각형을 지운다.
    /// 실행 시 메쉬 사본에만 적용하므로 원본 VRM은 바뀌지 않는다.
    /// </summary>
    public static class SkinUnderClothCuller
    {
        /// <param name="coverDistance">피부 → 옷 표면 거리가 이 안이면 덮인 것으로 본다 (m)</param>
        public static int Apply(SkinnedMeshRenderer smr, float coverDistance = 0.05f)
        {
            if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) return 0;
            var mats = smr.sharedMaterials;
            var src = smr.sharedMesh;
            int skinSub = -1;
            var clothSubs = new List<int>();
            for (int i = 0; i < src.subMeshCount && i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name : "";
                if (n.Contains("Body") && n.Contains("SKIN")) skinSub = i;
                else if (n.Contains("CLOTH") && !n.Contains("Accessory")) clothSubs.Add(i);
            }
            if (skinSub < 0 || clothSubs.Count == 0) return 0;

            var verts = src.vertices;
            var normals = src.normals;

            // 옷만 모은 임시 충돌 메쉬 (씬 아래 멀리 띄워 다른 물체와 겹치지 않게)
            var clothTris = new List<int>();
            foreach (int s in clothSubs) clothTris.AddRange(src.GetTriangles(s));
            var clothMesh = new Mesh { indexFormat = src.indexFormat };
            clothMesh.vertices = verts;
            clothMesh.triangles = clothTris.ToArray();
            Vector3 offset = new Vector3(0f, -5000f, 0f);
            var probe = new GameObject("ClothProbe");
            probe.transform.position = offset;
            var col = probe.AddComponent<MeshCollider>();
            col.sharedMesh = clothMesh;
            Physics.SyncTransforms();

            bool oldBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;   // 몸 안쪽에서 바깥으로 쏘면 옷의 뒷면에 맞는다
            var covered = new Dictionary<int, bool>();
            var skinTris = src.GetTriangles(skinSub);
            bool Covered(int v)
            {
                bool c;
                if (covered.TryGetValue(v, out c)) return c;
                Vector3 n = normals[v];
                Vector3 o = offset + verts[v] - n * 0.002f;
                c = col.Raycast(new Ray(o, n), out _, coverDistance);
                covered[v] = c;
                return c;
            }
            var kept = new List<int>(skinTris.Length);
            int removed = 0;
            for (int t = 0; t < skinTris.Length; t += 3)
            {
                int a = skinTris[t], b = skinTris[t + 1], c = skinTris[t + 2];
                if (Covered(a) && Covered(b) && Covered(c)) { removed++; continue; }
                kept.Add(a); kept.Add(b); kept.Add(c);
            }
            Physics.queriesHitBackfaces = oldBack;
            Object.Destroy(probe);
            Object.Destroy(clothMesh);

            if (removed == 0) return 0;
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_SkinCulled";
            mesh.SetTriangles(kept, skinSub);
            smr.sharedMesh = mesh;
            return removed;
        }
    }
}
