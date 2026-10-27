using System.Collections.Generic;
using UnityEngine;

// 국기 게양대: 도시 인도 모퉁이 곳곳에 굵고 긴 게양대를 세우고, 나라 국기를 무작위로 매달아 펄럭이게 함.
// 거인이 지나가며 닿으면 오른손으로 뽑아 들고 무기로 씀: 들고 있는 동안 계속 좌우로 크게 휘두르고(자동),
// 휘두르는 범위에 들어온 적(탱크·경찰·헬기·전투기)은 부서짐. 휘두르는 동작은 Human Melee Animations의
// 한 손 공격(Attack1H01_R)을 애니메이터 상체 레이어(Swing)에서 반복 재생하고, 게양대는 오른손에 붙임.
public class Flagpoles : MonoBehaviour
{
    public static Flagpoles Instance { get; private set; }
    // 오른손에 게양대를 들고 있거나 집는 중 (오른손을 쓰는 다른 동작은 왼손을 쓰거나 쉼)
    public static bool RightHandBusy => Instance && Instance.phase != Phase.None;

    [Header("배치")]
    [Tooltip("도시에 세우는 게양대 수")] public int count = 26;
    [Tooltip("게양대 길이 = 거인 키 × 이 범위")] public Vector2 heightRatio = new Vector2(0.95f, 1.15f);
    [Tooltip("게양대 굵기(반지름) = 길이 × 이 비율")] public float thicknessRatio = 0.022f;
    [Tooltip("깃발 가로 길이 = 게양대 길이 × 이 비율 (세로는 가로의 2/3)")] public float flagSizeRatio = 0.45f;
    [Tooltip("거인 시작 위치에서 이 거리(m) 안에는 세우지 않음")] public float minDistanceFromStart = 80f;
    public Color poleColor = new Color(0.86f, 0.87f, 0.9f);
    public Color finialColor = new Color(0.95f, 0.78f, 0.25f);

    [Header("줍기")]
    [Tooltip("거인 몸 반경 + 거인 키 × 이 비율 안을 지나가면 집음")] public float pickRangeRatio = 0.15f;
    public float reachTime = 0.22f;
    public float liftTime = 0.25f;
    [Tooltip("손으로 쥐는 위치 (게양대 아래 끝에서부터 길이 비율)")] [Range(0f, 0.5f)] public float gripAt = 0.15f;

    [Header("휘두르기 (자동, 계속)")]
    [Tooltip("애니메이터의 상체 레이어 이름 (Human Melee Animations의 한 손 공격 Attack1H01_R을 반복 재생)")] public string swingLayer = "Swing";
    [Tooltip("휘두르는 동작 재생 속도 배율")] public float swingSpeed = 1.2f;
    [Tooltip("손 뼈 기준 게양대 방향 보정 (오일러). 0 = 엄지 쪽(+Z)으로 뻗음")] public Vector3 poleAxisEuler = Vector3.zero;
    [Tooltip("게양대에 맞는 판정 굵기 = 거인 키 × 이 비율")] public float hitRadiusRatio = 0.16f;
    [Tooltip("손에 들고 이 시간(초)이 지나면 게양대가 부러져 사라짐")] public float holdTime = 12f;
    [Tooltip("건물을 부수는 판정 굵기 = 거인 키 × 이 비율 (게양대 둘레로 이만큼 닿는 건물이 부서짐)")] public float buildingHitRatio = 0.25f;

    class Pole
    {
        public GameObject root; public Transform cloth; public Mesh clothMesh; public Vector3[] baseVerts, verts;
        public float height, flagW, phase; public string country; public Texture2D tex;
    }
    readonly List<Pole> poles = new List<Pole>();

