using System.Collections.Generic;
using UnityEngine;

// 특수 건물(공장 등) 공통: 흰 상자 건물을 지정한 건물 모델로 바꾸고(없으면 색만 칠함) 목록으로 관리.
// 게임 시작 시 도시 전체에 ratio 비율만큼 깔고, 패시브 아이템이 추가 등장 확률(Chance)을 정하면
// 거인 주변에 들어온 건물마다 그 확률로 이 건물로 바뀜.
// 서로 다른 특수 건물끼리는 절대 겹치지 않음.
public abstract class SpecialBuildingSet : MonoBehaviour
{
    [Header("배치")]
    [Tooltip("게임 시작 시 도시 전체에서 이 건물로 바꿀 비율 (아이템 없이도 깔림, 0 = 아이템으로만 등장)")] [Range(0, 0.2f)] public float ratio = 0f;
    [Tooltip("흰 상자 대신 세울 건물 모델 (비우면 상자를 색만 칠함)")] public GameObject modelPrefab;
    [Tooltip("모델을 부지에 맞출 때 여유 비율 (1 = 부지에 꽉 차게)")] [Range(0.5f, 1.2f)] public float modelFit = 0.95f;
    [Tooltip("0이면 매번 다른 배치")] public int seed = 0;
    public Color color = Color.white;
    [Tooltip("등장 확률을 굴리는 거인 주변 반경 (m). 이 안에 처음 들어온 건물마다 한 번씩 굴림")] public float rollRadius = 350f;

    // 화면 안내용 이름 (예: "공장")
    public abstract string DisplayName { get; }

    readonly HashSet<GameObject> members = new HashSet<GameObject>();
    static readonly List<SpecialBuildingSet> sets = new List<SpecialBuildingSet>();
    static List<GameObject> buildings; // 건물 본체 목록 (위층 제외) 캐시
    static readonly int ColorId = Shader.PropertyToID("_Color");
    System.Random rng;
    MaterialPropertyBlock mpb;

