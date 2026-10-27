using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>떨어져 나온 벽 조각 하나. 땅/벽에 세게 부딪히면 작은 파편으로 쪼개지며 먼지를 낸다.</summary>
    public class DebrisChunk : MonoBehaviour
    {
        public bool canShatter = true;
        public float shatterSpeed = 6f;
        public Material mat;
        public Vector3 size = Vector3.one * 3f;
        float born;
        bool shattered;

        void Awake() { born = Time.time; }

        void OnCollisionEnter(Collision c)
        {
            if (!canShatter || shattered) return;
            if (Time.time - born < 0.2f) return;
            if (c.relativeVelocity.magnitude < shatterSpeed) return;
            Vector3 p = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            Shatter(p);
        }

        public void Shatter(Vector3 p)
        {
            if (shattered) return;
            shattered = true;
            var dm = DestructionManager.Instance;
            var rb = GetComponent<Rigidbody>();
            Vector3 v = rb != null ? rb.velocity : Vector3.zero;
            int frags = dm.ActiveCount < dm.maxDebris * 0.85f ? Random.Range(3, 6) : 0;
            for (int i = 0; i < frags; i++)
            {
                var f = new GameObject("frag");
                f.transform.position = transform.position + Random.insideUnitSphere * size.magnitude * 0.2f;
                f.transform.rotation = Random.rotation;
                f.transform.localScale = new Vector3(size.x, Mathf.Max(size.y, 0.6f), Mathf.Max(size.z, 0.6f)) * Random.Range(0.18f, 0.38f);
                f.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.UnitCube();
                var mr = f.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat != null ? mat : CityMaterials.Concrete;
                f.AddComponent<BoxCollider>();
                DestructibleBuilding.MakeDebris(f, v * 0.45f + Random.insideUnitSphere * 7f + Vector3.up * 3f, Random.insideUnitSphere * 9f, 14f, false);
            }
            Fx.Dust(p, Mathf.Clamp(size.x, 1.5f, 4f), 2, 3f);
            Sfx.Crumble(p, 0.22f);
            Destroy(gameObject);
        }
    }

    /// <summary>무너지는 건물 덩어리(여러 청크가 한 몸). 땅에 닿으면 산산조각 난다.</summary>
    public class ChunkIsland : MonoBehaviour
    {
        Rigidbody rb;
        float born;
        bool done;

        public void Init(Rigidbody r) { rb = r; born = Time.time; }

        void OnCollisionEnter(Collision c)
        {
            if (done) return;
            float t = Time.time - born;
            float sp = c.relativeVelocity.magnitude;
            if (sp > 7f || (t > 1.5f && sp > 3.5f))
                Shatter(c.contactCount > 0 ? c.GetContact(0).point : transform.position);
        }

        void Update()
        {
            if (!done && Time.time - born > 10f) Shatter(transform.position);
        }

        void Shatter(Vector3 p)
        {
            if (done) return;
            done = true;
            var kids = new List<Transform>();
            foreach (Transform t in transform) kids.Add(t);
            int n = kids.Count;
            for (int i = 0; i < n; i++)
            {
                var t = kids[i];
                if (t == null) continue;
                Vector3 v = rb != null ? rb.GetPointVelocity(t.position) : Vector3.zero;
                var go = t.gameObject;
                DestructibleBuilding.MakeDebris(go, v * 0.8f + Random.insideUnitSphere * 4f, Random.insideUnitSphere * 5f, 70f, true);
                if (i % 3 == 0) Fx.Dust(t.position, 3.5f, 1, 4f);
            }
            Fx.Dust(p, 7f, 6, 8f);
            Fx.Shake(p, Mathf.Clamp(n * 0.012f, 0.15f, 1.4f), 450f);
            Sfx.Boom(p, Mathf.Clamp(n * 0.1f, 4f, 20f));
            Sfx.Crumble(p, 0.9f);
            Destroy(gameObject);
        }
    }
}
