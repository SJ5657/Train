using System.Collections.Generic;
using UnityEngine;

// 거인이 부순 건물 수에 따라 경찰 → 군대(탱크/헬기/전투기)를 점점 더 많이 투입.
// 처음엔 경찰차가 나오고, firstTankAt 채를 부수면 탱크 1대 → 부술수록 증원. 파괴된 유닛도 목표 수량까지 보충.
// 거인이 집 마당(안전지대)에 들어가면 남아 있는 적은 모두 해산(물러나서 화면 밖에서 사라짐)하고 새로 투입하지 않음.
// 투입 수량은 부순 건물 수로만 정해지므로, 마당을 나오면 같은 난이도로 다시 보충됨.
public class MilitarySpawner : MonoBehaviour
{
    public GameObject tankPrefab;
    [Tooltip("중전차 (Tank 7): 일반 탱크보다 이동·포탄 속도가 빠름")] public GameObject heavyTankPrefab;
    [Tooltip("이만큼 부순 뒤부터 중전차가 나옴 (일반 탱크가 먼저 나오는 firstTankAt보다 커야 함)")] public int firstHeavyTankAt = 160;
    [Tooltip("중전차가 나올 수 있게 된 뒤, 탱크를 투입할 때 중전차가 나올 확률")] [Range(0f, 1f)] public float heavyTankChance = 0.4f;
    public GameObject[] heliPrefabs;
    [Tooltip("heliPrefabs 중 늦게 등장하는 헬기 (Helicopter 1). 이 헬기는 firstLateHeliAt만큼 부순 뒤부터만 나옴")] public GameObject lateHeliPrefab;
    [Tooltip("이만큼 부순 뒤부터 늦게 나오는 헬기도 섞여 나옴")] public int firstLateHeliAt = 250;
    public GameObject[] jetPrefabs;
    public CityGenerator city;

    [Header("경찰 (게임 초반부터)")]
    public GameObject policeCarPrefab;
    [Tooltip("경찰차 모델 크기 배율 (시민 크기에 맞춤)")] public float policeCarScale = 2f;
    [Tooltip("처음부터 나오는 경찰차 수")] public int startPolice = 1;
    [Tooltip("이만큼 더 부술 때마다 경찰차 +1")] public int buildingsPerExtraPolice = 15;
    public int maxPolice = 4;
    public float policeSpawnDistance = 380f;

    [Header("탱크 등장 조건")]
    public int firstTankAt = 40;          // 이만큼 부수면 첫 탱크 (그 전엔 경찰만)
    public int buildingsPerExtraTank = 40; // 이만큼 더 부술 때마다 +1
    public int maxTanks = 10;

    [Header("헬기 등장 조건")]
    public int firstHeliAt = 60;
    public int buildingsPerExtraHeli = 60;
    public int maxHelis = 6;

    [Header("전투기 등장 조건")]
    public int firstJetAt = 250;
    public int buildingsPerExtraJet = 120;
    public int maxJets = 4;

    [Header("레벨업 보너스 (레벨이 오를수록 더 많이 등장)")]
    [Tooltip("레벨이 1 오를 때마다 늘어나는 수 (소수는 누적해서 정수가 될 때마다 +1). 최대 수도 같은 만큼 늘어남")]
    public float policePerLevel = 0.15f;
    public float tanksPerLevel = 0.25f;
    public float helisPerLevel = 0.15f;
    public float jetsPerLevel = 0.08f;

    [Header("스폰")]
    public float tankSpawnDistance = 420f;
    public float heliSpawnDistance = 500f;
    public float spawnInterval = 1.5f;     // 한 대씩 순차 투입 간격

    readonly List<TankAI> tanks = new List<TankAI>();
    readonly List<HeliAI> helis = new List<HeliAI>();
    readonly List<JetAI> jets = new List<JetAI>();
    readonly List<PoliceUnit> police = new List<PoliceUnit>();
    int lastWantedJ;
    public float jetSpawnDistance = 1300f;
    float nextSpawn;
    int lastWantedT, lastWantedH;
    string banner; float bannerUntil;
    bool wasSafe;