    public int Count => members.Count;
    public bool Contains(GameObject building) => members.Contains(building);
    protected bool Take(GameObject building) => members.Remove(building);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sets.Clear(); buildings = null; }

    protected virtual void Awake()
    {
        sets.Add(this);
        rng = seed != 0 ? new System.Random(seed) : new System.Random();
        mpb = new MaterialPropertyBlock();
        mpb.SetColor(ColorId, color);
    }

    protected virtual void OnDestroy()
    {
        sets.Remove(this);
        if (sets.Count == 0) buildings = null; // 씬이 바뀌면 다시 수집
    }

    // 모든 특수 건물이 Awake에서 등록된 뒤에 고르므로 실행 순서와 관계없이 겹치지 않음
    protected virtual void Start()
    {
        if (ratio <= 0f) return;
        foreach (var b in Buildings)
            if (b && !IsSpecial(b) && rng.NextDouble() < ratio) Paint(b);
    }

    public static bool IsSpecial(GameObject building)
    {
        foreach (var s in sets) if (s.members.Contains(building)) return true;
        return false;
    }

    static List<GameObject> Buildings
    {
        get
        {
            if (buildings != null) return buildings;
            buildings = new List<GameObject>();
            int layer = LayerMask.NameToLayer("Building");
            foreach (var col in FindObjectsOfType<BoxCollider>())
            {
                var go = col.gameObject;
                if (go.layer != layer || (go.transform.parent && go.transform.parent.gameObject.layer == layer)) continue;
                buildings.Add(go);
            }
            return buildings;
        }
    }

    // ───────────── 등장 확률 (패시브 아이템) ─────────────
    // 거인 주변 rollRadius 안에 들어온 건물마다 딱 한 번 확률을 굴려, 당첨되면 이 건물로 바뀜.
    public float Chance { get; private set; }
    readonly HashSet<GameObject> rolled = new HashSet<GameObject>();

    // 이미 무너졌거나, 특수 건물이거나, 거인에게 맞아 금이 간 건물은 바꾸지 않음
    static bool Paintable(GameObject b) => b && b.activeInHierarchy && !IsSpecial(b) && !b.GetComponent<BuildingHealth>();

    // 아직 굴리지 않은 주변 건물에 확률 적용. 새로 바뀐 수를 돌려줌
    public int RollNearby(Vector3 center)
    {
        if (Chance <= 0f) return 0;
        float r2 = rollRadius * rollRadius;
        int n = 0;
        foreach (var b in Buildings)
        {
            if (!b) continue;
            Vector3 d = b.transform.position - center; d.y = 0f;
            if (d.sqrMagnitude > r2 || !rolled.Add(b)) continue;
            if (Paintable(b) && rng.NextDouble() < Chance) { Paint(b); n++; }
        }
        return n;
    }

    // 확률 변경. 올라간 경우 이미 굴린 건물에도 늘어난 만큼 추가로 굴려서 '처음부터 이 확률이었던 것'처럼 맞춤
    public int SetChance(float p)
    {
        float old = Chance;
        Chance = Mathf.Clamp01(p);
        if (Chance <= old || old >= 1f) return 0;
        float extra = (Chance - old) / (1f - old);
        int n = 0;
        foreach (var b in rolled)
            if (Paintable(b) && rng.NextDouble() < extra) { Paint(b); n++; }
        return n;
    }

    // ───────────── 세이브 (SaveGame) ─────────────
    public IEnumerable<GameObject> Members => members;
    public IEnumerable<GameObject> RolledBuildings => rolled;

    // 저장된 건물들을 다시 이 건물로 바꾸고, 이미 확률을 굴린 건물 목록을 되돌림
    public void Restore(IEnumerable<GameObject> painted, IEnumerable<GameObject> rolledList)
    {
        foreach (var b in painted) if (b && b.activeInHierarchy && !IsSpecial(b)) Paint(b);
        foreach (var b in rolledList) if (b) rolled.Add(b);
    }

    // 등장 확률만 되돌림 (SetChance와 달리 추가로 굴리지 않음)
    public void RestoreChance(float p) => Chance = Mathf.Clamp01(p);

    void Paint(GameObject b)
    {
        members.Add(b);
        foreach (var r in b.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(mpb);
        if (modelPrefab) PlaceModel(b);
    }

    // 흰 상자는 숨기고(충돌·체력·파괴 판정은 상자 그대로) 그 자리에 건물 모델을 부지에 맞춰 세움.
    // 모델은 상자의 자식이라 금 가며 기울기/무너지기를 그대로 따라가고 함께 사라짐. 정면(+Z)은 가까운 도로 쪽으로
    void PlaceModel(GameObject b)
    {
        foreach (var r in b.GetComponentsInChildren<Renderer>()) r.enabled = false;

        var t = b.transform;
        Vector3 size = t.lossyScale;
        // 블록 중심에서 멀어지는 방향(= 가까운 도로) 중 큰 축으로 정면을 돌림 (90도 단위라 부모의 비균일 스케일에도 찌그러지지 않음)
        var walk = t.parent ? t.parent.Find("Sidewalk") : null;
        Vector3 outward = walk ? t.position - walk.position : Vector3.forward; outward.y = 0f;
        int yaw = Mathf.Abs(outward.x) > Mathf.Abs(outward.z) ? (outward.x > 0 ? 90 : 270) : (outward.z > 0 ? 0 : 180);
        bool sideways = yaw == 90 || yaw == 270;

        var go = Instantiate(modelPrefab);
        go.name = modelPrefab.name;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Bounds mb = rs[0].bounds; foreach (var r in rs) mb.Encapsulate(r.bounds); // 원점·회전 0·크기 1 기준
        Vector3 offset = mb.center - go.transform.position; offset.y = mb.min.y - go.transform.position.y;

        // 비율 유지한 채 부지(상자 바닥면) 안에 맞춤
        float footX = sideways ? mb.size.z : mb.size.x, footZ = sideways ? mb.size.x : mb.size.z;
        float s = Mathf.Min(size.x / Mathf.Max(0.01f, footX), size.z / Mathf.Max(0.01f, footZ)) * modelFit;

        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        if (ModelScale > 1f)
        {
            // 부지보다 크게: 상자(충돌·파괴 판정)도 커진 모델 크기에 맞춤. 앞면(도로 쪽)은 그대로 두고 블록 안쪽으로 키우며,
            // 커진 자리에 겹치는 일반 건물은 숨김
            s *= ModelScale;
            foreach (Transform part in t) part.gameObject.SetActive(false); // 위층·ㄴ자 날개는 상자와 함께 늘어나지 않게 치움 (모델이 대신함)
            Vector3 newSize = new Vector3((sideways ? mb.size.z : mb.size.x) * s, mb.size.y * s, (sideways ? mb.size.x : mb.size.z) * s);
            float depthGrow = sideways ? newSize.x - size.x : newSize.z - size.z;
            Vector3 ground = t.position - Vector3.up * size.y * 0.5f;
            Vector3 ps = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(newSize.x / ps.x, newSize.y / ps.y, newSize.z / ps.z);
            t.position = ground - rot * Vector3.forward * depthGrow * 0.5f + Vector3.up * newSize.y * 0.5f;
            size = newSize;
            HideOverlapped(b);
        }

        Vector3 basePos = t.position - Vector3.up * size.y * 0.5f;
        go.transform.SetParent(t, false);
        go.transform.localRotation = rot;
        go.transform.position = basePos - rot * offset * s;
        // 부모(상자)의 스케일을 상쇄해서 실제 크기 s로 (90도 회전이면 X·Z 축이 바뀜)
        go.transform.localScale = sideways
            ? new Vector3(s / size.z, s / size.y, s / size.x)
            : new Vector3(s / size.x, s / size.y, s / size.z);

        OnModelPlaced(b, go, basePos + Vector3.up * mb.size.y * s);
    }

    // 모델 크기 배율 (1 = 부지에 맞춤, 1보다 크면 부지를 넘어 주변 건물 자리까지 차지)
    protected virtual float ModelScale => 1f;

    static readonly Collider[] overlapHits = new Collider[64];

    // 커진 특수 건물 자리에 겹치는 일반 건물을 숨김 (살짝 닿기만 한 건물은 남김)
    void HideOverlapped(GameObject b)
    {
        Physics.SyncTransforms();
        var t = b.transform;
        int n = Physics.OverlapBoxNonAlloc(t.position, t.lossyScale * 0.45f, overlapHits, t.rotation, 1 << b.layer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var o = overlapHits[i].gameObject;
            if (o == b || IsSpecial(o) || !Buildings.Contains(o)) continue;
            o.SetActive(false);
        }
    }

    // 모델을 세운 뒤 호출 (top = 모델 꼭대기 월드 위치). 표시 아이콘 등을 붙이는 용도
    protected virtual void OnModelPlaced(GameObject box, GameObject model, Vector3 top) { }
}
