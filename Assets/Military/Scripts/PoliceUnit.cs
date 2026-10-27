using System.Collections.Generic;
using UnityEngine;

// 경찰차 + 경찰 2명 (MilitarySpawner가 생성, 게임 초반의 적).
// 차는 도로 교차로를 따라 거인 근처(배치 거리)까지 와서 멈추고, 경찰 2명이 양옆으로 내려 권총을 쏨.
// 거인이 너무 가까이 오면 경찰들이 차로 뛰어가 타고, 차는 거인에게서 멀어지는 교차로로 이동해 거리를 벌린 뒤 다시 내림.
// 거인이 너무 멀어지면 다시 타고 다가감. 차를 밟으면 찌그러지며 폭발, 경찰을 밟으면 납작. 방귀 가스에 지속 피해.
public class PoliceUnit : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 52f;
    public float turnSpeed = 240f;

    [Header("거인과의 거리 (거인 크기 기준)")]
    [Tooltip("이 거리 안이면 멈춰서 경찰이 내림")] public float deployDistance = 4f;
    [Tooltip("이보다 가까우면 차에 타고 도망")] public float tooClose = 2.2f;
    [Tooltip("이보다 멀어지면 차에 타고 다시 다가감")] public float reengage = 6f;

    [Header("사격 (경찰 1명 기준)")]
    public float fireInterval = 1f;
    public float damage = 5f;
    [Range(0, 1)] public float accuracy = 0.6f;
    [Tooltip("방귀 가스(연막) 안에서의 명중률")] [Range(0, 1)] public float smokeAccuracy = 0.12f;

    [Header("체력 / 점수")]
    public float carHP = 30f;
    public float officerHP = 8f;
    public int carPoints = 200;
    public int officerPoints = 50;

    [HideInInspector] public CityGenerator city;
    [HideInInspector] public GameObject[] officerPrefabs;
    [HideInInspector] public RuntimeAnimatorController officerController;
    [HideInInspector] public float officerScale = 1.2f;

    public bool IsDead { get; private set; }

    enum State { Drive, Deploy, Board, Retreat, Leave }
    State state = State.Drive;

    class Officer
    {
        public Transform t; public Animator anim;
        public float hp, nextShot, animSpeed;
        public bool dead, inCar, fleeing;
        public float side;          // 차의 오른쪽 +1 / 왼쪽 -1
        public Vector3 spot;        // 내려서 설 자리
    }
    readonly List<Officer> officers = new List<Officer>();
    float hp;
    GiantController giant;

    Vector3 gridOrigin; float pitch; int maxI, maxJ;
    int ci, cj, ti, tj, lastI = -1, lastJ = -1;
    bool moving;

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly Color PoliceTint = new Color(0.35f, 0.45f, 1f);

    void Start()
    {
        hp = carHP;
        giant = FindObjectOfType<GiantController>();
        if (!city) city = FindObjectOfType<CityGenerator>();
        pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        gridOrigin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        maxI = city.blocksX; maxJ = city.blocksZ;
        ci = Mathf.Clamp(Mathf.RoundToInt((transform.position.x - gridOrigin.x) / pitch), 0, maxI);
        cj = Mathf.Clamp(Mathf.RoundToInt((transform.position.z - gridOrigin.z) / pitch), 0, maxJ);
        ti = ci; tj = cj; moving = true;

        for (int k = 0; k < 2; k++) officers.Add(CreateOfficer(k == 0 ? 1f : -1f));
    }

    Officer CreateOfficer(float side)
    {
        var o = new Officer { side = side, hp = officerHP, inCar = true, nextShot = Time.time + Random.Range(0.5f, 1.5f) };
        if (officerPrefabs == null || officerPrefabs.Length == 0) { o.dead = true; return o; }
        var go = Instantiate(officerPrefabs[Random.Range(0, officerPrefabs.Length)], transform.position, transform.rotation);
        go.name = "PoliceOfficer";
        go.transform.localScale = Vector3.one * officerScale;
        o.t = go.transform;
        o.anim = go.GetComponentInChildren<Animator>();
        if (o.anim) { o.anim.runtimeAnimatorController = officerController; o.anim.applyRootMotion = false; o.anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
        var mpb = new MaterialPropertyBlock();
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", PoliceTint); r.SetPropertyBlock(mpb); } // 파란 제복
        go.SetActive(false);
        return o;
    }

    // 해산(거인이 집에 들어감): 경찰이 차에 타고 거인에게서 멀어지다가 화면 밖에서 사라짐
    bool dispersing; float disperseAt;
    public void Disperse()
    {
        if (IsDead || dispersing) return;
        dispersing = true; disperseAt = Time.time;
        Board();
        state = State.Leave;
    }

    void OnDestroy()
    {
        foreach (var o in officers) if (o.t && (o.inCar || (IsDead || dispersing) && !o.fleeing)) Destroy(o.t.gameObject);
    }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
    Vector3 Node(int i, int j) => new Vector3(gridOrigin.x + i * pitch, transform.position.y, gridOrigin.z + j * pitch);
    bool AllAboard { get { foreach (var o in officers) if (!o.dead && !o.fleeing && !o.inCar) return false; return true; } }
    int AliveOfficers { get { int n = 0; foreach (var o in officers) if (!o.dead && !o.fleeing) n++; return n; } }

    void Update()
    {
        var g = GiantHealth.Instance;
        if (EnemyFreeze.Active) { CrushFrozenOfficers(g); return; } // 보급상자 효과: 적 정지 (밟으면 죽음)
        UpdateOfficers(g);
        if (IsDead || !g || !giant) return;
        float gs = giant.GameScale;
        Vector3 gp = g.transform.position;
        float dist = Flat(transform.position, gp);
        int alive = AliveOfficers;
        bool dazed = GasCloud.InSmoke(transform.position); // 방귀 연막 안: 거인이 다가와도 도망가지 않고 그 자리에 있음

        switch (state)
        {
            case State.Drive:
                if (!moving)
                {
                    if (alive == 0) { state = State.Leave; break; }
                    if (dist <= deployDistance * gs && dist > tooClose * gs) { Deploy(gp); break; }
                    if (dist <= tooClose * gs) { if (!dazed) state = State.Retreat; break; }
                    PickNode(gp, approach: true, gs);
                }
                break;
            case State.Deploy:
                if (alive == 0) { state = State.Leave; break; }
                if ((dist < tooClose * gs && !dazed) || dist > reengage * gs) Board();
                break;
            case State.Board:
                bool allIn = true;
                foreach (var o in officers) if (!o.dead && !o.fleeing && !o.inCar) allIn = false;
                if (allIn && !(dazed && dist < deployDistance * gs)) state = dist < deployDistance * gs ? State.Retreat : State.Drive;
                break;
            case State.Retreat:
                if (!moving)
                {
                    if (dazed) break;
                    if (dist >= deployDistance * gs * 1.05f && alive > 0) { state = State.Drive; break; }
                    PickNode(gp, approach: false, gs);
                }
                break;
            case State.Leave:
                if (!moving && (!dispersing || AllAboard)) PickNode(gp, approach: false, gs); // 해산 땐 경찰이 다 탈 때까지 기다렸다 출발
                if (dist > 600f) { Destroy(gameObject); return; }
                if (dispersing && (MilitarySpawner.OutOfSight(transform.position) && AllAboard || Time.time - disperseAt > 30f)) { Destroy(gameObject); return; }
                break;
        }
        if (moving) DriveStep();
    }

    // 인접 교차로 고르기: approach면 거인에게 가까워지되 너무 붙지 않는 곳, 아니면 거인에게서 가장 먼 곳
    void PickNode(Vector3 gp, bool approach, float gs)
    {
        float cur = Flat(Node(ci, cj), gp);
        int bi = ci, bj = cj;
        float best = approach ? cur : cur;
        foreach (var n in Neighbors())
        {
            float d = Flat(Node(n.x, n.y), gp);
            bool back = n.x == lastI && n.y == lastJ;
            if (approach) { if (d < best && d > tooClose * gs * 1.3f && !back) { best = d; bi = n.x; bj = n.y; } }
            else { float sc = d - (back ? 40f : 0f); if (sc > best) { best = sc; bi = n.x; bj = n.y; } }
        }
        if (bi != ci || bj != cj) { ti = bi; tj = bj; moving = true; }
        else if (approach && state == State.Drive) Deploy(gp); // 더 다가갈 교차로가 없으면 여기서 내림
    }

    IEnumerable<Vector2Int> Neighbors()
    {
        if (ci > 0 && city.RoadOpen(ci, cj, ci - 1, cj)) yield return new Vector2Int(ci - 1, cj); // 블록을 붙여 없어진 도로는 지나지 않음
        if (ci < maxI && city.RoadOpen(ci, cj, ci + 1, cj)) yield return new Vector2Int(ci + 1, cj);
        if (cj > 0 && city.RoadOpen(ci, cj, ci, cj - 1)) yield return new Vector2Int(ci, cj - 1);
        if (cj < maxJ && city.RoadOpen(ci, cj, ci, cj + 1)) yield return new Vector2Int(ci, cj + 1);
    }

    void DriveStep()
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
        if (Quaternion.Angle(transform.rotation, want) < 30f)
            transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * ChaseBoost.Factor(transform.position) * Time.deltaTime);
    }

    // ───────────── 경찰 ─────────────
    void Deploy(Vector3 gp)
    {
        state = State.Deploy;
        moving = false;
        Vector3 toG = gp - transform.position; toG.y = 0; toG = toG.sqrMagnitude > 0.01f ? toG.normalized : transform.forward;
        foreach (var o in officers)
        {
            if (o.dead || o.fleeing || !o.t) continue;
            // 차 문 옆에서 나와 거인 쪽으로 몇 걸음 나가서 섬
            o.t.position = transform.position + transform.right * o.side * 4f;
            o.spot = transform.position + transform.right * o.side * 8f + toG * 4f;
            o.t.gameObject.SetActive(true);
            o.inCar = false;
        }
    }

    void Board()
    {
        state = State.Board;
        foreach (var o in officers) if (!o.dead && !o.fleeing && o.t) o.inCar = false; // 아직 밖이면 뛰어와서 탐
    }

    // 정지 중에도 거인이 밟으면 경찰은 죽음 (UpdateOfficers의 밟기 판정과 같은 기준)
    void CrushFrozenOfficers(GiantHealth g)
    {
        if (!g) return;
        float gs = giant ? giant.GameScale : 27f;
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * gs;
        foreach (var o in officers)
        {
            if (o.dead || !o.t || !o.t.gameObject.activeSelf) continue;
            Vector3 toG = g.transform.position - o.t.position; toG.y = 0;
            if (toG.magnitude < squash) KillOfficer(o, true);
        }
    }

    void UpdateOfficers(GiantHealth g)
    {
        float dt = Time.deltaTime;
        float gs = giant ? giant.GameScale : 27f;
        float squash = (CivilianManager.Instance ? CivilianManager.Instance.squashRadius : 0.3f) * gs;
        float walk = 2.8f * officerScale, run = 11f * officerScale;

        foreach (var o in officers)
        {
            if (o.dead || !o.t || !o.t.gameObject.activeSelf) continue;
            Vector3 p = o.t.position;
            Vector3 toG = g ? g.transform.position - p : Vector3.forward; toG.y = 0;

            // 거인에게 밟힘
            if (g && toG.magnitude < squash) { KillOfficer(o, true); continue; }

            Vector3 target = p; float speed = 0f, anim01 = 0f;
            if (o.fleeing && GasCloud.InSmoke(p)) { } // 방귀 연막 안: 도망가지 않고 멈춰 있음
            else if (o.fleeing)
            {
                // 차를 잃은 경찰: 거인에게서 도망
                target = p - toG.normalized * 10f; speed = run; anim01 = 1f;
                if (toG.magnitude > 400f) { Destroy(o.t.gameObject); o.dead = true; continue; }
            }
            else if (state == State.Board || state == State.Retreat || state == State.Drive || state == State.Leave)
            {
                // 차로 뛰어감
                Vector3 door = transform.position + transform.right * o.side * 4f;
                target = door; speed = run; anim01 = 1f;
                if (Flat(p, door) < 1.5f) { o.inCar = true; o.t.gameObject.SetActive(false); continue; }
            }
            else if (state == State.Deploy)
            {
                if (Flat(p, o.spot) > 0.8f) { target = o.spot; speed = walk; anim01 = 0.5f; }
                else if (g && !g.IsDead && Time.time >= o.nextShot) Shoot(o, g);
            }

            Vector3 move = target - p; move.y = 0;
            if (speed > 0f && move.sqrMagnitude > 0.01f)
            {
                o.t.position = p + move.normalized * Mathf.Min(speed * dt, move.magnitude);
                o.t.rotation = Quaternion.RotateTowards(o.t.rotation, Quaternion.LookRotation(move), 540f * dt);
            }
            else if (toG.sqrMagnitude > 0.01f)
                o.t.rotation = Quaternion.RotateTowards(o.t.rotation, Quaternion.LookRotation(toG), 360f * dt); // 거인 쪽을 보고 조준
            o.animSpeed = Mathf.MoveTowards(o.animSpeed, anim01, dt * 3f);
            if (o.anim) o.anim.SetFloat(SpeedHash, o.animSpeed);
        }
    }

    void Shoot(Officer o, GiantHealth g)
    {
        if (GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden) { o.nextShot = Time.time + 0.5f; return; } // 거인의 집 안이거나 사람을 쥐고 있으면 쏘지 않음
        o.nextShot = Time.time + fireInterval * Random.Range(0.8f, 1.25f);
        Vector3 origin = o.t.position + Vector3.up * 2.6f * officerScale + o.t.forward * 1f;
        Vector3 aim = g.GetAimPoint();
        bool smoke = GasCloud.InSmoke(o.t.position);
        bool hit = Random.value < (smoke ? smokeAccuracy : accuracy);
        Vector3 end = hit ? aim : aim + Random.onUnitSphere * (giant ? giant.GameScale : 27f) * 0.8f;
        CombatFX.Muzzle(origin, (end - origin).normalized, 0.8f);
        Tracer(origin, end);
        if (hit) g.TakeDamage(damage, false);
    }

    // 총알 궤적: 노란 선이 잠깐 번쩍
    public static void Tracer(Vector3 a, Vector3 b)
    {
        if (!WarningZone.material) return;
        var go = new GameObject("Tracer");
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = WarningZone.material;
        lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
        lr.startWidth = 0.35f; lr.endWidth = 0.15f;
        lr.startColor = new Color(1f, 0.95f, 0.6f, 1f); lr.endColor = new Color(1f, 0.8f, 0.3f, 0.6f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(go, 0.06f);
    }

    void KillOfficer(Officer o, bool stomped)
    {
        o.dead = true;
        if (!o.t) return;
        // 새장이 있으면 쓰러지는 대신 새장에 갇힘 (CageSkill)
        if (CageSkill.Imprison(CageSkill.Kind.Police, 1) > 0)
        {
            ScoreManager.Add(officerPoints, "경찰");
            Destroy(o.t.gameObject);
            return;
        }
        if (o.anim) o.anim.enabled = false;
        var s = o.t.localScale;
        o.t.localScale = new Vector3(s.x * 1.4f, s.y * 0.12f, s.z * 1.4f);
        BloodSplat.Spawn(o.t.position, officerScale);
        ScoreManager.Add(officerPoints, "경찰");
        Destroy(o.t.gameObject, 3f);
    }

    // ───────────── 피해 ─────────────
    // 거인에게 밟힘: 찌그러지며 폭발. 차 안의 경찰도 함께. 밖에 있던 경찰은 도망
    public void Crush()
    {
        if (IsDead) return;
        IsDead = true;
        moving = false;
        ScoreManager.Add(carPoints, "POLICE");
        CombatFX.Explosion(transform.position + Vector3.up * 3f, 12f);
        Vector3 sc = transform.localScale;
        transform.localScale = new Vector3(sc.x * 1.15f, sc.y * 0.3f, sc.z * 1.1f);
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials) if (m.HasProperty("_Color")) m.color *= 0.35f;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        int aboard = 0;
        foreach (var o in officers)
        {
            if (o.dead) continue;
            if (o.inCar) { o.dead = true; aboard++; if (o.t) Destroy(o.t.gameObject); }
            else o.fleeing = true;
        }
        CageSkill.Imprison(CageSkill.Kind.Police, aboard); // 차 안에 있던 경찰은 새장으로
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.3f);
        Destroy(gameObject, 6f);
    }

    // 새장 포로의 총격: 차에 피해
    public void TakeDamage(float dmg)
    {
        if (IsDead) return;
        hp -= dmg;
        if (hp <= 0f) Crush();
    }

    // 새장 포로가 쏠 수 있는 경찰(차 밖에서 싸우는 중)마다 visit(조준점, 피해 함수)
    public void VisitOfficers(System.Action<Vector3, System.Action<float>> visit)
    {
        foreach (var o in officers)
        {
            if (o.dead || o.inCar || o.fleeing || !o.t || !o.t.gameObject.activeSelf) continue;
            var off = o;
            visit(o.t.position + Vector3.up * 1.2f * officerScale, d => { if (off.dead) return; off.hp -= d; if (off.hp <= 0f) KillOfficer(off, false); });
        }
    }

    // 방귀 가스: 가스 안에 있는 차·경찰에 피해
    public void TakeGas(System.Func<Vector3, bool> inside, float dmg)
    {
        if (!IsDead && inside(transform.position)) { hp -= dmg; if (hp <= 0f) Crush(); }
        foreach (var o in officers)
        {
            if (o.dead || o.inCar || !o.t || !inside(o.t.position)) continue;
            o.hp -= dmg;
            if (o.hp <= 0f) KillOfficer(o, false);
        }
    }
}