    public static MilitarySpawner Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        if (!city) city = FindObjectOfType<CityGenerator>();
        int g = LayerMask.NameToLayer("Giant"), m = LayerMask.NameToLayer("Military");
        if (g >= 0 && m >= 0) Physics.IgnoreLayerCollision(g, m, true);
    }

    int Destroyed => BuildingDestruction.Instance ? BuildingDestruction.Instance.DestroyedCount : 0;
    // 레벨업 보너스: (레벨 - 1) × 레벨당 증가량. 종류별 등장 조건(부순 건물 수)을 넘긴 뒤부터 적용
    int LevelBonus(float perLevel) => Mathf.FloorToInt((LevelSystem.Instance ? LevelSystem.Instance.Level - 1 : 0) * perLevel);
    public int WantedTanks => Destroyed < firstTankAt ? 0 : Mathf.Min(maxTanks, 1 + (Destroyed - firstTankAt) / buildingsPerExtraTank) + LevelBonus(tanksPerLevel);
    public int WantedHelis => Destroyed < firstHeliAt ? 0 : Mathf.Min(maxHelis, 1 + (Destroyed - firstHeliAt) / buildingsPerExtraHeli) + LevelBonus(helisPerLevel);
    public int WantedJets => Destroyed < firstJetAt ? 0 : Mathf.Min(maxJets, 1 + (Destroyed - firstJetAt) / buildingsPerExtraJet) + LevelBonus(jetsPerLevel);
    public int WantedPolice => Mathf.Min(maxPolice, startPolice + Destroyed / Mathf.Max(1, buildingsPerExtraPolice)) + LevelBonus(policePerLevel);

    void Update()
    {
        var g = GiantHealth.Instance;
        if (!g || g.IsDead || EnemyFreeze.Active || Time.time < retreatUntil) return; // 보급상자 정지·인질 구출 후퇴 중에는 새 적도 투입하지 않음
        tanks.RemoveAll(t => !t || t.IsDead);
        helis.RemoveAll(h => !h || h.IsDead);
        jets.RemoveAll(j => !j || j.IsDead);
        police.RemoveAll(p => !p || p.IsDead);

        // 거인의 집 안: 남아 있는 적 해산 + 투입 중지
        if (GiantHome.GiantSafe)
        {
            if (!wasSafe) DisperseAll();
            wasSafe = true;
            return;
        }
        wasSafe = false;

        int wj = WantedJets;
        if (wj > lastWantedJ) lastWantedJ = wj;

        int wt = WantedTanks, wh = WantedHelis;
        if (wt > lastWantedT || wh > lastWantedH)
        {
            lastWantedT = wt; lastWantedH = wh;
        }

        if (Time.time < nextSpawn) return;
        if (police.Count < WantedPolice && policeCarPrefab) { SpawnPolice(g); nextSpawn = Time.time + spawnInterval; }
        else if (tanks.Count < wt) { SpawnTank(g); nextSpawn = Time.time + spawnInterval; }
        else if (helis.Count < wh) { SpawnHeli(g); nextSpawn = Time.time + spawnInterval; }
        else if (jets.Count < wj) { SpawnJet(g); nextSpawn = Time.time + spawnInterval * 2f; }
    }

    // 거인 편(새장 포로, CageSkill)이 쏠 적 찾기: from에서 range 안의 가장 가까운 적과 그 적에게 피해를 주는 함수.
    // 해산 중인 적은 목록에서 빠져 있으므로 쏘지 않음
    public bool FindTarget(Vector3 from, float range, out Vector3 aim, out System.Action<float> hit)
    {
        float best = range * range;
        Vector3 bestAim = default; System.Action<float> bestHit = null;
        void Consider(Vector3 p, System.Action<float> h)
        {
            float d = (p - from).sqrMagnitude;
            if (d < best) { best = d; bestAim = p; bestHit = h; }
        }
        foreach (var p in police) if (p && !p.IsDead) { Consider(p.transform.position + Vector3.up * 1.5f, p.TakeDamage); p.VisitOfficers(Consider); }
        foreach (var t in tanks) if (t && !t.IsDead) Consider(t.transform.position + Vector3.up * 2f, t.TakeDamage);
        foreach (var h in helis) if (h && !h.IsDead) Consider(h.transform.position, h.TakeDamage);
        foreach (var j in jets) if (j && !j.IsDead) Consider(j.transform.position, j.TakeDamage);
        aim = bestAim; hit = bestHit;
        return bestHit != null;
    }

    // 인질 구출 보상: 남은 적을 모두 물러나게 하고 seconds초 동안 새 적도 투입하지 않음
    float retreatUntil;
    public static float RetreatLeft => Instance ? Mathf.Max(0f, Instance.retreatUntil - Time.time) : 0f;
    public static void Retreat(float seconds)
    {
        if (!Instance) return;
        Instance.DisperseAll();
        Instance.retreatUntil = Time.time + seconds;
    }

    // 남아 있는 적을 모두 해산시키고 목록에서 뺌 (해산 중인 유닛은 수량에 세지 않음)
    void DisperseAll()
    {
        foreach (var t in tanks) t.Disperse();
        foreach (var h in helis) h.Disperse();
        foreach (var j in jets) j.Disperse();
        foreach (var p in police) p.Disperse();
        tanks.Clear(); helis.Clear(); jets.Clear(); police.Clear();
    }

    // 해산 중인 유닛이 사라져도 되는지: 카메라 화면 밖
    public static bool OutOfSight(Vector3 p)
    {
        var cam = Camera.main;
        if (!cam) return true;
        Vector3 v = cam.WorldToViewportPoint(p);
        return v.z < 0f || v.x < -0.1f || v.x > 1.1f || v.y < -0.1f || v.y > 1.1f;
    }

    [Tooltip("화면 중앙 경고 문구(폭격 경고 등) 표시 여부")] public bool showBanners = false;
    public void ShowBanner(string text) { if (!showBanners) return; banner = text; bannerUntil = Time.time + 3f; }

    void SpawnTank(GiantHealth g)
    {
        if (!tankPrefab) return;
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;

        // 거인에게서 일정 거리 떨어진 도로 교차로
        Vector3 pos = g.transform.position;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 cand = g.transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * tankSpawnDistance;
            int i = Mathf.Clamp(Mathf.RoundToInt((cand.x - o.x) / pitch), 0, city.blocksX);
            int j = Mathf.Clamp(Mathf.RoundToInt((cand.z - o.z) / pitch), 0, city.blocksZ);
            pos = new Vector3(o.x + i * pitch, 0, o.z + j * pitch);
            Vector3 d = pos - g.transform.position; d.y = 0;
            if (d.magnitude > tankSpawnDistance * 0.6f) break;
        }
        Vector3 look = g.transform.position - pos; look.y = 0;
        bool heavy = heavyTankPrefab && Destroyed >= firstHeavyTankAt && Random.value < heavyTankChance;
        var go = Instantiate(heavy ? heavyTankPrefab : tankPrefab, pos, Quaternion.LookRotation(look.sqrMagnitude > 1 ? look : Vector3.forward), transform);
        go.name = heavy ? "HeavyTank" : "Tank";
        var ai = go.GetComponent<TankAI>();
        ai.city = city;
        tanks.Add(ai);
    }

    // 경찰차: 거인에게서 일정 거리 떨어진 도로 교차로에 생성 (모델은 시민 크기에 맞춰 키움)
    void SpawnPolice(GiantHealth g)
    {
        float pitch = city.blockSize + city.roadWidth;
        float sizeX = city.blocksX * pitch + city.roadWidth, sizeZ = city.blocksZ * pitch + city.roadWidth;
        Vector3 o = city.transform.position - new Vector3(sizeX, 0, sizeZ) * 0.5f + new Vector3(city.roadWidth, 0, city.roadWidth) * 0.5f;
        Vector3 pos = g.transform.position;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 cand = g.transform.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * policeSpawnDistance;
            int i = Mathf.Clamp(Mathf.RoundToInt((cand.x - o.x) / pitch), 0, city.blocksX);
            int j = Mathf.Clamp(Mathf.RoundToInt((cand.z - o.z) / pitch), 0, city.blocksZ);
            pos = new Vector3(o.x + i * pitch, 0, o.z + j * pitch);
            Vector3 d = pos - g.transform.position; d.y = 0;
            if (d.magnitude > policeSpawnDistance * 0.6f) break;
        }
        Vector3 look = g.transform.position - pos; look.y = 0;
        var root = new GameObject("PoliceCar");
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(pos, Quaternion.identity); // 충돌 박스를 정확히 재려고 회전은 나중에
        var model = Instantiate(policeCarPrefab, root.transform);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one * policeCarScale;
        // 거인 발에 밟히도록 Military 레이어 + 충돌 박스
        int layer = LayerMask.NameToLayer("Military");
        foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.layer = layer;
        var rs = root.GetComponentsInChildren<Renderer>();
        Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(pos, Vector3.one * 5f);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x, b.size.y, b.size.z);
        root.transform.rotation = Quaternion.LookRotation(look.sqrMagnitude > 1 ? look : Vector3.forward);
        var ai = root.AddComponent<PoliceUnit>();
        ai.city = city;
        var cm = CivilianManager.Instance ? CivilianManager.Instance : FindObjectOfType<CivilianManager>();
        if (cm) { ai.officerPrefabs = cm.prefabs; ai.officerController = cm.locomotion; ai.officerScale = cm.scale; }
        police.Add(ai);
    }

    // 헬기 모델 고르기: 늦게 나오는 헬기(lateHeliPrefab)는 firstLateHeliAt 이후에만 후보에 넣음
    GameObject PickHeliPrefab()
    {
        var pool = new List<GameObject>();
        foreach (var h in heliPrefabs)
            if (h && (h != lateHeliPrefab || Destroyed >= firstLateHeliAt)) pool.Add(h);
        if (pool.Count == 0) pool.AddRange(heliPrefabs); // 늦은 헬기만 등록돼 있어도 아예 안 나오지는 않게
        return pool[Random.Range(0, pool.Count)];
    }

    void SpawnHeli(GiantHealth g)
    {
        if (heliPrefabs == null || heliPrefabs.Length == 0) return;
        float a = Random.Range(0f, 360f);
        Vector3 dir = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad));
        Vector3 pos = g.transform.position + dir * heliSpawnDistance + Vector3.up * g.ShoulderY * 0.9f;
        var go = Instantiate(PickHeliPrefab(), pos, Quaternion.LookRotation(-dir), transform);
        go.name = "Helicopter";
        var ai = go.GetComponent<HeliAI>();
        ai.startAngle = a;
        ai.direction = Random.value < 0.5f ? 1f : -1f;
        ai.orbitRadius = Random.Range(130f, 200f);
        ai.altitude = g.ShoulderY * Random.Range(0.65f, 1f); // 어깨 높이 이하
        ai.angularSpeed = Random.Range(9f, 15f); // 실제 선회 속도는 HeliAI가 moveSpeedMultiplier를 곱함
        helis.Add(ai);
    }

    void SpawnJet(GiantHealth g)
    {
        if (jetPrefabs == null || jetPrefabs.Length == 0) return;
        float a = Random.Range(0f, Mathf.PI * 2f);
        Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        Vector3 pos = g.transform.position + dir * jetSpawnDistance + Vector3.up * (g.ShoulderY + 60f);
        var go = Instantiate(jetPrefabs[Random.Range(0, jetPrefabs.Length)], pos, Quaternion.LookRotation(-dir), transform);
        go.name = "Jet";
        jets.Add(go.GetComponent<JetAI>());
    }

    void OnGUI()
    {
        if (GameStartMenu.InMenu) return; // 시작 메뉴 중에는 HUD 숨김
        if (Time.time < bannerUntil && !string.IsNullOrEmpty(banner))
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float blink = Mathf.PingPong(Time.time * 3f, 1f);
            GUI.color = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(2, Screen.height * 0.22f + 2, Screen.width, 50), banner, big);
            GUI.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.3f), blink);
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 50), banner, big);
            GUI.color = Color.white;
        }
    }
}