    enum Phase { None, Reach, Lift, Hold }
    Phase phase; float t;
    Pole held; Vector3 liftFromPos; Quaternion liftFromRot;
    float swingWeight;                            // 상체 레이어 가중치 (집은 직후 0 → 1로 커짐)
    int swingLoop = -1;                           // 몇 번째 휘두르기인지 (바뀔 때마다 맞힌 목록을 비움)
    readonly HashSet<Object> hitThisSwing = new HashSet<Object>();
    Vector3 prevA, prevB; bool hasPrev;           // 이전 프레임 게양대 선분 (빠른 휘두르기도 놓치지 않게)
    Vector3 shoulderLocal; bool hasShoulder;      // 거인 모델 기준 오른쪽 어깨 위치 (LateUpdate에서 잼)
    float armLen = 1f;

    GiantHealth hp; GiantController ctrl; Animator anim;
    static Material poleMat, finialMat;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start() { Build(); }

    // ───────────── 배치 ─────────────
    void Build()
    {
        var city = FindObjectOfType<CityGenerator>();
        var giant = GiantHealth.Instance ? GiantHealth.Instance : FindObjectOfType<GiantHealth>();
        if (!city || !giant) return;
        float H = Mathf.Max(5f, giant.Height);
        if (!poleMat) poleMat = new Material(Shader.Find("Standard")) { color = poleColor };
        if (!finialMat) finialMat = new Material(Shader.Find("Standard")) { color = finialColor };

        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 origin = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f;
        Vector3 start = giant.transform.position; start.y = 0f;
        float inset = city.walkwayWidth * 0.5f;
        int blockMask = LayerMask.GetMask("Building", "Default");
        var used = new HashSet<Vector2Int>();
        for (int tries = 0; poles.Count < count && tries < count * 30; tries++)
        {
            int bx = Random.Range(0, city.blocksX), bz = Random.Range(0, city.blocksZ), corner = Random.Range(0, 4);
            if (!used.Add(new Vector2Int(bx * 4 + corner, bz))) continue;
            Vector3 min = origin + new Vector3(city.roadWidth + bx * pitch, 0f, city.roadWidth + bz * pitch);
            float cx = (corner & 1) == 0 ? inset : city.blockSize - inset, cz = (corner & 2) == 0 ? inset : city.blockSize - inset;
            Vector3 p = min + new Vector3(cx, 0f, cz);
            if ((p - start).magnitude < minDistanceFromStart) continue;
            // 건물이나 다른 구조물과 겹치는 곳은 건너뜀 (캡슐 아래 끝이 인도 바닥에 닿지 않게 4m 위부터 검사)
            if (Physics.CheckCapsule(p + Vector3.up * 4f, p + Vector3.up * 12f, 2.5f, blockMask, QueryTriggerInteraction.Ignore)) continue;
            p.y = ItemDrops.GroundY(p);
            // 깃발은 블록 바깥(도로) 쪽으로 날리게
            Vector3 outward = new Vector3((corner & 1) == 0 ? -1f : 1f, 0f, (corner & 2) == 0 ? -1f : 1f).normalized;
            poles.Add(MakePole(p, H * Random.Range(heightRatio.x, heightRatio.y), outward));
        }
    }

