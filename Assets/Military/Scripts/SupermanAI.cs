using System.Collections.Generic;
using UnityEngine;

// 슈퍼맨: 거인 주변 하늘을 자유롭게 날아다니며, 잠깐 멈춰 거인 쪽을 보고 눈에서 레이저를 한 발씩 쏨(단발).
// 몇 발 쏜 뒤 기를 모아 거인 가슴으로 주먹을 뻗고 돌진해 큰 피해를 줌.
// 패링: 슈퍼맨이 거인에게 딱 닿는 순간(앞뒤 contactWindow초)에 좌클릭하면 성공 → 거인이 돌아서서 손바닥으로 쳐 저 멀리 날려버림 (죽지 않고, 한참 뒤 다시 나타남).
// 돌진 중에 너무 일찍 누르면 잠깐 패링할 수 없음(연타 방지).
// 모델은 Assets/Models/SuperMan.vrm (휴머노이드). 멈춰 있을 땐 허리에 손을 얹은 자세(애니메이션), 빨리 날 때는 몸을 눕히고 오른팔을 앞으로 뻗음.
public class SupermanAI : MonoBehaviour
{
    public static SupermanAI Active { get; private set; }

    [Header("크기 · 비행 (거인 키 기준)")]
    [Tooltip("슈퍼맨 키 = 거인 키 × 이 비율")] public float heightRatio = 0.3f;
    [Tooltip("날아다니는 범위: 거인과의 수평 거리 (최소, 최대)")] public Vector2 roamDistance = new Vector2(1.3f, 3.2f);
    [Tooltip("날아다니는 범위: 높이 (최소, 최대)")] public Vector2 roamHeight = new Vector2(0.5f, 1.8f);
    [Tooltip("최고 비행 속도 (거인 키/초)")] public float flySpeedRatio = 2.2f;
    [Tooltip("가속도 (거인 키/초²)")] public float accelRatio = 3f;

    [Header("레이저 (단발)")]
    [Tooltip("레이저를 쏘는 간격 (초, 범위)")] public Vector2 shotInterval = new Vector2(1.2f, 2.2f);
    [Tooltip("쏘기 전에 멈춰서 거인을 바라보는 시간(초)")] public float aimTime = 0.35f;
    public float boltDamage = 8f;
    [Tooltip("레이저 탄 속도 (거인 키/초). 느릴수록 움직여서 피하기 쉬움")] public float boltSpeedRatio = 4.5f;
    [Tooltip("돌진하기 전까지 쏘는 발 수 (범위)")] public Vector2Int shotsBeforeDash = new Vector2Int(4, 6);
    public Color laserColor = new Color(1f, 0.12f, 0.08f, 1f);

    [Header("돌진 · 패링")]
    [Tooltip("돌진 전 기를 모으는 시간(초)")] public float windUpTime = 0.9f;
    [Tooltip("돌진 속도 (거인 키/초)")] public float dashSpeedRatio = 3.2f;
    public float dashDamage = 70f;
    [Tooltip("닿는 순간 앞뒤로 이 시간(초) 안에 좌클릭하면 패링 성공")] public float contactWindow = 0.12f;
    [Tooltip("너무 일찍 눌렀을 때 다시 패링할 수 없는 시간(초)")] public float parryMissLock = 0.5f;
    [Tooltip("이 거리(거인 키 비율) 안으로 들어오면 돌진이 닿음")] public float hitRadiusRatio = 0.28f;
    public int parryScore = 2000;

    enum State { Roam, Aim, WindUp, Dash, Contact, Struck, Flung }
    State state = State.Roam;
    float stateT, nextShotAt, parryLockUntil, lastClickAt = -99f;
    int shotsLeft;
    Vector3 vel, waypoint, spin;
    float H, scale;
    Animator anim;
    static Material beamMat;

    class Bolt { public LineRenderer lr; public Vector3 start, dir, target; public float travelled, total, length; public bool deals; }
    readonly List<Bolt> bolts = new List<Bolt>();

    public bool IsDead => state == State.Struck || state == State.Flung; // 패링당해 날아가는 중

