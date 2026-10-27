using System.Collections.Generic;
using UnityEngine;

// 새장 스킬: 거인의 집 상점에서 사면 거인이 왼손에 새장을 들고 다님 (강화하면 수용 인원 증가).
// 적을 죽이면 새장에 갇혀 거인 편이 되고, 새장 안에서 가까운 적을 향해 총을 쏨. 포로가 죽인 적도 다시 갇힘.
// 탈것은 탑승자가 갇힘: 경찰차 = 차 안에 있던 경찰, 탱크·헬기·전투기 = 승무원(군인). 새장이 꽉 차면 더 가두지 못함.
// 새장은 바닥판 + 기둥 + 고리로 만들고 위는 열어 둠 (위에서 내려다보는 카메라에 포로가 보이게).
// 세이브(SaveGame) 시 레벨과 포로 목록을 저장.
public class CageSkill : MonoBehaviour
{
    public static CageSkill Instance { get; private set; }
    public static bool HoldingCage => Instance && Instance.Level > 0;

    public enum Kind { Police, Soldier }

    [Header("새장 모양 (거인 크기 기준)")]
    public float cageHeight = 0.2f;
    public float cageRadius = 0.11f;
    [Tooltip("손에서 새장 윗고리까지")] public float hangOffset = 0.06f;
    [Tooltip("몸에 겹치지 않게 바깥(왼쪽)으로 띄우는 거리")] public float sideOffset = 0.05f;
    public int bars = 14;
    public float barThickness = 0.004f;
    public Color cageColor = new Color(0.85f, 0.66f, 0.25f);

    [Header("수용 / 강화 (상점)")]
    public int baseCapacity = 3;
    public int capacityPerLevel = 2;
    public int maxLevel = 5;
    [Tooltip("첫 구매 가격")] public int price = 0;
    [Tooltip("강화할 때마다 가격이 오르는 비율 (0.5 = 레벨마다 +50%)")] public float priceGrowth = 0.5f;

    [Header("탑승자 수 (탈것을 부쉈을 때 갇히는 인원)")]
    public int tankCrew = 2;
    public int heliCrew = 2;
    public int jetCrew = 1;

    [Header("포로 사격")]
    [Tooltip("사격 거리 (m)")] public float range = 260f;
    public float fireInterval = 1f;
    public float policeDamage = 3f;
    public float soldierDamage = 5f;
    [Range(0, 1)] public float accuracy = 0.75f;
    [Tooltip("포로 캐릭터 크기")] public float prisonerScale = 0.9f;
    public Color policeColor = new Color(0.35f, 0.45f, 1f);
    public Color soldierColor = new Color(0.45f, 0.55f, 0.3f);

    public int Level { get; private set; }
    public int Capacity => Level <= 0 ? 0 : baseCapacity + capacityPerLevel * (Level - 1);
    public int Count => prisoners.Count;
    public bool Maxed => Level >= maxLevel;
    public int Price => Mathf.RoundToInt(price * (1f + priceGrowth * Level));

    class Prisoner
    {
        public Kind kind;
        public Transform t;
        public float nextShot;
        public Vector3 aimDir; // 마지막으로 쏜 방향 (0이면 거인 정면)
    }
    readonly List<Prisoner> prisoners = new List<Prisoner>();
    Transform cage, crew;
    GiantController giant;
    Material cageMat;
    Mesh cylinder;
    string toast; float toastUntil, pulse;
    static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake() { Instance = this; giant = GetComponent<GiantController>(); }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // ───────────── 상점 ─────────────
    // 돈을 내고 구매(첫 구매) 또는 강화. 성공하면 true
    public bool Buy()
    {
        if (Maxed || !Money.TrySpend(Price)) return false;
        Level++;
        return true;
    }

    // ───────────── 가두기 ─────────────
    public static int TankCrew => Instance ? Instance.tankCrew : 0;
    public static int HeliCrew => Instance ? Instance.heliCrew : 0;
    public static int JetCrew => Instance ? Instance.jetCrew : 0;

    // 적을 죽였을 때 호출: count명을 새장에 가둠 (자리가 없으면 남는 인원은 놓침). 실제로 가둔 수를 돌려줌
    public static int Imprison(Kind kind, int count)
    {
        var c = Instance;
        if (!c || c.Level <= 0 || count <= 0) return 0;
        int n = 0;
        while (n < count && c.prisoners.Count < c.Capacity) { c.AddPrisoner(kind); n++; }
        if (n > 0)
        {
            c.pulse = 1f;
            c.toast = $"+{n} {(kind == Kind.Police ? "경찰" : "군인")} 포로";
            c.toastUntil = Time.unscaledTime + 1.6f;
        }
        return n;
    }

