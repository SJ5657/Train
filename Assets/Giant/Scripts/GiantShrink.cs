using System.Collections.Generic;
using UnityEngine;

// UFO 축소빔에 맞으면 거인이 휴대폰만 하게 작아짐 (duration초 뒤 원래 크기로).
// 작아진 동안:
//  - 군대 공격을 받지 않고, 건물을 부수거나 무엇을 밟지도 못함 (GiantStomp 정지). 건물에는 막힘
//  - 크기와 상관없이 정해진 속도로 이동, 카메라는 가까이 붙음
//  - 주변에 사람들이 몰려 지나다니고, 가까운 도로에 차가 더 생김
//  - 사람 발이나 차(탱크·경찰차 포함)에 닿으면 즉사 → 게임 오버
public class GiantShrink : MonoBehaviour
{
    public static GiantShrink Instance { get; private set; }
    public static bool Active => Instance && Instance.active;
    public static float SavedScale => Instance ? Instance.savedScale : 1f;
    public static float CameraBlend => Instance ? Instance.camK : 0f;   // 0 = 보통 카메라, 1 = 작아진 거인용 카메라
    public static float TinyCameraDistance => Instance ? Instance.tinyCameraDistance : 8f;
    public static float TinyCameraPitch => Instance ? Instance.tinyCameraPitch : 70f;
    public static float TinyCameraFov => Instance ? Instance.tinyCameraFov : 18f;
    public static float TimeLeft => Active ? Mathf.Max(0f, Instance.duration - (Time.time - Instance.startAt)) : 0f;

    [Header("축소")]
    [Tooltip("작아져 있는 시간(초)")] public float duration = 10f;
    [Tooltip("작아졌을 때 키(m). 휴대폰 크기")] public float tinyHeight = 0.35f;
    [Tooltip("작아지거나 커지는 데 걸리는 시간(초)")] public float transitionTime = 0.3f;
    [Tooltip("작아졌을 때 달리기 속도(m/s, 크기와 무관. Shift 걷기는 절반)")] public float tinyRunSpeed = 3.6f;
    [Tooltip("작아졌을 때 카메라 거리(m). 사람 머리보다 높게 떠서 내려다봄")] public float tinyCameraDistance = 30f;
    [Tooltip("작아졌을 때 내려다보는 각도 (90 = 수직)")] public float tinyCameraPitch = 70f;
    [Tooltip("작아졌을 때 카메라 시야각: 좁을수록 크게 확대됨")] public float tinyCameraFov = 6.8f;

    [Header("군중")]
    [Tooltip("거인 주변에 유지할 지나가는 사람 수")] public int walkerCount = 24;
    public Vector2 walkerSpawnDistance = new Vector2(8f, 28f);
    public float walkerDespawnDistance = 40f;
    [Tooltip("걷는 속도 범위(m/s). 4.5 이상이면 뛰는 동작")] public Vector2 walkerSpeed = new Vector2(2f, 4.8f);
    [Tooltip("사람들이 거인 근처를 지나가도록, 거인 주변 이 거리(m) 안의 한 점을 향해 걸음")] public float walkerAimSpread = 6f;
    [Tooltip("작아지는 순간 가까운 도로에 더 세우는 차 수")] public int extraCars = 0;
    public Vector2 extraCarDistance = new Vector2(40f, 220f);

    [Header("밟힘 판정")]
    [Tooltip("다 작아진 뒤 이 시간(초) 동안은 밟혀도 죽지 않음")] public float graceTime = 1.5f;

    bool active, restoring;
    float savedScale = 1f, startAt, scaleFrom, scaleTo, scaleT = 1f, tinyAt, camK = 0f, savedNear = -1f, tinyRatio = 0.01f;
    GiantController ctrl; GiantHealth hp; CharacterController cc;
    readonly List<CrowdWalker> walkers = new List<CrowdWalker>();
    readonly Collider[] hits = new Collider[16];
    int giantLayer, buildingLayer, militaryMask;
    string toast; float toastUntil;

    void Awake()
    {
        Instance = this;
        giantLayer = LayerMask.NameToLayer("Giant");
        buildingLayer = LayerMask.NameToLayer("Building");
        militaryMask = LayerMask.GetMask("Military");
    }

    void OnDestroy() { if (Instance == this) { Restore(); Instance = null; } }

