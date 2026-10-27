using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 공장(폭발 건물): 흰 상자 대신 공장 모델로 서 있고, 위에 폭탄 표시가 둥실 떠서 돎.
// 무너지면(거인·폭탄 무엇이든) 잠깐 뒤 폭발해서 반경 안 건물을 모두 부숨 (explodeChance, 기본 무조건).
// 폭발에 휘말린 다른 공장도 연쇄 폭발. 배치/등장은 SpecialBuildingSet 참고.
public class ExplosiveBuildings : SpecialBuildingSet
{
    public static ExplosiveBuildings Instance { get; private set; }
    public override string DisplayName => "공장";

    [Header("크기")]
    [Tooltip("공장 크기 배율 (1 = 일반 건물 부지에 맞춤). 커진 자리에 겹치는 일반 건물은 숨겨짐")] [Range(1f, 3f)] public float sizeMultiplier = 1.8f;
    protected override float ModelScale => sizeMultiplier;

    [Header("폭발")]
    [Tooltip("무너질 때 폭발할 확률 (1 = 무조건)")] [Range(0, 1)] public float explodeChance = 1f;
    public float blastRadius = 65f;
    public float explosionSize = 45f;
    [Tooltip("무너진 뒤 폭발까지 걸리는 시간 (연쇄 폭발 간격)")] public float delay = 0.2f;
    [Tooltip("폭발로 부서지는 건물의 파편 양 배율")] [Range(0, 1)] public float shardMultiplier = 0.35f;

    [Header("폭발 표시 (공장 위에 떠 있는 폭탄)")]
    public GameObject markerPrefab;
    [Tooltip("폭탄 표시 크기 (m)")] public float markerSize = 10f;
    [Tooltip("공장 꼭대기에서 띄울 높이 (m)")] public float markerHover = 8f;

    static readonly Collider[] hits = new Collider[256];
    readonly List<Transform> markers = new List<Transform>();

    protected override void Awake() { Instance = this; base.Awake(); }
    void Reset() { color = new Color(0.9f, 0.13f, 0.1f); } // 컴포넌트를 새로 붙일 때 기본 색

    public static bool IsExplosive(GameObject building) => Instance && Instance.Contains(building);

    // BuildingDestruction이 건물을 부술 때 호출
    public static void OnBroken(GameObject building)
    {
        if (!Instance || !Instance.Take(building)) return;
        if (Random.value >= Instance.explodeChance) return; // 이번엔 그냥 무너짐
        Instance.StartCoroutine(Instance.Detonate(building.transform.position, building.transform.lossyScale));
    }

    // 공장 위에 폭탄 표시 (모델의 자식이라 공장과 함께 기울고 사라짐)
    protected override void OnModelPlaced(GameObject box, GameObject model, Vector3 top)
    {
        if (!markerPrefab) return;
        var m = Instantiate(markerPrefab);
        m.name = "BombMarker";
        foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        var rs = m.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Destroy(m); return; }
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        float s = markerSize / Mathf.Max(0.01f, b.size.y);

        // 기준점(회전) → 둥실 노드(위아래) → 폭탄(중심 정렬)
        var pivot = new GameObject("BombMarkerPivot").transform;
        pivot.SetParent(model.transform, false);
        pivot.position = top + Vector3.up * markerHover;
        pivot.localScale = Vector3.one / model.transform.lossyScale.y; // 모델 크기 상쇄 (모델은 균일 스케일)
        var bob = new GameObject("Bob").transform;
        bob.SetParent(pivot, false);
        m.transform.SetParent(bob, false);
        m.transform.localRotation = Quaternion.Euler(180f, 0f, 0f); // 폭탄 머리가 아래로 (떨어지는 폭탄처럼)
        m.transform.localScale = Vector3.one * s;
        Bounds wb = rs[0].bounds; foreach (var r in rs) wb.Encapsulate(r.bounds);
        m.transform.position += pivot.position - wb.center; // 폭탄 중심이 기준점에 오도록
        foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markers.Add(pivot);
    }

    void Update()
    {
        // 폭탄 표시: 둥실둥실 + 천천히 회전
        float t = Time.time;
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            var p = markers[i];
            if (!p) { markers.RemoveAt(i); continue; }
            p.localRotation = Quaternion.Euler(0f, t * 60f + i * 37f, 0f);
            p.GetChild(0).localPosition = Vector3.up * Mathf.Sin(t * 2f + i) * markerSize * 0.12f;
        }
    }

    IEnumerator Detonate(Vector3 center, Vector3 size)
    {
        yield return new WaitForSeconds(delay);
        Vector3 ground = new Vector3(center.x, center.y - size.y * 0.5f, center.z);

        CombatFX.Explosion(ground + Vector3.up * explosionSize * 0.25f, explosionSize);
        CombatFX.Explosion(ground + Vector3.up * explosionSize * 0.8f, explosionSize * 0.7f);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            CombatFX.Explosion(ground + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * blastRadius * 0.45f, explosionSize * 0.5f);
        }
        if (GiantCamera.Instance) GiantCamera.Instance.Shake(0.7f);

        int n = Physics.OverlapSphereNonAlloc(ground, blastRadius, hits, LayerMask.GetMask("Building"), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (hits[i]) BuildingDestruction.Break(hits[i].gameObject, ground, Vector3.zero, true, shardMultiplier);
    }
}