    void AddPrisoner(Kind kind)
    {
        EnsureCage();
        var p = new Prisoner { kind = kind, nextShot = Time.time + Random.Range(0.5f, 1.5f) };
        var cm = CivilianManager.Instance ? CivilianManager.Instance : FindObjectOfType<CivilianManager>();
        if (cm && cm.prefabs != null && cm.prefabs.Length > 0)
        {
            var go = Instantiate(cm.prefabs[Random.Range(0, cm.prefabs.Length)], crew);
            go.name = kind == Kind.Police ? "CagedPolice" : "CagedSoldier";
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            var civ = go.GetComponent<Civilian>(); if (civ) Destroy(civ);
            go.transform.localScale = Vector3.one * prisonerScale;
            var anim = go.GetComponentInChildren<Animator>();
            if (anim)
            {
                anim.runtimeAnimatorController = cm.locomotion;
                anim.applyRootMotion = false;
                anim.SetFloat(SpeedHash, 0f);
            }
            var mpb = new MaterialPropertyBlock();
            Color tint = kind == Kind.Police ? policeColor : soldierColor;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.GetPropertyBlock(mpb); mpb.SetColor("_Color", tint); r.SetPropertyBlock(mpb); }
            p.t = go.transform;
        }
        prisoners.Add(p);
    }

    // ───────────── 포로 사격 ─────────────
    void Update()
    {
        pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime * 2f);
        if (Level <= 0 || prisoners.Count == 0 || GameStartMenu.InMenu) return;
        var ms = MilitarySpawner.Instance;
        if (!ms) return;
        float now = Time.time;
        // 쏜 적이 죽으면 그 자리에서 새 포로가 들어올 수 있어서 처음 수만큼만 돎
        for (int i = 0, n = prisoners.Count; i < n; i++)
        {
            var p = prisoners[i];
            if (!p.t || now < p.nextShot) continue;
            Vector3 origin = p.t.position + Vector3.up * 1.3f * prisonerScale;
            if (!ms.FindTarget(origin, range, out Vector3 aim, out var hit)) { p.nextShot = now + 0.5f; continue; }
            p.nextShot = now + fireInterval * Random.Range(0.8f, 1.25f);
            bool hitIt = Random.value < accuracy;
            Vector3 end = hitIt ? aim : aim + Random.insideUnitSphere * 6f;
            Vector3 dir = end - origin;
            p.aimDir = new Vector3(dir.x, 0f, dir.z);
            CombatFX.Muzzle(origin, dir.normalized, 0.6f);
            PoliceUnit.Tracer(origin, end);
            if (hitIt) hit(p.kind == Kind.Soldier ? soldierDamage : policeDamage);
        }
    }

    // ───────────── 새장 위치 (애니메이션 뒤, 왼손에 매달림) ─────────────
    void LateUpdate()
    {
        bool show = Level > 0 && giant && !GameStartMenu.InMenu;
        if (cage) cage.gameObject.SetActive(show);
        if (crew) crew.gameObject.SetActive(show);
        if (!show) return;
        EnsureCage();

        float gs = giant.GameScale;
        var hp = GiantHealth.Instance;
        var hand = hp ? hp.Bone(HumanBodyBones.LeftHand) : null;
        Vector3 fwd = giant.transform.forward; fwd.y = 0f;
        Quaternion rot = Quaternion.LookRotation(fwd.sqrMagnitude > 0.01f ? fwd : Vector3.forward);
        Vector3 grip = hand ? hand.position : giant.transform.position + Vector3.up * gs * 0.75f;
        // 손잡이를 쥔 채 몸 바깥쪽으로 살짝 들고 있음 (기울지 않고 똑바로 매달림)
        Vector3 top = grip - giant.transform.right * sideOffset * gs;
        cage.SetPositionAndRotation(top, rot);
        cage.localScale = Vector3.one * gs;

        Vector3 floor = top + Vector3.down * (hangOffset + cageHeight - barThickness) * gs;
        crew.SetPositionAndRotation(floor, rot);
        float r = cageRadius * gs * 0.72f;
        for (int i = 0; i < prisoners.Count; i++)
        {
            var p = prisoners[i];
            if (!p.t) continue;
            // 해바라기 배치: 인원이 늘어도 바닥에 고르게 퍼짐
            float d = r * Mathf.Sqrt((i + 0.5f) / Mathf.Max(1, Capacity));
            float a = i * 137.5f * Mathf.Deg2Rad;
            p.t.localPosition = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            p.t.rotation = p.aimDir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(p.aimDir) : rot;
        }
    }

    // 새장 모델: 원점 = 윗고리(손잡이). 아래로 고리 → 손잡이 살 → 윗테 → 기둥 → 아랫테 + 바닥판
    void EnsureCage()
    {
        if (cage) return;
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);
        var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
        cageMat = new Material(shader) { color = cageColor };
        if (cageMat.HasProperty("_Glossiness")) cageMat.SetFloat("_Glossiness", 0.6f);
        if (cageMat.HasProperty("_Metallic")) cageMat.SetFloat("_Metallic", 0.5f);

        cage = new GameObject("Cage").transform;
        crew = new GameObject("CagePrisoners").transform;
        float R = cageRadius, H = cageHeight, y0 = -hangOffset, y1 = -hangOffset - H, t = barThickness;

        // 윗테 · 아랫테
        Ring(y0, R, t);
        Ring(y1 + t, R, t * 1.5f);
        // 기둥
        for (int i = 0; i < bars; i++)
        {
            float a = i * Mathf.PI * 2f / bars;
            Vector3 o = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R);
            Bar(o + Vector3.up * y0, o + Vector3.up * y1, t);
        }
        // 손잡이: 윗테에서 고리로 모이는 살 4개 + 고리
        for (int i = 0; i < 4; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 0.5f;
            Bar(new Vector3(Mathf.Cos(a) * R, y0, Mathf.Sin(a) * R), Vector3.up * -t * 4f, t);
        }
        Piece("Hook", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(t * 8f, t, t * 8f));
        // 바닥판
        Piece("Floor", new Vector3(0f, y1, 0f), Quaternion.identity, new Vector3(R * 2.05f, t, R * 2.05f));
    }

    void Ring(float y, float r, float t)
    {
        const int seg = 24;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Bar(new Vector3(Mathf.Cos(a0) * r, y, Mathf.Sin(a0) * r), new Vector3(Mathf.Cos(a1) * r, y, Mathf.Sin(a1) * r), t);
        }
    }

    // a에서 b까지 굵기 t의 원기둥 (Unity 원기둥은 높이 2라 y 스케일 = 길이 / 2)
    void Bar(Vector3 a, Vector3 b, float t)
    {
        Vector3 d = b - a;
        Piece("Bar", (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d), new Vector3(t, d.magnitude * 0.5f + t * 0.5f, t));
    }

    Transform Piece(string name, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(cage, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = cylinder;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = cageMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // ───────────── 세이브 (SaveGame) ─────────────
    [System.Serializable]
    public class State
    {
        public int level;
        public int[] prisoners; // Kind
    }

    public State Capture()
    {
        var s = new State { level = Level, prisoners = new int[prisoners.Count] };
        for (int i = 0; i < prisoners.Count; i++) s.prisoners[i] = (int)prisoners[i].kind;
        return s;
    }

    public void Restore(State s)
    {
        foreach (var p in prisoners) if (p.t) Destroy(p.t.gameObject);
        prisoners.Clear();
        Level = Mathf.Clamp(s.level, 0, maxLevel);
        if (Level <= 0 || s.prisoners == null) return;
        foreach (int k in s.prisoners) if (prisoners.Count < Capacity) AddPrisoner((Kind)k);
    }

    // ───────────── 화면 (돈 아래) ─────────────
    void OnGUI()
    {
        if (Level <= 0 || GameStartMenu.InMenu || GameStartMenu.HideHud) return;
        var r = new Rect(Screen.width - 260, 50, 240, 26);
        bool full = prisoners.Count >= Capacity;
        CardDraft.ShadowLabel(r, $"새장  {prisoners.Count} / {Capacity}", CardDraft.Style(Mathf.RoundToInt(18 + pulse * 4), FontStyle.Bold, TextAnchor.MiddleRight),
            full ? new Color(1f, 0.55f, 0.4f) : Color.Lerp(cageColor, Color.white, pulse));
        if (Time.unscaledTime < toastUntil)
            CardDraft.ShadowLabel(new Rect(r.x, r.yMax, r.width, 22), toast, CardDraft.Style(14, FontStyle.Bold, TextAnchor.MiddleRight),
                new Color(1f, 0.9f, 0.5f, Mathf.Clamp01((toastUntil - Time.unscaledTime) / 0.4f)));
    }
}