    // UFO 빔에 맞았을 때 호출. 이미 작아져 있거나 죽었으면 false
    public static bool TryBegin() => Instance && Instance.Begin();

    bool Begin()
    {
        if (active) return false;
        hp = GiantHealth.Instance;
        if (!hp || hp.IsDead) return false;
        ctrl = hp.GetComponent<GiantController>(); cc = hp.GetComponent<CharacterController>();
        if (!ctrl || !cc) return false;
        active = true; restoring = false; startAt = Time.time; tinyAt = float.MaxValue;
        tinyHP = tinyMaxHP; invulnUntil = 0f;
        savedScale = ctrl.ModelScale;
        scaleFrom = savedScale;
        scaleTo = savedScale * tinyHeight / Mathf.Max(0.01f, hp.Height);
        tinyRatio = Mathf.Clamp(scaleTo / savedScale, 1e-5f, 0.999f);
        scaleT = 0f;
        // 작아지면서 가장 가까운 인도로 달려가 피함. 건물 안에 있었을 수도 있으니 도착할 때까지는 건물을 통과하게 두고, 도착하면 건물에 막히게 함
        Vector3 walk = SidewalkTarget(hp.transform.position);
        float far = Vector3.Distance(new Vector3(walk.x, 0f, walk.z), new Vector3(hp.transform.position.x, 0f, hp.transform.position.z));
        movingToSidewalk = far > 0.3f;
        if (movingToSidewalk)
        {
            ctrl.autoMoveTarget = walk;
            ctrl.speedOverride = Mathf.Clamp(far / Mathf.Max(0.3f, sidewalkMoveTime), tinyRunSpeed, 40f); // 너무 오래 걸리지 않게 거리에 맞춰 빨리
        }
        else { ctrl.speedOverride = tinyRunSpeed; BlockBuildings(); }
        militaryLayer = LayerMask.NameToLayer("Military");
        if (giantLayer >= 0 && militaryLayer >= 0)
        {
            prevIgnoreMilitary = Physics.GetIgnoreLayerCollision(giantLayer, militaryLayer);
            Physics.IgnoreLayerCollision(giantLayer, militaryLayer, true); // 차 몸체에 밀리지 않고 차 밑으로 지나갈 수 있게 (죽는 건 바퀴로 판정)
        }
        var cam = Camera.main;
        if (cam) { savedNear = cam.nearClipPlane; cam.nearClipPlane = 0.02f; }
        var tm = FindObjectOfType<TrafficManager>();
        if (tm) tm.SpawnBurst(extraCars, extraCarDistance.x, extraCarDistance.y);
        Show("UFO 축소빔! 사람과 차를 피하세요");
        return true;
    }

    [Tooltip("작아질 때 가까운 인도까지 달려가는 데 걸리는 시간(초, 대략)")] public float sidewalkMoveTime = 1.2f;
    bool movingToSidewalk;

    void BlockBuildings() { if (giantLayer >= 0 && buildingLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, buildingLayer, false); }

    [Tooltip("작아졌을 때 자동으로 올라설 수 있는 턱 높이(m). 인도 연석(약 0.3~0.45m)은 소인 키보다 높아 그냥은 못 올라감")] public float maxStepUp = 0.6f;

