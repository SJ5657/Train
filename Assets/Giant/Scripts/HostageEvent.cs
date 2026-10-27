using UnityEngine;

// 인질 이벤트: 건물을 부술 때 eventChance 확률로 시작.
//  1) 부서진 건물 근처에서 검은 차량이 나와 거인에게서 도망침
//  2) 거인이 가까이 다가가면 허리를 숙여 차량을 집고, 손 위에 인질(GentleMan)이 나타남
//  3) 그 순간 적당히 먼 곳에 목적지(초록 빛기둥)가 생김
//  4) 인질을 손에 든 채 목적지에 도착하면 인질이 사라지고 HP가 가득 회복됨
// 손 동작은 GiantEat처럼 애니메이터 IK로 만들고, GiantIKRelay가 OnIK를 넘겨줌.
public class HostageEvent : MonoBehaviour
{
    public static HostageEvent Instance { get; private set; }

    [Header("이벤트")]
    [Tooltip("건물을 하나 부술 때 이벤트가 시작될 확률 (0.01 = 1%)")] [Range(0f, 1f)] public float eventChance = 0.05f;
    [Tooltip("이벤트가 끝난 뒤 다음 이벤트까지 최소 간격(초)")] public float cooldown = 30f;
    [Tooltip("인질 모델 (GentleMan)")] public GameObject hostagePrefab;

    [Header("검은 차량")]
    [Tooltip("차량 크기 배율 (일반 도시 차량 프리팩 기준)")] public float carScale = 2.2f;
    [Tooltip("도망 속도 = 거인 달리기 속도 × 이 비율 (1보다 작아야 따라잡을 수 있음)")] public float carSpeedRatio = 0.92f;
    [Tooltip("차량이 나온 직후 이 시간(초) 동안은 잡을 수 없음")] public float graceTime = 3f;
    [Tooltip("이 시간(초) 안에 못 잡으면 차량이 도망침")] public float chaseTime = 90f;
    [Tooltip("거인 키 × 이 비율 안으로 가까워지면 집음")] public float pickRangeRatio = 0.35f;

    [Header("인질 · 목적지")]
    [Tooltip("인질 키 = 거인 키 × 이 비율")] public float hostageHeightRatio = 0.07f;
    [Tooltip("목적지까지 거리 = 거인 키 × 이 배율 (최소~최대 m로 제한)")] public float destDistancePerHeight = 18f;
    public Vector2 destDistanceClamp = new Vector2(350f, 800f);
    [Tooltip("거인 키 × 이 비율 안에 들어가면 도착")] public float arriveRatio = 0.6f;
    public int rescueScore = 500;
    [Tooltip("보상 중 경험치 2배가 나왔을 때 지속 시간(초)")] public float xpBoostSeconds = 30f;
    [Tooltip("보상 중 적 후퇴가 나왔을 때 지속 시간(초)")] public float retreatSeconds = 20f;

    [Header("동작 시간(초)")]
    [Tooltip("차를 줍기 전, 달리기를 멈추고 자세가 정리될 때까지 기다리는 시간(초)")] public float settleTime = 0.15f;
    public float reachTime = 0.3f;
    public float liftTime = 0.35f;
    public float releaseTime = 0.6f;
    [Tooltip("엉덩이를 낮추는 정도 (모델 크기 비율, 무릎을 굽혀 쪼그림)")] public float crouchDepth = 0.2f;
    [Tooltip("숙이는 각도 중 엉덩이로 기울이는 비율 (나머지는 허리·가슴을 굽힘)")] [Range(0f, 1f)] public float hipLeanShare = 0.35f;
    [Tooltip("상체를 앞으로 숙이는 각도")] public float leanAngle = 55f;

    enum Phase { None, Chase, Reach, Lift, Carry, Release, Escape, Drop }
    Phase phase;
    float t, nextAt, chaseLeft;

    GiantController ctrl;
    GiantHealth hp;
    Animator anim;
    GameObject car; TrafficCar carMove; Vector3 carStartScale;
    GameObject hostage; float hostageScale = 1f;
    GameObject carBeacon, destBeacon;
    Vector3 dest, liftStart; Quaternion reachRot = Quaternion.identity;
    Animator hostageAnim; float hostageWorldHeight;
    Camera pipCam; RenderTexture pipRT;

    [Header("인질 연출")]
    [Tooltip("인질이 손에 잡혀 있는 동안 하는 말 (말풍선)")] public string helpText = "Help Me!";
    [Tooltip("발버둥 세기 (0 = 가만히)")] [Range(0f, 2f)] public float struggle = 1f;
    [Tooltip("물체 위에 뜨는 확대 화면 한 변 길이(px)")] public float pipSize = 110f;
    [Tooltip("손에 쥔 인질을 보여주는 우측 상단 확대 화면 한 변 길이(px)")] public float heldPipSize = 170f;

    // 인질 이벤트 중엔 손을 쓰는 다른 동작(시민 잡아먹기)을 막음
    public bool Busy => phase != Phase.None; // 이벤트 진행 중 (UFO는 이때 빔을 쏘지 않음)
    public static bool HandBusy => Instance && Instance.phase != Phase.None && Instance.phase != Phase.Chase;
    // 사람을 손에 쥐고 있는 동안 (적이 공격하지 않음)
    public static bool HoldingHuman => Instance && (Instance.phase == Phase.Lift || Instance.phase == Phase.Carry || Instance.phase == Phase.Escape);

    // ───── 잔해에 깔린 사람 구하기 (RubbleVictim) ─────
    [Header("잔해에 깔린 사람")]
    [Tooltip("쥐고 있다가 탈출을 시도하기까지 시간 범위(초)")] public Vector2 victimHoldTime = new Vector2(9f, 14f);
    [Tooltip("탈출하려고 버둥거리는 시간(초) 뒤 손에서 떨어짐")] public float escapeTime = 1.3f;
    public string escapeText = "놔줘!";
    bool victimMode, escaping; RubbleVictim victim; float dropAt; Vector3 victimStart;