    public void Init(float giantHeight)
    {
        Active = this;
        H = Mathf.Max(5f, giantHeight);
        anim = GetComponentInChildren<Animator>();
        // 모델 키(약 1.8m)를 거인 키 비율에 맞춤
        float modelH = 1.8f;
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); if (b.size.y > 0.1f) modelH = b.size.y / transform.lossyScale.y; }
        scale = H * heightRatio / modelH;
        transform.localScale = Vector3.one * scale;
        foreach (var c in GetComponentsInChildren<Collider>()) Destroy(c);
        if (!beamMat) beamMat = new Material(Shader.Find("Sprites/Default"));
        shotsLeft = Random.Range(shotsBeforeDash.x, shotsBeforeDash.y + 1);
        nextShotAt = Time.time + Random.Range(shotInterval.x, shotInterval.y) + 1.5f;
        PickWaypoint();
        SupplyCrates.Notice("슈퍼맨이 나타났다!");
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (var b in bolts) if (b.lr) Destroy(b.lr.gameObject);
    }

    // ───────────── 거인 관련 ─────────────
    static GiantHealth Giant => GiantHealth.Instance;
    static Vector3 GiantChest()
    {
        var g = Giant;
        if (!g) return Vector3.zero;
        var b = g.Bone(HumanBodyBones.Chest);
        return b ? b.position : g.transform.position + Vector3.up * g.Height * 0.7f;
    }
    // 적이 공격하지 않는 상황 (집 안·연막·인질·정지 효과·메뉴)
    static bool AttacksBlocked => GameStartMenu.InMenu || !Giant || Giant.IsDead || GiantHome.GiantSafe || HostageEvent.HoldingHuman || GasCloud.GiantHidden || EnemyFreeze.Active;

    // 거인 주변 하늘의 아무 곳 중 화면 안에 보이는 곳 (화면 가장자리에서 조금 떨어진 곳)
    [Tooltip("화면 안에 머무는 범위 (화면 비율: 가로·세로 여백)")] public Vector2 screenMargin = new Vector2(0.12f, 0.15f);
    void PickWaypoint()
    {
        var g = Giant ? Giant.transform.position : transform.position;
        var cam = Camera.main;
        Vector3 best = g + Vector3.up * H; float bestScore = float.MaxValue;
        for (int i = 0; i < 24; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), d = Random.Range(roamDistance.x, roamDistance.y) * H;
            Vector3 p = g + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d + Vector3.up * Random.Range(roamHeight.x, roamHeight.y) * H;
            if (!cam) { best = p; break; }
            // 화면 안쪽일수록 좋음 (밖으로 벗어난 만큼 점수가 나빠짐)
            Vector3 v = cam.WorldToViewportPoint(p);
            float off = v.z <= 0f ? 10f : Mathf.Max(0f, screenMargin.x - v.x) + Mathf.Max(0f, v.x - (1f - screenMargin.x))
                                            + Mathf.Max(0f, screenMargin.y - v.y) + Mathf.Max(0f, v.y - (1f - screenMargin.y));
            if (off < bestScore) { bestScore = off; best = p; }
            if (off <= 0f) break;
        }
        waypoint = best;
    }

    // 슈퍼맨이 화면 밖으로 나갔는지 (조금의 여유는 허용)
    bool OffScreen()
    {
        var cam = Camera.main;
        if (!cam) return false;
        Vector3 v = cam.WorldToViewportPoint(transform.position + transform.up * H * heightRatio * 0.5f);
        return v.z <= 0f || v.x < 0.03f || v.x > 0.97f || v.y < 0.03f || v.y > 0.97f;
    }
    float nextScreenCheck;
    bool IsOffScreenPoint(Camera cam, Vector3 p)
    {
        Vector3 v = cam.WorldToViewportPoint(p);
        return v.z <= 0f || v.x < screenMargin.x * 0.5f || v.x > 1f - screenMargin.x * 0.5f || v.y < screenMargin.y * 0.5f || v.y > 1f - screenMargin.y * 0.5f;
    }

    void Go(State s) { state = s; stateT = 0f; }

    // ───────────── 진행 ─────────────
    void Update()
    {
        UpdateBolts();
        if (!Giant) return;
        float dt = Time.deltaTime;
        stateT += dt;
        ReadClick();
        // 거인이 움직이면 함께 따라 이동 (거인 기준으로 날아다님 → 거인이 달려도 화면에서 뒤처지지 않음)
        if (state != State.Flung && state != State.Struck && state != State.Dash && state != State.Contact && GiantController.Instance)
            transform.position += GiantController.Instance.Velocity * dt;

        switch (state)
        {
            case State.Roam:
            {
                // 목표 지점까지 가속하며 날아가고, 가까워지면 새 목표 (거인이 움직이면 범위도 따라감)
                Vector3 to = waypoint - transform.position;
                if (to.magnitude < H * 0.4f) PickWaypoint();
                // 화면 밖으로 벗어나면(거인이 돌거나 움직여서) 화면 안의 새 목표로 바로 돌아옴
                else if (Time.time >= nextScreenCheck)
                {
                    nextScreenCheck = Time.time + 0.4f;
                    var cam = Camera.main;
                    if (OffScreen() || (cam && IsOffScreenPoint(cam, waypoint))) PickWaypoint();
                }
                to = waypoint - transform.position;
                bool outside = OffScreen();
                float speed = H * flySpeedRatio * (outside ? 1.8f : 1f); // 화면 밖이면 빨리 돌아옴
                vel = Vector3.MoveTowards(vel, to.normalized * speed, H * accelRatio * (outside ? 2f : 1f) * dt);
                transform.position += vel * dt;
                Pose(vel, Mathf.Clamp01(vel.magnitude / (H * flySpeedRatio * 0.6f)));
                if (!AttacksBlocked && !outside && Time.time >= nextShotAt) // 화면 밖에서는 쏘지 않음
                {
                    if (shotsLeft <= 0) { Go(State.WindUp); SupplyCrates.Notice("슈퍼맨이 돌진한다!"); }
                    else Go(State.Aim);
                }
                break;
            }
            case State.Aim:
            {
                // 잠깐 멈춰 서서 거인을 바라보고 한 발 쏨
                vel = Vector3.MoveTowards(vel, Vector3.zero, H * accelRatio * 2f * dt);
                transform.position += vel * dt;
                Pose(GiantChest() - transform.position, 0f);
                if (OffScreen()) { PickWaypoint(); Go(State.Roam); break; } // 거인이 돌아서 화면 밖이 됐으면 쏘지 않고 화면 안으로
                if (stateT >= aimTime)
                {
                    if (!AttacksBlocked) Shoot();
                    shotsLeft--;
                    BackToRoam();
                }
                break;
            }
            case State.WindUp:
            {
                // 멈춰서 뒤로 살짝 물러나며 기를 모음
                vel = Vector3.MoveTowards(vel, Vector3.zero, H * accelRatio * 2f * dt);
                Vector3 away = transform.position - GiantChest(); away.y = 0f;
                transform.position += vel * dt + away.normalized * H * 0.25f * dt + Vector3.up * H * 0.08f * dt;
                Pose(GiantChest() - transform.position, Mathf.Clamp01(stateT / windUpTime));
                if (AttacksBlocked) { BackToRoam(); break; }
                if (stateT >= windUpTime) Go(State.Dash);
                break;
            }
            case State.Dash:
            {
                Vector3 to = GiantChest() - transform.position;
                float dist = to.magnitude, speed = H * dashSpeedRatio;
                Pose(to, 1f);
                if (AttacksBlocked) { BackToRoam(); break; }
                if (dist <= H * hitRadiusRatio)
                {
                    // 딱 닿은 순간: 바로 전에 누른 클릭이 있으면 패링
                    if (Time.time - lastClickAt <= contactWindow) { Parried(); break; }
                    Go(State.Contact);
                    break;
                }
                transform.position += to.normalized * Mathf.Min(dist, speed * dt);
                vel = to.normalized * speed;
                break;
            }
            case State.Contact:
            {
                // 닿은 직후 아주 잠깐: 이때 누르면 패링, 지나면 피해
                Vector3 c = GiantChest();
                transform.position = c - (c - transform.position).normalized * H * hitRadiusRatio;
                if (stateT >= contactWindow) Impact();
                break;
            }
            case State.Struck:
            {
                // 거인이 돌아서서 손바닥을 뻗는 동안 그 자리에 붙잡혀 있다가, 손바닥이 닿는 순간 날아감
                if (Time.time >= struckUntil) Launch();
                break;
            }
            case State.Flung:
            {
                // 빙글빙글 돌며 저 멀리 하늘로 날아가 사라짐 (죽지 않음, 한참 뒤 다시 나타남)
                vel += Vector3.down * H * 0.15f * dt;
                transform.position += vel * dt;
                transform.rotation = Quaternion.Euler(spin * dt) * transform.rotation;
                if (stateT > 4f || (transform.position - GiantChest()).magnitude > H * 40f) Destroy(gameObject);
                break;
            }
        }
    }

    // 화면 가장자리 밖으로 나가려 하면 화면 안쪽으로 부드럽게 끌어당김 (같은 깊이에서)
    void LateUpdateKeepOnScreen()
    {
        if (state == State.Flung || state == State.Struck || state == State.Dash || state == State.Contact) return;
        var cam = Camera.main;
        if (!cam) return;
        Vector3 anchor = transform.position + transform.up * H * heightRatio * 0.5f;
        Vector3 v = cam.WorldToViewportPoint(anchor);
        if (v.z <= 0.1f) return;
        const float lo = 0.05f, hi = 0.95f;
        Vector3 c = new Vector3(Mathf.Clamp(v.x, lo, hi), Mathf.Clamp(v.y, lo, hi), v.z);
        if (c == v) return;
        Vector3 inside = cam.ViewportToWorldPoint(c);
        transform.position += (inside - anchor) * (1f - Mathf.Exp(-8f * Time.deltaTime));
    }

    void BackToRoam()
    {
        nextShotAt = Time.time + Random.Range(shotInterval.x, shotInterval.y);
        PickWaypoint();
        Go(State.Roam);
    }

    // ───────────── 레이저 (단발) ─────────────
    void Shoot()
    {
        Vector3 target = GiantChest();
        Transform head = anim ? anim.GetBoneTransform(HumanBodyBones.Head) : null;
        Transform le = anim ? anim.GetBoneTransform(HumanBodyBones.LeftEye) : null, re = anim ? anim.GetBoneTransform(HumanBodyBones.RightEye) : null;
        Vector3 hp = head ? head.position : transform.position + transform.up * H * heightRatio * 0.9f;
        Vector3[] eyes = { le ? le.position : hp, re ? re.position : hp };
        float w = Mathf.Max(0.3f, H * 0.022f);
        for (int i = 0; i < eyes.Length; i++)
        {
            var go = new GameObject("LaserBolt");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = beamMat;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = laserColor; lr.endColor = new Color(1f, 0.9f, 0.8f, 1f); // 앞쪽 끝이 하얗게 빛남
            lr.startWidth = w * 0.8f; lr.endWidth = w * 1.3f;
            lr.SetPosition(0, eyes[i]); lr.SetPosition(1, eyes[i]);
            // 눈 두 개에서 나간 한 쌍은 한 번만 피해
            bolts.Add(new Bolt { lr = lr, start = eyes[i], target = target, dir = (target - eyes[i]).normalized, total = Vector3.Distance(eyes[i], target), length = H * 0.35f, deals = i == 0 });
        }
        CombatFX.Muzzle(eyes[0], (target - eyes[0]).normalized, H * 0.05f);
    }

    // 레이저 탄은 쏜 순간의 거인 위치로 곧게 날아감. 날아가는 동안 거인 몸(발~머리 기둥)에 닿아야 맞고,
    // 거인이 움직여 비켜나면 그대로 지나쳐 사라짐 → 움직여서 피할 수 있음
    void UpdateBolts()
    {
        float step = H * boltSpeedRatio * Time.deltaTime;
        var g = Giant;
        var cc = g ? g.GetComponent<CharacterController>() : null;
        for (int i = bolts.Count - 1; i >= 0; i--)
        {
            var b = bolts[i];
            if (!b.lr) { bolts.RemoveAt(i); continue; }
            Vector3 before = b.start + b.dir * b.travelled;
            b.travelled += step;
            Vector3 head = b.start + b.dir * b.travelled, tail = b.start + b.dir * Mathf.Max(0f, b.travelled - b.length);
            b.lr.SetPosition(0, tail); b.lr.SetPosition(1, head);
            if (g && !g.IsDead && cc)
            {
                // 이번 프레임에 지나간 구간과 거인 몸 기둥 사이 거리
                Vector3 feet = g.transform.position, top = feet + Vector3.up * g.Height;
                if (SegSegDistance(before, head, feet, top) < cc.radius * 1.1f)
                {
                    if (b.deals) { g.TakeDamage(boltDamage, false); CombatFX.Muzzle(head, -b.dir, H * 0.08f); }
                    Destroy(b.lr.gameObject); bolts.RemoveAt(i); continue;
                }
            }
            // 빗나감: 목표 지점을 한참 지나거나 땅에 닿으면 사라짐
            if (b.travelled > b.total * 1.8f || head.y < 0.3f)
            {
                if (head.y < 0.3f) CombatFX.Muzzle(head, Vector3.up, H * 0.05f);
                Destroy(b.lr.gameObject); bolts.RemoveAt(i);
            }
        }
    }

    // 두 선분 사이 가장 가까운 거리 (샘플링으로 충분: 탄 한 프레임 구간은 짧음)
    static float SegSegDistance(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1)
    {
        float best = float.MaxValue;
        for (int k = 0; k <= 4; k++)
        {
            Vector3 p = Vector3.Lerp(a0, a1, k / 4f);
            Vector3 ab = b1 - b0; float t = Mathf.Clamp01(Vector3.Dot(p - b0, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector3.Distance(p, b0 + ab * t));
        }
        return best;
    }

    // ───────────── 돌진 · 패링 ─────────────
    // 지금 좌클릭하면 패링되는 순간 (닿기 contactWindow초 전 ~ 닿은 뒤 contactWindow초)
    float TimeToContact => Mathf.Max(0f, Vector3.Distance(transform.position, GiantChest()) - H * hitRadiusRatio) / (H * dashSpeedRatio);
    bool InParryTime => state == State.Contact || (state == State.Dash && TimeToContact <= contactWindow);

    void ReadClick()
    {
        if (!Input.GetMouseButtonDown(0) || GameStartMenu.InMenu || CardDraft.IsOpen || IsDead) return;
        if (Time.time < parryLockUntil) return;
        if (state == State.Contact) { Parried(); return; }
        if (state == State.WindUp) { parryLockUntil = Time.time + parryMissLock; return; } // 너무 일찍
        if (state == State.Dash)
        {
            float tti = Mathf.Max(0f, Vector3.Distance(transform.position, GiantChest()) - H * hitRadiusRatio) / (H * dashSpeedRatio);
            if (tti > contactWindow) { parryLockUntil = Time.time + parryMissLock; return; } // 너무 일찍
        }
        lastClickAt = Time.time;
    }

    void Impact()
    {
        Giant.TakeDamage(dashDamage);
        CombatFX.Explosion(GiantChest(), H * 0.35f);
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.9f);
        SupplyCrates.Notice($"슈퍼맨의 돌진! -{dashDamage:0}");
        // 튕겨 나가며 다시 날아다님
        Vector3 away = transform.position - GiantChest(); away = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;
        vel = (away + Vector3.up * 0.5f).normalized * H * flySpeedRatio;
        shotsLeft = Random.Range(shotsBeforeDash.x, shotsBeforeDash.y + 1);
        BackToRoam();
    }

    // 패링 성공: 거인이 슈퍼맨 쪽으로 돌아서서 왼손 손바닥으로 침 → 손바닥이 닿는 순간(Launch) 저 멀리 날아감
    float struckUntil;
    void Parried()
    {
        Vector3 toS = transform.position - Giant.transform.position;
        if (GiantController.Instance) GiantController.Instance.FaceTowards(toS, 0.5f);
        GiantPalmStrike.Begin(transform);
        struckUntil = Time.time + GiantPalmStrike.HitDelay;
        SupermanSpawner.SlowMo(1.1f, 0.35f); // 패링 성공 순간: 돌아서서 손바닥으로 치는 모습이 잘 보이게 잠깐 느리게
        vel = Vector3.zero;
        Go(State.Struck);
    }

    void Launch()
    {
        // 슈퍼맨이 있던 쪽(= 거인이 돌아선 쪽)으로, 살짝 위로 아주 빠르게
        Vector3 away = transform.position - GiantChest(); away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : Giant.transform.forward;
        vel = (away + Vector3.up * 0.35f).normalized * H * 9f;
        spin = new Vector3(Random.Range(500f, 800f), Random.Range(-300f, 300f), Random.Range(-500f, 500f));
        CombatFX.Explosion(transform.position, H * 0.25f); // 손바닥에 맞은 충격
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.7f);
        ScoreManager.Add(parryScore, "패링!");
        SupplyCrates.Notice("패링 성공! 슈퍼맨이 저 멀리 날아갔다!");

        SupermanSpawner.OnSupermanDied();
        Go(State.Flung);
    }

    // ───────────── 패링 타이밍 표시: 슈퍼맨 위에 마우스 아이콘 (왼쪽 버튼이 빨갛게 깜빡임) ─────────────
    static Texture2D mouseTex, leftBtnTex;
    static void MakeMouseTextures()
    {
        const int w = 48, h = 72;
        mouseTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        leftBtnTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f, rx = w * 0.5f - 2f, ry = h * 0.5f - 2f;
        float splitY = h * 0.58f; // 이보다 위(텍스처 y가 큰 쪽 = 화면 위)가 버튼
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 둥근 마우스 몸통 (타원), 위쪽이 버튼, 가운데 세로줄로 왼쪽·오른쪽 버튼 구분
                float ex = (x - cx) / rx, ey = (y - cy) / ry;
                float d = Mathf.Sqrt(ex * ex + ey * ey);
                bool inside = d <= 1f, edge = inside && d > 0.86f;
                bool buttonArea = y > splitY;
                bool split = inside && ((buttonArea && Mathf.Abs(x - cx) < 1.2f) || Mathf.Abs(y - splitY) < 1.2f);
                mouseTex.SetPixel(x, y, !inside ? Color.clear : (edge || split) ? new Color(0.1f, 0.1f, 0.12f, 1f) : new Color(0.95f, 0.95f, 0.97f, 1f));
                bool left = inside && !edge && !split && buttonArea && x < cx; // 왼쪽 버튼
                leftBtnTex.SetPixel(x, y, left ? Color.white : Color.clear);
            }
        mouseTex.Apply(); leftBtnTex.Apply();
    }

    void OnGUI()
    {
        if (!InParryTime || GameStartMenu.InMenu) return;
        var cam = Camera.main;
        if (!cam) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + transform.up * H * heightRatio * 1.15f);
        if (sp.z < 0f) return;
        if (!mouseTex) MakeMouseTextures();
        const float iw = 48f, ih = 72f;
        var r = new Rect(sp.x - iw * 0.5f, Screen.height - sp.y - ih - 8f, iw, ih);
        GUI.color = Color.white;
        GUI.DrawTexture(r, mouseTex);
        bool blinkOn = Mathf.Repeat(Time.unscaledTime * 8f, 1f) < 0.55f; // 빠르게 깜빡
        GUI.color = new Color(1f, 0.1f, 0.1f, blinkOn ? 1f : 0.25f);
        GUI.DrawTexture(r, leftBtnTex);
        GUI.color = Color.white;
    }

    // ───────────── 자세 ─────────────
    // flight 0 = 서서 dir 쪽을 바라봄, 1 = 몸을 눕혀 dir 쪽으로 머리부터 날아감 (오른팔을 앞으로 뻗음)
    float flight;
    Vector3 flightDir = Vector3.forward;
    void Pose(Vector3 dir, float flightAmount)
    {
        float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
        flight = Mathf.Lerp(flight, flightAmount, k);
        if (dir.sqrMagnitude > 1e-4f) flightDir = Vector3.Slerp(flightDir, dir.normalized, k).normalized;
        Vector3 flat = flightDir; flat.y = 0f;
        Quaternion upright = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : transform.forward, Vector3.up);
        Vector3 belly = Vector3.ProjectOnPlane(Vector3.down, flightDir);
        Quaternion lying = Quaternion.LookRotation(belly.sqrMagnitude > 1e-4f ? belly.normalized : Vector3.down, flightDir); // 머리(위)가 날아가는 쪽, 배(앞)가 땅 쪽
        transform.rotation = Quaternion.Slerp(upright, lying, flight);
    }

    void LateUpdate()
    {
        LateUpdateKeepOnScreen();
        if (!anim || flight < 0.01f || IsDead) return;
        // 날 때 오른팔을 머리 위(몸 위쪽 = 날아가는 쪽)로 쭉 뻗음
        var up = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lo = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (!up || !lo || !hand) return;
        Vector3 bodyUp = transform.up;
        Vector3 cur = lo.position - up.position;
        up.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, bodyUp), flight) * up.rotation;
        Vector3 cur2 = hand.position - lo.position;
        lo.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur2, bodyUp), flight) * lo.rotation;
    }
}