    // 연석 오르기: 이동하는 쪽 바로 앞 바닥이 조금 높으면 그 위로 올려 줌
    void StepUp()
    {
        if (!cc) return;
        Vector3 v = ctrl.Velocity; v.y = 0f;
        if (v.sqrMagnitude < 0.01f) return;
        Vector3 pos = hp.transform.position, dir = v.normalized;
        Vector3 probe = pos + dir * (cc.radius + 0.08f) + Vector3.up * (maxStepUp + 0.05f);
        int mask = ~((1 << Mathf.Max(0, giantLayer)) | militaryMask);
        if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, maxStepUp + 0.3f, mask, QueryTriggerInteraction.Ignore)) return;
        float rise = hit.point.y - pos.y;
        if (rise < 0.03f || rise > maxStepUp) return;
        cc.enabled = false;
        hp.transform.position = new Vector3(hit.point.x, hit.point.y + 0.01f, hit.point.z);
        cc.enabled = true;
    }

    // 주변 블록들의 인도 중 가장 가까운 지점
    static Vector3 SidewalkTarget(Vector3 p)
    {
        var mgr = CivilianManager.Instance;
        if (!mgr || !mgr.city) return p;
        var b0 = mgr.BlockAt(p);
        Vector3 best = p; float bd = float.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var b = new Vector2Int(b0.x + dx, b0.y + dz);
                if (b.x < 0 || b.y < 0 || b.x >= mgr.city.blocksX || b.y >= mgr.city.blocksZ) continue;
                Vector3 q = mgr.NearestOnWalkway(b, p, out _);
                float d = new Vector3(q.x - p.x, 0f, q.z - p.z).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
        best.y = p.y;
        return best;
    }

    // 원래 상태로 되돌림 (크기 제외)
    void Restore()
    {
        if (ctrl) { ctrl.speedOverride = 0f; ctrl.autoMoveTarget = null; }
        movingToSidewalk = false;
        if (giantLayer >= 0 && buildingLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, buildingLayer, true);
        if (giantLayer >= 0 && militaryLayer >= 0) Physics.IgnoreLayerCollision(giantLayer, militaryLayer, prevIgnoreMilitary);
        HideTracks();
        var cam = Camera.main;
        if (cam && savedNear > 0f) cam.nearClipPlane = savedNear;
        savedNear = -1f;
        foreach (var w in walkers) if (w) Destroy(w.gameObject);
        walkers.Clear();
        camK = 0f;
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    void Update()
    {
        if (!active) return;
        if (!hp || !ctrl) { active = false; Restore(); return; }
        float dt = Time.deltaTime;

        // 크기 변화 (로그 공간에서 보간해 작아지는 속도가 일정하게 보이게)
        if (scaleT < 1f)
        {
            scaleT = Mathf.Min(1f, scaleT + dt / Mathf.Max(0.05f, transitionTime));
            float s = Mathf.Exp(Mathf.Lerp(Mathf.Log(scaleFrom), Mathf.Log(scaleTo), Smooth(scaleT)));
            ctrl.SetModelScale(s);
            if (scaleT >= 1f)
            {
                if (restoring) { active = false; Restore(); return; }
                tinyAt = Time.time;
            }
        }
        // 카메라 전환 정도: 작은 정도에 맞춰 0 ~ 1
        float r = ctrl.ModelScale / Mathf.Max(1e-6f, savedScale);           // 1(원래) → tinyRatio(가장 작음)
        float k = Mathf.Clamp01(Mathf.Log(Mathf.Max(1e-6f, r)) / Mathf.Log(tinyRatio));
        camK = k;

        if (hp.IsDead) return; // 죽은 뒤엔 그대로 (게임 오버 화면)
        if (scaleT >= 1f && !restoring) StepUp();

        // 인도로 피하는 중: 도착하면(또는 너무 오래 걸리면) 건물에 막히게 하고, 축소 시간·무적 시간은 그때부터 셈
        if (movingToSidewalk)
        {
            if (!ctrl.autoMoveTarget.HasValue || Time.time - startAt > sidewalkMoveTime * 3f + transitionTime)
            {
                ctrl.autoMoveTarget = null; movingToSidewalk = false;
                ctrl.speedOverride = tinyRunSpeed; BlockBuildings();
                startAt = Time.time; tinyAt = Time.time;
            }
            else return;
        }

        if (!restoring && Time.time - startAt >= duration)
        {
            restoring = true; scaleFrom = ctrl.ModelScale; scaleTo = savedScale; scaleT = 0f;
            foreach (var w in walkers) if (w) Destroy(w.gameObject);
            walkers.Clear();
            HideTracks();
            Show("원래 크기로 돌아왔다!");
            return;
        }
        if (restoring || scaleT < 1f) return;

        UpdateCrowd();
        UpdateTracks();
        if (Time.time - tinyAt >= graceTime) CheckSquashed();
    }

    // ───── 자동차 바퀴 ─────
    [Header("자동차 바퀴")]
    [Tooltip("앞뒤 바퀴 위치 = 차 길이 × ±이 비율")] public float wheelAxle = 0.33f;
    [Tooltip("좌우 바퀴 위치 = 차 폭 × ±이 비율")] public float wheelSide = 0.4f;
    [Tooltip("타이어 반경 = 차 폭 × 이 비율 (밟힘 판정 · 궤적 폭)")] public float tireWidthRatio = 0.11f;
    [Tooltip("이 거리(m) 안의 차들만 바퀴 궤적 표시")] public float trackRange = 30f;
    [Tooltip("바퀴 궤적을 앞으로 그리는 길이(m)")] public float trackAhead = 20f;
    public int maxTrackedCars = 12;
    public Color trackColor = new Color(1f, 0.15f, 0.1f, 0.55f);

    readonly Vector3[] wheel = new Vector3[4]; // 0 앞왼 1 앞오 2 뒤왼 3 뒤오
    readonly List<LineRenderer> tracks = new List<LineRenderer>();
    readonly List<TrafficCar> nearCars = new List<TrafficCar>();
    static Material trackMat;
    bool prevIgnoreMilitary; int militaryLayer;

    // 차 바퀴 4개 위치(월드)와 타이어 반경: 차 충돌 박스 기준으로 추정 (모델이 통짜 메시라 바퀴가 따로 없음)
    bool Wheels(TrafficCar car, out float tireR)
    {
        tireR = 0f;
        var box = car ? car.GetComponent<BoxCollider>() : null;
        if (!box) return false;
        Vector3 c = box.center, s = box.size;
        float x = s.x * wheelSide, z = s.z * wheelAxle, y = c.y - s.y * 0.5f;
        var t = car.transform;
        wheel[0] = t.TransformPoint(new Vector3(c.x - x, y, c.z + z));
        wheel[1] = t.TransformPoint(new Vector3(c.x + x, y, c.z + z));
        wheel[2] = t.TransformPoint(new Vector3(c.x - x, y, c.z - z));
        wheel[3] = t.TransformPoint(new Vector3(c.x + x, y, c.z - z));
        tireR = s.x * tireWidthRatio * t.lossyScale.x;
        return true;
    }

    // 가까운 차들의 바퀴가 지나갈 길(좌우 두 줄)을 바닥에 빨간 띠로 그림
    void UpdateTracks()
    {
        Vector3 gp = hp.transform.position; gp.y = 0f;
        nearCars.Clear();
        foreach (var c in TrafficCar.All)
        {
            if (!c || c.IsDead) continue;
            Vector3 p = c.transform.position; p.y = 0f;
            if ((p - gp).sqrMagnitude < trackRange * trackRange) nearCars.Add(c);
        }
        nearCars.Sort((a, b) => (a.transform.position - gp).sqrMagnitude.CompareTo((b.transform.position - gp).sqrMagnitude));
        if (!trackMat) trackMat = new Material(Shader.Find("Sprites/Default"));
        int used = 0;
        for (int i = 0; i < nearCars.Count && i < maxTrackedCars; i++)
        {
            var car = nearCars[i];
            if (!Wheels(car, out float tireR)) continue;
            Vector3 fwd = car.transform.forward; fwd.y = 0f; fwd.Normalize();
            for (int side = 0; side < 2; side++) // 왼쪽 줄: 뒤왼→앞왼+앞으로, 오른쪽 줄: 뒤오→앞오+앞으로
            {
                var lr = Track(used++);
                Vector3 a = wheel[2 + side], b = wheel[side] + fwd * trackAhead;
                a.y = b.y = 0.02f;
                lr.SetPosition(0, a); lr.SetPosition(1, b);
                lr.startWidth = lr.endWidth = tireR * 2f;
                lr.enabled = true;
            }
        }
        for (int i = used; i < tracks.Count; i++) if (tracks[i]) tracks[i].enabled = false;
    }

    LineRenderer Track(int i)
    {
        while (tracks.Count <= i)
        {
            var go = new GameObject("WheelTrack");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = trackMat; lr.positionCount = 2; lr.useWorldSpace = true;
            lr.alignment = LineAlignment.TransformZ;            // 바닥에 눕힘 (go를 아래로 향하게 회전)
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.startColor = trackColor; lr.endColor = new Color(trackColor.r, trackColor.g, trackColor.b, 0f);
            tracks.Add(lr);
        }
        return tracks[i];
    }

    void HideTracks() { foreach (var t in tracks) if (t) t.enabled = false; }

    // 지나가는 사람 유지: 멀어진 사람은 치우고, 모자라면 주변에 새로 세움
    void UpdateCrowd()
    {
        Vector3 gp = hp.transform.position; gp.y = 0f;
        for (int i = walkers.Count - 1; i >= 0; i--)
        {
            var w = walkers[i];
            if (!w) { walkers.RemoveAt(i); continue; }
            Vector3 p = w.transform.position; p.y = 0f;
            if ((p - gp).magnitude > walkerDespawnDistance) { Destroy(w.gameObject); walkers.RemoveAt(i); }
        }
        var mgr = CivilianManager.Instance;
        if (!mgr || mgr.prefabs == null || mgr.prefabs.Length == 0) return;
        for (int n = 0; n < 3 && walkers.Count < walkerCount; n++) SpawnWalker(mgr, gp);
    }

    void SpawnWalker(CivilianManager mgr, Vector3 gp)
    {
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 pos = gp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(walkerSpawnDistance.x, walkerSpawnDistance.y);
        Vector2 off = Random.insideUnitCircle * walkerAimSpread;
        Vector3 aim = gp + new Vector3(off.x, 0f, off.y);
        var go = Instantiate(mgr.prefabs[Random.Range(0, mgr.prefabs.Length)], pos, Quaternion.identity, transform);
        go.name = "CrowdWalker";
        go.transform.localScale = Vector3.one * mgr.scale;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); // 작은 거인을 밀어내지 않게 (밟힘은 거리로 판정)
        var anim = go.GetComponentInChildren<Animator>();
        if (anim) { anim.runtimeAnimatorController = mgr.locomotion; anim.applyRootMotion = false; }
        float speed = Random.Range(walkerSpeed.x, walkerSpeed.y);
        var w = go.AddComponent<CrowdWalker>();
        w.Init(aim - pos, speed, anim, speed >= 4.5f);
        walkers.Add(w);
    }

    // ───── 사람 발 ─────
    // 한쪽 발의 발바닥 중심(발목과 발끝 사이)과 크기(발 길이). 휴머노이드 뼈 기준
    static bool Foot(Animator a, bool left, out Vector3 sole, out float len, out float ankleY)
    {
        sole = Vector3.zero; len = 0f; ankleY = 0f;
        if (!a || !a.isHuman) return false;
        var f = a.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
        var t = a.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
        if (!f) return false;
        Vector3 tp = t ? t.position : f.position + a.transform.forward * 0.2f * a.transform.lossyScale.y;
        Vector3 d = tp - f.position; d.y = 0f;
        len = Mathf.Max(0.05f, d.magnitude * 1.6f);           // 발목~발끝 뼈 거리보다 발이 조금 더 김
        sole = f.position + d * 0.5f; ankleY = f.position.y;
        return true;
    }

    // 사람이 이 자리를 밟는지: 땅을 디딘(발목이 낮은) 발의 발바닥 범위 안에 거인이 있으면
    bool StepsOn(Transform person, Animator a, Vector3 flat, float gr)
    {
        Vector3 pp = person.position; pp.y = 0f;
        if ((pp - flat).sqrMagnitude > 9f) return false; // 3m 밖 생략
        for (int side = 0; side < 2; side++)
        {
            if (!Foot(a, side == 0, out Vector3 sole, out float len, out float ankleY)) continue;
            if (ankleY - person.position.y > len * 0.6f) continue; // 발을 든 상태
            Vector3 s = sole; s.y = 0f;
            float r = len * 0.5f + gr;
            if ((s - flat).sqrMagnitude < r * r) return true;
        }
        return false;
    }


    // ───── 소인 HP ─────
    [Header("소인 HP")]
    [Tooltip("작아졌을 때 HP (원래 HP와 별개)")] public float tinyMaxHP = 20f;
    [Tooltip("한 번 밟히거나 치일 때 깎이는 HP")] public float damagePerHit = 10f;
    [Tooltip("밟힌 뒤 이 시간(초) 동안은 다시 밟혀도 피해 없음 (같은 발에 여러 번 깎이지 않게)")] public float hitInvulnerable = 1f;
    float tinyHP, invulnUntil, hitFlashUntil;
    public static float TinyHP => Instance ? Instance.tinyHP : 0f;
    public static float TinyMaxHP => Instance ? Instance.tinyMaxHP : 1f;
    public static bool TinyHitFlash => Instance && Time.time < Instance.hitFlashUntil;

    void Squash(string reason)
    {
        if (Time.time < invulnUntil) return;
        tinyHP = Mathf.Max(0f, tinyHP - damagePerHit);
        if (tinyHP <= 0f) { hp.Kill(reason); return; }
        invulnUntil = Time.time + hitInvulnerable; hitFlashUntil = Time.time + 0.3f;
        Show($"{reason}  -{damagePerHit:0}");
    }

    // 사람 발이나 차에 닿았는지
    void CheckSquashed()
    {
        Vector3 gp = hp.transform.position;
        float gr = cc ? cc.radius : 0.02f;
        Vector3 flat = new Vector3(gp.x, 0f, gp.z);
        // 사람: 실제 발(발목~발끝)이 땅을 디딘 자리에 있으면 밟힘. 다리 사이로는 지나갈 수 있음
        foreach (var w in walkers)
            if (w && StepsOn(w.transform, w.GetComponentInChildren<Animator>(), flat, gr)) { Squash("사람에게 밟혔다!"); return; }
        var mgr = CivilianManager.Instance;
        if (mgr)
            foreach (var c in mgr.Civilians)
                if (c && StepsOn(c.transform, c.GetComponentInChildren<Animator>(), flat, gr)) { Squash("사람에게 밟혔다!"); return; }
        // 차: 거인 몸 높이의 구로 Military 레이어(자동차·탱크·경찰차) 충돌 박스와 겹치는지
        float h = cc ? cc.height : 0.15f;
        int n = Physics.OverlapSphereNonAlloc(gp + Vector3.up * h * 0.5f, Mathf.Max(gr, h * 0.5f), hits, militaryMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var col = hits[i];
            if (!col) continue;
            if (col.GetComponentInParent<TrafficCar>()) continue; // 일반 차는 아래에서 바퀴로만 판정 (차 밑 바퀴 사이로는 지나갈 수 있음)
            if (col.GetComponentInParent<TankAI>() || col.GetComponentInParent<PoliceUnit>()) { Squash("차에 치였다!"); return; }
        }
        // 일반 차: 바퀴(타이어 접지면)에 닿으면 깔림
        foreach (var car in TrafficCar.All)
        {
            if (!car || car.IsDead) continue;
            Vector3 cp = car.transform.position; cp.y = 0f;
            if ((cp - flat).sqrMagnitude > 400f) continue; // 20m 밖은 생략
            if (!Wheels(car, out float tireR)) continue;
            for (int w = 0; w < 4; w++)
            {
                Vector3 p = wheel[w]; p.y = 0f;
                float rr = tireR + gr;
                if ((p - flat).sqrMagnitude < rr * rr) { Squash("차 바퀴에 깔렸다!"); return; }
            }
        }
    }

    void Show(string text) { toast = text; toastUntil = Time.time + 3f; }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return;
        var big = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (Time.time < toastUntil && !string.IsNullOrEmpty(toast))
        {
            float a = Mathf.Clamp01((toastUntil - Time.time) / 0.5f);
            var r = new Rect(0, Screen.height * 0.16f, Screen.width, 40);
            GUI.color = new Color(0, 0, 0, 0.8f * a); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), toast, big);
            GUI.color = new Color(0.55f, 1f, 0.9f, a); GUI.Label(r, toast, big);
        }
        if (active && hp && Camera.main)
        {
            // 작아진 거인 위치 표시: 머리 위에서 아래를 가리키는 화살표 (멀리서도 찾기 쉽게)
            Vector3 sp = Camera.main.WorldToScreenPoint(hp.transform.position + Vector3.up * (cc ? cc.height * 1.3f : 0.2f));
            if (sp.z > 0f)
            {
                var arrow = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
                float bob = Mathf.Sin(Time.time * 6f) * 4f;
                var ar = new Rect(sp.x - 20, Screen.height - sp.y - 30 + bob, 40, 28);
                GUI.color = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(ar.x + 1, ar.y + 1, ar.width, ar.height), "▼", arrow);
                GUI.color = new Color(0.55f, 1f, 0.9f); GUI.Label(ar, "▼", arrow);
            }
        }
        if (active && !restoring && hp && !hp.IsDead)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            string t = $"축소 {TimeLeft:0}s";
            GUI.color = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(1, Screen.height * 0.16f + 41, Screen.width, 26), t, small);
            GUI.color = new Color(0.55f, 1f, 0.9f); GUI.Label(new Rect(0, Screen.height * 0.16f + 40, Screen.width, 26), t, small);
        }
        GUI.color = Color.white;
    }
}
