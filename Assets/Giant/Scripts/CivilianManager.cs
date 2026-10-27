using System.Collections.Generic;
using UnityEngine;

// 시민(로우폴리 캐릭터) 관리: 거인 주변 블록의 인도 위에 시민을 채워 넣고, 멀어지면 정리.
// 시민 각자의 행동(인도를 따라 걷기 / 거인이 가까이 오면 도망 / 밟히면 납작)은 Civilian이 처리.
public class CivilianManager : MonoBehaviour
{
    public static CivilianManager Instance { get; private set; }

    [Header("연결")]
    public GameObject[] prefabs;                       // 로우폴리 캐릭터 프리팹들
    public RuntimeAnimatorController locomotion;       // Speed(0 정지 / 0.5 걷기 / 1 달리기) 파라미터가 있는 컨트롤러
    public CityGenerator city;

    [Header("수량 / 범위")]
    [Tooltip("거인 주변에 유지할 시민 수")] public int maxCivilians = 160;
    [Tooltip("거인으로부터 이 거리 범위의 인도에 생김 (m)")] public Vector2 spawnDistance = new Vector2(80f, 380f);
    [Tooltip("거인과 이만큼 멀어지면 사라짐 (m)")] public float despawnDistance = 600f;

    [Header("시민")]
    [Tooltip("캐릭터 크기 배율")] public float scale = 1.2f;
    [Tooltip("걷기 속도 (m/s, 크기 1 기준)")] public float walkSpeed = 2.8f;
    [Tooltip("도망 속도 (m/s, 크기 1 기준)")] public float runSpeed = 11f;
    [Tooltip("거인이 이 거리 안으로 오면 도망 (거인 크기 기준)")] public float sightRadius = 2.5f;
    [Tooltip("이만큼 멀어지면 진정하고 인도로 돌아감 (거인 크기 기준)")] public float calmRadius = 5f;
    [Tooltip("밟히는 반경 (거인 크기 기준)")] public float squashRadius = 0.3f;
    [Tooltip("시민을 밟았을 때 얻는 점수(=경험치)")] public int stompScore = 10;

    readonly List<Civilian> civilians = new List<Civilian>();
    GiantController giant;
    int buildingMask, groundMask;
    Vector3 origin, cityMax;
    float pitch;

    public GiantController Giant => giant;
    public IReadOnlyList<Civilian> Civilians => civilians;

    void Awake()
    {
        Instance = this;
        buildingMask = LayerMask.GetMask("Building");
        groundMask = LayerMask.GetMask("Default");
    }

    void Start()
    {
        if (!city) city = FindObjectOfType<CityGenerator>();
        giant = FindObjectOfType<GiantController>();
        if (city)
        {
            pitch = city.blockSize + city.roadWidth;
            float sx = city.blocksX * pitch + city.roadWidth, sz = city.blocksZ * pitch + city.roadWidth;
            origin = city.transform.position - new Vector3(sx, 0, sz) * 0.5f;
            cityMax = origin + new Vector3(sx, 0, sz);
        }
    }

    void Update()
    {
        if (!giant || prefabs == null || prefabs.Length == 0 || !city) return;
        Vector3 gp = giant.transform.position;

        for (int i = civilians.Count - 1; i >= 0; i--)
        {
            var c = civilians[i];
            if (!c) { civilians.RemoveAt(i); continue; }
            Vector3 d = c.transform.position - gp; d.y = 0f;
            if (d.sqrMagnitude > despawnDistance * despawnDistance) { Destroy(c.gameObject); civilians.RemoveAt(i); }
        }

        // 모자란 만큼 한 프레임에 조금씩 채움
        for (int k = 0; k < 4 && civilians.Count < maxCivilians; k++)
            TrySpawn(gp);
    }

    void TrySpawn(Vector3 center)
    {
        float ang = Random.Range(0f, Mathf.PI * 2f), dist = Random.Range(spawnDistance.x, spawnDistance.y);
        Vector3 p = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
        if (!InsideCity(p)) return;
        Vector2Int block = BlockAt(p);
        Vector3 pos = NearestOnWalkway(block, p, out int edge);

        var prefab = prefabs[Random.Range(0, prefabs.Length)];
        var go = Instantiate(prefab, pos, Quaternion.identity, transform);
        go.name = "Civilian";
        go.transform.localScale = Vector3.one * scale;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim)
        {
            anim.runtimeAnimatorController = locomotion;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        }
        var c = go.AddComponent<Civilian>();
        c.Init(this, anim, block, edge);
        civilians.Add(c);
    }

    // ───────────── 블록 / 인도 ─────────────
    public bool InsideCity(Vector3 p) => p.x > origin.x && p.x < cityMax.x && p.z > origin.z && p.z < cityMax.z;

    // 그 지점에서 가장 가까운 블록
    public Vector2Int BlockAt(Vector3 p)
    {
        int bx = Mathf.Clamp(Mathf.FloorToInt((p.x - origin.x - city.roadWidth * 0.5f) / pitch), 0, city.blocksX - 1);
        int bz = Mathf.Clamp(Mathf.FloorToInt((p.z - origin.z - city.roadWidth * 0.5f) / pitch), 0, city.blocksZ - 1);
        return new Vector2Int(bx, bz);
    }

    // 블록 인도의 한가운데를 잇는 사각 경로의 모서리 (0: -x-z, 1: +x-z, 2: +x+z, 3: -x+z → 반시계)
    public Vector3 Corner(Vector2Int b, int i)
    {
        float inset = city.walkwayWidth * 0.5f;
        float x0 = origin.x + city.roadWidth + b.x * pitch + inset, z0 = origin.z + city.roadWidth + b.y * pitch + inset;
        float x1 = x0 + city.blockSize - inset * 2f, z1 = z0 + city.blockSize - inset * 2f;
        float y = city.transform.position.y + city.walkwayHeight;
        switch (((i % 4) + 4) % 4)
        {
            case 0: return new Vector3(x0, y, z0);
            case 1: return new Vector3(x1, y, z0);
            case 2: return new Vector3(x1, y, z1);
            default: return new Vector3(x0, y, z1);
        }
    }

    // 인도 경로 위에서 p와 가장 가까운 점. edge = 그 점이 있는 변 (모서리 edge → edge+1 사이)
    public Vector3 NearestOnWalkway(Vector2Int b, Vector3 p, out int edge)
    {
        edge = 0;
        Vector3 best = Corner(b, 0);
        float bestD = float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = Corner(b, i), c = Corner(b, i + 1);
            Vector3 ab = c - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            Vector3 q = a + ab * t;
            Vector3 d = q - p; d.y = 0f;
            if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = q; edge = i; }
        }
        return best;
    }

    // 건물에 막히지 않는 곳인지 (도망칠 때 사용, r = 확인 반경)
    public bool Free(Vector3 p, float r = 1.2f)
        => InsideCity(p) && !Physics.CheckSphere(new Vector3(p.x, 2f, p.z), r, buildingMask, QueryTriggerInteraction.Ignore);

    // 그 지점의 바닥 높이 (도로 0, 블록 0.3, 인도 0.45)
    public float GroundY(Vector3 p)
        => Physics.Raycast(new Vector3(p.x, 5f, p.z), Vector3.down, out RaycastHit hit, 10f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
}
