using UnityEngine;

// 탱크: 도로(교차로 그리드)를 따라 이동하며 거인과 일정 거리(minRange~maxRange)를 유지.
// 거인이 가까이 오면 멀어지는 교차로로 도망, 너무 멀면 접근, 사거리 안이면 멈춰서 포탑을 돌려 사격.
public class TankAI : MonoBehaviour
{
    public CityGenerator city;
    public Transform turret;
    public Transform muzzle;

    [Header("거리 유지")]
    public float minRange = 90f;
    public float maxRange = 220f;

    [Header("이동")]
    public float moveSpeed = 50f;
    public float turnSpeed = 240f;
    public float turretTurnSpeed = 90f;

    [Header("사격")]
    public float fireInterval = 2.6f;
    public float shellSpeed = 120f;
    public float damage = 65f;
    public float spread = 7f;

    public bool IsDead { get; private set; }
    [Tooltip("체력 (방귀 가스 등 지속 피해용. 거인에게 밟히면 체력과 관계없이 즉사)")] public float maxHP = 60f;
    float hp = -1f;

    // 해산(거인이 집에 들어감): 사격을 멈추고 거인에게서 멀어지다가 화면 밖에서 사라짐
    bool dispersing; float disperseAt;
    public void Disperse() { if (!IsDead && !dispersing) { dispersing = true; disperseAt = Time.time; } }