    // 거인이 깔린 사람에게 다가가면 RubbleVictim이 호출. 손이 비어 있으면 집기 시작
    public static bool TryGrabVictim(RubbleVictim v)
    {
        var e = Instance;
        if (!e || !v || e.phase != Phase.None || GiantShrink.Active || WaterPuddles.Slipping) return false; // 넘어지는 중엔 못 집음
        e.Find();
        if (!e.hp || e.hp.IsDead) return false;
        e.victimMode = true; e.victim = v; e.car = null;
        e.LockInput(true);
        e.Begin(Phase.Reach);
        e.t = -e.settleTime / e.reachTime;
        return true;
    }

    // 인질을 쥐는 손: 왼손 (오른손은 국기 게양대 무기용). S = -1 이면 왼손 → 좌우가 바뀌는 방향·회전은 모두 S를 곱해 거울처럼 뒤집음
    const float S = -1f;
    const AvatarIKGoal Goal = AvatarIKGoal.LeftHand;
    const AvatarIKHint Elbow = AvatarIKHint.LeftElbow;
    const HumanBodyBones HandBone = HumanBodyBones.LeftHand, GripBone = HumanBodyBones.LeftMiddleProximal, UpperArm = HumanBodyBones.LeftUpperArm;

    void Awake() { Instance = this; }

    // ───── 인질 = 아이템 (습득 알림) ─────
    [Tooltip("인질을 손에 넣을 때 알림에 뜨는 이름 (무작위)")]
    public string[] hostageTitles = { "VIP 인질 · 국방부 장관", "VIP 인질 · 대통령 비서실장", "VIP 인질 · 재벌 그룹 회장", "VIP 인질 · 중앙은행 총재", "VIP 인질 · UN 특사", "VIP 인질 · 4성 장군", "VIP 인질 · 국가정보원장", "VIP 인질 · 외교부 장관" };
    Texture2D hostageIcon;
    void Start() { hostageIcon = ItemFeed.RenderIcon(hostagePrefab, 96, 160f); }

    string currentTitle;
    public string PickTitle() => hostageTitles != null && hostageTitles.Length > 0 ? hostageTitles[Random.Range(0, hostageTitles.Length)] : "VIP 인질";

    void AnnounceHostage()
    {
        string title = victimMode && victim && !string.IsNullOrEmpty(victim.Title) ? victim.Title : PickTitle();
        currentTitle = title;
        ItemFeed.Show(title, hostageIcon, "확보! 쥐고 있으면 적이 공격 안 함", new Color(1f, 0.82f, 0.3f));
    }
    void OnDestroy() { if (Instance == this) Instance = null; Cleanup(); }

    // 건물이 부서질 때 BuildingDestruction이 호출
    public static void OnBuildingBroken(Vector3 groundPos)
    {
        if (Instance) Instance.TryStart(groundPos);
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    float GiantHeight => hp ? Mathf.Max(1f, hp.Height) : 25f;

    void Find()
    {
        if (!hp) hp = GiantHealth.Instance;
        if (hp && !ctrl) ctrl = hp.GetComponent<GiantController>();
        var a = hp ? hp.GetComponentInChildren<Animator>() : null;
        anim = a;
    }

    // ───────────── 시작: 검은 차량 ─────────────
    void TryStart(Vector3 pos)
    {
        if (phase != Phase.None || Time.time < nextAt || ItemDrops.Skip(eventChance) || !hostagePrefab) return;
        Find();
        if (!hp || hp.IsDead) return;
        var traffic = FindObjectOfType<TrafficManager>();
        if (!traffic || !traffic.city || traffic.carPrefabs == null || traffic.carPrefabs.Length == 0) return;
        var city = traffic.city;

        car = Instantiate(traffic.carPrefabs[Random.Range(0, traffic.carPrefabs.Length)], transform);
        car.name = "HostageCar";
        car.transform.localScale *= carScale;
        foreach (var c in car.GetComponentsInChildren<Collider>()) Destroy(c); // 거인 발에 밟히지 않게 (충돌·Military 레이어 없음)
        foreach (var tr in car.GetComponentsInChildren<Transform>()) tr.gameObject.layer = 0;
        Blacken(car);
        carStartScale = car.transform.localScale;

        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        int i = Mathf.Clamp(Mathf.RoundToInt((pos.x - o.x) / pitch), 0, city.blocksX);
        int j = Mathf.Clamp(Mathf.RoundToInt((pos.z - o.z) / pitch), 0, city.blocksZ);
        carMove = car.AddComponent<TrafficCar>();
        carMove.flee = true;
        carMove.speed = FleeSpeed();
        carMove.Init(city, i, j);
        carMove.EmergeFrom(new Vector3(pos.x, 0f, pos.z), 1.2f); // 부서진 건물 자리에서 도로로 빠져나옴

        // 노란 테두리 하이라이트 (멀리서도 보이게)
        var glow = ThreatGlow.Add(car.transform, 18f, 0.03f, new Color(1f, 0.85f, 0.2f, 0.9f));
        if (glow) glow.transform.localPosition = Vector3.up * 1f;
        chaseLeft = chaseTime;
        phase = Phase.Chase; ItemDrops.Spawned++;
    }

    float FleeSpeed() => ctrl ? Mathf.Max(12f, ctrl.runSpeed * ctrl.GameScale * carSpeedRatio) : 25f;

    static void Blacken(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_Color")) m.color = new Color(0.04f, 0.04f, 0.05f, 1f);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.05f, 1f));
            }
    }

