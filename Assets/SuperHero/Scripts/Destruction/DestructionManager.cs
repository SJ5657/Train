using System.Collections.Generic;
using UnityEngine;

namespace SuperHero
{
    /// <summary>
    /// 파편(리지드바디) 예산 관리 + 폭발 한 방 API.
    /// 파편이 너무 많아지면 오래된 것부터 지우고, 멈춘 파편은 리지드바디를 떼서 정적 잔해로 굳힌다.
    /// </summary>
    public class DestructionManager : MonoBehaviour
    {
        static DestructionManager inst;

        public static DestructionManager Instance
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("DestructionManager");
                    inst = go.AddComponent<DestructionManager>();
                }
                return inst;
            }
        }

        [Header("Debris budget")]
        public int maxDebris = 1500;
        public float gravity = -22f;

        public Transform DebrisRoot { get; private set; }
        public int ActiveCount { get { return list.Count; } }

        class Entry
        {
            public GameObject go; public Rigidbody rb; public float expire; public float sleepT; public bool frozen;
            public float shrinkT = -1f; public Vector3 baseScale;
        }

        readonly List<Entry> list = new List<Entry>(2048);
        static readonly Collider[] pushBuf = new Collider[1024];

        void Awake()
        {
            inst = this;
            Physics.gravity = new Vector3(0f, gravity, 0f);
            GameLayers.ConfigurePhysics();
            var root = new GameObject("Debris");
            root.transform.SetParent(transform, false);
            DebrisRoot = root.transform;
        }

        void OnDestroy() { if (inst == this) inst = null; }

        public static void Register(GameObject go, Rigidbody rb, float life)
        {
            var m = Instance;
            m.list.Add(new Entry { go = go, rb = rb, expire = Time.time + life });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            // 예산 초과 시 오래된 것부터 정리
            int over = list.Count - maxDebris;
            if (over > 0)
            {
                for (int i = 0; i < over; i++) if (list[i].go != null) Destroy(list[i].go);
                list.RemoveRange(0, over);
            }

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e.go == null) { list.RemoveAt(i); continue; }

                if (e.shrinkT >= 0f)
                {
                    e.shrinkT += dt;
                    float k = 1f - e.shrinkT / 1.2f;
                    if (k <= 0f) { Destroy(e.go); list.RemoveAt(i); continue; }
                    e.go.transform.localScale = e.baseScale * k;
                    continue;
                }

                if (now >= e.expire)
                {
                    e.shrinkT = 0f; e.baseScale = e.go.transform.localScale;
                    continue;
                }

                if (!e.frozen && e.rb != null)
                {
                    if (e.rb.IsSleeping()) e.sleepT += dt; else e.sleepT = 0f;
                    if (e.sleepT > 3f)
                    {
                        Destroy(e.rb);     // 멈춘 잔해는 정적 콜라이더로 굳힘
                        e.rb = null; e.frozen = true;
                    }
                }
            }
        }

        // ───────────────────────── 폭발 / 피해 ─────────────────────────

        /// <summary>구 범위에 건물 피해 + 물리 오브젝트 밀어내기. 부서진 셀 수 반환.</summary>
        public static int Explode(Vector3 center, float radius, float damage, float force, float minDist = 0f)
        {
            int broke = DamageBuildings(center, radius, damage, force, minDist);
            if (force > 0f) Push(center, radius, force, minDist);
            return broke;
        }

        public static int DamageBuildings(Vector3 center, float radius, float damage, float force, float minDist = 0f)
        {
            int broke = 0;
            var all = DestructibleBuilding.All;
            float r2 = radius * radius;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var b = all[i];
                if (b == null) { all.RemoveAt(i); continue; }
                if (b.SqrDistanceTo(center) > r2) continue;
                broke += b.ApplyDamage(center, radius, damage, force, minDist);
            }
            return broke;
        }

        public static void Push(Vector3 center, float radius, float force, float minDist = 0f)
        {
            int n = Physics.OverlapSphereNonAlloc(center, radius, pushBuf, GameLayers.PushMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = pushBuf[i].attachedRigidbody;
                if (rb == null) continue;
                var prop = rb.GetComponent<ReactiveProp>();
                if (prop != null) prop.Wake();
                if (rb.isKinematic) continue;
                float d = Vector3.Distance(rb.worldCenterOfMass, center);
                if (d < minDist) continue;
                float k = 1f - Mathf.Clamp01(d / radius);
                Vector3 dir = rb.worldCenterOfMass - center;
                dir = dir.sqrMagnitude < 0.01f ? Vector3.up : dir.normalized;
                dir = (dir + Vector3.up * 0.35f).normalized;
                rb.AddForce(dir * force * (0.3f + k), ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * force * 0.15f, ForceMode.VelocityChange);
            }
        }
    }

    /// <summary>폭발에 날아가는 자동차 같은 소품. 처음엔 키네마틱, 폭발이 닿으면 물리 활성화.</summary>
    public class ReactiveProp : MonoBehaviour
    {
        Rigidbody rb;

        void Awake() { rb = GetComponent<Rigidbody>(); }

        public void Wake()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            if (rb == null || !rb.isKinematic) return;
            rb.isKinematic = false;
            gameObject.layer = GameLayers.Debris;
            DestructionManager.Register(gameObject, rb, 40f);
        }
    }
}