    Pole MakePole(Vector3 basePos, float h, Vector3 flyDir)
    {
        var pole = new Pole { height = h, phase = Random.value * 10f };
        var root = new GameObject("Flagpole");
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(basePos, Quaternion.LookRotation(flyDir));
        pole.root = root;
        float r = Mathf.Max(0.2f, h * thicknessRatio);

        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        shaft.name = "Pole";
        Destroy(shaft.GetComponent<Collider>());
        shaft.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
        shaft.transform.SetParent(root.transform, false);
        shaft.transform.localScale = new Vector3(r * 2f, h * 0.5f, r * 2f);
        shaft.transform.localPosition = Vector3.up * h * 0.5f;

        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Finial";
        Destroy(ball.GetComponent<Collider>());
        ball.GetComponent<MeshRenderer>().sharedMaterial = finialMat;
        ball.transform.SetParent(root.transform, false);
        ball.transform.localScale = Vector3.one * r * 3.2f;
        ball.transform.localPosition = Vector3.up * (h + r * 1.2f);

        // 깃발: 게양대 꼭대기 바로 아래, 앞쪽(+Z)으로 펼쳐짐. 양면이 보이는 Sprites/Default 재질에 국기 그림
        var flag = Flags.Random();
        pole.country = flag.name; pole.tex = flag.tex;
        pole.flagW = h * flagSizeRatio;
        float fh = pole.flagW * 2f / 3f;
        var cloth = new GameObject("Flag");
        cloth.transform.SetParent(root.transform, false);
        cloth.transform.localPosition = new Vector3(0f, h - r * 2f - fh, r);
        pole.cloth = cloth.transform;
        pole.clothMesh = ClothMesh(pole.flagW, fh, out pole.baseVerts);
        pole.verts = (Vector3[])pole.baseVerts.Clone();
        cloth.AddComponent<MeshFilter>().sharedMesh = pole.clothMesh;
        var mr = cloth.AddComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = flag.tex };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        return pole;
    }

    const int ClothX = 14, ClothY = 8;
    static Mesh ClothMesh(float w, float h, out Vector3[] verts)
    {
        verts = new Vector3[(ClothX + 1) * (ClothY + 1)];
        var uv = new Vector2[verts.Length];
        var tris = new List<int>();
        for (int y = 0; y <= ClothY; y++)
            for (int x = 0; x <= ClothX; x++)
            {
                int i = y * (ClothX + 1) + x;
                verts[i] = new Vector3(0f, h * y / ClothY, w * x / ClothX); // 게양대에서 +Z(깃발 날리는 쪽)로
                uv[i] = new Vector2((float)x / ClothX, (float)y / ClothY);
                if (x < ClothX && y < ClothY)
                {
                    int a = i, b = i + 1, c = i + ClothX + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
        var m = new Mesh { vertices = verts, uv = uv, triangles = tris.ToArray() };
        m.MarkDynamic();
        m.RecalculateBounds();
        m.RecalculateNormals();
        return m;
    }

    // 깃발 펄럭임: 게양대에서 멀어질수록 크게 출렁이는 물결
    void Wave(Pole p, float speed)
    {
        if (!p.cloth) return;
        float time = Time.time * speed + p.phase;
        float k = Mathf.PI * 2f / (p.flagW * 0.8f);
        for (int i = 0; i < p.verts.Length; i++)
        {
            Vector3 b = p.baseVerts[i];
            float u = b.z / p.flagW;
            float wave = Mathf.Sin(b.z * k - time * 6f) * p.flagW * 0.09f * u;
            float droop = (1f - Mathf.Cos(u * 1.2f)) * p.flagW * 0.05f;
            p.verts[i] = new Vector3(wave, b.y - droop + Mathf.Sin(time * 4f + b.z * k * 0.5f) * p.flagW * 0.02f * u, b.z * (1f - 0.04f * Mathf.Abs(Mathf.Sin(time * 3f))));
        }
        p.clothMesh.vertices = p.verts;
        p.clothMesh.RecalculateBounds();
    }

    // ───────────── 진행 ─────────────
    void Find()
    {
        if (!hp) hp = GiantHealth.Instance;
        if (hp && !ctrl) ctrl = hp.GetComponent<GiantController>();
        anim = hp ? hp.GetComponentInChildren<Animator>() : null;
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    float H => hp ? Mathf.Max(1f, hp.Height) : 25f;
    // 휘두르기를 쉬는 중 (넘어짐 등): 게양대는 손에 든 채 모션 그대로
    bool Paused => WaterPuddles.Slipping;

    void Update()
    {
        foreach (var p in poles) Wave(p, p == held && phase == Phase.Hold ? 2.5f : 1f);
        if (GameStartMenu.InMenu) return;
        Find();
        if (!hp || !anim) return;
        if (hp.IsDead || GiantShrink.Active || Paused)
        {
            if (phase == Phase.Reach) { phase = Phase.None; held = null; }
            hasPrev = false;
            BlendSwingLayer(0f); // 넘어지는 중엔 넘어지는 모션 그대로
            return;
        }

        switch (phase)
        {
            case Phase.None: TryPick(); break;
            case Phase.Reach:
                if (held == null || !held.root) { phase = Phase.None; held = null; break; }
                t += Time.deltaTime / reachTime;
                if (t >= 1f)
                {
                    liftFromPos = held.root.transform.position; liftFromRot = held.root.transform.rotation;
                    phase = Phase.Lift; t = 0f;
                    ItemFeed.Show($"국기 게양대 · {held.country}", held.tex, "계속 크게 휘둘러 닿는 적을 부숨", new Color(0.75f, 0.85f, 1f));
                }
                break;
            case Phase.Lift:
                t += Time.deltaTime / liftTime;
                if (t >= 1f) { phase = Phase.Hold; t = 0f; swingLoop = -1; hasPrev = false; }
                break;
            case Phase.Hold: UpdateSwing(); break;
        }
        if (phase != Phase.Hold) BlendSwingLayer(0f);
    }

    void TryPick()
    {
        if (WaterPuddles.Slipping || (ctrl && ctrl.inputLocked)) return;
        var eat = hp.GetComponent<GiantEat>();
        if (eat && eat.IsEating) return; // 오른손으로 시민을 먹는 중일 수 있음
        var cc = hp.GetComponent<CharacterController>();
        float range = (cc ? cc.radius : 0f) + H * pickRangeRatio;
        foreach (var p in poles)
        {
            if (!p.root) continue;
            if (ItemDrops.PathDistance(p.root.transform.position) < range)
            {
                held = p; phase = Phase.Reach; t = 0f; // 들고 있는 게양대도 목록에 남겨 계속 펄럭이게 함
                return;
            }
        }
    }

    // ───────────── 휘두르기 ─────────────
    static readonly int SwingSpeedHash = Animator.StringToHash("SwingSpeed");

    // 상체 레이어(한 손 공격 동작 반복)를 켜고 끔. 다리는 아래 레이어의 걷기·달리기 그대로
    void BlendSwingLayer(float target)
    {
        if (!anim) return;
        int li = anim.GetLayerIndex(swingLayer);
        if (li < 0) return;
        swingWeight = Mathf.MoveTowards(swingWeight, target, Time.deltaTime / 0.25f);
        anim.SetLayerWeight(li, swingWeight);
        anim.SetFloat(SwingSpeedHash, swingSpeed);
    }

    void UpdateSwing()
    {
        if (held == null || !held.root) { phase = Phase.None; held = null; return; }
        t += Time.deltaTime; // 들고 있는 시간
        if (t >= holdTime) { BreakHeld(); return; }
        BlendSwingLayer(1f);
        // 동작이 한 번 끝날 때마다 맞힌 목록을 비움 (다음 휘두르기에 다시 맞을 수 있게)
        int li = anim.GetLayerIndex(swingLayer);
        int loop = li >= 0 ? Mathf.FloorToInt(anim.GetCurrentAnimatorStateInfo(li).normalizedTime) : 0;
        if (loop != swingLoop) { swingLoop = loop; hitThisSwing.Clear(); }
        if (swingWeight > 0.5f) Hit();
        else hasPrev = false;
    }

    // 오래 휘둘러 게양대가 부러짐: 손잡이 위가 꺾여 떨어지고 먼지와 함께 사라짐. 손은 다시 빈손으로
    void BreakHeld()
    {
        var root = held.root.transform;
        poles.Remove(held);
        Vector3 snap = root.position + root.up * held.height * (gripAt + 0.06f); // 손 바로 위에서 꺾임
        CombatFX.Explosion(snap, Mathf.Max(5f, H * 0.2f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.3f);
        // 게양대는 손에서 떨어져 나가 넘어지며 떨어짐 (충돌 없이 잠깐 떨어지다 사라짐)
        root.SetParent(transform, true);
        var rb = root.gameObject.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.velocity = hp.transform.forward * H * 0.4f + Vector3.up * H * 0.2f;
        rb.angularVelocity = hp.transform.right * 2.5f;
        Destroy(root.gameObject, 2.5f);
        SupplyCrates.Notice($"국기 게양대({held.country})가 부러졌다!");
        held = null; phase = Phase.None; t = 0f; hasPrev = false;
    }

    struct Target { public Component c; public Vector3 pos; }

    IEnumerable<Target> Enemies()
    {
        foreach (var x in FindObjectsOfType<TankAI>()) if (!x.IsDead) yield return new Target { c = x, pos = x.transform.position + Vector3.up * 1.5f };
        foreach (var x in FindObjectsOfType<PoliceUnit>()) if (!x.IsDead) yield return new Target { c = x, pos = x.transform.position + Vector3.up * 1.2f };
        foreach (var x in FindObjectsOfType<HeliAI>()) if (!x.IsDead) yield return new Target { c = x, pos = x.transform.position };
        foreach (var x in FindObjectsOfType<JetAI>()) if (!x.IsDead) yield return new Target { c = x, pos = x.transform.position };
    }

    Vector3 Shoulder => hasShoulder && anim ? anim.transform.TransformPoint(shoulderLocal) : hp.transform.position + Vector3.up * H * 0.8f;
    static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

    // 게양대 선분 (아래 끝 → 꼭대기)
    void PoleSegment(out Vector3 a, out Vector3 b)
    {
        var tr = held.root.transform;
        a = tr.position; b = tr.position + tr.up * held.height;
    }

    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }

    // 이번 프레임에 게양대가 쓸고 지나간 자리(이전 선분 → 지금 선분)에 닿은 적을 부숨.
    // 게양대 아래에 있는 땅 위 적도 맞도록, 게양대가 지나간 방향(수평 각도)과 거리 안이면 높이는 넉넉히 봄
    void Hit()
    {
        PoleSegment(out Vector3 a, out Vector3 b);
        if (!hasPrev) { prevA = a; prevB = b; hasPrev = true; return; }
        float r = Mathf.Max(3f, H * hitRadiusRatio);
        Vector3 s = Shoulder;
        float yawPrev = YawOf(prevB - prevA), yawNow = YawOf(b - a);
        float sweep = Mathf.DeltaAngle(yawPrev, yawNow);
        float far = new Vector2(b.x - s.x, b.z - s.z).magnitude;
        float top = Mathf.Max(a.y, b.y);
        foreach (var e in Enemies())
        {
            if (hitThisSwing.Contains(e.c)) continue;
            bool hit = false;
            for (int k = 0; k <= 4 && !hit; k++)
            {
                float u = k / 4f;
                if (SegDist(e.pos, Vector3.Lerp(prevA, a, u), Vector3.Lerp(prevB, b, u)) < r) hit = true;
            }
            if (!hit) // 휘두르는 게양대 아래(땅 위)에 있는 적도 맞음
            {
                Vector3 d = e.pos - s; float flat = new Vector2(d.x, d.z).magnitude;
                float rel = Mathf.DeltaAngle(yawPrev, YawOf(d));
                bool crossed = sweep >= 0f ? rel >= -2f && rel <= sweep + 2f : rel <= 2f && rel >= sweep - 2f;
                hit = crossed && flat < far + r && e.pos.y < top + r;
            }
            if (!hit) continue;
            hitThisSwing.Add(e.c);
            Smash(e);
        }
        BreakBuildings(a, b, s, yawPrev, sweep, far);
        prevA = a; prevB = b;
    }

    // 휘두르는 게양대(손보다 바깥쪽 부분)에 닿은 건물을 부숨. 부술 수 없는 초고층도 게양대에 맞으면 무너짐
    readonly Collider[] buildingHits = new Collider[64];
    readonly HashSet<GameObject> brokenNow = new HashSet<GameObject>();
    void BreakBuildings(Vector3 a, Vector3 b, Vector3 s, float yawPrev, float sweep, float far)
    {
        var tall = TallBuildings.Instance;
        int buildingLayer = LayerMask.NameToLayer("Building");
        int mask = LayerMask.GetMask("Building") | (tall ? LayerMask.GetMask(tall.solidLayerName) : 0);
        float r = Mathf.Max(2f, H * buildingHitRatio);
        Vector3 push = FlatDir(b - prevB);
        brokenNow.Clear();
        for (int k = 0; k <= 2; k++)
        {
            float u = k / 2f;
            Vector3 pa = Vector3.Lerp(prevA, a, u), pb = Vector3.Lerp(prevB, b, u);
            Vector3 from = Vector3.Lerp(pa, pb, gripAt + 0.15f); // 손 근처는 빼고 바깥쪽만
            int n = Physics.OverlapCapsuleNonAlloc(from, pb, r, buildingHits, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = buildingHits[i];
                if (!c) continue;
                if (c.gameObject.layer == buildingLayer)
                {
                    Transform t = c.transform;
                    while (t.parent && t.parent.gameObject.layer == buildingLayer) t = t.parent; // 건물 본체
                    if (!t.gameObject.activeInHierarchy || !brokenNow.Add(t.gameObject)) continue;
                    BuildingDestruction.Break(t.gameObject, c.ClosestPoint(pb), push);
                }
                else if (tall)
                {
                    Transform t = tall.SolidRoot(c); // 땅·인도 등은 null
                    if (!t || !brokenNow.Add(t.gameObject)) continue;
                    tall.ForceBreak(t, c.ClosestPoint(pb), push);
                }
            }
        }
        // 게양대 아래(휘두르는 호 안, 게양대 끝이 닿는 거리 안)에 있는 낮은 건물도 함께 무너짐
        Vector3 sf = new Vector3(s.x, 0f, s.z);
        int m = Physics.OverlapSphereNonAlloc(sf, far + r, buildingHits, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < m; i++)
        {
            var c = buildingHits[i];
            if (!c) continue;
            Transform t = c.transform;
            while (t.parent && t.parent.gameObject.layer == buildingLayer) t = t.parent;
            if (!t.gameObject.activeInHierarchy || brokenNow.Contains(t.gameObject)) continue;
            Vector3 d = c.bounds.center - sf; d.y = 0f;
            float rel = Mathf.DeltaAngle(yawPrev, YawOf(d));
            bool crossed = sweep >= 0f ? rel >= -2f && rel <= sweep + 2f : rel <= 2f && rel >= sweep - 2f;
            if (!crossed || d.magnitude > far + r * 0.5f) continue;
            brokenNow.Add(t.gameObject);
            BuildingDestruction.Break(t.gameObject, c.ClosestPoint(sf + Vector3.up * c.bounds.max.y), push);
        }
        if (brokenNow.Count > 0 && GiantCamera.Instance) GiantCamera.Instance.Shake(0.25f);
    }

    void Smash(Target e)
    {
        switch (e.c)
        {
            case TankAI tank: tank.Crush(); break;
            case PoliceUnit pol:
                float rr = Mathf.Max(4f, H * 0.3f);
                pol.TakeGas(q => (q - e.pos).sqrMagnitude < rr * rr, 99999f); // 차 옆에 내려 있던 경찰도
                pol.Crush(); break;
            case HeliAI heli: heli.TakeDamage(99999f); break;
            case JetAI jet: jet.TakeDamage(99999f); break;
        }
        CombatFX.Explosion(e.pos, Mathf.Max(6f, H * 0.25f));
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.35f);
    }

    // ───────────── 손 IK (게양대를 집을 때만) ─────────────
    // GiantIKRelay가 매 프레임 호출 (GiantEat·HostageEvent 다음). 휘두르는 동안은 상체 레이어의 공격 동작이 팔을 움직임
    public void OnIK(Animator a)
    {
        if (phase != Phase.Reach || held == null || !held.root || !hp) return;
        // 서 있는 게양대를 손 높이쯤에서 붙잡음
        float w = Paused ? 0f : Smooth(t);
        Vector3 bp = held.root.transform.position;
        Vector3 hand = bp + Vector3.up * Mathf.Min(held.height * 0.5f, Shoulder.y - bp.y - armLen * 0.7f);
        Vector3 F = FlatDir(bp - hp.transform.position), R = Vector3.Cross(Vector3.up, F);
        float s = anim ? anim.transform.lossyScale.y : 1f;
        a.SetIKPositionWeight(AvatarIKGoal.RightHand, w);
        a.SetIKPosition(AvatarIKGoal.RightHand, hand);
        a.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        a.SetIKHintPositionWeight(AvatarIKHint.RightElbow, w * 0.6f);
        a.SetIKHintPosition(AvatarIKHint.RightElbow, Shoulder + R * 0.35f * s - Vector3.up * 0.25f * s - F * 0.1f * s);
    }

    static Vector3 FlatDir(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }

    // 오른손 손가락을 말아 주먹으로 쥠 (손가락 -Z 회전)
    static readonly HumanBodyBones[] Fingers =
    {
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
    };
    static readonly float[] FistAngles = { 75f, 85f, 55f };

    // 애니메이션·IK가 끝난 뒤: 주먹을 쥐고, 손(공격 동작의 칼 쥔 방향)에 게양대를 붙임
    void LateUpdate()
    {
        if (!hp || !anim) return;
        var handT = anim.GetBoneTransform(HumanBodyBones.RightHand);
        var upper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        var lower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        if (!handT || !upper || !lower) return;
        if (!hasShoulder || phase == Phase.None)
        {
            shoulderLocal = anim.transform.InverseTransformPoint(upper.position); hasShoulder = true;
            armLen = (lower.position - upper.position).magnitude + (handT.position - lower.position).magnitude;
        }
        if (held == null || !held.root || (phase != Phase.Lift && phase != Phase.Hold)) return;

        for (int i = 0; i < Fingers.Length; i++)
        {
            var b = anim.GetBoneTransform(Fingers[i]);
            if (b) b.localRotation = b.localRotation * Quaternion.Euler(0f, 0f, -FistAngles[i % 3]);
        }
        var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbIntermediate);
        if (thumb) thumb.rotation = Quaternion.AngleAxis(-35f, handT.forward) * thumb.rotation;

        // 게양대 방향 = 손의 엄지 쪽(+Z) (+ 보정). 쥔 손 안(손바닥 가운데에서 손바닥 쪽으로 조금)을 지나감
        Quaternion axis = handT.rotation * Quaternion.Euler(poleAxisEuler);
        Vector3 dir = axis * Vector3.forward;
        var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        Vector3 palm = mid ? Vector3.Lerp(handT.position, mid.position, 0.6f) : handT.position;
        float handLen = mid ? (mid.position - handT.position).magnitude : armLen * 0.12f;
        Vector3 grip = palm - handT.up * handLen * 0.35f;
        Quaternion rot = Quaternion.LookRotation(axis * Vector3.right, dir);
        Vector3 pos = grip - dir * held.height * gripAt;
        if (phase == Phase.Lift)
        {
            float k = Smooth(t);
            pos = Vector3.Lerp(liftFromPos, pos, k); rot = Quaternion.Slerp(liftFromRot, rot, k);
        }
        held.root.transform.SetPositionAndRotation(pos, rot);
    }
}