    // ───────────── 진행 ─────────────
    void Update()
    {
        if (phase == Phase.None || GameStartMenu.InMenu) return;
        Find();
        if (!hp || hp.IsDead) { Cleanup(); return; }
        float dt = Time.deltaTime;

        switch (phase)
        {
            case Phase.Chase:
                if (!car) { EndEvent(false); break; }
                carMove.speed = FleeSpeed();
                chaseLeft -= dt;
                if (chaseTime - chaseLeft < graceTime) break; // 등장 직후 무적: 아직 잡을 수 없음
                if (chaseLeft <= 0f) { Show("차량을 놓쳤다..."); Destroy(car); EndEvent(false); break; }
                if (carBeacon) carBeacon.transform.position = Flat(car.transform.position);
                if ((Flat(car.transform.position) - Flat(hp.transform.position)).magnitude < GiantHeight * pickRangeRatio)
                {
                    carMove.enabled = false;
                    LockInput(true);                      // 달리기 입력을 막고 제자리에서 멈춘 뒤 줍는 동작을 시작
                    Begin(Phase.Reach);
                    t = -settleTime / reachTime;          // 달리기 → 멈춤 애니메이션이 정리될 때까지 잠깐 대기
                }
                break;
            case Phase.Reach:
                if (victimMode && !victim) { EndEvent(false); break; } // 집기 전에 사라짐
                t += dt / reachTime;
                if (t >= 1f)
                {
                    liftStart = PalmWorld();
                    Vector3 tp = ReachTargetPos(); Vector3 toT = tp - hp.transform.position; toT.y = 0f;
                    reachRot = HandRotation(toT, Vector3.down);
                    if (victimMode) TakeVictim();
                    else { SpawnHostage(); SpawnDestination(); }
                    Begin(Phase.Lift);
                }
                break;
            case Phase.Lift:
                t += dt / liftTime;
                if (t >= 1f) { if (car) Destroy(car); Destroy(carBeacon); LockInput(false); Begin(Phase.Carry); }
                break;
            case Phase.Carry:
                if (victimMode) { if (Time.time >= dropAt) Begin(Phase.Escape); }
                else if (Flat(hp.transform.position - dest).magnitude < GiantHeight * arriveRatio) Begin(Phase.Release);
                break;
            case Phase.Escape: // 소인이 손에서 빠져나가려고 크게 버둥거림 → 떨어짐
                t += dt / Mathf.Max(0.1f, escapeTime);
                if (t >= 1f) DropVictim();
                break;
            case Phase.Drop: // 손을 펴며 원래 자세로
                t += dt / 0.4f;
                if (t >= 1f) EndEvent(false);
                break;
            case Phase.Release:
                t += dt / releaseTime;
                if (t >= 1f)
                {
                    GiveReward();
                    ScoreManager.Add(rescueScore, "인질 구출!");
                    EndEvent(true);
                }
                break;
        }
    }

    // 인질 구출 보상 (셋 중 하나): HP 가득 회복 / 경험치 2배 / 적 후퇴
    void GiveReward()
    {
        switch (Random.Range(0, 3))
        {
            case 0:
                hp.Heal(hp.maxHP);
                SupplyCrates.Notice("인질 구출! HP 완전 회복");
                break;
            case 1:
                SupplyCrates.GiveXpBoost(xpBoostSeconds);
                SupplyCrates.Notice($"인질 구출! 경험치 2배 ({xpBoostSeconds:0}초)");
                break;
            default:
                MilitarySpawner.Retreat(retreatSeconds);
                SupplyCrates.Notice($"인질 구출! 적 후퇴 ({retreatSeconds:0}초)");
                break;
        }
    }

    void Begin(Phase p) { phase = p; t = 0f; }
    void LockInput(bool on) { if (ctrl) ctrl.inputLocked = on; }

    void EndEvent(bool success)
    {
        bool wasVictim = victimMode;
        Cleanup();
        phase = Phase.None;
        nextAt = Time.time + (wasVictim ? 1.5f : cooldown); // 깔린 사람 구하기는 인질 이벤트 쿨다운을 쓰지 않음
    }

    // 집으러 손을 뻗는 목표 지점 (차 지붕 / 깔린 사람 등)
    Vector3 ReachTargetPos()
    {
        if (victimMode && victim) return victim.transform.position + Vector3.up * victim.Height * 0.25f;
        return car ? car.transform.position + Vector3.up * 1.8f * carScale : hp.transform.position;
    }

    // 깔린 사람을 손에 쥠: 그 사람이 그대로 인질 역할 (크기 유지)
    void TakeVictim()
    {
        hostage = victim.gameObject;
        hostageAnim = victim.Anim;
        hostageWorldHeight = victim.Height;
        hostageScale = victim.transform.localScale.y;
        victimStart = victim.transform.position;
        victim.OnGrabbed();
        AnnounceHostage();
        dropAt = Time.time + liftTime + Random.Range(victimHoldTime.x, victimHoldTime.y);
        MakePip();
    }

    // 버둥거리다 손에서 떨어짐: 사람은 FallingVictim이 떨어뜨리고, 손은 천천히 폄
    void DropVictim()
    {
        if (hostage)
        {
            var fv = hostage.AddComponent<FallingVictim>();
            Vector3 push = hp.transform.forward * 2f + hp.transform.right * Random.Range(-1.5f, 1.5f);
            fv.Init(hostageAnim, victim ? victim.Height : hostageWorldHeight, push, hp.transform.position);
        }
        hostage = null; victim = null; // Cleanup에서 지우지 않게
        if (pipCam) Destroy(pipCam.gameObject);
        if (pipRT) { pipRT.Release(); Destroy(pipRT); }
        pipCam = null; pipRT = null; hostageAnim = null;
        Begin(Phase.Drop);
    }

    void Cleanup()
    {
        LockInput(false);
        if (car) Destroy(car);
        if (hostage) Destroy(hostage);
        if (carBeacon) Destroy(carBeacon);
        if (destBeacon) Destroy(destBeacon);
        if (pipCam) Destroy(pipCam.gameObject);
        if (pipRT) { pipRT.Release(); Destroy(pipRT); }
        pipCam = null; pipRT = null; hostageAnim = null;
        car = null; hostage = null; carBeacon = null; destBeacon = null;
        if (phase != Phase.None) { phase = Phase.None; nextAt = Time.time + (victimMode ? 1.5f : cooldown); }
        victimMode = false; victim = null;
    }