    // 지속 피해 (방귀 가스). 체력이 다 떨어지면 터짐
    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        if (hp < 0f) hp = maxHP;
        hp -= dmg;
        if (hp <= 0f) Crush();
    }
    [Tooltip("시야가 이 시간 이상 막혀 있으면 자리 이동")] public float repositionAfter = 1.5f;
    float noLosTime;
    int buildingMask;
    static readonly HumanBodyBones[] LosBones = { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };

    int ci, cj, ti, tj, lastI = -1, lastJ = -1;
    bool moving;
    float nextFire;
    Vector3 gridOrigin; float pitch; int maxI, maxJ;

    void Start()
    {
        if (!city) city = FindObjectOfType<CityGenerator>();
        pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        gridOrigin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        maxI = city.blocksX; maxJ = city.blocksZ;
        ci = Mathf.Clamp(Mathf.RoundToInt((transform.position.x - gridOrigin.x) / pitch), 0, maxI);
        cj = Mathf.Clamp(Mathf.RoundToInt((transform.position.z - gridOrigin.z) / pitch), 0, maxJ);
        ti = ci; tj = cj; moving = true; // 가까운 교차로로 먼저 이동
        nextFire = Time.time + Random.Range(1f, fireInterval);
        buildingMask = LayerMask.GetMask("Building");
    }

    // origin에서 건물에 가리지 않고 보이는 거인 부위 찾기
    bool FindVisibleAim(GiantHealth g, Vector3 origin, out Vector3 aim)
    {
        int start = Random.Range(0, LosBones.Length);
        for (int k = 0; k < LosBones.Length; k++)
        {
            var b = g.Bone(LosBones[(start + k) % LosBones.Length]);
            if (!b) continue;
            if (!Physics.Linecast(origin, b.position, buildingMask, QueryTriggerInteraction.Ignore)) { aim = b.position; return true; }
        }
        aim = Vector3.zero;
        return false;
    }

    Vector3 MuzzlePos => muzzle ? muzzle.position : transform.position + Vector3.up * 5f;

    Vector3 Node(int i, int j) => new Vector3(gridOrigin.x + i * pitch, transform.position.y, gridOrigin.z + j * pitch);

    static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

    void Update()
    {
        var g = GiantHealth.Instance;
        if (!g || EnemyFreeze.Active) return; // 보급상자 효과: 적 정지
        Vector3 gp = g.transform.position;
        float dist = Flat(transform.position, gp);

        if (dispersing)
        {
            if (MilitarySpawner.OutOfSight(transform.position) || Time.time - disperseAt > 30f) { Destroy(gameObject); return; }
            if (!moving) Decide(gp, 0f); // 거리 0으로 넘겨서 항상 멀어지는 교차로 선택
            if (moving) Drive();
            return;
        }

        bool los = FindVisibleAim(g, MuzzlePos, out _);
        noLosTime = los ? 0f : noLosTime + Time.deltaTime;

        if (!moving) Decide(gp, dist);
        if (moving) Drive();

        // 포탑 조준
        if (turret)
        {
            Vector3 to = gp - turret.position; to.y = 0;
            if (to.sqrMagnitude > 1f)
            {
                var want = Quaternion.LookRotation(to);
                turret.rotation = Quaternion.RotateTowards(turret.rotation, want, turretTurnSpeed * Time.deltaTime);
            }
        }

        // 사격 (이동 중에도 사거리 안이면)
        if (!g.IsDead && dist <= maxRange + 80f && Time.time >= nextFire)
        {
            Vector3 fwd = turret ? turret.forward : transform.forward;
            Vector3 to = gp - transform.position; to.y = 0;
            if (Vector3.Angle(fwd, to) < 12f) Fire(g);
        }
    }

    void Decide(Vector3 gp, float dist)
    {
        int bi = ci, bj = cj;
        if (dist < minRange && !GasCloud.InSmoke(transform.position)) // 방귀 연막 안에서는 거인이 다가와도 도망가지 않음
        {
            // 도망: 거인에게서 가장 멀어지는 인접 교차로
            float best = dist;
            foreach (var n in Neighbors())
            {
                float d = Flat(Node(n.x, n.y), gp) + ((n.x == lastI && n.y == lastJ) ? -30f : 0f);
                if (d > best) { best = d; bi = n.x; bj = n.y; }
            }
        }
        else if (dist > maxRange)
        {
            // 접근: 거인에게 가까워지되 minRange 안으로는 들어가지 않는 교차로
            float best = dist;
            foreach (var n in Neighbors())
            {
                float d = Flat(Node(n.x, n.y), gp);
                if (d < best && d > minRange) { best = d; bi = n.x; bj = n.y; }
            }
        }
        else if (noLosTime > repositionAfter)
        {
            // 사거리 안이지만 건물에 가려 쏠 수 없음 → 시야가 트인 인접 교차로로 이동 (없으면 거인 쪽으로)
            var g = GiantHealth.Instance;
            float best = float.MaxValue;
            foreach (var nb in Neighbors())
            {
                Vector3 np = Node(nb.x, nb.y);
                float d = Flat(np, gp);
                if (d < minRange * 0.6f) continue;
                bool clear = g && FindVisibleAim(g, np + Vector3.up * 5f, out _);
                float score = (clear ? 0f : 10000f) + d + ((nb.x == lastI && nb.y == lastJ) ? 50f : 0f);
                if (score < best) { best = score; bi = nb.x; bj = nb.y; }
            }
            noLosTime = 0f;
        }
        if (bi != ci || bj != cj) { ti = bi; tj = bj; moving = true; }
    }

    System.Collections.Generic.IEnumerable<Vector2Int> Neighbors()
    {
        if (ci > 0 && city.RoadOpen(ci, cj, ci - 1, cj)) yield return new Vector2Int(ci - 1, cj); // 블록을 붙여 없어진 도로는 지나지 않음
        if (ci < maxI && city.RoadOpen(ci, cj, ci + 1, cj)) yield return new Vector2Int(ci + 1, cj);
        if (cj > 0 && city.RoadOpen(ci, cj, ci, cj - 1)) yield return new Vector2Int(ci, cj - 1);
        if (cj < maxJ && city.RoadOpen(ci, cj, ci, cj + 1)) yield return new Vector2Int(ci, cj + 1);
    }

    void Drive()
    {
        Vector3 target = Node(ti, tj);
        Vector3 to = target - transform.position; to.y = 0;
        if (to.magnitude < 1.5f)
        {
            transform.position = target;
            lastI = ci; lastJ = cj; ci = ti; cj = tj;
            moving = false;
            return;
        }
        var want = Quaternion.LookRotation(to);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, want) < 25f)
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * ChaseBoost.Factor(transform.position) * Time.deltaTime);
    }

    // 거인에게 밟힘: 폭발 + 납작하게 찌그러진 잔해로 남았다가 사라짐
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        enabled = false;
        ScoreManager.Add(ScoreManager.Instance ? ScoreManager.Instance.tankPoints : 500, "TANK");
        CageSkill.Imprison(CageSkill.Kind.Soldier, CageSkill.TankCrew); // 승무원은 새장으로
        CombatFX.Explosion(transform.position + Vector3.up * 3f, 16f);
        CombatFX.Explosion(transform.position + Vector3.up * 1f, 10f);
        Vector3 sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.2f, sc.y * 0.22f, sc.z * 1.15f);
        transform.rotation *= Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-20f, 20f), Random.Range(-4f, 4f));
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.3f;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.4f);
        Destroy(gameObject, 8f);
    }

    void Fire(GiantHealth g)
    {
        if (GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden) { nextFire = Time.time + 0.5f; return; } // 거인의 집 안이거나 거인이 사람을 손에 쥐고 있으면(인질) 공격 안 함
        nextFire = Time.time + fireInterval * Random.Range(0.8f, 1.2f);
        Vector3 origin = MuzzlePos;
        if (!FindVisibleAim(g, origin, out Vector3 aim)) { nextFire = Time.time + 0.5f; return; } // 건물에 가려지면 쏘지 않음
        float t = Vector3.Distance(origin, aim) / shellSpeed;
        // 방귀 가스(연막) 안에서는 거인 움직임을 못 읽고 조준이 크게 흔들림
        bool smoke = GasCloud.InSmoke(transform.position);
        aim += (smoke ? Vector3.zero : g.Velocity * t) + Random.insideUnitSphere * spread * (smoke ? 3f : 0.5f);
        Vector3 dir = (aim - origin).normalized;
        CombatFX.Muzzle(origin, dir, 3f);
        CombatFX.Spawn(CombatFX.Kind.Shell, origin, dir, shellSpeed, damage, 0f);
    }
}