    // ───────────── 인질 · 목적지 ─────────────
    void SpawnHostage()
    {
        hostage = Instantiate(hostagePrefab, transform);
        hostage.name = "Hostage";
        foreach (var c in hostage.GetComponentsInChildren<Collider>()) Destroy(c);
        // 키가 인질 키 비율이 되도록 크기 맞춤 (VRM 기준 약 1.7m)
        var rs = hostage.GetComponentsInChildren<Renderer>();
        float h = 1.7f;
        if (rs.Length > 0) { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); if (b.size.y > 0.1f) h = b.size.y; }
        hostageScale = GiantHeight * hostageHeightRatio / h * hostage.transform.localScale.y;
        var a = hostage.GetComponentInChildren<Animator>();
        if (a && anim) { a.runtimeAnimatorController = anim.runtimeAnimatorController; a.applyRootMotion = false; }
        hostage.transform.localScale = Vector3.one * 0.01f;
        hostageAnim = a;
        hostageWorldHeight = GiantHeight * hostageHeightRatio;
        MakePip();
        AnnounceHostage();
    }

    // 우측 상단 확대 화면용 카메라 (인질을 앞에서 비춤)
    void MakePip()
    {
        var cg = new GameObject("HostageCam");
        cg.transform.SetParent(transform, false);
        pipCam = cg.AddComponent<Camera>();
        pipCam.fieldOfView = 40f; pipCam.nearClipPlane = 0.2f; pipCam.farClipPlane = 3000f;
        pipCam.clearFlags = CameraClearFlags.Skybox;
        pipRT = new RenderTexture(384, 384, 16);
        pipCam.targetTexture = pipRT;
    }

    // 확대 화면 카메라: 인질 앞쪽 위에서 인질을 바라봄 (인질이 얼굴을 바깥으로 향하고 있음)
    void UpdatePip(Vector3 feet)
    {
        if (!pipCam || !hostage) return;
        float H = Mathf.Max(0.5f, hostageWorldHeight);
        Vector3 f = FaceDir();
        pipCam.transform.position = feet + f * (H * 3.2f) + Vector3.up * (H * 1.0f);
        pipCam.transform.LookAt(feet + Vector3.up * (H * 0.55f));
    }

    // 발버둥: 몸을 비틀고 팔다리를 휘저음 (애니메이터 적용 뒤 LateUpdate에서 뼈 회전을 덧씌움)
    void Struggle()
    {
        if (!hostage || !hostageAnim || struggle <= 0f) return;
        float tt = Time.time, k = struggle * (escaping ? 2.5f : 1f); // 탈출 시도 중엔 크게 버둥거림
        Transform root = hostage.transform;
        Vector3 fwd = root.forward, right = root.right;
        // 몸통: 좌우 흔들림은 거의 없고 위아래로 살짝 들썩임
        root.rotation = Quaternion.AngleAxis(Mathf.Sin(tt * 15f) * 3f * k, fwd) * Quaternion.AngleAxis(Mathf.Sin(tt * 9f) * 4f * k, Vector3.up) * root.rotation;
        root.position += right * Mathf.Sin(tt * 17f) * 0.012f * hostageWorldHeight * k + Vector3.up * Mathf.Sin(tt * 14f) * 0.02f * hostageWorldHeight * k;
        // 팔: 옆으로 벌어지는 폭은 작게, 몸 앞뒤로(바깥으로 뻗었다 당기는 식) 휘젓기
        Rot(HumanBodyBones.LeftUpperArm, fwd, -(18f + 10f * Mathf.Sin(tt * 13f)) * k);
        Rot(HumanBodyBones.RightUpperArm, fwd, (18f + 10f * Mathf.Sin(tt * 13f + 1.7f)) * k);
        Rot(HumanBodyBones.LeftUpperArm, right, 40f * Mathf.Sin(tt * 12f) * k);
        Rot(HumanBodyBones.RightUpperArm, right, 40f * Mathf.Sin(tt * 12f + 2.1f) * k);
        Rot(HumanBodyBones.LeftLowerArm, right, 22f * Mathf.Sin(tt * 19f) * k);
        Rot(HumanBodyBones.RightLowerArm, right, 22f * Mathf.Sin(tt * 19f + 0.9f) * k);
        Rot(HumanBodyBones.LeftUpperLeg, right, 32f * Mathf.Sin(tt * 11f) * k);
        Rot(HumanBodyBones.RightUpperLeg, right, -32f * Mathf.Sin(tt * 11f + 0.6f) * k);
    }

    void Rot(HumanBodyBones b, Vector3 axis, float deg)
    {
        var tr = hostageAnim.GetBoneTransform(b);
        if (tr) tr.rotation = Quaternion.AngleAxis(deg, axis) * tr.rotation;
    }

    void SpawnDestination()
    {
        float h = GiantHeight;
        float d = Mathf.Clamp(h * destDistancePerHeight, destDistanceClamp.x, destDistanceClamp.y);
        float a = Random.Range(0f, Mathf.PI * 2f);
        dest = Flat(hp.transform.position) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
        float r = h * arriveRatio;
        destBeacon = MakeBeacon(new Color(0.3f, 1f, 0.45f), r, 600f);
        destBeacon.transform.position = dest;
        // 바닥 링
        var ring = new GameObject("Ring");
        ring.transform.SetParent(destBeacon.transform, false);
        ring.AddComponent<MeshFilter>().sharedMesh = WarningZone.Annulus(r * 0.85f, r, new Color(0.3f, 1f, 0.45f, 0.9f), new Color(0.3f, 1f, 0.45f, 0.9f));
        ring.AddComponent<MeshRenderer>().sharedMaterial = BeaconMaterial();
        ring.transform.localPosition = Vector3.up * (0.5f / 600f); // 부모 스케일(높이 600)을 감안해 바닥 위 0.5m
        ring.transform.localScale = new Vector3(1f / r, 1f / 600f, 1f / r); // 부모(빛기둥) 스케일을 상쇄
        Show("인질을 목적지(초록 빛기둥)까지 옮겨라!");
    }

    static Material beaconMat;
    static Material BeaconMaterial()
    {
        if (!beaconMat) beaconMat = new Material(Shader.Find("Sprites/Default"));
        return beaconMat;
    }

    static GameObject MakeBeacon(Color c, float radius, float height)
    {
        var go = new GameObject("Beacon");
        go.AddComponent<MeshFilter>().sharedMesh = WarningZone.Wall(new Color(c.r, c.g, c.b, 0.55f), new Color(c.r, c.g, c.b, 0f));
        go.AddComponent<MeshRenderer>().sharedMaterial = BeaconMaterial();
        go.transform.localScale = new Vector3(radius, height, radius);
        return go;
    }

    // ───────────── 손 IK ─────────────
    // 인질을 손가락으로 감싸 쥔 자세로 듦: 손 방향(회전)까지 IK로 맞추고, 손가락을 구부리고,
    // 인질은 쥔 손 안에 세움. 이 리그의 손 뼈 축: 손가락 +X(오른손) / -X(왼손), 손바닥(바깥 법선) -Y, 엄지쪽 +Z.
    Vector3 Bone(HumanBodyBones b, Vector3 fallback)
    {
        var tr = anim ? anim.GetBoneTransform(b) : null;
        return tr ? tr.position : fallback;
    }
    float ModelScale => anim ? anim.transform.lossyScale.y : 1f;
    Transform HandT => anim ? anim.GetBoneTransform(HandBone) : null;

    // 손바닥 중심 (손 뼈 로컬 좌표): 손목 → 가운데 손가락 뿌리 방향의 60% 지점
    Vector3 PalmLocal(Transform hand)
    {
        var g = anim ? anim.GetBoneTransform(GripBone) : null;
        return g ? hand.InverseTransformPoint(g.position) * 0.6f : Vector3.zero;
    }
    Vector3 PalmWorld() { var h = HandT; return h ? h.TransformPoint(PalmLocal(h)) : hp.transform.position; }

    // 손바닥 면 방향(n)과 손가락 방향(f)에 맞는 손 뼈 월드 회전
    static Quaternion HandRotation(Vector3 fingerDir, Vector3 palmNormal)
    {
        Vector3 n = palmNormal.normalized;
        Vector3 f = Vector3.ProjectOnPlane(fingerDir, n);
        if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(Vector3.forward, n);
        f.Normalize();
        Vector3 y = -n, z = Vector3.Cross(f * S, y); // 왼손은 손가락이 -X라서 엄지(+Z) 방향이 거울처럼 뒤집힘
        return Quaternion.LookRotation(z, y);
    }

    // 달리면서 들고 있을 때 손바닥 위치: 가슴 아래 앞쪽
    Vector3 CarryPoint()
    {
        float s = ModelScale;
        Vector3 chest = Bone(HumanBodyBones.Chest, Bone(HumanBodyBones.Spine, hp.transform.position + Vector3.up * s));
        return chest + hp.transform.forward * 0.32f * s + hp.transform.right * 0.1f * S * s - Vector3.up * 0.12f * s;
    }

    Vector3 FlatForward() { Vector3 f = hp.transform.forward; f.y = 0f; return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward; }
    // 인질이 바라보는 방향 = 확대 화면 카메라가 있는 쪽: 쥔 손의 손바닥이 향하는 몸 안쪽 앞
    Vector3 FaceDir() { Vector3 d = -S * hp.transform.right + hp.transform.forward * 0.35f; d.y = 0f; return d.normalized; }
    // 인질을 쥐는 자세: 팔을 앞으로, 엄지가 위를 향하고 손바닥이 몸 안쪽을 보게 해서 세로로 선 인질을 손가락으로 감쌈
    Quaternion CarryRotation() => HandRotation(FlatForward(), (-S * hp.transform.right + Vector3.up * 0.2f).normalized);

    // IK 손 회전 목표와 실제 손 뼈 축 사이의 고정 보정 (오른손 기준으로 측정한 값: 손 뼈가 IK 목표보다 Y축으로 -90° 돌아가 있음. 왼손은 거울로 뒤집어 씀)
    [Tooltip("IK 손 회전 보정 (오일러). 손이 90° 돌아가 보이면 이 값을 조정")] public Vector3 ikRotOffsetEuler = new Vector3(0f, 90f, 0f);

    // GiantIKRelay가 매 프레임 호출 (GiantEat 다음)
    public void OnIK(Animator a)
    {
        if (!hp || phase == Phase.None || phase == Phase.Chase) return;
        var hand = a.GetBoneTransform(HandBone);
        if (!hand) return;
        float w = 0f, crouch = 0f;
        Vector3 palm = CarryPoint();
        Quaternion rot = CarryRotation();
        switch (phase)
        {
            case Phase.Reach:
                w = Smooth(t); crouch = Smooth(t);
                if (car || (victimMode && victim))
                {
                    palm = ReachTargetPos();
                    Vector3 to = palm - hp.transform.position; to.y = 0f;
                    rot = HandRotation(to, Vector3.down); // 위에서 덮듯이 손바닥을 아래로
                }
                break;
            case Phase.Escape: w = 1f; break;
            case Phase.Drop: w = 1f - Smooth(t); break;
            case Phase.Lift:
                w = 1f; crouch = 1f - Smooth(t / 0.7f);
                palm = Vector3.Lerp(liftStart, CarryPoint(), Smooth(t));
                rot = Quaternion.Slerp(reachRot, CarryRotation(), Smooth(t)); // 손바닥을 뒤집어 받쳐 듦
                break;
            case Phase.Carry: w = 1f; break;
            case Phase.Release: w = 1f - Smooth(t); break;
        }

        float s = ModelScale;
        if (crouch > 0.001f)
        {
            PinFoot(a, AvatarIKGoal.LeftFoot); PinFoot(a, AvatarIKGoal.RightFoot);
            KneeForward(a, AvatarIKHint.LeftKnee, HumanBodyBones.LeftLowerLeg, crouch); KneeForward(a, AvatarIKHint.RightKnee, HumanBodyBones.RightLowerLeg, crouch);
            a.bodyPosition = a.bodyPosition - Vector3.up * crouchDepth * s * crouch;
            // 엉덩이(몸 전체)는 조금만 기울이고 나머지는 허리·가슴을 굽혀 숙임: 엉덩이를 크게 돌리면 다리가 뒤로 꺾여 팔처럼 보였음
            a.bodyRotation = Quaternion.AngleAxis(leanAngle * hipLeanShare * crouch, hp.transform.right) * a.bodyRotation;
            float spineLean = leanAngle * (1f - hipLeanShare) * crouch / 3f;
            BendBone(a, HumanBodyBones.Spine, spineLean); BendBone(a, HumanBodyBones.Chest, spineLean); BendBone(a, HumanBodyBones.UpperChest, spineLean);
        }
        else
        {
            a.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f); a.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            a.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f); a.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            a.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f); a.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
        }

        // 손목 목표 = 손바닥 목표 − (목표 회전에서의 손목→손바닥 벡터)
        Vector3 palmOffset = rot * (PalmLocal(hand) * hand.lossyScale.x);
        a.SetIKPositionWeight(Goal, w);
        a.SetIKPosition(Goal, palm - palmOffset);
        a.SetIKRotationWeight(Goal, w);
        a.SetIKRotation(Goal, rot * Quaternion.Euler(ikRotOffsetEuler.x, ikRotOffsetEuler.y * S, ikRotOffsetEuler.z * S));

        Vector3 shoulder = Bone(UpperArm, hp.transform.position + Vector3.up * s);
        a.SetIKHintPositionWeight(Elbow, w * 0.8f);
        a.SetIKHintPosition(Elbow, shoulder + hp.transform.right * 0.25f * S * s - Vector3.up * 0.3f * s - hp.transform.forward * 0.05f * s);
        if (phase == Phase.Reach && (car || (victimMode && victim)))
        {
            a.SetLookAtWeight(Smooth(t) * 0.9f, 0.3f, 1f);
            a.SetLookAtPosition(ReachTargetPos());
        }
    }

    // 뼈 하나를 거인 오른쪽 축으로 앞으로 굽힘 (OnAnimatorIK 안에서 SetBoneLocalRotation으로)
    void BendBone(Animator a, HumanBodyBones bone, float deg)
    {
        var b = a.GetBoneTransform(bone);
        if (!b || !b.parent || Mathf.Abs(deg) < 0.01f) return;
        Quaternion world = Quaternion.AngleAxis(deg, hp.transform.right) * b.rotation;
        a.SetBoneLocalRotation(bone, Quaternion.Inverse(b.parent.rotation) * world);
    }

    // 쪼그릴 때 무릎이 앞으로 굽게 힌트를 줌 (힌트가 없으면 다리가 뒤로 꺾여 들려서 팔처럼 보였음)
    void KneeForward(Animator a, AvatarIKHint hint, HumanBodyBones knee, float w)
    {
        var k = a.GetBoneTransform(knee);
        if (!k) return;
        a.SetIKHintPositionWeight(hint, w);
        a.SetIKHintPosition(hint, k.position + hp.transform.forward * ModelScale * 0.4f);
    }

    static void PinFoot(Animator a, AvatarIKGoal foot)
    {
        Vector3 p = a.GetIKPosition(foot);
        // 발은 땅에: 달리던 자세의 들린 발 높이가 아니라 바닥 높이로 고정
        p.y = Mathf.Min(p.y, a.transform.position.y + a.transform.lossyScale.y * 0.1f); Quaternion r = a.GetIKRotation(foot);
        a.SetIKPositionWeight(foot, 1f); a.SetIKRotationWeight(foot, 1f);
        a.SetIKPosition(foot, p); a.SetIKRotation(foot, r);
    }

    // 손가락 구부리기: 손바닥을 덮는 방향(오른손 -Z, 왼손 +Z 회전)으로 마디마다 조금씩 (검지~새끼, 엄지 제외)
    static readonly HumanBodyBones[] CurlBones =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
    };
    static readonly float[] CurlAngles = { 55f, 70f, 45f };   // 마디별 최대 굽힘 (손바닥 쪽으로 쥐는 정도)
    [Tooltip("손가락 구부림 방향 (오른손 기준 -1 / +1, 반대로 구부러지면 부호를 바꿈. 왼손은 자동으로 뒤집힘)")] public float curlSign = -1f;
    [Tooltip("손으로 쥐는 정도 (1 = 기본, 0 = 펴짐)")] [Range(0f, 1.5f)] public float gripStrength = 1f;
    [Tooltip("쥔 손 안에서 인질 몸통 중심이 손바닥 면에서 떨어진 거리 (손 길이 대비)")] public float gripDepth = 0.2f;
    [Tooltip("인질이 쥐어지는 높이 (발 기준, 인질 키 대비. 0.55 = 허리·가슴 부근을 쥐고 머리가 위로 나옴)")] public float gripHeight = 0.55f;

    [Tooltip("엄지를 손바닥 쪽으로 모으는 각도 (손가락 방향 축 회전, 부호로 방향 조정)")] public float thumbOpposeDeg = 30f;
    [Tooltip("엄지 마디를 구부리는 각도 (엄지 쪽 축 회전, 부호로 방향 조정)")] public float thumbCurlDeg = -40f;

    void CurlFingers(float amount)
    {
        if (amount <= 0.001f || !anim) return;
        for (int i = 0; i < CurlBones.Length; i++)
        {
            var b = anim.GetBoneTransform(CurlBones[i]);
            if (b) b.localRotation = b.localRotation * Quaternion.Euler(0f, 0f, curlSign * S * CurlAngles[i % 3] * amount);
        }
        // 엄지: 뿌리를 손바닥 쪽으로 모으고 마디를 구부려 인질을 감싸는 손가락들과 맞물리게 함
        var hand = HandT;
        if (!hand) return;
        var tp = anim.GetBoneTransform(HumanBodyBones.LeftThumbProximal);
        var ti = anim.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate);
        var td = anim.GetBoneTransform(HumanBodyBones.LeftThumbDistal);
        if (tp) tp.rotation = Quaternion.AngleAxis(thumbOpposeDeg * amount, hand.right) * tp.rotation;
        if (ti) ti.rotation = Quaternion.AngleAxis(thumbCurlDeg * S * amount, hand.forward) * ti.rotation;
        if (td) td.rotation = Quaternion.AngleAxis(thumbCurlDeg * S * amount, hand.forward) * td.rotation;
    }

    // IK가 끝난 손바닥에 차량·인질을 붙임
    void LateUpdate()
    {
        if (phase == Phase.None || phase == Phase.Chase || !anim) return;
        var hand = HandT;
        if (!hand) return;
        float curl = phase == Phase.Lift ? Smooth(t) : (phase == Phase.Carry || phase == Phase.Escape) ? 1f
                   : (phase == Phase.Release || phase == Phase.Drop) ? 1f - Smooth(t) : 0f;
        CurlFingers(curl * gripStrength);
        Vector3 palmW = PalmWorld();
        float handLen = (Bone(GripBone, hand.position) - hand.position).magnitude;
        Vector3 grip = palmW - hand.up * (gripDepth * handLen); // 쥔 손 안, 손바닥 면(-Y 방향)에서 gripDepth만큼 떨어진 지점 = 인질 몸통 중심
        if (phase == Phase.Lift)
        {
            float k = Smooth(t);
            if (car) { car.transform.position = Vector3.Lerp(car.transform.position, grip, k); car.transform.localScale = carStartScale * (1f - k); }
            if (victimMode)
            {
                // 깔린 사람: 크기는 그대로, 땅에서 손으로 들어 올림
                SetHostage(grip, 1f);
                if (hostage) hostage.transform.position = Vector3.Lerp(victimStart, hostage.transform.position, k);
            }
            else SetHostage(grip, k);
        }
        else if (phase == Phase.Carry) SetHostage(grip, 1f);
        else if (phase == Phase.Escape) { escaping = true; SetHostage(grip, 1f); escaping = false; }
        else if (phase == Phase.Release) SetHostage(grip, 1f - Smooth(t));
    }

    // 인질을 쥔 손 안(몸통 중심이 grip 위치)에 세로로 세움. 머리는 손 위로, 다리는 아래로 나옴
    void SetHostage(Vector3 grip, float scale01)
    {
        if (!hostage) return;
        hostage.transform.localScale = Vector3.one * Mathf.Max(0.01f, hostageScale * scale01);
        Vector3 pos = grip - Vector3.up * (gripHeight * hostageWorldHeight * scale01); // 발 위치
        hostage.transform.position = pos;
        Vector3 face = FaceDir(); // 얼굴은 확대 화면 카메라(거인 왼쪽 앞)를 향함
        if (face.sqrMagnitude > 0.01f) hostage.transform.rotation = Quaternion.LookRotation(face);
        if (scale01 > 0.5f) Struggle();
        UpdatePip(pos);
    }

    // ───────────── 화면 표시 ─────────────
    // 안내문 없이, 화면 테두리에서 목표(검은 차량 → 목적지)를 가리키는 화살표만 표시
    void Show(string text) { }

    static Texture2D arrowTex;
    static Texture2D ArrowTexture()
    {
        if (arrowTex) return arrowTex;
        const int n = 64;
        arrowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // 오른쪽을 향한 삼각형 (꼭짓점 (n-3, n/2), 밑변 x=4). 안쪽은 흰색, 가장자리는 어두운 테두리
                float u = (x - 4f) / (n - 7f);                 // 0(밑변) → 1(꼭짓점)
                float half = (1f - u) * (n * 0.45f);           // 이 위치에서 삼각형 반폭
                float d = Mathf.Abs(y - (n - 1) / 2f);
                float edge = Mathf.Min(half - d, u * 20f, (1f - u) * 20f); // 변까지 거리
                float a = u < 0f || u > 1f ? 0f : Mathf.Clamp01(edge / 1.5f);
                float inner = Mathf.Clamp01((edge - 4f) / 1.5f);
                arrowTex.SetPixel(x, y, new Color(Mathf.Lerp(0f, 1f, inner), Mathf.Lerp(0f, 1f, inner), Mathf.Lerp(0f, 1f, inner), a));
            }
        arrowTex.Apply();
        return arrowTex;
    }

    // 확대 화면 상자 위치: 세계 좌표 world가 화면에 보이는 자리 바로 위 (화면 밖으로 나가지 않게)
    public static Rect PipRectAbove(Vector3 world, float size)
    {
        var cam = Camera.main;
        Vector3 sp = cam ? cam.WorldToScreenPoint(world) : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 1f);
        if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = 0f; }
        float x = sp.x - size * 0.5f, y = (Screen.height - sp.y) - size - 34f; // 아래에 이름 띠가 들어갈 자리를 남김
        x = Mathf.Clamp(x, 10f, Screen.width - size - 10f);
        y = Mathf.Clamp(y, 10f, Screen.height - size - 30f);
        return new Rect(x, y, size, size);
    }

    public static bool ShowingPip => Instance && Instance.HostageHeld; // 손에 쥔 인질 확대 화면이 떠 있는지
    bool HostageHeld => hostage && ((phase == Phase.Lift && t > 0.5f) || phase == Phase.Carry || phase == Phase.Escape || (phase == Phase.Release && t < 0.5f));

    // 말풍선: center(GUI 좌표)를 가운데로 한 흰 상자 + 아래쪽 꼬리. bounds를 주면 그 영역 안으로 밀어 넣음
    public static void DrawBubble(Vector2 c, string text, int fontSize, Rect? bounds = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        style.normal.textColor = Color.black;
        Vector2 size = style.CalcSize(new GUIContent(text)) + new Vector2(fontSize, fontSize * 0.45f);
        if (bounds.HasValue)
        {
            var b = bounds.Value;
            c.x = Mathf.Clamp(c.x, b.xMin + size.x * 0.5f + 4f, b.xMax - size.x * 0.5f - 4f);
            c.y = Mathf.Clamp(c.y, b.yMin + size.y * 0.5f + 4f, b.yMax - size.y * 0.5f - 16f);
        }
        var box = new Rect(c.x - size.x * 0.5f, c.y - size.y * 0.5f, size.x, size.y);
        var old = GUI.matrix;
        Vector2 tail = new Vector2(c.x, box.yMax + 1f);
        float ts = fontSize * 0.7f;
        GUI.color = Color.black; GUI.DrawTexture(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), Texture2D.whiteTexture);
        GUIUtility.RotateAroundPivot(45f, tail); GUI.DrawTexture(new Rect(tail.x - ts * 0.5f - 2, tail.y - ts * 0.5f - 2, ts + 4, ts + 4), Texture2D.whiteTexture); GUI.matrix = old;
        GUI.color = Color.white; GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUIUtility.RotateAroundPivot(45f, tail); GUI.DrawTexture(new Rect(tail.x - ts * 0.5f, tail.y - ts * 0.5f, ts, ts), Texture2D.whiteTexture); GUI.matrix = old;
        GUI.Label(box, text, style);
        GUI.color = Color.white;
    }

    // 우측 상단: 손에 잡힌 인질 확대 화면 (말풍선도 이 화면 안에서 말함)
    void DrawHostageView()
    {
        if (!HostageHeld || !pipRT || !pipCam) return;
        // 손에 쥔 인질은 우측 상단 모서리에 (깔린 사람·감자가방은 그 물체 바로 위에 뜸)
        Rect box = new Rect(Screen.width - heldPipSize - 20f, 20f, heldPipSize, heldPipSize);
        DrawPip(box, pipRT, new Color(1f, 0.85f, 0.2f), string.IsNullOrEmpty(currentTitle) ? "VIP 인질" : currentTitle);
    }

    // 확대 화면 상자 + 아래에 아이템 이름 (인질·깔린 사람·감자가방 공용)
    public static void DrawPip(Rect box, Texture tex, Color frame, string name)
    {
        GUI.color = frame;
        GUI.DrawTexture(new Rect(box.x - 3, box.y - 3, box.width + 6, box.height + 6), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(box, tex, ScaleMode.StretchToFill, false);
        if (string.IsNullOrEmpty(name)) return;
        var st = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        float w = Mathf.Max(box.width + 6f, st.CalcSize(new GUIContent(name)).x + 12f);
        var lr = new Rect(box.center.x - w * 0.5f, box.yMax + 3f, w, 18f);
        GUI.color = new Color(0f, 0f, 0f, 0.65f); GUI.DrawTexture(lr, Texture2D.whiteTexture);
        GUI.color = frame; GUI.Label(lr, name, st);
        GUI.color = Color.white;
    }

    // 도망치는 검은 차 말풍선: 상황(처음 · 거리 · 잡힘)에 맞는 짧은 대사
    static readonly string[] LinesStart = { "건물이 무너졌다! 튀어!", "으악, 거인이다!" };
    static readonly string[] LinesNear = { "으악! 따라온다!", "제발 저리 가!", "더 밟아, 더 밟아!" };
    static readonly string[] LinesMid = { "쫓아오지 마!", "거인이다! 도망쳐!", "엑셀 밟아!" };
    static readonly string[] LinesFar = { "후후, 못 잡을걸?", "이 정도면 안전하겠지?", "여긴 못 따라오겠지!" };
    string carLine; float carLineUntil;

    string CarLine()
    {
        if (phase == Phase.Reach) return "으아악! 잡혔다!";
        if (Time.time >= carLineUntil || carLine == null)
        {
            float d = Flat(car.transform.position - hp.transform.position).magnitude;
            var pool = chaseTime - chaseLeft < 3f ? LinesStart : d < GiantHeight * 3f ? LinesNear : d < GiantHeight * 8f ? LinesMid : LinesFar;
            string next = pool[Random.Range(0, pool.Length)];
            if (pool.Length > 1) while (next == carLine) next = pool[Random.Range(0, pool.Length)]; // 같은 말 연속 방지
            carLine = next; carLineUntil = Time.time + 2.5f;
        }
        return carLine;
    }

    void DrawCarBubble()
    {
        if (!car || (phase != Phase.Chase && phase != Phase.Reach)) return;
        var cam = Camera.main;
        if (!cam) return;
        Vector3 sp = cam.WorldToScreenPoint(car.transform.position + Vector3.up * 9f * carScale * 0.5f);
        if (sp.z <= 0f || sp.x < -50f || sp.x > Screen.width + 50f || sp.y < -50f || sp.y > Screen.height + 50f) return;
        DrawBubble(new Vector2(sp.x, Screen.height - sp.y - 24f), CarLine(), 20);
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu || phase == Phase.None || !hp) return;
        DrawHostageView();
        DrawCarBubble();
        Vector3 target; Color col;
        if (phase == Phase.Chase && car) { target = car.transform.position; col = new Color(1f, 0.85f, 0.2f); }
        else if (destBeacon) { target = dest; col = new Color(0.4f, 1f, 0.5f); }
        else return;
        var cam = Camera.main;
        if (!cam) return;

        Vector3 sp = cam.WorldToScreenPoint(target + Vector3.up * 5f);
        Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 d = new Vector2(sp.x, sp.y) - c;           // 화면 중심에서 목표까지 (y는 위쪽이 +)
        if (sp.z < 0f) d = -d;                             // 카메라 뒤쪽이면 반대
        if (d.sqrMagnitude < 1f) d = Vector2.up;
        const float margin = 48f;
        float k = Mathf.Min((c.x - margin) / Mathf.Max(0.001f, Mathf.Abs(d.x)), (c.y - margin) / Mathf.Max(0.001f, Mathf.Abs(d.y)));
        Vector2 p = c + d * k;                             // 화면 테두리 위 위치
        Vector2 gp = new Vector2(p.x, Screen.height - p.y); // GUI 좌표(y 아래가 +)
        float angle = Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg; // GUI 좌표계에서 화살표가 향할 각도 (그림은 오른쪽 기준)

        var old = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, gp);
        GUI.color = col;
        GUI.DrawTexture(new Rect(gp.x - 28, gp.y - 28, 56, 56), ArrowTexture());
        GUI.matrix = old;
        GUI.color = Color.white;
    }
}
